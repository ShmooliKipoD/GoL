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

    /// <summary>Soil quality under everything - what explains where each kind of
    /// plant grows and why creatures migrate.</summary>
    Fertility = 1 << 5,

    /// <summary>Scent laid by creatures. Separate from fertility because the two
    /// answer different questions and stacking both translucent layers muddies
    /// each of them.</summary>
    Pheromone = 1 << 6,

    /// <summary>Every action this creature can take, and which it is taking now.</summary>
    Actions = 1 << 7,

    All = Vision | Smell | Mouth | Brain | Attributes | Fertility | Pheromone | Actions,
}

public static class OverlayFlagsExtensions
{
    public static bool Has(this OverlayFlags flags, OverlayFlags flag) => (flags & flag) != 0;

    public static OverlayFlags Toggle(this OverlayFlags flags, OverlayFlags flag) =>
        flags.Has(flag) ? flags & ~flag : flags | flag;
}
