using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoGame.Extended;
using MonoGame.Extended.Screens;

namespace GoL.App.Screens;

/// <summary>
/// Step 0 checkpoint screen. It exists to prove four things at once, each of
/// which is otherwise only discoverable at runtime:
///   1. the MGCB-built font survived the dest/ output redirect (it loads at all);
///   2. the font's cmap covers digits and punctuation (they render, not as boxes);
///   3. MonoGame.Extended resolves and works on net8.0 (DrawCircle draws);
///   4. the window opens on macOS DesktopGL.
/// Replaced by the real main menu in Step 1.
/// </summary>
public sealed class SmokeTestScreen : GameScreen
{
    private static readonly Color Background = new(18, 20, 24);
    private static readonly Color Ink = new(214, 228, 214);
    private static readonly Color Accent = new(126, 196, 122);

    private SpriteBatch _batch = null!;
    private SpriteFont _font = null!;
    private float _time;

    public SmokeTestScreen(Game game) : base(game) { }

    public override void LoadContent()
    {
        base.LoadContent();
        _batch = new SpriteBatch(GraphicsDevice);
        _font = Content.Load<SpriteFont>("Fonts/UiFont");
    }

    public override void Update(GameTime gameTime)
        => _time += (float)gameTime.ElapsedGameTime.TotalSeconds;

    public override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(Background);

        // PointClamp + integer scales + whole-pixel positions: monogram is baked
        // at its native 16px grid, so any of the three going missing makes it mushy.
        _batch.Begin(samplerState: SamplerState.PointClamp);

        var center = new Vector2(
            GraphicsDevice.Viewport.Width / 2f,
            GraphicsDevice.Viewport.Height / 2f);

        // Digits and punctuation are the point of this line, not decoration:
        // QiLight's title font has a letters-only cmap and would render "AAA".
        DrawCentered("GoL 0123456789 !@#$%", center.Y - 90f, Ink, 3f);
        DrawCentered("step 0 - skeleton", center.Y - 50f, Ink * 0.6f, 2f);

        // A creature-sized circle, drawn by MonoGame.Extended. Proves the
        // package resolves on net8.0 despite shipping only a net6.0 lib.
        float pulse = 1f + 0.12f * MathF.Sin(_time * 2f);
        _batch.DrawCircle(center + new Vector2(0f, 40f), 46f * pulse, 48, Accent, 2f);
        _batch.DrawLine(center + new Vector2(0f, 40f), 46f * pulse, _time * 0.6f, Accent * 0.8f, 2f);

        _batch.End();
    }

    private void DrawCentered(string text, float y, Color color, float scale)
    {
        var pos = new Vector2(
            MathF.Round(GraphicsDevice.Viewport.Width / 2f - _font.MeasureString(text).X * scale / 2f),
            MathF.Round(y));
        _batch.DrawString(_font, text, pos, color, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
    }
}
