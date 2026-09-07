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

### The docs, and which one to reach for

| File | What it is |
|---|---|
| `docs/SPEC.md` | The owner's original brief. **The source of truth for scope** |
| `docs/DESIGN.md` | Cumulative build log — one section per step: problem, approach, measurements, files touched |
| `docs/ARCHITECTURE.md` | Project layout, the **tick order** and why it is load-bearing |
| `docs/GENOME.md` | Trait axes, latent attributes, mutation classes |
| `docs/CONTROLS.md` | Every key in the board view and the Creature Lab |

## Workflow

**Documentation is updated by commit, not at the end of a step.** Every commit
that changes behaviour carries its doc change with it — `DESIGN.md` for what was
learned, plus `ARCHITECTURE.md` / `CONTROLS.md` / `GENOME.md` when the thing they
describe moved. A commit whose docs land later is a commit whose docs describe a
state that never existed.

Two habits this repository has paid for, both worth keeping:

- **Measure before you change, on the same branch.** Numbers carried forward from
  an earlier step are usually measuring different code. Re-run the baseline first
  and put both numbers in the commit message.
- **Instrument rather than reason** when behaviour is wrong. Four separate freezes
  in Step 3g were each mis-diagnosed by inspection and all four found in one run of
  a throwaway probe that printed per-creature state.

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

### Tests

`dotnet test` needs no `DYLD` variable: the sim tests link no graphics, and the UI
tests exercise pure input logic. A full run is about 10 seconds.

```bash
dotnet test --filter ActionExecutionTests             # one class
dotnet test --filter FullyEatenPlant_IsRemoved        # one test
dotnet test --filter "FullyQualifiedName~Feeding"     # substring match
dotnet test tests/GoL.Sim.Tests                       # one project
```

A filtered run prints `No test matches the given testcase filter ... in
GoL.Ui.Tests.dll` for whichever project does not contain the test. That is not a
failure — scope the run to one project to silence it.

### The soak runner

`tools/GoL.Headless` is how behaviour is actually measured, and the standing proof
that the core needs no graphics — it references `GoL.Sim` only and runs **without**
`DYLD_FALLBACK_LIBRARY_PATH`. Use `-c Release`; a Debug soak is several times
slower.

```bash
# the board
dotnet run --project tools/GoL.Headless -c Release -- --ticks 20000 --seed 42

# the Creature Lab arena, which is small and dense
dotnet run --project tools/GoL.Headless -c Release -- \
    --lab --plants 240 --creatures 8 --ticks 20000 --seed 7
```

| Flag | Meaning |
|---|---|
| `--ticks`, `--seed`, `--creatures` | Run length, RNG seed, starting population |
| `--lab`, `--plants` | Use `LabEnvironment` instead of the board, with N plants |
| `--unlock-all` | Grant every latent attribute at spawn. **Not a simulation mode** — it makes the latent rows of the behaviour histogram falsifiable, since a short run unlocks nothing and a permanently-zero row is indistinguishable from a broken one |

It prints a per-action histogram with **`running` and `blocked` in separate
columns** — a row that is mostly blocked is a creature repeatedly attempting
something it cannot do — plus bites per creature-minute, mouth-open share, action
switches per creature-second, and a `world hash`. Two runs that should be identical
must produce the same hash; it is quantised, so display-only changes do not move
it.

In VS Code the `build`/`run` tasks and the **Debug GoL** launch config already
inject it; press F5.

### Why the VS Code tasks use an absolute path to `dotnet`

`.vscode/tasks.json` sets `"command": "/usr/local/share/dotnet/dotnet"` and puts
that directory on `PATH`. Both are needed, for different reasons, and reverting
either brings a distinct failure back:

- `"type": "process"` launches the executable directly, resolved against **VS
  Code's own** `PATH`. macOS registers dotnet in `/etc/paths.d/dotnet`, but that
  file is read by `path_helper` only during **login shell** startup, so a VS Code
  launched from the Dock or Finder never sees it — the task then fails with
  `Path to shell executable "dotnet" does not exist`. Switching to
  `"type": "shell"` does **not** fix it: VS Code runs the shell non-interactively,
  which skips `.zshrc` too.
