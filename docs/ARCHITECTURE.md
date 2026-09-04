# Architecture

## Project graph

```
GoL.Sim  <--  GoL.Render  <--  GoL.App
   ^                              (MonoGame DesktopGL + MonoGame.Extended)
   |
   +--  GoL.Sim.Tests            (xunit)
   +--  GoL.Headless             (soak runner)
```

| Project | Responsibility | May reference |
|---|---|---|
| `src/GoL.Sim` | Genome, brain, world tick, spatial index, plants, pheromones | BCL only |
| `src/GoL.Render` | Drawing sim state. No simulation logic, never mutates | Sim, MonoGame, Extended |
| `src/GoL.App` | `Game`, `ScreenManager`, screens, config persistence | Sim, Render, MonoGame, Extended |
| `tests/GoL.Sim.Tests` | Unit tests over the core | Sim |
| `tests/GoL.Ui.Tests` | Menu, prompt and input-mapping logic | Sim, Render |
| `tools/GoL.Headless` | Long soak runs, world-hash checks | Sim |

### Why the core is headless

`GoL.Sim` must never reference MonoGame or MonoGame.Extended. Three things
depend on that:

1. **Tests run without a window.** No GL context, no
   `DYLD_FALLBACK_LIBRARY_PATH`, no display server.
2. **Soak runs are cheap.** `GoL.Headless` can run 10 000+ ticks at full speed
   with no frame pacing and no graphics driver in the process.
3. **Determinism is auditable.** Graphics libraries bring their own statics,
   time sources and float paths. Keeping them out of the core means the only
   things that can affect the simulation are the ones deliberately put there.

The core uses `System.Numerics.Vector2`. `GoL.Render` converts to
`Microsoft.Xna.Framework.Vector2` at the boundary. This costs one extension
method and buys the whole property above.

`ArchitectureTests.SimAssembly_DoesNotReferenceGraphicsStack` enforces the rule
by reflecting over the assembly's referenced-assembly list.

**Corollary:** `MonoGame.Extended.ECS` and `MonoGame.Extended.Collisions.QuadTree`
are deliberately unused. Both are attractive for the simulation and both would
break the rule. The sim gets its own uniform spatial hash instead.

## What MonoGame.Extended is used for

| Feature | Use |
|---|---|
| `Screens.ScreenManager` + `GameScreen` | One screen class per phase, in place of a `GamePhase` enum with parallel `switch` blocks in `Update`/`Draw` |
| `OrthographicCamera` + `BoxingViewportAdapter` | Board camera: `Zoom`, `Move`, `GetViewMatrix()`, `ScreenToWorld()` |
| `Input.KeyboardExtended` / `MouseExtended` | `WasKeyJustDown` edge detection, wheel and drag |
| `SpriteBatchExtensions.DrawCircle` / `DrawLine` / `DrawPolygon` | Creature bodies, vision cones, smell rings, mouth arcs, brain-graph edges |
| `Particles` | Birth / death / eat bursts |
| `Tweening.Tweener` | Menu and camera easing |
| `FramesPerSecondCounter` | Debug HUD |

## Build output

`Directory.Build.props` redirects all output to `dest/`:

```xml
<BaseOutputPath>$(MSBuildThisFileDirectory)dest/$(MSBuildProjectName)/</BaseOutputPath>
<BaseIntermediateOutputPath>$(MSBuildThisFileDirectory)dest/obj/$(MSBuildProjectName)/</BaseIntermediateOutputPath>
```

`BaseIntermediateOutputPath` is read *before* the SDK imports, so it only works
from `Directory.Build.props`; in a `.csproj` it is a silent no-op.

The redirect interacts with the MGCB content pipeline, which writes `.xnb` files
to `bin/$(Platform)` relative to `Content/` and then copies them into the output
directory. Verified working: `dest/GoL.App/Debug/net8.0/Content/Fonts/UiFont.xnb`
exists after a build and loads at runtime.

**`src/GoL.App/Content/bin` and `.../Content/obj` exist and are correct.** MGCB's
`/outputDir` and `/intermediateDir` are relative to `Content/`, not to the
project's output path, so they sit under `src/` regardless of the `dest/`
redirect. They are gitignored. Do not "fix" this — the `.xnb` does reach `dest/`.

`GoL.Sim` additionally sets `<ImplicitUsings>disable</ImplicitUsings>`. The
solution-wide default would put `System.Linq` in scope in every core file, and
LINQ allocates; the tick loop must not. Turning it off makes `using System.Linq;`
a visible line in review rather than an invisible default.

## Screens vs. overlays

`ScreenManager.LoadScreen` **unloads the outgoing screen**. That is fine for
menu↔config, but it means pause cannot be a screen: loading a pause screen over
the simulation would dispose the world.

So the split is:

| Kind | Members | Why |
|---|---|---|
| **Screens** (`GameScreen`, swapped by `ScreenManager`) | `MainMenuScreen`, `ConfigScreen`, `CreatureLabScreen`, `SimulationScreen` | Each owns state the others do not need |
| **Overlays** (a `MenuList` drawn by its host screen, gated on a local flag) | exit-confirm, pause | Must not destroy the state underneath them |

