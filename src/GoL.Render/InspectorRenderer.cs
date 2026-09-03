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
    public void DrawAttributes(SpriteBatch batch, TextRenderer text, Creature creature, Vector2 topLeft)
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

        text.Draw($"energy {creature.Energy:0}/{creature.MaxEnergy:0}",
            new Vector2(x, y), Palette.Ink, Scale);
        y += lineHeight;

        text.Draw($"brain {creature.Brain.NodeCount}n {creature.Brain.ConnCount}c",
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
    public void DrawBrain(SpriteBatch batch, TextRenderer text, Creature creature, RectangleF bounds)
    {
        var genome = creature.Genome;
        var brain = creature.Brain;

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
