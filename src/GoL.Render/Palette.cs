using Microsoft.Xna.Framework;

namespace GoL.Render;

/// <summary>
/// The single source of colour for the whole game. A muted, slightly desaturated
/// set - creatures and plants need to read as distinct organisms against it for
/// hours at a time, so the chrome stays quiet.
/// </summary>
public static class Palette
{
    public static readonly Color Background = new(18, 20, 24);
    public static readonly Color Panel = new(26, 30, 35);

    public static readonly Color Ink = new(214, 228, 214);
    public static readonly Color InkDim = new(120, 132, 128);
    public static readonly Color Accent = new(126, 196, 122);
    public static readonly Color Warning = new(224, 152, 92);
    public static readonly Color Danger = new(214, 102, 96);
}
