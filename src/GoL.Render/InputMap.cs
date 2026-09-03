using Microsoft.Xna.Framework.Input;
using MonoGame.Extended.Input;

namespace GoL.Render;

/// <summary>Translates the frame's keyboard edges into a <see cref="MenuInput"/>.
/// The single place key bindings are named; see docs/CONTROLS.md.</summary>
public static class InputMap
{
    public static MenuInput ReadMenu(KeyboardStateExtended kb) => new(
        Up: kb.WasKeyPressed(Keys.Up) || kb.WasKeyPressed(Keys.W),
        Down: kb.WasKeyPressed(Keys.Down) || kb.WasKeyPressed(Keys.S),
        Left: kb.WasKeyPressed(Keys.Left) || kb.WasKeyPressed(Keys.A),
        Right: kb.WasKeyPressed(Keys.Right) || kb.WasKeyPressed(Keys.D),
        Accept: kb.WasKeyPressed(Keys.Enter) || kb.WasKeyPressed(Keys.Space),
        Cancel: kb.WasKeyPressed(Keys.Escape),
        Yes: kb.WasKeyPressed(Keys.Y),
        No: kb.WasKeyPressed(Keys.N));
}
