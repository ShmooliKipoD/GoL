using GoL.Render;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using MonoGame.Extended.Input;

namespace GoL.App.Screens;

/// <summary>
/// Placeholder destination for menu items whose real screen lands in a later
/// step. Exists so the menu's navigation is fully exercisable now - a dead
/// button is not a testable menu.
/// </summary>
public sealed class NotYetScreen : GolScreen
{
    private readonly string _title;
    private readonly string _detail;

    public NotYetScreen(GolGame game, string title, string detail) : base(game)
    {
        _title = title;
        _detail = detail;
    }

    public override void Update(GameTime gameTime)
    {
        var input = InputMap.ReadMenu(KeyboardExtended.GetState());
        if (input.Cancel || input.Accept)
            Gol.ShowScreen(new MainMenuScreen(Gol));
    }

    public override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(Palette.Background);
        BeginUi();

        float cx = ViewCenter.X;
        float y = ViewCenter.Y - 60f;

        Text.DrawCentered(_title, cx, y, Palette.Ink, 4f);
        Text.DrawCentered(_detail, cx, y + Text.LineHeight(4f) + 12f, Palette.InkDim, 2f);
        Text.DrawCentered("Esc - back", cx, y + Text.LineHeight(4f) + 70f, Palette.InkDim, 2f);

        EndUi();
    }
}
