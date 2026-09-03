# GoL — an evolving artificial-life simulator

A "game of life" that is closer to actual life than to Conway's. There are no
cellular-automaton rules. Instead the world is populated by **creatures**, each
driven by a small neural network — a graph of weighted connections — that reads
its senses and drives its body.

Creatures see through eyes with a range and a field-of-view cone, smell
pheromones through a nose with a radius, move forward and turn at speeds set by
their own genes, and eat plants or each other through a mouth with a limited
reach and arc. Everything costs energy. Run out and you die.

When a creature reproduces, its offspring's genome is mutated. Usually that
means a nudged weight or a slightly faster metabolism. Occasionally it adds a
neuron or a connection. **Rarely, it unlocks an attribute the lineage did not
previously have** — a second eye facing backwards, a gut that can digest a plant
nobody else can eat, resistance to a toxin, a scent gland. Each unlock registers
new sensor or effector nodes on the brain and adds its own metabolic upkeep, so
it has to earn its keep or selection removes it.

That last mechanism is the point of the project: the set of attributes in the
population is **open-ended and grows over generations**, rather than being fixed
when the game was written.

## Status

**Step 0 of 5 complete** — project skeleton building and running on macOS.

| Step | What | State |
|---|---|---|
| 0 | Project skeleton, build output, content pipeline | ✅ done |
| 1 | Game infrastructure: main menu, configuration, exit confirm | ⬜ next |
| 2 | A single creature: genome, brain, senses, energy, overlays | ⬜ |
| 3 | The board: camera, plants and their spread, pheromones, spatial index | ⬜ |
| 4 | Creatures on the board: population, reproduction, evolution, stats | ⬜ |

## Quick start (macOS)

MonoGame DesktopGL needs Homebrew's freetype at runtime, so
`DYLD_FALLBACK_LIBRARY_PATH` is required on every build and run.

```bash
brew install freetype        # one-time

DYLD_FALLBACK_LIBRARY_PATH=/opt/homebrew/lib:/usr/local/lib dotnet build
DYLD_FALLBACK_LIBRARY_PATH=/opt/homebrew/lib:/usr/local/lib dotnet run --project src/GoL.App

dotnet test                  # the core is headless - no env var, no window
```

In VS Code, press F5 (the launch config injects the variable for you).

## Layout

```
docs/     design and specification documents
src/      projects, by functionality
tools/    headless soak runner
tests/    unit tests
dest/     build output (gitignored)
```

| Project | Responsibility |
|---|---|
| `src/GoL.Sim` | The simulation. Genome, brain, world tick, plants, pheromones. **Headless** |
| `src/GoL.Render` | Drawing simulation state. No game logic |
| `src/GoL.App` | Window, screens, menu, configuration |
| `tools/GoL.Headless` | Long soak runs and determinism checks, with no graphics stack |
| `tests/GoL.Sim.Tests` | Unit tests over the core |

**`GoL.Sim` never references MonoGame.** That keeps the simulation testable
without a window, lets a 10 000-tick soak run anywhere, and keeps a graphics
library's statics and time sources out of a system that has to be
bit-reproducible from a seed. A unit test enforces it.

## Documentation

| Document | Contents |
|---|---|
| [`docs/SPEC.md`](docs/SPEC.md) | The owner's original brief, verbatim — the source of truth for scope |
| [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) | Project boundaries, the headless-core rule, tick order, determinism |
| [`docs/DESIGN.md`](docs/DESIGN.md) | Cumulative build log — one section per step: problem, approach, files |
| [`CLAUDE.md`](CLAUDE.md) | Conventions, build incantation, content-pipeline and font gotchas |

`docs/GENOME.md` (trait catalog, mutation operators, brain evaluation) and
`docs/CONTROLS.md` (key bindings and overlay shortcuts) arrive with Steps 1–2.

## Built with

[MonoGame](https://monogame.net) DesktopGL 3.8.1.303 ·
[MonoGame.Extended](https://github.com/craftworkgames/MonoGame.Extended) 4.0.0 ·
.NET 8 · macOS

Menu structure and the pixel-font treatment follow the sibling **QiLight**
project.
