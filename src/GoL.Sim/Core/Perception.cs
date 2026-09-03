using System;
using System.Numerics;

namespace GoL.Sim.Core;

/// <summary>What a vision bin found. Ordering matters: <see cref="None"/> is 0 so
/// a cleared bin array reads as empty.</summary>
public enum SeenKind : byte { None = 0, Plant = 1, Creature = 2 }

/// <summary>
/// One eye's state: its geometry and what it currently sees.
/// <para>
/// The eye divides its cone into a fixed number of angular bins and reports the
/// <b>nearest</b> hit in each. That keeps the brain's input vector a fixed size no
/// matter how many things are in view - which is what makes a variable-topology
/// genome workable at all - and gives occlusion for free, since a near object
/// hides a far one in the same bin. It is also directly drawable, which is what
/// the V overlay renders.
/// </para>
/// </summary>
public sealed class Eye
{
    public Eye(int binCount)
    {
        BinCount = binCount;
        Distance = new float[binCount];
        Kind = new SeenKind[binCount];
        Hue = new float[binCount];
    }

    public int BinCount { get; }

    /// <summary>Offset from the body's heading. Zero for the forward eye, pi for
    /// a rear eye.</summary>
    public float HeadingOffset { get; set; }

    public float Range { get; set; }

    /// <summary>Half the field of view; the full cone spans twice this.</summary>
    public float HalfFov { get; set; }

    /// <summary>Distance to the nearest hit per bin, or <see cref="Range"/> if the
    /// bin is empty. The overlay draws a ray of this length.</summary>
    public float[] Distance { get; }

    public SeenKind[] Kind { get; }

    /// <summary>Hue of what was seen, readable only with ColourVision.</summary>
    public float[] Hue { get; }

    public void Clear()
    {
        for (int i = 0; i < BinCount; i++)
        {
            Distance[i] = Range;
            Kind[i] = SeenKind.None;
            Hue[i] = 0f;
        }
    }

    /// <summary>World-space direction the given bin looks along.</summary>
    public Vector2 BinDirection(float bodyHeading, int bin)
    {
        float angle = bodyHeading + HeadingOffset + BinAngle(bin);
        return new Vector2(MathF.Cos(angle), MathF.Sin(angle));
    }

    /// <summary>Angle of a bin's centre relative to the eye's own heading.</summary>
    public float BinAngle(int bin)
    {
        if (BinCount == 1) return 0f;
        float t = (bin + 0.5f) / BinCount;          // bin centre in 0..1
        return -HalfFov + t * (2f * HalfFov);
    }

    /// <summary>Records a hit, keeping only the nearest per bin.</summary>
    public void Report(int bin, float distance, SeenKind kind, float hue)
    {
        if (distance >= Distance[bin]) return;
        Distance[bin] = distance;
        Kind[bin] = kind;
        Hue[bin] = hue;
    }

    /// <summary>Which bin an angle relative to the eye's heading falls in, or -1 if
    /// outside the cone.</summary>
    public int BinFor(float relativeAngle)
    {
        if (relativeAngle < -HalfFov || relativeAngle > HalfFov) return -1;
        float t = (relativeAngle + HalfFov) / (2f * HalfFov);
        int bin = (int)(t * BinCount);
        return bin >= BinCount ? BinCount - 1 : bin;
    }
}
