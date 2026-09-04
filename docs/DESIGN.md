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

---

## 3. Step 2 — The creature

**Problem.** The spec describes a creature as a circle with eyes (range, field of
view), a nose (pheromone radius), movement (speed, agility, costing energy) and a
mouth (eats plants and creatures) — plus, separately, that *"new attributes will be
added over time by birth of new mutations"*.

Those two requirements pull hard against each other. The obvious reading of the
bullet list is a class with fixed fields — `float Speed; float VisionRange;` — and
that reading makes the mutation system a rewrite of the creature, the renderer and
every overlay the moment an attribute nobody planned for appears.

**Approach.**

- **The creature is a genome, not a struct of named fields.** Traits are a
  `float[]` indexed by `TraitAxis`; acquired attributes are a `ulong` bitmask.
  Mutation perturbs "some trait" and the overlay lists "every trait", and both
  simply iterate. `docs/GENOME.md` has the full design.
- **Node ids are derived from the trait catalog, not allocated by a counter**
  (`Sensor(slot, channel) = base + slot*32 + channel`). A given sensor therefore has
  the same id in every genome in every run, with no registry to keep in sync — and
  unlocking an attribute is just inserting the ids it declares.
- **Evaluability is structural.** No mutation removes a node; connections are only
  made between existing nodes; adding a neuron splits an existing connection. A
  dangling reference is unconstructible, so there is no validation pass that could
  itself be buggy.
- **Brain evaluation is a single pass over the previous tick's activations.** This
  makes cycles and self-loops legal by construction — no topological sort to get
  wrong, no cycle detection to keep correct across every mutation operator — at a
  cost of one tick of propagation delay per layer.
- **An unlock wires itself in immediately.** Every new sensor gets an outgoing edge
  and every new effector an incoming one. Otherwise the attribute costs upkeep while
  being invisible to the brain, and selection deletes it before it can ever be
  useful — the standard reason this kind of system quietly does nothing.
- **One overlay renderer iterating the genome**, not one per body part. When a
  mutation unlocks an attribute the game has never displayed, it appears in the
  panel and its nodes appear in the brain graph with no rendering code written for
  it. Verified: the screenshot below shows Cellulose gut and Toxin resistance listed,
  and the brain grown from 36n/46c to 41n/55c, without a line of trait-specific
  drawing.

**The Step 2 / Step 3 seam.** A creature senses plants and pheromones, both of
which are Step 3 work. Rather than reorder the spec's steps, `ISenseField` defines
the sensing surface and `LabWorld` backs it with brute-force scans over a dozen
hand-placed plants and a pheromone grid that decays but does not diffuse. Step 3
replaces the implementation. **If that swap requires touching creature code, the
interface was drawn in the wrong place and the interface is what should change.**

**Appearance**, chosen by the owner, each decision one the body makes a trait
visible rather than decoration:

- **Body**: filled disc with a darker rim, plus a brighter inner core. The core's
  *area* — not its radius — is proportional to energy; the eye judges discs by area,
  so a radius-linear core reads as far emptier than the creature is.
- **Colour**: hue is the inherited `Hue` trait, so a lineage is recognisable on
  sight and can be watched spreading; brightness carries energy, but gently, so it
  does not fight the core for attention. Hue is also what `ColorVision` reads, which
  makes body colour a signalling channel evolution can exploit.
- **Eyes**: two dots on the forward rim, separated by the field-of-view angle — a
  wide-FOV creature visibly has wide-set eyes. They are also the only heading cue;
  nothing else marks facing. A rear pair appears if `RearEye` is unlocked.
- **Nose**: nothing on the body. A third mark would compete with the eyes and core
  on a creature that is often a few pixels across; smell lives in the `N` overlay.
- **Mouth**: a warm band on the rim spanning the bite arc, flaring bright while
  feeding — so eating is watchable rather than inferred from a number. Tinted rather
  than merely darkened, or it reads as "the rim, slightly shaded".

