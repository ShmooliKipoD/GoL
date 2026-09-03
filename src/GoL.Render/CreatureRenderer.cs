using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoGame.Extended;
using GoL.Sim.Core;
using GoL.Sim.Genetics;

namespace GoL.Render;

/// <summary>
/// Draws creatures. Pure presentation: handed simulation state, never writes to it.
/// <para>
/// The body is a circle with eyes, per the spec. Everything the body shows is a
/// trait made visible: eye separation is field of view, the rim arc is the bite
/// arc, hue is inherited lineage, and the inner disc is current energy. The nose
/// has no body feature at all - it would be a third mark competing for space on a
/// creature that is often six pixels across - and lives entirely in its overlay.
/// </para>
/// </summary>
public sealed class CreatureRenderer
{
    private const int BodySegments = 24;
    private const int CoreSegments = 16;
    private const int ArcSegments = 12;

    private const float RimThickness = 1.6f;
    private const float MouthThickness = 3.0f;

    /// <summary>Energy dims the body, but only gently: the core disc is the precise
    /// readout, and a wide brightness swing would fight it for attention and make
    /// dark-hued lineages unreadable when hungry.</summary>
    private const float MinBrightness = 0.55f;

    public void Draw(SpriteBatch batch, CreatureView creature)
    {
        var centre = ToXna(creature.Position);
        float radius = creature.Radius;
        float energy = creature.EnergyFraction;

        Color body = BodyColor(creature.Genome, energy);
        Color rim = Scale(body, 0.55f);

        batch.DrawCircle(centre, radius, BodySegments, body, radius);
        batch.DrawCircle(centre, radius, BodySegments, rim, RimThickness);

        DrawEnergyCore(batch, centre, radius, energy, body);
        DrawMouth(batch, creature, centre, radius, body);
        DrawEyes(batch, creature, centre, radius);
    }

    /// <summary>
    /// A brighter inner disc sized by energy. Its <b>area</b> is proportional to
    /// energy, not its radius - the eye judges discs by area, so a radius-linear
    /// core reads as far emptier than the creature actually is.
    /// </summary>
    private static void DrawEnergyCore(
        SpriteBatch batch, Vector2 centre, float radius, float energy, Color body)
    {
        if (energy <= 0.02f) return;

        float coreRadius = radius * 0.62f * MathF.Sqrt(energy);
        if (coreRadius < 0.4f) return;

        batch.DrawCircle(centre, coreRadius, CoreSegments, Lighten(body, 0.55f), coreRadius);
    }

    /// <summary>
    /// Two dots on the forward rim, separated by the field-of-view angle - so a
    /// wide-FOV creature visibly has wide-set eyes and a narrow-FOV one stares
    /// forward. The eyes are also the heading cue; nothing else marks facing.
    /// </summary>
    private static void DrawEyes(SpriteBatch batch, CreatureView creature, Vector2 centre, float radius)
    {
        float halfFov = creature.Genome.Trait(TraitAxis.EyeHalfFov);
        float eyeRadius = MathF.Max(0.9f, radius * 0.20f);

        // Eyes sit slightly inside the rim so they read as on the body, not beside it.
        float ring = radius * 0.72f;

        // Larger eyes for longer sight, but kept subtle so FOV stays the dominant
        // signal the shape carries.
        float rangeNorm = creature.Genome.Normalized(TraitAxis.EyeRange);
        eyeRadius *= 0.8f + 0.5f * rangeNorm;

        DrawEyePair(batch, creature.Heading, halfFov, centre, ring, eyeRadius, EyeInk);

        if (creature.Genome.Has(LatentTraitId.RearEye))
        {
            // A visible consequence of a mutation: the creature grows a second pair.
            DrawEyePair(batch, creature.Heading + MathF.PI, halfFov,
                centre, ring, eyeRadius * 0.75f, RearEyeInk);
        }
    }

    private static void DrawEyePair(
        SpriteBatch batch, float heading, float halfFov,
        Vector2 centre, float ring, float eyeRadius, Color ink)
    {
        // Separation tracks FOV but is capped: at very wide fields the eyes would
        // slide round to the sides and stop reading as a face.
        float separation = MathF.Min(halfFov, 1.1f) * 0.75f;

        foreach (float side in stackalloc[] { -1f, 1f })
        {
            float angle = heading + side * separation;
            var position = centre + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * ring;
            batch.DrawCircle(position, eyeRadius, 8, ink, eyeRadius);
        }
    }

