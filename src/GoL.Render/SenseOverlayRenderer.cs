using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoGame.Extended;
using GoL.Sim.Core;
using GoL.Sim.Genetics;

namespace GoL.Render;

/// <summary>
/// Draws the world-space sense overlays: vision cones, smell radius and mouth
/// reach. Screen-space panels (attributes, brain graph) live in their own renderer
/// because they draw outside the camera transform.
/// </summary>
public sealed class SenseOverlayRenderer
{
    private static readonly Color VisionInk = new(120, 180, 230);
    private static readonly Color VisionRayEmpty = new(70, 96, 120);
    private static readonly Color PlantInk = new(126, 196, 122);
    private static readonly Color CreatureInk = new(228, 132, 120);
    private static readonly Color SmellInk = new(196, 150, 220);
    private static readonly Color MouthInk = new(232, 190, 110);

    public void Draw(SpriteBatch batch, Creature creature, ISenseField field, OverlayFlags flags)
    {
        if (flags.Has(OverlayFlags.Vision)) DrawVision(batch, creature);
        if (flags.Has(OverlayFlags.Smell)) DrawSmell(batch, creature, field);
        if (flags.Has(OverlayFlags.Mouth)) DrawMouth(batch, creature, field);
    }

    /// <summary>
    /// The cone's edges and range arc, plus one ray per vision bin drawn out to
    /// whatever that bin actually hit. The rays are the point: they show not where
    /// the creature <i>could</i> look but what it is seeing right now, and a short
    /// ray means something is close in that direction.
    /// </summary>
    private static void DrawVision(SpriteBatch batch, Creature creature)
    {
        DrawEye(batch, creature, creature.ForwardEye, 1f);

        if (creature.RearEye is { } rear)
            DrawEye(batch, creature, rear, 0.65f);
    }

    private static void DrawEye(SpriteBatch batch, Creature creature, Eye eye, float alpha)
    {
        var centre = CreatureRenderer.ToXna(creature.Position);
        float eyeHeading = creature.Heading + eye.HeadingOffset;

        // Cone edges and the range arc.
        var edge = VisionInk * (0.7f * alpha);
        batch.DrawLine(centre, centre + CreatureRenderer.Polar(eyeHeading - eye.HalfFov, eye.Range), edge, 1f);
        batch.DrawLine(centre, centre + CreatureRenderer.Polar(eyeHeading + eye.HalfFov, eye.Range), edge, 1f);

        CreatureRenderer.DrawArc(batch, centre, eye.Range,
            eyeHeading - eye.HalfFov, eyeHeading + eye.HalfFov, edge, 1f, 20);

        for (int bin = 0; bin < eye.BinCount; bin++)
        {
            var direction = eye.BinDirection(creature.Heading, bin);
            var end = centre + new Vector2(direction.X, direction.Y) * eye.Distance[bin];

            (Color colour, float thickness) = eye.Kind[bin] switch
            {
                SeenKind.Plant => (PlantInk * alpha, 1.6f),
                SeenKind.Creature => (CreatureInk * alpha, 1.6f),
                // An empty bin still draws, faintly, at full range - otherwise a
                // creature staring at nothing looks like a broken overlay.
                _ => (VisionRayEmpty * (0.45f * alpha), 0.8f),
            };

            batch.DrawLine(centre, end, colour, thickness);

            if (eye.Kind[bin] != SeenKind.None)
                batch.DrawCircle(end, 2f, 6, colour, 2f);
        }
    }

    /// <summary>
    /// A dashed ring at the nose radius - dashed so it never reads as a second body
    /// outline - plus, once ScentGradient is unlocked, an arrow pointing the way the
    /// scent strengthens. The arrow is the difference between smelling something and
    /// knowing where it is.
    /// </summary>
    private static void DrawSmell(SpriteBatch batch, Creature creature, ISenseField field)
    {
        float radius = creature.Genome.Trait(TraitAxis.NoseRadius);
        if (radius < 1f) return;

        var nose = CreatureRenderer.ToXna(creature.NosePosition);
        DrawDashedCircle(batch, nose, radius, SmellInk * 0.75f, 1.2f);

        if (!creature.Genome.Has(LatentTraitId.ScentGradient)) return;

        var forward = creature.Forward;
        var left = new System.Numerics.Vector2(-forward.Y, forward.X);

        float ahead = field.SampleScent(field.Wrap(creature.NosePosition + forward * radius), 0);
        float behind = field.SampleScent(field.Wrap(creature.NosePosition - forward * radius), 0);
        float port = field.SampleScent(field.Wrap(creature.NosePosition + left * radius), 0);
        float starboard = field.SampleScent(field.Wrap(creature.NosePosition - left * radius), 0);

        var gradient = new Vector2(ahead - behind, port - starboard);
        if (gradient.LengthSquared() < 1e-6f) return;

        gradient.Normalize();
        var worldDirection = new Vector2(
            forward.X * gradient.X + left.X * gradient.Y,
            forward.Y * gradient.X + left.Y * gradient.Y);

        DrawArrow(batch, nose, nose + worldDirection * radius, SmellInk, 2f);
    }

