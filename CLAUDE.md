# CLAUDE.md

Guidance for Claude Code (claude.ai/code) when working in this repository.

## What this is

**GoL** is an evolutionary artificial-life simulator, not a cellular automaton.
Each creature is an agent driven by a neural-network genome — a graph with
weights — that senses the world through eyes and a nose, moves, eats, spends
energy and reproduces. Offspring are mutated, and a rare mutation class
**unlocks entirely new attributes**, so the population's trait set is
open-ended rather than fixed at design time.

MonoGame DesktopGL 3.8.1.303 + MonoGame.Extended 4.0.0, `net8.0`, macOS desktop.

`docs/SPEC.md` is the owner's original brief and the source of truth for scope.
`docs/DESIGN.md` is the cumulative build log — one section per step with
problem, approach and files touched. **Append to it and commit at the end of
every step**; that cadence is an explicit requirement, not a convention.

## Build & Run (macOS)

MonoGame DesktopGL needs Homebrew's freetype at runtime, so
`DYLD_FALLBACK_LIBRARY_PATH` is **required for every build and run**. Both
Homebrew prefixes are listed so the same command works on Apple Silicon
(`/opt/homebrew`) and Intel (`/usr/local`) — setting this var *replaces*
dyld's default fallback list, so a single wrong prefix hides freetype entirely.

```bash
brew install freetype                    # one-time

DYLD_FALLBACK_LIBRARY_PATH=/opt/homebrew/lib:/usr/local/lib dotnet build
DYLD_FALLBACK_LIBRARY_PATH=/opt/homebrew/lib:/usr/local/lib dotnet run --project src/GoL.App
dotnet test                              # needs no env var - see below
```

In VS Code the `build`/`run` tasks and the **Debug GoL** launch config already
inject it; press F5.

## Architecture

Five projects. The dependency direction is the whole design:

```
GoL.Sim  <--  GoL.Render  <--  GoL.App
   ^                              (MonoGame + Extended)
   |
   +--  GoL.Sim.Tests,  GoL.Headless      (no graphics stack at all)
```

- **`src/GoL.Sim`** — the headless simulation core. Genome, brain, world tick,
  spatial index, plants, pheromones.
- **`src/GoL.Render`** — draws sim state. Pure presentation; holds no
  simulation logic and never mutates what it is handed.
- **`src/GoL.App`** — `Game` subclass, `ScreenManager`, screens, config.
- **`tests/GoL.Sim.Tests`** — xunit against the core.
- **`tools/GoL.Headless`** — soak runner; references `GoL.Sim` only, so it
  needs no GL context and no `DYLD_FALLBACK_LIBRARY_PATH`.

### The one rule that must not break: the core stays headless

`GoL.Sim` must be runnable with **no graphics device, no window, and no
`DYLD_FALLBACK_LIBRARY_PATH`**. That is what makes the sim unit-testable, lets a
10 000-tick soak run anywhere, and keeps a graphics library's statics and time
sources out of a system that has to be bit-reproducible from a seed.

Note what the rule is and is not. It is *not* "never reference MonoGame" — the
core references `MonoGame.Extended` for its ECS (see below), and that was verified
to load and run with no GL context. It **is** "never need graphics to run".
`tools/GoL.Headless` is the standing proof: it references only `GoL.Sim` and is
built and run without the `DYLD` variable.

Inside the core, use `System.Numerics.Vector2` rather than the XNA type;
`GoL.Render` converts at the boundary.

## Architecture style: ECS

The simulation is **entities, components and systems**. Creatures are entities;
their traits, body, brain and senses are components; each phase of the tick is a
system. This suits the project directly: a creature's capabilities grow by
mutation, and composition expresses "this lineage acquired a rear eye" far better
than an inheritance hierarchy or a widening struct would.

We use **`MonoGame.Extended.ECS`** (`WorldBuilder`, `EntityUpdateSystem`,
`ComponentMapper<T>`, `Aspect`), not a hand-rolled one.

Four things about it were established by experiment, not from documentation. Each
is non-obvious and each will otherwise be re-litigated:

1. **It runs fully headless.** A console app using `WorldBuilder`/`EntityUpdateSystem`
   builds and runs with no `DYLD_FALLBACK_LIBRARY_PATH`, no window and no
   `GraphicsDevice`. Referencing `MonoGame.Framework` does not force graphics
   initialization.
