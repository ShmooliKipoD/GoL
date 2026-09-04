using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoGame.Extended;
using GoL.Sim.Board;

namespace GoL.Render;

/// <summary>
/// Draws the board: soil, scent and vegetation.
/// <para>
/// Each plant kind gets its own colour <i>and</i> silhouette. Colour alone is not
/// enough - grass and bramble are both green, and those two are exactly the pair
/// that has to be distinguishable, since bramble is the one a creature cannot eat
/// without a cellulose gut. Radius tracks remaining energy, so a grazed patch
/// visibly thins.
/// </para>
/// </summary>
public sealed class BoardRenderer
{
    private static readonly Color GrassInk = new(104, 168, 92);
    private static readonly Color FruitInk = new(226, 164, 74);
    private static readonly Color BrambleInk = new(58, 104, 66);
    private static readonly Color BlightcapInk = new(196, 206, 156);
    private static readonly Color BlightcapCore = new(58, 48, 70);
    private static readonly Color CarrionInk = new(132, 116, 110);

    /// <summary>
    /// Only cells inside the view are drawn. A board is tens of thousands of cells
    /// and at any useful zoom most are off screen; drawing them all would dominate
    /// the frame for pixels nobody sees.
    /// </summary>
    public void DrawPlants(SpriteBatch batch, BoardEnvironment board, RectangleF view)
    {
        var plants = board.Plants;
        float cell = plants.CellSize;

        int minX = (int)MathF.Floor(view.Left / cell) - 1;
        int maxX = (int)MathF.Ceiling(view.Right / cell) + 1;
        int minY = (int)MathF.Floor(view.Top / cell) - 1;
        int maxY = (int)MathF.Ceiling(view.Bottom / cell) + 1;

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                int wx = Wrap(x, plants.Resolution);
                int wy = Wrap(y, plants.Resolution);

                int index = wy * plants.Resolution + wx;
                var kind = plants.KindAt(index);
                if (kind == PlantKind.None) continue;

                float energy = plants.EnergyAt(index);
                if (energy <= 0.05f) continue;

                // Drawn at the unwrapped position so a plant across the world seam
                // still appears beside the viewer rather than jumping to the far side.
                var centre = new Vector2((x + 0.5f) * cell, (y + 0.5f) * cell);

                var spec = PlantSpecs.Get(kind);
                float fullness = Math.Clamp(energy / spec.MaxEnergy, 0f, 1f);
                float radius = spec.Radius * (0.35f + 0.65f * fullness);

                Draw(batch, kind, centre, radius, fullness);
            }
        }
    }

    private static void Draw(SpriteBatch batch, PlantKind kind, Vector2 centre, float radius, float fullness)
    {
        switch (kind)
        {
            case PlantKind.Grass:
                batch.DrawCircle(centre, radius, 6, GrassInk * (0.55f + 0.45f * fullness), radius);
                break;

            case PlantKind.Fruit:
                // Fat and bright: worth crossing the map for, and it should look it.
                batch.DrawCircle(centre, radius, 10, FruitInk * (0.6f + 0.4f * fullness), radius);
                batch.DrawCircle(centre, radius, 10, FruitInk * 0.5f, 1f);
                break;

            case PlantKind.Bramble:
                DrawSpiky(batch, centre, radius, BrambleInk * (0.6f + 0.4f * fullness));
                break;

            case PlantKind.Blightcap:
                // Pale cap, dark centre - reads as "do not eat this" at a glance.
                batch.DrawCircle(centre, radius, 8, BlightcapInk * (0.55f + 0.45f * fullness), radius);
                batch.DrawCircle(centre, radius * 0.4f, 6, BlightcapCore, radius * 0.4f);
                break;

            case PlantKind.Carrion:
                DrawCross(batch, centre, radius, CarrionInk);
                break;
        }
    }

    /// <summary>A six-pointed star: unmistakably not a berry, even a few pixels across.</summary>
    private static void DrawSpiky(SpriteBatch batch, Vector2 centre, float radius, Color colour)
    {
        const int Spikes = 6;

        for (int i = 0; i < Spikes; i++)
        {
            float angle = MathF.Tau * i / Spikes;
            batch.DrawLine(centre, centre + CreatureRenderer.Polar(angle, radius), colour, 1.4f);
        }

        batch.DrawCircle(centre, radius * 0.35f, 6, colour, radius * 0.35f);
    }

    private static void DrawCross(SpriteBatch batch, Vector2 centre, float radius, Color colour)
    {
        batch.DrawLine(centre + CreatureRenderer.Polar(MathF.PI * 0.25f, radius),
                       centre + CreatureRenderer.Polar(MathF.PI * 1.25f, radius), colour, 1.6f);
        batch.DrawLine(centre + CreatureRenderer.Polar(MathF.PI * 0.75f, radius),
                       centre + CreatureRenderer.Polar(MathF.PI * 1.75f, radius), colour, 1.6f);
    }

    /// <summary>
    /// Soil quality, beneath everything. This is what explains the ecology - why
    /// fruit clusters where it does, why bramble takes exhausted ground, and why
    /// populations migrate as patches boom and bust.
    /// </summary>
    public void DrawFertility(SpriteBatch batch, BoardEnvironment board, RectangleF view)
    {
        var fertility = board.Fertility;
        float cell = board.WorldSize / fertility.Resolution;

        int minX = (int)MathF.Floor(view.Left / cell) - 1;
        int maxX = (int)MathF.Ceiling(view.Right / cell) + 1;
        int minY = (int)MathF.Floor(view.Top / cell) - 1;
        int maxY = (int)MathF.Ceiling(view.Bottom / cell) + 1;

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                float value = fertility.AtCell(
                    Wrap(x, fertility.Resolution), Wrap(y, fertility.Resolution));

                batch.FillRectangle(
                    new RectangleF(x * cell, y * cell, cell, cell),
                    FertilityInk(value));
            }
        }
    }

    /// <summary>Dark and cold for exhausted ground, warm for rich. Kept low-contrast
    /// so creatures and plants stay the foreground.</summary>
    private static Color FertilityInk(float value)
    {
        value = Math.Clamp(value, 0f, 1f);
        return new Color(
            (int)(28 + 62 * value),
            (int)(26 + 44 * value),
            (int)(30 + 18 * value));
    }

    /// <summary>Scent, tinted per channel. Shows what the gland attributes are
    /// actually for.</summary>
    public void DrawPheromones(SpriteBatch batch, BoardEnvironment board, RectangleF view)
    {
        var scent = board.Scent;
        float cell = scent.CellSize;

        int minX = (int)MathF.Floor(view.Left / cell) - 1;
        int maxX = (int)MathF.Ceiling(view.Right / cell) + 1;
        int minY = (int)MathF.Floor(view.Top / cell) - 1;
        int maxY = (int)MathF.Ceiling(view.Bottom / cell) + 1;

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                int cx = Wrap(x, scent.Resolution);
                int cy = Wrap(y, scent.Resolution);

                float a = scent.At(0, cx, cy);
                float b = scent.At(1, cx, cy);
                if (a + b <= 0.01f) continue;

                var colour = new Color(
                    (int)(180 * b + 90 * a),
                    (int)(80 * b + 120 * a),
                    (int)(210 * b + 190 * a)) * MathF.Min(1f, (a + b) * 0.7f);

                batch.FillRectangle(new RectangleF(x * cell, y * cell, cell, cell), colour);
            }
        }
    }

    private static int Wrap(int c, int resolution)
    {
        c %= resolution;
        return c < 0 ? c + resolution : c;
    }
}
