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

*(Filled in by Step 3. The world tick order and its load-bearing constraints —
sense/think fully separated from actuate, spatial index rebuilt between all
writes and all reads, contested resources resolved in a canonical key order —
are documented here once implemented.)*

## Determinism

*(Filled in by Step 4. Summary of intent: per-creature RNG streams keyed on
`(worldSeed, creatureId, birthTick)` so creature updates are order-independent;
ordered iteration everywhere in the sim; `World.ComputeHash()` over quantised
state as the regression signal.)*
