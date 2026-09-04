using System;
using System.Globalization;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoGame.Extended;
using GoL.Sim.Brains;
using GoL.Sim.Core;
using GoL.Sim.Genetics;

namespace GoL.Render;

/// <summary>
/// Screen-space panels: the attribute readout and the brain graph.
/// <para>
/// Both <b>iterate the genome</b> rather than naming traits. That is what makes
/// the open-ended design work visually: when a mutation unlocks an attribute the
/// game has never shown before, it appears in the panel and its nodes appear in the
/// graph with no rendering code written for it.
/// </para>
/// </summary>
public sealed class InspectorRenderer
{
    private const float Scale = 2f;
    private const float PanelWidth = 300f;
    private const float Pad = 12f;

    private float[] _activations = new float[256];

    /// <summary>Lists every trait this creature has - continuous axes with a bar,
    /// then the latent attributes it has acquired.</summary>
    public void DrawAttributes(SpriteBatch batch, TextRenderer text, CreatureView creature, Vector2 topLeft)
    {
        var genome = creature.Genome;
        float lineHeight = text.LineHeight(Scale);

        int rows = Traits.Count + 6 + CountUnlocked(genome);
        float height = Pad * 2 + rows * lineHeight;

        var panel = new RectangleF(topLeft.X, topLeft.Y, PanelWidth, height);
        batch.FillRectangle(panel, Palette.Panel * 0.92f);
        batch.DrawRectangle(panel, Palette.InkDim * 0.5f, 1f);

        float x = topLeft.X + Pad;
        float y = topLeft.Y + Pad;

        text.Draw($"creature {creature.Id}", new Vector2(x, y), Palette.Accent, Scale);
        y += lineHeight;

        text.Draw($"gen {genome.Generation}  age {creature.Age:0}s",
            new Vector2(x, y), Palette.InkDim, Scale);
        y += lineHeight;

        text.Draw($"energy {creature.Energy.Current:0}/{creature.Energy.Maximum:0}",
            new Vector2(x, y), Palette.Ink, Scale);
        y += lineHeight;

        text.Draw($"brain {creature.Mind.Brain.NodeCount}n {creature.Mind.Brain.ConnCount}c",
            new Vector2(x, y), Palette.InkDim, Scale);
        y += lineHeight * 1.5f;

        // Every continuous trait, with a bar for its position in its own range -
        // the bar is what makes "is this creature fast?" answerable at a glance,
        // since the raw number means nothing without knowing the range.
        for (int i = 0; i < Traits.Count; i++)
        {
            var axis = (TraitAxis)i;
            var range = Traits.Range(axis);
            float normalized = genome.Normalized(axis);

            text.Draw(range.Name, new Vector2(x, y), Palette.InkDim, Scale);

            string value = genome.Trait(axis).ToString("0.##", CultureInfo.InvariantCulture) + range.Unit;
            var valueSize = text.Measure(value, Scale);
            text.Draw(value, new Vector2(topLeft.X + PanelWidth - Pad - valueSize.X, y), Palette.Ink, Scale);

            DrawBar(batch, new Vector2(x, y + lineHeight - 4f),
                PanelWidth - Pad * 2, 2f, normalized);

            y += lineHeight;
        }

        y += lineHeight * 0.5f;
        text.Draw("attributes", new Vector2(x, y), Palette.Accent, Scale);
        y += lineHeight;

        if (genome.UnlockedMask == 0)
        {
            text.Draw("  none yet", new Vector2(x, y), Palette.InkDim, Scale);
            return;
        }

        foreach (var trait in LatentTraitCatalog.All)
        {
            if (!genome.Has(trait.Id)) continue;
            text.Draw("+ " + trait.Name, new Vector2(x, y), Palette.Warning, Scale);
            y += lineHeight;
        }
    }

