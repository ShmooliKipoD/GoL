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

## Simulation overlays

*(Step 2. Reserved so the bindings stay stable while the features land.)*

| Key | Overlay |
|---|---|
| `V` | Vision cones — range and field of view per eye |
| `N` | Smell radius |
| `M` | Mouth reach and bite arc |
| `B` | Brain graph of the selected creature |
| `A` | Attribute panel of the selected creature |
| `G` | Plant and fertility heat map |
| `F1` | All overlays on/off, with a legend |

## Camera

*(Step 3.)*

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

`GOL_SCREEN=config` starts the game on the configuration screen instead of the
menu. macOS blocks synthetic keystrokes, so clicking through to a screen cannot
be scripted; this is how one gets looked at directly during development.
Unrecognised values fall back to the main menu.