**Verified.** 65 tests. The ones that earn their keep: 3000 chained mutations at
inflated rates, every result still compiling to a brain with finite bounded outputs;
a self-loop weighted 50 staying bounded over 10 000 ticks; every one of the twelve
attributes adding exactly the nodes it declares *and* having them wired in;
prerequisites never violated by a random unlock; vision respecting range, field of
view, occlusion and toroidal wrapping; and the energy relationships that must hold
for selection to mean anything (size costs, attributes cost, doubling speed costs
more than double).

Visually confirmed in the lab: a creature moving under brain control, its vision
rays terminating on a plant, the mouth band, the energy core, and the attribute
panel listing acquired traits.

**Files:** `src/GoL.Sim/Genetics/{TraitAxis,NodeIds,LatentTraits,Genome,Mutator}.cs`,
`src/GoL.Sim/Brains/Brain.cs`,
`src/GoL.Sim/Core/{Pcg32,Creature,Eye,ISenseField,Senses,SensorLayout,Metabolism,Locomotion,LabWorld}.cs`,
`src/GoL.Render/{CreatureRenderer,SenseOverlayRenderer,InspectorRenderer,OverlayFlags}.cs`,
`src/GoL.App/Screens/CreatureLabScreen.cs`, `docs/GENOME.md`,
`tests/GoL.Sim.Tests/{GenomeMutation,LatentTrait,Sensing,Metabolism}Tests.cs`.

**Gotchas found.**

- `kill` on a backgrounded `dotnet run` kills the wrapper, not the game — a stale
  window survived and was screenshotted as if it were the new build. Use
  `pkill -f GoL.App`.
- Every sensing query writes into a caller-owned span and returns a count. The
  scratch buffer grows when a result fills it exactly, which is the only signal the
  field has that it may have truncated — silent truncation would blind a creature to
  whatever sorted last.
- Distances must go through `ISenseField.Offset`, never plain subtraction, or
  creatures go blind at the world seam. There is a test for exactly this.

**Open risk.** A newly unlocked attribute arrives with random wiring and immediate
upkeep, so it is usually worse than its parent and gets selected straight back out.
This is the most likely reason the headline feature could appear not to work once
populations run in Step 4. Mitigations, in escalation order, are in `docs/GENOME.md`.

---

## 4. Step 3a — The simulation on ECS

**Problem.** The owner asked for ECS. The simulation was a `Creature` class with
fields and a hand-rolled loop, and the core carried a rule that it must never
reference MonoGame — which appeared to rule out `MonoGame.Extended.ECS`.

**Approach.** Test the assumption rather than argue from it. Four experiments,
each recorded in `CLAUDE.md`:

1. Extended's ECS **runs fully headless** — no `GraphicsDevice`, no window, no
   `DYLD` variable. Referencing `MonoGame.Framework` does not force graphics
   initialisation, so the objection was unfounded.
2. `world.Update()` **allocates zero bytes** per tick once warmed.
3. `ActiveEntities` iterates by **ascending entity id**, not insertion order.
   Proven by destroying low ids and recreating: the replacements came back as ids
   `1, 0` from a LIFO free list yet still iterated `0,1,2,3,4,5`. This is what
   makes a seeded run reproducible, and it is undocumented upstream.
4. **Components must be reference types** — `ComponentMapper<T>` constrains
   `T : class` — so births allocate. Bounded and asserted at under 4 KB/tick.

The first ordering test was inconclusive: both runs matched only because id
recycling happened to hand back the same ids, which any iteration scheme would
satisfy. Redone with low-id churn so insertion order and id order actually differ.

The rule was **restated, not relaxed**: not "never references MonoGame" but
"never *needs* graphics to run". `tools/GoL.Headless` is the standing proof — it
references only `GoL.Sim` and is run without the `DYLD` variable.