    /// <summary>
    /// Every action the creature could take, whether it can take it, and how hard
    /// it is taking it right now.
    /// <para>
    /// Signed actions get a centre-anchored bar, so <c>Move</c> reads as one axis
    /// with chase at one end and flee at the other rather than as two rows. That is
    /// the whole point: they are one effector, and drawing them apart would imply a
    /// distinction the brain does not make.
    /// </para>
    /// <para>
    /// Rows the genome cannot perform are greyed rather than hidden, so the panel
    /// answers "what could this lineage acquire?" as well as "what is it doing?" -
    /// and a mutation lights a row up without any rendering code written for it.
    /// </para>
    /// </summary>
    public void DrawActions(SpriteBatch batch, TextRenderer text, CreatureView creature, Vector2 topLeft)
    {
        var behaviour = creature.Behaviour;
        float lineHeight = text.LineHeight(Scale);

        float height = Pad * 2 + (Actions.Count + 3) * lineHeight;
        var panel = new RectangleF(topLeft.X, topLeft.Y, PanelWidth, height);
        batch.FillRectangle(panel, Palette.Panel * 0.92f);
        batch.DrawRectangle(panel, Palette.InkDim * 0.5f, 1f);

        float x = topLeft.X + Pad;
        float y = topLeft.Y + Pad;
        float barWidth = PanelWidth - Pad * 2;

        text.Draw("actions", new Vector2(x, y), Palette.Accent, Scale);
        y += lineHeight;

        text.Draw("now: " + creature.ActionLabel, new Vector2(x, y),
            behaviour.Active ? Palette.Warning : Palette.InkDim, Scale);
        y += lineHeight * 1.5f;

        for (int i = 0; i < Actions.Count; i++)
        {
            var action = (CreatureAction)i;
            bool can = behaviour.Can(action);
            bool isCurrent = behaviour.Active && behaviour.Current == action;
            float value = behaviour.Values[i];
            bool active = can && MathF.Abs(value) >= Actions.Deadband;

            var colour = !can ? Palette.InkDim * 0.45f
                       : isCurrent ? Palette.Warning
                       : active ? Palette.Ink
                       : Palette.InkDim;

            // The pole name rather than the bare name, so a row reading "Flee"
            // says which end of the axis the creature is at without decoding a sign.
            string name = (isCurrent ? "> " : "  ")
                + (active ? Actions.PoleName(action, value) : Actions.Name(action));
            text.Draw(name, new Vector2(x, y), colour, Scale);

            if (can)
            {
                string shown = value.ToString("+0.00;-0.00; 0.00", CultureInfo.InvariantCulture);
                var size = text.Measure(shown, Scale);
                text.Draw(shown, new Vector2(topLeft.X + PanelWidth - Pad - size.X, y), colour, Scale);
            }
            else
            {
                // Just "locked". Naming the attribute that would unlock it was
                // tried and overran the name column - and the attribute panel
                // already answers "what does this creature have".
                const string Locked = "locked";
                var size = text.Measure(Locked, Scale);
                text.Draw(Locked, new Vector2(topLeft.X + PanelWidth - Pad - size.X, y), colour, Scale);
            }

            var barAt = new Vector2(x, y + lineHeight - 4f);
            if (Actions.IsSigned(action)) DrawSignedBar(batch, barAt, barWidth, 2f, can ? value : 0f);
            else DrawBar(batch, barAt, barWidth, 2f, can ? MathF.Abs(value) : 0f);

            y += lineHeight;
        }
    }

    /// <summary>A bar anchored at its centre, filling left for negative and right
    /// for positive - one axis, drawn as one axis.</summary>
    private static void DrawSignedBar(
        SpriteBatch batch, Vector2 position, float width, float height, float value)
    {
        batch.FillRectangle(new RectangleF(position.X, position.Y, width, height), Palette.InkDim * 0.3f);

        float centre = position.X + width * 0.5f;
        float extent = width * 0.5f * Math.Clamp(MathF.Abs(value), 0f, 1f);

        var colour = value < 0f ? Palette.Danger * 0.85f : Palette.Accent * 0.85f;
        float from = value < 0f ? centre - extent : centre;

        batch.FillRectangle(new RectangleF(from, position.Y, MathF.Max(extent, 0f), height), colour);
        batch.FillRectangle(new RectangleF(centre - 0.5f, position.Y - 1f, 1f, height + 2f), Palette.InkDim * 0.7f);
    }

    /// <summary>
    /// The current action, above the creature.
    /// <para>
    /// Takes an <b>already-computed screen position</b> rather than a camera: the
    /// board projects through <c>BoardCamera.WorldToScreen</c>, but the lab has no
    /// camera at all and draws through a plain scale-and-offset matrix. Passing the
    /// result keeps one label renderer working for both.
    /// </para>
    /// <para>
    /// Screen space, not world space - a world-space label would scale with zoom and
    /// be unreadable at both ends of the range.
    /// </para>
    /// </summary>
    public static void DrawActionLabel(
        SpriteBatch batch, TextRenderer text, CreatureView creature, Vector2 screenPosition)
    {
        string label = creature.ActionLabel;
        var size = text.Measure(label, Scale);

        // Whole pixels: monogram is a pixel font and samples unevenly off-grid.
        var at = new Vector2(
            MathF.Round(screenPosition.X - size.X * 0.5f),
            MathF.Round(screenPosition.Y - size.Y));

        batch.FillRectangle(
            new RectangleF(at.X - 3f, at.Y - 2f, size.X + 6f, size.Y + 4f),
            Palette.Panel * 0.85f);

        text.Draw(label, at,
            creature.Behaviour.Active ? Palette.Warning : Palette.InkDim, Scale);
    }

    private static void DrawBar(SpriteBatch batch, Vector2 position, float width, float height, float fraction)
    {
        batch.FillRectangle(new RectangleF(position.X, position.Y, width, height), Palette.InkDim * 0.3f);
        batch.FillRectangle(
            new RectangleF(position.X, position.Y, width * Math.Clamp(fraction, 0f, 1f), height),
            Palette.Accent * 0.8f);
    }

