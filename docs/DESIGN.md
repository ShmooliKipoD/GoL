# GoL — Design & Implementation Log

Cumulative record of the work. Each section is a self-contained step or fix with
its problem, approach and the key files touched. Reflects the implemented state
of the code, not the plan.

---

## 1. Step 0 — Project skeleton

**Problem.** The repository was empty (no commits) and every downstream step
rested on three assumptions that could only be settled by building something:
whether the MGCB content pipeline survives redirecting build output to `dest/`,
whether MonoGame.Extended 4.0.0 — which ships only a `lib/net6.0` and declares
no dependencies — resolves and runs on `net8.0`, and whether the chosen font
actually renders digits. Each fails at *runtime*, not at build, so guessing
would have meant discovering it several steps later.

A fourth problem was immediate: `README.md` held the owner's original brief but
was found **empty on disk** (1 byte) at the start of work, despite a directory
listing minutes earlier reporting 1668 bytes. With no commits in the repo there
was nothing to recover from.

**Approach.**

- **Spec preserved first.** `docs/SPEC.md` was written from the originating
  request, verbatim including its original spelling, and confirmed non-empty
  before anything else touched `README.md`.
- **Five projects** matching the dependency graph in `ARCHITECTURE.md`. The
  `dotnet new` templates hardcode `<TargetFramework>net10.0</TargetFramework>`
  into each generated `.csproj`, which overrides `Directory.Build.props`; every
  csproj was rewritten without it so the solution-wide `net8.0` actually applies.
- **`GoL.App.csproj` deliberately omits** `<OutputType>WinExe</OutputType>`,
  `app.manifest` and `<ApplicationIcon>`. All three come from the MonoGame
  template, are Windows-only, and are inert on macOS — QiLight carries them
  unused.
- **`SmokeTestScreen`** proves all three runtime assumptions in one frame: it
  draws `GoL 0123456789 !@#$%` (font loaded → content path survived the
  redirect; digits and punctuation visible → cmap is full ASCII) and a pulsing
  circle via `SpriteBatch.DrawCircle` (→ Extended resolves and works on net8.0).
- **`ArchitectureTests`** asserts `GoL.Sim`'s referenced-assembly list contains
  no `MonoGame*` or `Microsoft.Xna*` entry, making the headless-core rule a
  build failure rather than a convention.

**Verified.** Build succeeds with 0 warnings and logs
`Building Font .../monogram.ttf`; `dest/` holds all output and no `bin/`/`obj/`
appears beside any csproj; `UiFont.xnb` lands in the app's output; `dotnet test`
passes; the window opens and was screenshotted showing crisp digits,
punctuation and the circle.

**Files:** `Directory.Build.props`, `.gitignore`, `GoL.slnx`, `CLAUDE.md`,
`README.md`, `docs/{SPEC,ARCHITECTURE,DESIGN}.md`, `.vscode/{tasks,launch}.json`,
`src/GoL.App/{GolGame.cs,Program.cs,GoL.App.csproj}`,
`src/GoL.App/Screens/SmokeTestScreen.cs`,
`src/GoL.App/Content/{Content.mgcb,Fonts/UiFont.spritefont,Fonts/monogram.ttf,Fonts/NOTICE.md}`,
`src/GoL.Sim/`, `src/GoL.Render/`, `tools/GoL.Headless/`,
`tests/GoL.Sim.Tests/ArchitectureTests.cs`.

**Gotchas found.**

- The .NET 10 SDK's `dotnet new sln` produces a **`GoL.slnx`**, not a `.sln`.
  Tooling that hardcodes the old name (VS Code tasks, scripts) needs updating.
- `Window.Title` set in the `Game` constructor is silently overwritten —
  MonoGame creates the window during `base.Initialize()` and stamps it with the
  assembly name. It must be set after that call.
- The font license is unverified and unbundled (carried over from QiLight, where
  it was likewise flagged). See `src/GoL.App/Content/Fonts/NOTICE.md`. **Open
  item — must be resolved before distributing.**