**Verification.** The 65 existing tests were the oracle. All the behavioural ones
passed unchanged; the single failure was the architecture test asserting the rule
this change deliberately replaced.

**Caught in review of my own design:** `SenseSystem` and `ThinkSystem` would have
shared one scratch buffer, leaving only the last creature's readings by the time
thinking began. Sensor and effector vectors now live on the `Mind` component.

**Scene graphs were considered and rejected on fact:** `SceneGraph`, `SceneNode`
and `SpriteEntity` are documented on monogameextended.net but **do not exist in
MonoGame.Extended 4.0.0** — verified against the shipped assembly.

**Files:** `src/GoL.Sim/Components/`, `src/GoL.Sim/Systems/`,
`src/GoL.Sim/Core/{SenseSubject,CreatureView,IEnvironment,LabEnvironment}.cs`,
`src/GoL.Sim/GlobalUsings.cs`, `tools/GoL.Headless/Program.cs`,
`tests/GoL.Sim.Tests/{Architecture,Determinism}Tests.cs`.

**Gotcha found.** Referencing MonoGame made `Vector2` ambiguous in the core. A
global using alias pins it to `System.Numerics`, so the ambiguity can never be
resolved the wrong way by accident.

---

## 5. Step 3b — The board

**Problem.** The spec asks for a camera that zooms and pans, and for a board that
generates greens — noting explicitly that the types and how they spread needed
thought.

**Approach.**

- **Four kinds, each with a job.** Grass is the staple. Fruit is scarce, rich and
  grows only on good soil. **Bramble is the locked niche** — it colonises exhausted
  ground nothing else will take, and is edible only with a `CelluloseGut`, so a
  population that never unlocks that attribute watches bramble take the map.
  Blightcap is high-energy and poisonous without `ToxinResistance`, and grows only
  on poor soil, so it rewards going where food is scarce. Carrion makes `Carnivory`
  pay off before a lineage can reliably hunt.
- **Fertility from value noise**, drained by plants and regrowing logistically.
  Without non-uniform terrain there is nowhere worth travelling to, and spatial
  strategy cannot evolve because there is no space worth strategising about.
- **Spread** is per-cell: a mature plant seeds one of eight neighbours, if that
  cell is empty and the soil suits its kind. Seedlings start small and must mature
  before spreading on.
- **Camera** is hand-rolled rather than Extended's `OrthographicCamera`, for one
  reason: this world wraps, and following a creature across the seam needs the view
  to move continuously rather than jump the width of the map.

**Performance was the real work here.** The first board ran at 175 ticks/s against
the lab's 1661. Rather than guess, cost was measured against creature count, which
showed it scaling linearly — a per-creature cost. Two fixes, in order:

1. **Vision was gathering every plant in range** — a ~1200-cell square sweep per
   creature per tick. Replaced with a DDA ray march, one ray per vision bin. Cost
   now tracks how far the ray travels, and nearest-hit-per-bin and occlusion come
   for free.
2. That exposed a large **fixed** cost: scent diffusion calling a wrapping helper
   for all four neighbours of every cell. Split into an interior fast path and an
   edge path, with buffer swapping instead of copying.

Result at 500 creatures: **107 ticks/s**, against the 60 real time needs.

**Appearance**, chosen by the owner: each plant kind gets its own colour *and*
silhouette, with radius tracking remaining energy so a grazed patch visibly thins.
Colour alone was rejected because grass and bramble are both green — and those two
are exactly the pair that must be distinguishable. Soil and scent got separate
keys (`G`, `H`) rather than one combined overlay, since they answer different
questions and two translucent layers muddy each other.

**Verified.** 83 tests. New ones cover the locked niche (bramble worthless without
the gut, food with it), toxin resistance, carrion digestion, regrowth after
grazing, colonisation of empty ground over 4000 ticks, scent diffusing and
decaying, spatial-hash results arriving in id order and wrapping across the seam,
and the ray march stopping at the nearest plant rather than the far one.

