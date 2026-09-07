# Controls

`src/GoL.Render/InputMap.cs` is the one place **menu** bindings live — the keys that
drive a list, a prompt or an overlay. A screen's own keys are bound inline in that
screen, because they are not shared with anything: the sandbox's overlay toggles are
in `SandboxScreen`, the board's camera keys in `BoardScreen`. So is the mouse.

## Menus

| Key | Action |
|---|---|
| `Up` / `W` | Previous item (wraps to the last) |
| `Down` / `S` | Next item (wraps to the first) |
| `Left` / `A` | Decrease the selected setting |
| `Right` / `D` | Increase the selected setting |
| `Enter` / `Space` | Activate the selected item |
| `Esc` | Back — on the main menu it arms the exit prompt, and in the Sandbox it raises the pause overlay |

## Exit prompt

| Key | Action |
|---|---|
| `Y` | Quit |
| `Esc` | Quit — per the spec, Esc at this prompt exits |
| `N` | Cancel and return to the menu |

Note this differs from QiLight, which arms a three-second window and quits on a
second keypress. The spec asks for an explicit y/n prompt, so that is what this
is; the difference is deliberate.

## The Sandbox

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
| `R` | Rebuild the arena — bare ground, one fresh creature, same size |
| `Esc` | Pause overlay |

Forcing stands in for the brain's *wanting*, not for the creature's *body*: a forced
action still has to pass its own `CanStart`, so forcing Rest on a creature without the
Torpor attribute reads `blocked` rather than resting. Forced actions also do not fall
through to something else the way brain-chosen ones do — watching a pinned action fail
is the whole point of the key.

The actions panel shows the running action **and its status**: `Biting - blocked` is a
creature with its mouth open at nothing, `Biting` one that is getting somewhere.

The arena starts **empty**: one creature in the middle and nothing to eat. The greens
are yours to place.

| Mouse | Action |
|---|---|
| Left click | Send the creature there. A cross marks the spot |
| `P` + left click | Drop a green where you clicked |

The creature walks to the cross **once `Move` is what it is doing** — press `O` to pin
Move if its brain is busy chewing. Until then the order simply stands, and so does the
cross; it clears on arrival. Clicks outside the arena rectangle do nothing. The world
wraps, so a coordinate past the edge is technically valid, but honouring it would send
the creature off in the opposite direction to the one you pointed.

`P` is held rather than toggled on purpose: a placement mode needs its own indicator
and its own way out, and greens get placed in bursts and then not at all.

### The pause overlay

`Esc` raises it, and it is modal — the sim holds and no key underneath it fires.

| Row | Effect |
|---|---|
| Resume | Close it. `Esc` again does the same |
| Arena size | `Left` / `Right` cycles 240 / 340 / 680 / 1360 world units |
| Back to Main Menu | Leave the Sandbox |

**Changing the size rebuilds the arena**, so anything placed is gone. Carrying an
arrangement into different bounds would mean deciding what happens to the greens that
fall outside a smaller square, and there is no honest answer to that.

Starving does **not** rebuild. A new creature appears at the centre and everything you
placed stays exactly where it was — the arena is the experiment, and a creature dying
in it is a result rather than a reason to clear the table.

## The board

| Input | Action |
|---|---|
| Mouse wheel | Zoom **toward the cursor** — not the screen centre, or what you aim at slides away as you zoom in |
| `+` / `-` | Zoom about the centre |
| Arrows / `WASD`, middle-drag | Pan. Speed scales with zoom, so a pan covers the same amount of screen however far in you are |
| Left click | Select a creature and pin the inspector to it |
| `F` | Follow the selection. Wrap-aware, so following across the world seam does not whip the camera across the map |
| `V` `N` `M` `B` `A` `K` | The same inspector overlays as the Sandbox, on the selected creature |
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
| `GOL_SCREEN=sandbox` | Start in the Sandbox |
| `GOL_SCREEN=board` | Start on the populated board |
| `GOL_SANDBOX_TRAITS=n` | Give the Sandbox's creature `n` unlocked attributes at birth |
| `GOL_OVERLAYS=all` | Start with every overlay on, and keep the inspector pinned to a living creature — re-pinning when the subject dies, or the panels would vanish for good the first time one starved |

Unrecognised `GOL_SCREEN` values fall back to the main menu.