- The build spawns *further* dotnet processes — MGCB compiles the spritefont
  through dotnet local tools — and those look `dotnet` up on `PATH`. A content
  build that cannot find it fails **without failing the build**, which is one of
  the ways the `ContentLoadException: Content/Fonts/UiFont.xnb` crash below
  arrives.

The **C# extension needs telling separately** — fixing the tasks does not fix it,
and it surfaces as a popup even when the build itself succeeds:

```
Error running dotnet --info: Command failed: dotnet --info
/bin/sh: dotnet: command not found
```

`.vscode/settings.json` sets `omnisharp.dotNetCliPaths`. That is a list of
**directories** the extension searches for a `dotnet` executable (it joins the
directory and the exe name itself). It defaults to `[]`, and with nothing found the
extension falls back to a bare `dotnet --info` through `/bin/sh` — which loads no
profile and so cannot recover `PATH` either. The `omnisharp.` prefix is legacy
naming; the setting is read by the shared CLI-discovery helper and still applies
under the modern Roslyn language server.

Both files hardcode `/usr/local/share/dotnet`, the standard .NET installer location
on both Intel and Apple Silicon. If dotnet ever moves, both need updating.

### The root fix, on this machine

Patching consumers one at a time was losing — three symptoms appeared in a row
(the task, the `dotnet --info` popup, then the debugger), each from a different
extension with its own discovery mechanism. The cause underneath all of them is
that **a GUI-launched process has no dotnet on `PATH` at all**.

So the GUI session's `PATH` is now set directly, by a LaunchAgent at
`~/Library/LaunchAgents/local.setenv.path.plist` that runs
`launchctl setenv PATH …` at login. Verify it with:

```bash
launchctl print gui/$(id -u) | sed -n '/environment = {/,/}/p'
```

`PATH` there should list `/usr/local/share/dotnet`. **The value is inherited at
process launch**, so after changing it VS Code must be fully quit (`Cmd+Q`) and
reopened — reloading the window is not enough.

To remove it: `launchctl unload ~/Library/LaunchAgents/local.setenv.path.plist`
and delete the file.

The two settings files above stay useful regardless — they make the repo work for
anyone cloning it who has not done this to their own machine.

### `program ''` in the debugger: a bad `DOTNET_ROOT`

If F5 fails with `launch: program '' does not exist` even though `launch.json`
names a real dll, the cause is **not** in `launch.json` — C# Dev Kit registers its
own debug configuration provider for `coreclr`, and when its language server is
dead it supplies a configuration with an empty `program`, overriding the one on
disk. Changing `launch.json` therefore has no effect at all, which is what makes
this so confusing to chase.

The server was dead because of one wrong character class in a user setting:

```jsonc
// wrong - a directory
"dotnetAcquisitionExtension.sharedExistingDotnetPath": "/usr/local/share/dotnet"
// right - the executable
"dotnetAcquisitionExtension.sharedExistingDotnetPath": "/usr/local/share/dotnet/dotnet"
```

Dev Kit takes the **dirname** of that value to build `DOTNET_ROOT`, so a directory
loses a segment and yields `DOTNET_ROOT=/usr/local/share`, where no runtime lives.
`dotnetAcquisitionExtension.allowInvalidPaths: true` suppresses the complaint, so
it fails silently.

**The evidence lives in the logs**, not in the popup:

```bash
tail -60 ~/Library/Application\ Support/Code/logs/*/window1/exthost/ms-dotnettools.csdevkit/C\#\ Dev\ Kit.log
```

A healthy start logs `.NET SDK found`; a broken one logs
`.NET server STDERR: You must install .NET to run this application`, prints the
`DOTNET_ROOT` it used, and ends `.NET server exited with 131`. Reach for that log
first next time — three rounds were spent editing `launch.json`, which was never
the problem.

Unrelated but adjacent: `/etc/paths.d/dotnet-cli-tools` has **no trailing
newline**, so `path_helper` glues it to the next file and a login shell ends up
with a literal, unexpanded `~/.dotnet/tools` in `PATH`. Harmless, but it means
that entry has never actually worked. Fixing it needs `sudo`.

## Architecture

