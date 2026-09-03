using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace GoL.Render;

/// <summary>
/// Pixel-font text drawing. Adapted from QiLight's GameRenderer text helpers.
/// <para>
/// monogram is baked at its native 16px em grid, so keeping it crisp requires
/// three things together: <see cref="SamplerState.PointClamp"/> sampling,
/// integer draw scales, and whole-pixel positions. Drop any one of the three and
/// the glyph grid samples unevenly - some pixel rows double, others vanish - and
/// the text goes visibly mushy. Every method here rounds its positions for you.
/// </para>
/// </summary>
public sealed class TextRenderer
{
    private const float ShadowAlpha = 0.55f;
    private static readonly Vector2 ShadowOffset = new(2f, 2f);

    private readonly SpriteBatch _batch;
    private readonly SpriteFont _font;

    public TextRenderer(SpriteBatch batch, SpriteFont font)
    {
        _batch = batch;
        _font = font;
    }

    /// <summary>Height of one line at the given scale. Uses a fixed probe string
    /// so the value does not jitter with the text's ascenders/descenders.</summary>
    public float LineHeight(float scale) => _font.MeasureString("Ag").Y * scale;

    public Vector2 Measure(string text, float scale) => _font.MeasureString(text) * scale;

    /// <summary>Top-left position that centres <paramref name="text"/> on
    /// <paramref name="centerX"/>, snapped to whole pixels.</summary>
    public Vector2 CenteredAt(string text, float centerX, float y, float scale) =>
        new(MathF.Round(centerX - _font.MeasureString(text).X * scale / 2f), MathF.Round(y));

    public void Draw(string text, Vector2 pos, Color color, float scale = 1f)
    {
        pos = new Vector2(MathF.Round(pos.X), MathF.Round(pos.Y));
        _batch.DrawString(_font, text, pos, color, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
    }

    /// <summary>
    /// QiLight's FlyingText treatment: a dark drop shadow that stays put while the
    /// crisp label is displaced by <paramref name="bob"/>. Only the selected menu
    /// item bobs, so the shadow staying still is what sells the lift.
    /// </summary>
    public void DrawShadowed(string text, Vector2 pos, Color color, float scale, Vector2 bob = default)
    {
        pos = new Vector2(MathF.Round(pos.X), MathF.Round(pos.Y));
        _batch.DrawString(_font, text, pos + ShadowOffset, Color.Black * ShadowAlpha,
            0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
        _batch.DrawString(_font, text, pos + bob, color,
            0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
    }

    public void DrawCentered(string text, float centerX, float y, Color color, float scale = 1f)
        => Draw(text, CenteredAt(text, centerX, y, scale), color, scale);

    public void DrawCenteredShadowed(string text, float centerX, float y, Color color,
        float scale, Vector2 bob = default)
        => DrawShadowed(text, CenteredAt(text, centerX, y, scale), color, scale, bob);
}