    /// <summary>
    /// The bite arc swept out to full reach, plus a line to whatever is in range
    /// right now. A plain circle would be actively misleading - it would imply the
    /// creature can eat in any direction, when in fact it must face its food.
    /// </summary>
    private static void DrawMouth(SpriteBatch batch, Creature creature, ISenseField field)
    {
        var centre = CreatureRenderer.ToXna(creature.Position);
        float reach = creature.MouthRange;
        float arc = creature.Genome.Trait(TraitAxis.MouthArc);

        var from = creature.Heading - arc;
        var to = creature.Heading + arc;

        batch.DrawLine(centre, centre + CreatureRenderer.Polar(from, reach), MouthInk * 0.7f, 1f);
        batch.DrawLine(centre, centre + CreatureRenderer.Polar(to, reach), MouthInk * 0.7f, 1f);
        CreatureRenderer.DrawArc(batch, centre, reach, from, to, MouthInk, 1.4f, 14);

        DrawBiteTarget(batch, creature, field, reach, arc, centre);
    }

    private static void DrawBiteTarget(
        SpriteBatch batch, Creature creature, ISenseField field,
        float reach, float arc, Vector2 centre)
    {
        Span<Percept> found = stackalloc Percept[64];
        int count = field.Query(creature.Position, reach, creature.Id, found);

        float bestDistance = float.MaxValue;
        Percept best = default;
        bool any = false;

        for (int i = 0; i < count; i++)
        {
            var offset = field.Offset(creature.Position, found[i].Position);
            float distance = offset.Length() - found[i].Radius;
            if (distance > reach) continue;

            float relative = Senses.WrapAngle(MathF.Atan2(offset.Y, offset.X) - creature.Heading);
            if (MathF.Abs(relative) > arc) continue;

            if (distance < bestDistance) { bestDistance = distance; best = found[i]; any = true; }
        }

        if (!any) return;

        var target = centre + new Vector2(
            field.Offset(creature.Position, best.Position).X,
            field.Offset(creature.Position, best.Position).Y);

        var colour = best.Kind == SeenKind.Creature ? CreatureInk : PlantInk;
        batch.DrawLine(centre, target, colour, 2f);
        batch.DrawCircle(target, best.Radius + 2f, 12, colour, 1.5f);
    }

    /// <summary>Dashes make the ring read as a sensing range rather than a body.</summary>
    private static void DrawDashedCircle(
        SpriteBatch batch, Vector2 centre, float radius, Color colour, float thickness)
    {
        const int Dashes = 24;
        float step = MathF.Tau / Dashes;

        for (int i = 0; i < Dashes; i += 2)
        {
            float from = i * step;
            CreatureRenderer.DrawArc(batch, centre, radius, from, from + step, colour, thickness, 2);
        }
    }

    private static void DrawArrow(SpriteBatch batch, Vector2 from, Vector2 to, Color colour, float thickness)
    {
        batch.DrawLine(from, to, colour, thickness);

        var direction = to - from;
        if (direction.LengthSquared() < 1e-6f) return;
        direction.Normalize();

        float angle = MathF.Atan2(direction.Y, direction.X);
        const float HeadLength = 6f;
        const float HeadSpread = 2.6f;

        batch.DrawLine(to, to + CreatureRenderer.Polar(angle + HeadSpread, HeadLength), colour, thickness);
        batch.DrawLine(to, to + CreatureRenderer.Polar(angle - HeadSpread, HeadLength), colour, thickness);
    }
}