2. **`world.Update()` allocates zero bytes per tick** once warmed up (measured with
   `GC.GetAllocatedBytesForCurrentThread`).
3. **`ActiveEntities` iterates in ascending entity id**, not insertion order. Proven
   by destroying low ids and recreating: the replacements came back as ids `1, 0`
   from a LIFO free list, yet still iterated `0,1,2,3,4,5`. This is what makes a
   seeded run reproducible — but the guarantee is undocumented upstream, so if
   determinism tests ever start failing, check this first.
4. **Components must be reference types.** `ComponentMapper<T>` constrains
   `T : class`, so components are heap objects. The per-tick path stays
   allocation-free, but **birth allocates** one object per component. Step 4 has
   hundreds of births in a run; pool component objects on the entity free list if
   that shows up in a profile.

### What is *not* an entity

**Plants, the fertility field and the pheromone field are grids, not entities.**
The plant grid alone is tens of thousands of cells; making each an entity would be
a large regression for no gain, and the bucketed sweep that keeps plant growth
cheap depends on grid indexing. "Prefer ECS" is not "everything is an entity" —
dense uniform data stays in arrays.

### Scene graphs are not available

`SceneGraph`, `SceneNode` and `SpriteEntity` are documented on
monogameextended.net but **do not exist in MonoGame.Extended 4.0.0**, the version
this project uses — verified by inspecting the shipped assembly. Do not plan around
them.

Even if they were present they would be a poor fit: the feature is built on
`Sprite` and hierarchical parent-child transforms (its worked example is a car body
with wheels), whereas creatures are flat, drawn from vector primitives
(`DrawCircle`/`DrawLine`), and number in the hundreds. Picking is served by the
sim's spatial hash, not by a tree walk.

If a later Extended release restores the feature, this note stops being true —
check the version before trusting it.

### Build output goes to `dest/`

`Directory.Build.props` redirects `BaseOutputPath` and
`BaseIntermediateOutputPath` to `dest/`. **`BaseIntermediateOutputPath` only
takes effect from `Directory.Build.props`** — it is read before the SDK
imports, so setting it in a `.csproj` is a silent no-op.

Project templates hardcode `<TargetFramework>` into the generated `.csproj`,
which overrides `Directory.Build.props`. When adding a project, **delete that
line from its csproj** or it silently builds for the wrong framework.

## Content pipeline

The `UiFont` spritefont is compiled by MGCB during `dotnet build`, wired via
`MonoGame.Content.Builder.Task` and dotnet local tools
(`src/GoL.App/.config/dotnet-tools.json`). A healthy build logs
`Building Font .../monogram.ttf`.

If the game crashes at startup with
`ContentLoadException: Content/Fonts/UiFont.xnb`, the content build silently
no-oped — look for `The file /quiet does not exist.` in the build log, which
means the MGCB invocation is broken. Fix by deleting
`~/.nuget/packages/monogame.content.builder.task` and re-restoring. Note a
content-build failure does **not** fail `dotnet build`.

### Font rules

`monogram.ttf` is the only font, and it covers **full printable ASCII (32–126)**.
This matters: QiLight (the sibling project this menu style comes from) uses a
display font whose cmap holds only space and A–Z/a–z, so digits render as `A`.
GoL is nothing but numeric readouts — trait values, weights, energy, generation
counts — so a letters-only font is not survivable here.

monogram is baked at `Size 12` (= its native 16px em grid at 96 DPI). To keep it
crisp, all three of these are required together:
**`SamplerState.PointClamp`**, **integer draw scales**, and **whole-pixel
positions** (`MathF.Round` the position). Drop any one and the glyph grid
samples unevenly — some pixel rows double, others vanish.

Font license is unverified — see `src/GoL.App/Content/Fonts/NOTICE.md`.

## Gotchas

- `Window.Title` must be set in `Initialize()` **after** `base.Initialize()`.
  MonoGame creates the window during that call and stamps it with the assembly
  name, so a title set in the constructor is silently overwritten.
- DesktopGL has no `PrimitiveType.TriangleFan` — use `TriangleList`.
- MonoGame.Extended 4.0.0 ships only a `lib/net6.0` and declares no
  dependencies. It works on `net8.0` here, but that combination is not something
  upstream tests.