**Not yet verified: the board's appearance.** The display was asleep for the whole
of this step and `screencapture` could not produce an image. The board was run for
12 s with `GOL_OVERLAYS=all`, which exercises every draw path including plants,
fertility, scent, the inspector and the brain graph, with no exception — but
nobody has looked at it. Worth a glance before Step 4 builds on it.

**Files:** `src/GoL.Sim/Board/{SpatialHash,Plants,PlantGrid,FertilityField,PheromoneField,BoardEnvironment}.cs`,
`src/GoL.Render/{BoardRenderer,BoardCamera}.cs`,
`src/GoL.App/Screens/BoardScreen.cs`, `tests/GoL.Sim.Tests/BoardTests.cs`.

---

## 6. Step 3c — Creature actions: a readout, and Torpor

**Problem.** Before populations run, the owner asked to review the creature: what
actions can it take, a way to list them all, and a way to see what it is doing now.

The review answer came first, because it shaped everything else. Reading
`InnateAction` and `Intent`: the creature had exactly **seven** actions — Thrust,
Turn, Bite, Reproduce innately, plus Sprint, Scent A and Scent B once the matching
attribute is unlocked. **No sleep, no flee, no hide.**

And a deeper point than the missing entries: the brain is a *continuous controller,
not a planner*. It emits a thrust every tick and never decides to flee.

**The design turned on one correction from the owner.** The first plan listed
`Chasing` and `Fleeing` as separate actions, recognised by a heuristic — "reversing
while a creature is in view". The owner rejected it in one line:

> Fleeing and chasing are the same action only with negative value.

That is right, and it collapses the whole design. Both are `Move`: chase is positive
thrust, flee is negative. Left and right are likewise one signed `Turn`. Modelling
them apart would invent a distinction the controller does not have, and would then
*need* the heuristic to sustain it.

So the vocabulary became **the effector set itself** — `CreatureAction` mirrors
`InnateAction` and the latent effector channels, one entry each, carrying a signed
value read straight off `Intent`. Three things fell out of that:

- No inference anywhere. Every row is a number the brain actually produced, so the
  readout cannot drift from the real model.
- The "current" action is simply the greatest magnitude, ties broken in a fixed
  documented order (discrete events ahead of continuous ones) — rather than a
  hand-ranked list of interpretations.
- The soak histogram became a direct distribution over controller output, with no
  predicate in between that could be miscalibrated. That mattered: a badly tuned
  `Chasing` predicate would have flat-lined the histogram in exactly the way a
  broken brain does, and the two would have been indistinguishable.

`Idle` is deliberately **not** an enum member — it is the absence of an action.

**Torpor** was added as the thirteenth latent attribute, so the one genuinely new
action arrives by mutation like every other rather than being granted. It is the
only action that *saves* energy, so it carries three costs rather than one: running
cost ×0.35, thrust and turn ×0.15, eye range ×0.5. Each is pinned by its own test,
so a regression names which half of the trade broke. Its own upkeep is charged in
full even while resting — an attribute that paid for itself whenever it was used
could never be selected against.

The eye dulling reads *last* tick's gate, because sensing runs before thinking. That
is the right way round: a creature cannot look first and then close its eyes.

**Structure.** A new `Behaviour` component and an `ActionSystem` running **last**,
after `LifecycleSystem`, so a birth is read from a stamp lifecycle just wrote rather
than by re-deriving the reproduction predicate and risking the two disagreeing. That
stamp is a new `Vitals.LastBirthTick` (long) rather than comparing the existing
float `LastBirthTime` against `SimTime`: float equality standing in for "did this
happen this tick" is exact today but silently becomes "never" the moment simulation
time accumulates differently.

`ActionSystem` writes only to `Behaviour`, and nothing in the simulation reads it
back — otherwise the readout would start driving behaviour instead of describing it.
It also skips the dead, which makes it moot whether an entity destroyed by
`LifecycleSystem` is still visible to a later system in the same `world.Update()`,
something Extended does not document.