    /// <summary>
    /// The brain, drawn as sensors on the left, hidden neurons by depth in the
    /// middle, effectors on the right.
    /// <para>
    /// A layered layout, not a force-directed one. Topology here is arbitrary and
    /// changes every generation; a physics layout would drift, jitter between
    /// frames and never settle, whereas layering by depth is stable, deterministic
    /// and readable at a glance.
    /// </para>
    /// </summary>
    public void DrawBrain(SpriteBatch batch, TextRenderer text, CreatureView creature, RectangleF bounds)
    {
        var genome = creature.Genome;
        var brain = creature.Mind.Brain;

        batch.FillRectangle(bounds, Palette.Panel * 0.92f);
        batch.DrawRectangle(bounds, Palette.InkDim * 0.5f, 1f);

        text.Draw($"brain  {brain.NodeCount} neurons  {brain.ConnCount} connections",
            new Vector2(bounds.X + Pad, bounds.Y + Pad), Palette.Accent, Scale);

        if (_activations.Length < brain.NodeCount) _activations = new float[brain.NodeCount * 2];
        brain.CopyActivations(_activations);

        int nodeCount = genome.Nodes.Count;
        Span<Vector2> positions = nodeCount <= 512 ? stackalloc Vector2[nodeCount] : new Vector2[nodeCount];

        LayOutNodes(genome, bounds, positions);

        // Edges first so nodes sit on top of them.
        foreach (var conn in genome.Conns)
        {
            if (!conn.Enabled) continue;

            int from = genome.FindNode(conn.From);
            int to = genome.FindNode(conn.To);
            if (from < 0 || to < 0) continue;

            // Sign is the thing worth seeing: excitatory versus inhibitory tells you
            // more about behaviour than magnitude does.
            var colour = conn.Weight >= 0f
                ? new Color(120, 200, 150) * MathF.Min(1f, 0.25f + MathF.Abs(conn.Weight) * 0.5f)
                : new Color(220, 130, 130) * MathF.Min(1f, 0.25f + MathF.Abs(conn.Weight) * 0.5f);

            batch.DrawLine(positions[from], positions[to], colour,
                MathF.Min(2.5f, 0.5f + MathF.Abs(conn.Weight)));
        }

        for (int i = 0; i < nodeCount; i++)
        {
            var node = genome.Nodes[i];
            float activation = i < brain.NodeCount ? _activations[i] : 0f;

            var baseColour = node.Kind switch
            {
                NodeKind.Sensor => new Color(120, 180, 230),
                NodeKind.Effector => new Color(232, 190, 110),
                NodeKind.Bias => new Color(150, 150, 160),
                _ => new Color(180, 170, 220),
            };

            // Brightness shows live activation, so the graph animates as it thinks.
            float intensity = 0.35f + 0.65f * MathF.Min(1f, MathF.Abs(activation));
            batch.DrawCircle(positions[i], 3.5f, 8, baseColour * intensity, 3.5f);
        }
    }

    /// <summary>
    /// Places nodes in columns: sensors left, effectors right, hidden neurons spread
    /// between by their distance from the sensors.
    /// </summary>
    private static void LayOutNodes(Genome genome, RectangleF bounds, Span<Vector2> positions)
    {
        int count = genome.Nodes.Count;

        float left = bounds.X + Pad * 3;
        float right = bounds.X + bounds.Width - Pad * 3;
        float top = bounds.Y + Pad * 4;
        float bottom = bounds.Y + bounds.Height - Pad * 2;

        int sensors = 0, hidden = 0, effectors = 0;
        for (int i = 0; i < count; i++)
        {
            switch (genome.Nodes[i].Kind)
            {
                case NodeKind.Sensor: case NodeKind.Bias: sensors++; break;
                case NodeKind.Effector: effectors++; break;
                default: hidden++; break;
            }
        }

        int si = 0, hi = 0, ei = 0;
        for (int i = 0; i < count; i++)
        {
            var kind = genome.Nodes[i].Kind;

            positions[i] = kind switch
            {
                NodeKind.Sensor or NodeKind.Bias =>
                    new Vector2(left, Spread(top, bottom, si++, sensors)),
                NodeKind.Effector =>
                    new Vector2(right, Spread(top, bottom, ei++, effectors)),
                _ =>
                    new Vector2((left + right) * 0.5f, Spread(top, bottom, hi++, hidden)),
            };
        }
    }

    private static float Spread(float from, float to, int index, int count) =>
        count <= 1 ? (from + to) * 0.5f : from + (to - from) * index / (count - 1);

    private static int CountUnlocked(Genome genome)
    {
        int n = 0;
        foreach (var trait in LatentTraitCatalog.All) if (genome.Has(trait.Id)) n++;
        return n;
    }
}
