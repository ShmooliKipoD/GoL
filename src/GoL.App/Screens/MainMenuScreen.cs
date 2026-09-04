using GoL.Render;
using Microsoft.Xna.Framework;
using MonoGame.Extended.Input;

namespace GoL.App.Screens;

/// <summary>New Game / Configuration / Exit, per the spec.</summary>
public sealed class MainMenuScreen : GolScreen
{
    private const float TitleScale = 5f;
    private const float ItemScale = 3f;
    private const float SubtitleScale = 2f;

    private readonly MenuList _menu;
    private readonly ConfirmPrompt _exitPrompt;

    public MainMenuScreen(GolGame game) : base(game)
    {
        _exitPrompt = new ConfirmPrompt("Exit GoL?", () => Gol.Exit());

        _menu = new MenuList(new[]
        {
            new MenuEntry("New Game", () => Gol.ShowScreen(new BoardScreen(Gol))),
            new MenuEntry("Creature Lab", OnNewGame),
            new MenuEntry("Configuration", () => Gol.ShowScreen(new ConfigScreen(Gol))),
            new MenuEntry("Exit", _exitPrompt.Open),
        });
    }

    private void OnNewGame()
    {
        Gol.ShowScreen(new CreatureLabScreen(Gol));
    }

    public override void Update(GameTime gameTime)
    {
        float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
        var input = InputMap.ReadMenu(KeyboardExtended.GetState());

        if (_exitPrompt.Update(input)) return;
        if (_menu.Update(dt, input)) return;

        // Esc from the menu arms the same prompt as the Exit item, and moves the
        // cursor there so the menu shows what is armed.
        if (input.Cancel)
        {
            _menu.SelectLast();
            _exitPrompt.Open();
        }
    }

    public override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(Palette.Background);
        BeginUi();

        float cx = ViewCenter.X;
        float titleHeight = Text.LineHeight(TitleScale);
        float itemHeight = Text.LineHeight(ItemScale);
        const float gap = 40f;

        float totalHeight = titleHeight + gap + _menu.Count * itemHeight;
        float y = (ViewHeight - totalHeight) / 2f;

        Text.DrawCentered("GoL", cx, y, Palette.Accent, TitleScale);
        Text.DrawCentered("evolving artificial life", cx,
            y + titleHeight, Palette.InkDim, SubtitleScale);

        y += titleHeight + gap;
        _menu.Draw(Text, cx, y, ItemScale);

        _exitPrompt.Draw(Batch, Text, cx, ViewCenter.Y);

        EndUi();
    }
}