**Display.** A panel on `K` with a signed, centre-anchored bar per action, so `Move`
reads as one axis with chase at one end and flee at the other; locked rows greyed
rather than hidden, so the panel answers "what could this lineage acquire?" too. Plus
the current action's name above the creature, in **screen space** — a world-space
label would scale with zoom and be unreadable at both ends. `DrawActionLabel` takes
an already-computed screen position because the board projects through
`BoardCamera.WorldToScreen` while the lab has no camera at all and draws through a
plain scale-and-offset matrix.

**Verified.** 100 tests, up from 84. The strongest evidence was not a test:
**the world hash and the entire population trajectory of a 30 000-tick run are
byte-identical before and after this step** (`49BF88CF3354BE3E`), confirming the
readout is inert and Torpor changes nothing until a lineage unlocks it.

The soak runner gained a behaviour histogram. Its latent rows read 0.00% forever on
a normal run, because 6000 ticks unlock nothing — and a row that is always zero is
indistinguishable from a row that is broken. So `--unlock-all` was added to grant the
starting population every attribute purely to prove them. With it: Sprint 1.94%,
Scent A 0.17%, Scent B 0.10%, **Torpor 3.63%**, idle 0.11%.

**The board was finally looked at.** The display was awake this time, and Step 3b's
outstanding visual check is now closed — plants, soil patches, creatures, HUD and the
inspector panels all render correctly. Two defects were found and fixed only because
of it:

- The board's auto-select branch was **dead**: it ran in the constructor, but
  `Living` is published by `LifecycleSystem` and so is still empty there. Moved to
  after the first step, and made to re-pin when the subject dies — otherwise the
  panels vanished for good the first time one starved. Scoped to `GOL_OVERLAYS`, since
  silently jumping to a new creature would be surprising during normal play.
- Locked rows read `needs Sprint gland` and overran the name column. Shortened to
  `locked`; the attribute panel already answers what a creature has.

**Known, unchanged:** population still collapses over long runs (120 → 0 by tick
27 000). That is pre-existing — the byte-identical baseline proves this step did not
cause it — and it is precisely what Step 4 exists to tune. One cosmetic issue is
noted but not fixed: with all thirteen attributes unlocked the attribute panel is
taller than the window and overflows. It is only reachable through `GOL_LAB_TRAITS`.

**Files:** `src/GoL.Sim/Core/CreatureAction.cs`, `src/GoL.Sim/Systems/ActionSystem.cs`
(new); `src/GoL.Sim/Components/Components.cs`, `Core/{Locomotion,Metabolism,Senses,CreatureView}.cs`,
`Genetics/LatentTraits.cs`, `Systems/{SimWorld,EnergySystem}.cs`;
`src/GoL.Render/{InspectorRenderer,OverlayFlags}.cs`;
`src/GoL.App/Screens/{Board,CreatureLab}Screen.cs`;
`tools/GoL.Headless/Program.cs`; `tests/GoL.Sim.Tests/{ActionTests,MetabolismTests}.cs`.

---

## 7. Step 3d — Greens get eaten, eating pays, and the intake is visible

**Problem.** Watching a creature in the Creature Lab, the owner saw it detect a
green, close, bite — and its energy kept falling. *"He does not gain any calories."*
And: *"green should also be consumed — when all calories are eaten, the green should
disappear."*

Both bite paths already credited energy, so nothing was plainly broken. Three things
combined.

**1. An eaten green never went away**, and that was deliberate. `PlantGrid.Consume`
said so outright — *"a living plant regrows from its roots"* — keeping the cell as
`Grass` at zero energy. The lab was worse: `Step` regrew *every* plant in the list
including dead ones. The result was an invisible zombie green a creature could sit
on forever, grazing regrowth at 0.66/s against 1.17/s of upkeep. Now a stripped cell
is cleared, for every kind.

