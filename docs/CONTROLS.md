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
| `K` | Actions panel — every action the creature could take, with a signed bar; greyed when the genome cannot perform it. Also puts the current action's name above the creature |
| `G` | Pheromone field |
| `F1` | All overlays on/off |
| `U` | Force an attribute unlock, so the trait system is demonstrable without waiting for a 0.4% chance |
| `O` | Cycle the **forced action** — off, then each action in turn, named in the status line. Pins the creature to one behaviour so it can be watched in isolation, since an unevolved brain may simply never choose the one you want to see |
| `Space` | Pause / resume |
| `.` | Step one tick while paused |
| `+` / `-`, wheel | Zoom |
| `F` | Toggle follow-the-creature |
| `R` | Reset with a fresh creature |
| `Esc` | Back to the menu |

Forcing stands in for the brain's *wanting*, not for the creature's *body*: a forced
action still has to pass its own `CanStart`, so forcing Rest on a creature without the
Torpor attribute reads `blocked` rather than resting. Forced actions also do not fall
through to something else the way brain-chosen ones do — watching a pinned action fail
is the whole point of the key.

The actions panel shows the running action **and its status**: `Biting - blocked` is a
creature with its mouth open at nothing, `Biting` one that is getting somewhere.

## The board

| Input | Action |
|---|---|
| Mouse wheel | Zoom **toward the cursor** — not the screen centre, or what you aim at slides away as you zoom in |
| `+` / `-` | Zoom about the centre |
| Arrows / `WASD`, middle-drag | Pan. Speed scales with zoom, so a pan covers the same amount of screen however far in you are |
| Left click | Select a creature and pin the inspector to it |
| `F` | Follow the selection. Wrap-aware, so following across the world seam does not whip the camera across the map |
| `V` `N` `M` `B` `A` `K` | The same inspector overlays as the lab, on the selected creature |
| `G` | Soil fertility, drawn beneath everything |
| `H` | Scent, tinted per channel |
| `F1` | All overlays on/off |
| `Space` | Pause / resume |
| `.` | Step one tick while paused |
| `1` / `2` / `3` | Run at 1x / 4x / 16x |
| `Esc` | Back to the menu |

Soil and scent are on **separate keys** deliberately: they answer different
questions, and stacking both translucent layers muddies each of them.

## Development

macOS blocks synthetic keystrokes, so clicking through to a screen cannot be
scripted. These environment variables are how a screen gets looked at directly.

| Variable | Effect |
|---|---|
| `GOL_SCREEN=config` | Start on the configuration screen |
| `GOL_SCREEN=lab` | Start in the Creature Lab |
| `GOL_SCREEN=board` | Start on the populated board |
| `GOL_LAB_TRAITS=n` | Give the lab's creature `n` unlocked attributes at birth |
| `GOL_OVERLAYS=all` | Start with every overlay on, and keep the inspector pinned to a living creature — re-pinning when the subject dies, or the panels would vanish for good the first time one starved |

Unrecognised `GOL_SCREEN` values fall back to the main menu.