Six projects. The dependency direction is the whole design:

```
GoL.Sim  <--  GoL.Render  <--  GoL.App
   ^              ^               (MonoGame + Extended)
   |              |
   |              +--  GoL.Ui.Tests       (menu list, confirm prompt)
   |
   +--  GoL.Sim.Tests,  GoL.Headless      (no graphics stack at all)
```

- **`src/GoL.Sim`** — the headless simulation core. Genome, brain, world tick,
  spatial index, plants, pheromones.
- **`src/GoL.Render`** — draws sim state. Pure presentation; holds no
  simulation logic and never mutates what it is handed.
- **`src/GoL.App`** — `Game` subclass, `ScreenManager`, screens, config.
- **`tests/GoL.Sim.Tests`** — xunit against the core; the bulk of the suite.
- **`tests/GoL.Ui.Tests`** — xunit against the input-handling widgets in
  `GoL.Render` that are pure logic (menu selection, modal prompts). It references
  `GoL.Render`, so unlike the sim tests it is not proof of anything headless.
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

### Actions: what a creature *does*

A creature runs **exactly one action at a time**, and while it runs it owns the
body. This layer is `src/GoL.Sim/Acting/`:

```csharp
enum ActionStatus { Running, Done, Blocked }

interface ICreatureAction
{
    CreatureAction Id { get; }
    bool CanStart(in ActionContext ctx);        // is there anything to eat?
    ActionStatus Execute(in ActionContext ctx, float dt);
}
```

`ActionRunnerSystem` selects one and runs it; the `Doing` component records what
ran, its status and its target. **`ActuateSystem` and `FeedSystem` no longer
exist** — they applied thrust, turn, scent and biting as four unrelated effects of
one `Intent`, so nothing owned "eating" and nothing could notice a creature biting
at empty air.

Five rules here are load-bearing, and each was arrived at the hard way:

1. **Selection reads `Intent`, never the body.** The readout deliberately reads
   Move and Turn off the body (what it is visibly doing, not what it asked for).
   Selecting on those same values self-latches: the body moves because Move ran
   last tick, so Move reads high, so Move wins again regardless of the brain — and
   the symptom is actions running to completion, which is the desired outcome, so
   the bug reads as success.
2. **An action returning `Blocked` must not have moved the body.** The runner falls
   through to the next-best action on a block, and that one will drive; two actions
   moving one creature in a tick charges it twice.
3. **A finished action is finished.** `CanStart` gates every fresh start, and
   "fresh" means the status was not `Running` — not merely that the action id
   changed. Comparing only the id left `Done` actions repeating forever.
4. **Forced actions do not fall through.** The lab pins an action (`O`) so it can be
   watched in isolation; watching a pinned action *fail* is the diagnostic. Nothing
   outside the lab ever sets `Doing.Forced`.
5. **Actions execute, they never choose.** Closing distance to a green the creature
   already decided to eat is execution. Deciding to eat rather than flee is the
   brain's, and must never migrate into an action — food-finding has to stay
   something a lineage evolves.

`Eat` steers by **sight** (its own eye bins), never by asking the environment where
food is. An environment query would give a creature with terrible eyes the same
food-finding as one with excellent eyes. Because an eye reports a *direction and a
range* and never an identity, approach and biting are separate: `Doing.Target`
holds where to go, `Mind.BiteTarget` keeps the mouth-reach latch by id.

Opposite behaviours are **one signed action**, not two: chase/flee are `Move` with
a sign, left/right are `Turn`. Sprint is a *modifier* on movement and gets no
`ICreatureAction` at all — `Acting/SprintAction.cs` exists only to say so, because
an empty slot in the runner's table looks exactly like an unfinished one.

### Two environments, two implementations

`IEnvironment` has two implementations, and **they do not share their feeding or
ray-casting code**. `LabEnvironment` brute-forces a plant list; `BoardEnvironment`
marches a grid (DDA) and has its own `ResolveBite`, `FindBiteTarget` and
`StillBiting`.

So **anything touching sensing or eating must be measured on both.** A change can
be perfect in the lab and broken on the board. The signature of that failure in the
soak report is `Bite` high with `in/s` near zero.

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