## Input

`KeyboardExtended` and `MouseExtended` hold static previous/current state, so
`WasKeyJustDown` only works if their `Update()` is called **exactly once per
frame**. Zero calls and edges never fire (the menu looks dead); two calls and
edges are lost intermittently (the menu looks like it skips keypresses).

Both are called at the top of `GolGame.Update`, **before** `base.Update()` —
which is what ticks the `ScreenManager` component and therefore the screens, so
screens see fresh edge state.

`InputMap.ReadMenu` then converts that into a plain `MenuInput` record struct,
and `MenuList`/`ConfirmPrompt` take *that* rather than a `KeyboardStateExtended`.
This is not indirection for its own sake: macOS blocks synthetic keystrokes, so
the running UI cannot be driven from a script, and the plain-data seam is what
makes navigation, wrapping, adjustment and the confirm flow testable at all
(`tests/GoL.Ui.Tests`). `InputMap` is also the single place key bindings are
named — see `docs/CONTROLS.md`.

## Simulation structure

The simulation is **entities, components and systems**, on
`MonoGame.Extended.ECS`. Creatures are entities; their body, energy, vitals,
genome, brain and eyes are components; each phase of the tick is a system.

Not everything is an entity. **Vegetation, fertility and scent are grids** — the
plant grid alone is tens of thousands of cells, and making each an entity would
be a large regression for no gain. Dense uniform data stays in arrays.

### Tick order

The system registration order in `SimWorld` *is* the tick order, and it is
load-bearing:

| # | System | Why here |
|---|---|---|
| 1 | `FieldSystem` | Scent, fertility, plant growth, spatial index rebuild. First, so every creature perceives the same field state, and the index is rebuilt after the previous tick's movement but before this tick's sensing |
| 2 | `SenseSystem` | Reads only |
| 3 | `ThinkSystem` | Reads only |
| 4 | `ActuateSystem` | **First writer.** Everything before it only read |
| 5 | `FeedSystem` | After movement, so a creature bites from where it ended up |
| 6 | `EnergySystem` | After feeding, so what it ate is credited before it is charged |
| 7 | `LifecycleSystem` | Births and deaths, so entities are never created or destroyed mid-iteration |
| 8 | `ActionSystem` | After lifecycle, so a birth is read from the tick stamp lifecycle just wrote rather than by re-deriving the reproduction predicate |

The split that matters most: **sensing and thinking both complete before anything
acts.** If creature 0 moved before creature 1 sensed, the run would depend on the
order the entity list happens to be in, and reproducibility from a seed would be
gone.

`ActionSystem` writes only to `Behaviour`, and nothing in the simulation reads that
component back. If anything did, the readout would start *driving* behaviour instead
of describing it. It also skips the dead, which makes it moot whether an entity
destroyed by `LifecycleSystem` is still visible to a later system in the same
`world.Update()` — something Extended does not document.

`LifecycleSystem` collects births and deaths into lists and applies them after its
pass, births first — so a parent that dies this tick still leaves issue, and a
freed entity slot cannot be handed to its own offspring (which would give the
child its parent's random stream).

### Sub-rate work

Two costs are **population-independent**, so they run below tick rate:

- **Scent diffusion** every 4th ticks. Emission still happens every tick; only the
  diffuse-and-decay pass is sub-rate. At full rate this was measured as the
  largest single cost in the simulation.
- **Plant growth** in 16 buckets, one per tick, so a full sweep completes every 16
  ticks at a flat cost that does not depend on how much is growing.

### Performance, measured

At 1024-unit world, 600 ticks, headless:

| Creatures | ticks/s |
|---|---|
| 20 | 600 |
| 80 | 474 |
| 200 | 219 |
| 500 | 107 |

Real time needs 60. Two bottlenecks were found and fixed by measurement, not
guesswork:

1. **Vision gathering every plant in range.** Sweeping the square enclosing eye
   range is ~1200 cells per creature per tick, and it dominated everything.
   Replaced with a **DDA ray march, one ray per vision bin** — cost now depends on
   how far the ray travels, not the area it could have covered, and it yields
   nearest-hit-per-bin and occlusion for free. Creatures still come from the
   spatial hash, since there are few of them and their angular width matters.
2. **Scent diffusion calling a wrapping helper for every neighbour.** The modulo
   arithmetic, run tens of thousands of times per pass for a wrap that only applies
   on the boundary, became the dominant fixed cost once vision was fixed. Split
   into an interior fast path and an edge path, and the buffers are swapped rather
   than copied.

## Determinism

*(Filled in by Step 4. Summary of intent: per-creature RNG streams keyed on
`(worldSeed, creatureId, birthTick)` so creature updates are order-independent;
ordered iteration everywhere in the sim; `World.ComputeHash()` over quantised
state as the regression signal.)*