**2. The gain was invisible.** One tick of biting grass credits 0.20 energy, and the
panel prints energy as a whole number out of 193. Feeding could not move the display
at all. Nothing anywhere answered "is this creature taking in calories?"

**3. Density, not per-plant value, was the real constraint.** This was the finding
that mattered, and it only appeared once the intake readout existed. In the lab,
measured intake was **0.00/s across every seed tried** — creatures were not eating
*at all*, then starving in about two minutes. The board works because it is roughly
**seventy times denser** in plants per unit area. Fourteen morsels in a 340-unit
arena is not a food supply an untrained brain can find. The lab now seeds 240.

**A pre-existing bug surfaced on the way.** The plan said to read the new fertility
column first, and it read 0.02 — soil pinned at nothing. Running with **zero
creatures** proved it had nothing to do with grazing: mean fertility still fell from
0.5 to 0.02 in 4500 ticks. Two causes, both real:

- `Drain` was called per *plant* cell but applied to the coarser fertility grid with
  no scaling, so four plant cells each charged the full amount — a unit error, now
  scaled by the area ratio;
- `RegrowthRate` was 0.02, and a cell settles at `ceiling - drain/RegrowthRate`, so
  any drain above ~0.012/s pinned it at zero. Grass drained 0.35.

That mattered more than it looks: fruit needs `MinFertility 0.55`, so on dead soil
**fruit went permanently extinct within a minute**, the fertility sensor read zero
everywhere, and the value-noise terrain the board is built on meant nothing. Soil now
settles around 0.47.

**Accounting.** Both environments duplicated the clamp-and-credit; both now go
through `Energy.Gain`, which clamps, records `LifetimeIntake`, and credits what was
absorbed rather than what was offered. `IntakeRate` is smoothed, because the raw
per-tick value is 0.20 or 0 and flickers uselessly. The inspector gained a line —
`+2.4/s   eaten 118`, coloured by sign — and the soak runner gained intake, net,
live plant count and mean fertility columns.

**Result.** The lab, which was the actual complaint, now works: 8 creatures become
75 over 20 000 ticks, generation 4, intake climbing to 2.36/s and net positive —
selection visibly favouring creatures that eat. On the board, the population no
longer collapses: it used to reach **zero by tick 27 000**, and now grows.

**What is not fixed, and is deliberately left for Step 4.** It now grows *without
bound* — 120 → 1755 by tick 20 000 — with vegetation carpeting the map. I traded a
collapse for a runaway. Four levers were tried against it (plant energy, growth
rate, spread chance, a soil-exhaustion die-off) and each moved the balance without
bounding it, at which point this was plainly the population-balance work Step 4
exists for rather than something to smuggle in here. The die-off mechanism is kept
and directly tested, but on typical soil it rarely fires.

**Verified.** 112 tests, up from 101. New ones pin that a fully eaten green is
removed and a partly eaten one is not, that intake and energy cannot disagree, that
scraps are not chosen over a meal, that a stripped world recovers, that ambient
seeding never produces carrion, and that the lab holds its plant count. One existing
test, `PlantsGrowBack_AfterBeingGrazed`, encoded the old contract and was updated to
the new one.

**Files:** `src/GoL.Sim/Board/{PlantGrid,Plants,FertilityField,BoardEnvironment}.cs`,
`src/GoL.Sim/Core/{LabEnvironment,Metabolism,CreatureView}.cs`,
`src/GoL.Sim/Components/Components.cs`, `src/GoL.Sim/Systems/{Sense,Energy}System.cs`,
`src/GoL.Sim/SimConfig.cs`, `src/GoL.Render/InspectorRenderer.cs`,
`src/GoL.App/Screens/CreatureLabScreen.cs`, `tools/GoL.Headless/Program.cs`,
`tests/GoL.Sim.Tests/{FeedingTests,BoardTests}.cs`.