    /// <summary>
    /// A thicker segment of the rim spanning the bite arc, which flares bright when
    /// the creature is actually feeding - so you can watch it eat rather than
    /// inferring it from an energy number.
    /// </summary>
    private static void DrawMouth(
        SpriteBatch batch, CreatureView creature, Vector2 centre, float radius, Color body)
    {
        float arc = creature.Genome.Trait(TraitAxis.MouthArc);
        bool biting = creature.LastIntent.Bite;
        bool feeding = creature.BitThisTick;

        // At rest the mouth is a warm dark band. Tinting it rather than merely
        // dimming the body colour is what keeps it distinguishable from the rim -
        // a darker shade of the same hue reads as "the rim, slightly shaded".
        Color colour = feeding ? FeedingInk
            : biting ? Lighten(MouthInk, 0.35f)
            : MouthInk;

        float thickness = feeding ? MouthThickness * 1.7f : MouthThickness;

        // Drawn just outside the rim so it is never buried under it.
        DrawArc(batch, centre, radius + thickness * 0.35f,
            creature.Heading - arc, creature.Heading + arc,
            colour, thickness, ArcSegments);
    }

    /// <summary>Polyline arc. MonoGame.Extended has no arc primitive, and a filled
    /// pie would cover the energy core.</summary>
    public static void DrawArc(
        SpriteBatch batch, Vector2 centre, float radius,
        float fromAngle, float toAngle, Color colour, float thickness, int segments)
    {
        if (segments < 1) segments = 1;

        float step = (toAngle - fromAngle) / segments;
        var previous = centre + Polar(fromAngle, radius);

        for (int i = 1; i <= segments; i++)
        {
            var next = centre + Polar(fromAngle + step * i, radius);
            batch.DrawLine(previous, next, colour, thickness);
            previous = next;
        }
    }

    public static Vector2 Polar(float angle, float radius) =>
        new(MathF.Cos(angle) * radius, MathF.Sin(angle) * radius);

    /// <summary>
    /// Hue is inherited, so a lineage is recognisable on sight and can be watched
    /// spreading across the map - and it is what the ColourVision trait reads,
    /// which makes body colour a signalling channel evolution can exploit.
    /// Brightness carries current energy.
    /// </summary>
    public static Color BodyColor(Genome genome, float energyFraction)
    {
        float hue = genome.Normalized(TraitAxis.Hue);
        float value = MinBrightness + (1f - MinBrightness) * energyFraction;
        return FromHsv(hue, 0.55f, value);
    }

    private static readonly Color EyeInk = new(14, 16, 20);
    private static readonly Color RearEyeInk = new(40, 44, 52);
    private static readonly Color FeedingInk = new(255, 236, 168);
    private static readonly Color MouthInk = new(150, 96, 74);

    public static Vector2 ToXna(System.Numerics.Vector2 v) => new(v.X, v.Y);

    private static Color Scale(Color c, float factor) =>
        new((int)(c.R * factor), (int)(c.G * factor), (int)(c.B * factor));

    private static Color Lighten(Color c, float amount) => new(
        (int)(c.R + (255 - c.R) * amount),
        (int)(c.G + (255 - c.G) * amount),
        (int)(c.B + (255 - c.B) * amount));

    /// <summary>HSV to RGB. Hue wraps; saturation and value are 0..1.</summary>
    public static Color FromHsv(float hue, float saturation, float value)
    {
        hue -= MathF.Floor(hue);
        float h = hue * 6f;
        int sector = (int)h;
        float f = h - sector;

        float p = value * (1f - saturation);
        float q = value * (1f - saturation * f);
        float t = value * (1f - saturation * (1f - f));

        (float r, float g, float b) = sector switch
        {
            0 => (value, t, p),
            1 => (q, value, p),
            2 => (p, value, t),
            3 => (p, q, value),
            4 => (t, p, value),
            _ => (value, p, q),
        };

        return new Color(r, g, b);
    }
}
