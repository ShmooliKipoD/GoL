# Controls

Key bindings live in one place in code: `src/GoL.Render/InputMap.cs`.

## Menus

| Key | Action |
|---|---|
| `Up` / `W` | Previous item (wraps to the last) |
| `Down` / `S` | Next item (wraps to the first) |
| `Left` / `A` | Decrease the selected setting |
| `Right` / `D` | Increase the selected setting |
| `Enter` / `Space` | Activate the selected item |
| `Esc` | Back — and on the main menu, arm the exit prompt |

## Exit prompt

| Key | Action |
|---|---|
| `Y` | Quit |
| `Esc` | Quit — per the spec, Esc at this prompt exits |
| `N` | Cancel and return to the menu |

Note this differs from QiLight, which arms a three-second window and quits on a
second keypress. The spec asks for an explicit y/n prompt, so that is what this
is; the difference is deliberate.

## Creature Lab

| Key | Action |
|---|---|
| `V` | Vision cones — the cone outline plus one ray per bin, drawn to whatever that bin actually hit (green = plant, red = creature, faint = nothing) |
| `N` | Smell — a dashed ring at the nose radius, plus a gradient arrow once `ScentGradient` is unlocked |
| `M` | Mouth — the bite arc swept to full reach, plus a line to whatever is in range now |
| `B` | Brain graph — sensors left, hidden by depth, effectors right; green edges excitatory, red inhibitory, node brightness is live activation |
| `A` | Attribute panel — every trait with a bar, then the attributes this lineage has acquired |
| `G` | Pheromone field |
| `F1` | All overlays on/off |
| `U` | Force an attribute unlock, so the trait system is demonstrable without waiting for a 0.4% chance |
| `Space` | Pause / resume |
| `.` | Step one tick while paused |
| `+` / `-`, wheel | Zoom |
| `F` | Toggle follow-the-creature |
| `R` | Reset with a fresh creature |
| `Esc` | Back to the menu |

## Camera

*(Step 3 — the full board camera. The lab has its own simpler zoom, above.)*

| Input | Action |
|---|---|
| Mouse wheel, `+` / `-` | Zoom toward the cursor |
| Arrows / `WASD`, middle-drag | Pan |
| Left click | Select a creature and pin its overlays to it |

## Simulation speed

*(Step 4.)*

| Key | Action |
|---|---|
| `Space` | Pause / resume |
| `.` | Step one tick while paused |
| `1` / `2` / `3` | Run at 1x / 4x / 16x |

## Development

macOS blocks synthetic keystrokes, so clicking through to a screen cannot be
scripted. These environment variables are how a screen gets looked at directly.

| Variable | Effect |
|---|---|
| `GOL_SCREEN=config` | Start on the configuration screen |
| `GOL_SCREEN=lab` | Start in the Creature Lab |
| `GOL_LAB_TRAITS=n` | Give the lab's creature `n` unlocked attributes at birth |

Unrecognised `GOL_SCREEN` values fall back to the main menu.
