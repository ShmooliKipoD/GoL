using GoL.App.Config;
using GoL.Render;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using MonoGame.Extended.Input;

namespace GoL.App.Screens;

/// <summary>
/// Edits the live <see cref="GoL.Sim.SimConfig"/> and persists it on the way out.
/// Every simulation constant is reachable from here on purpose: the energy and
/// mutation numbers get retuned constantly, and a rebuild-per-tweak loop makes
/// balancing an evolutionary system impractical.
/// </summary>
public sealed class ConfigScreen : GolScreen
{
    private const float TitleScale = 4f;
    private const float ItemScale = 2f;

    private readonly MenuList _menu;
    private string _status = string.Empty;

    public ConfigScreen(GolGame game) : base(game)
    {
        var entries = new List<MenuEntry>();

        foreach (var field in ConfigFields.All)
        {
            var f = field; // capture per iteration
            entries.Add(new MenuEntry(
                Label: f.Label,
                ValueText: () => f.Format(Gol.Config),
                OnAdjust: direction =>
                {
                    Gol.Config = f.Step(Gol.Config, direction);
                    _status = string.Empty;
                }));
        }

        entries.Add(new MenuEntry("Back", GoBack));
        _menu = new MenuList(entries);
    }

    private void GoBack()
    {
        _status = Gol.ConfigStore.Save(Gol.Config) ? "saved" : "could not save";
        Gol.ShowScreen(new MainMenuScreen(Gol));
    }

    public override void Update(GameTime gameTime)
    {
        float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
        var input = InputMap.ReadMenu(KeyboardExtended.GetState());

        if (_menu.Update(dt, input)) return;
        if (input.Cancel) GoBack();
    }

    public override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(Palette.Background);
        BeginUi();

        float cx = ViewCenter.X;
        float titleHeight = Text.LineHeight(TitleScale);
        float itemHeight = Text.LineHeight(ItemScale);
        const float gap = 28f;

        float totalHeight = titleHeight + gap + _menu.Count * itemHeight;
        float y = MathF.Max(40f, (ViewHeight - totalHeight) / 2f);

        Text.DrawCentered("Configuration", cx, y, Palette.Ink, TitleScale);
        y += titleHeight + gap;

        float bottom = _menu.Draw(Text, cx, y, ItemScale);

        Text.DrawCentered("Left / Right - adjust     Enter - Back     Esc - Back",
            cx, bottom + 24f, Palette.InkDim, ItemScale);

        if (_status.Length > 0)
            Text.DrawCentered(_status, cx, bottom + 24f + itemHeight, Palette.Accent, ItemScale);

        EndUi();
    }
}
