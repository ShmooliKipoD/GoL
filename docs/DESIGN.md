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

---

## 2. Step 1 — Game infrastructure and the main menu

**Problem.** The spec asks for a main menu taken from QiLight offering New Game,
Configuration (with a Back) and Exit (with a y/n confirm where Esc also exits).
QiLight turns out to have **no menu framework to take**: its menu is ~70 lines of
update logic on the `Game` subclass plus ~55 lines in the renderer, with the item
labels as a `string[]` literal whose order must be hand-synced against a private
enum and a separate count constant — and the pause overlay is a copy-paste clone
of the whole thing. Adding an item means editing three places in two files.

**Approach.**

- **Menus are data.** `MenuList` owns a `List<MenuEntry>` — label, optional
  activate, optional value formatter, optional adjuster — and handles wrapping
  navigation, adjustment and activation. The main menu, the configuration screen
  and (in Step 4) the pause overlay are three instances of one type. What was
  ported from QiLight is the *look*: whole-pixel snapping, `PointClamp`, the
  drop-shadow-plus-bob treatment on the selected item (`TextRenderer`).
- **Screens via `MonoGame.Extended.ScreenManager`** rather than a `GamePhase`
  enum with parallel `switch` blocks. `ScreenManager.LoadScreen` **unloads** the
  outgoing screen, so pause and exit-confirm cannot be screens — they would
  dispose the world beneath them. They are overlays drawn by their host screen.
  Recorded in `ARCHITECTURE.md` before Step 4 can discover it by losing a world.
- **Exit prompt deviates from QiLight deliberately.** QiLight arms a
  three-second window and quits on a second keypress. The spec asks for y/n with
  Esc also exiting. Porting the timer verbatim would quietly not be what was
  asked for, so `ConfirmPrompt` implements the spec's version.
- **`SimConfig` is a non-positional record** with init-only properties and inline
  defaults. Non-positional because it is persisted to JSON and grows fields every
  step: a positional record binds through its constructor, so every field absent
  from an older file would arrive as `0`, and a zero metabolic rate presents as
  "evolution mysteriously does nothing" rather than as an error. A record so the
  configuration screen can edit with `with` expressions and the world never sees
  its config mutate mid-run.
- **`SimConfigStore` lives in `GoL.Sim`, not the app.** It depends only on
  `System.Text.Json` and the config type, and putting it in the headless assembly
  makes its round-trip and fallback behaviour testable. Persistence is
  best-effort throughout: a corrupt or unwritable file must never stop the game
  launching.

**Verification, and a constraint on it.** macOS blocks synthetic keystrokes
(`osascript` returns "not allowed to send keystrokes"), so the running UI cannot
be driven from a script. Rather than leave the menu unverified, the logic was
decoupled from MonoGame input: `MenuList` and `ConfirmPrompt` take a plain
`MenuInput` record struct, and `InputMap` is the single place that turns keyboard
edges into one. 22 tests now cover navigation wrapping, selective activation,
input consumption, adjuster stepping, prompt modality, Esc-confirms-exit, and
config round-trip, corrupt-file fallback and defaults-for-absent-fields.

What was verified visually instead: the menu renders and the configuration screen
loads a hand-written sparse `~/.gol/config.json` showing its four values and
**defaults, not zeros, for the eight fields the file omits** — the JSON hazard
above, closed end to end in the real application.

**Not yet verified:** the interactive feel — navigation, adjusting a value, and
Back writing the file — has correct logic under test but has not been exercised
by a human. `GOL_SCREEN=config` was added to start directly on a screen, since
clicking through to one cannot be scripted.

**Files:** `src/GoL.Render/{MenuList,MenuInput,InputMap,TextRenderer,Palette,ConfirmPrompt}.cs`,
`src/GoL.Sim/{SimConfig,SimConfigStore}.cs`,
`src/GoL.App/{GolGame.cs,Config/ConfigField.cs}`,
`src/GoL.App/Screens/{GolScreen,MainMenuScreen,ConfigScreen,NotYetScreen}.cs`,
`tests/GoL.Ui.Tests/*`, `tests/GoL.Sim.Tests/SimConfig*Tests.cs`,
`docs/CONTROLS.md`.

**Gotchas found.**

- `MonoGame.Extended`'s `GameScreen` has no `Dispose(bool)` to override; release
  resources in `UnloadContent()`.
- `KeyboardExtended`/`MouseExtended` hold **static** prev/cur state.
  `Update()` must be called exactly once per frame, before `base.Update()` ticks
  the `ScreenManager` component. Zero calls and edges never fire (the menu looks
  dead); two calls and edges are lost intermittently (the menu looks like it
  skips keypresses).
- `TieredCompilation=false` was dropped from `GoL.App` — inherited from QiLight,
  where it avoids early-frame JIT stutter in a twitch arcade game. It also
  disables TieredPGO, and setting it on the app but not on `GoL.Headless` would
  mean the soak runner's performance numbers do not transfer to the real app.
- `GoL.Sim` sets `<ImplicitUsings>disable</ImplicitUsings>`. The solution-wide
  default puts `System.Linq` in scope in every core file, and the tick loop must
  not allocate; turning it off makes `using System.Linq;` a visible line in review.
