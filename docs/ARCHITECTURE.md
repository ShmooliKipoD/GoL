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
