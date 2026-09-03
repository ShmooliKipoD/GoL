using System;

namespace GoL.Render;

/// <summary>
/// Which inspection overlays are on. One bitfield and one renderer, rather than a
/// bespoke drawing path per body part: every overlay reads the same genome, so
/// adding a trait must not mean adding a fifth way to draw one.
/// </summary>
[Flags]
public enum OverlayFlags
{
    None = 0,
    Vision = 1 << 0,
    Smell = 1 << 1,
    Mouth = 1 << 2,
    Brain = 1 << 3,
    Attributes = 1 << 4,
    Field = 1 << 5,

    All = Vision | Smell | Mouth | Brain | Attributes | Field,
}

public static class OverlayFlagsExtensions
{
    public static bool Has(this OverlayFlags flags, OverlayFlags flag) => (flags & flag) != 0;

    public static OverlayFlags Toggle(this OverlayFlags flags, OverlayFlags flag) =>
        flags.Has(flag) ? flags & ~flag : flags | flag;
}
