using System;
using GoL.Sim.Core;

namespace GoL.Sim.Board;

/// <summary>
/// Ground quality, 0..1, over a coarse grid.
/// <para>
/// Seeded from value noise so terrain is <b>not uniform</b>: without it every patch
/// of the map is equally good, there is nowhere worth travelling to, and spatial
/// strategy cannot evolve because there is no space worth strategising about.
/// </para>
/// <para>
/// Plants drain it and it regrows logistically, which produces boom-and-bust
/// patches on its own - and stops fruit carpeting the map.
/// </para>
/// </summary>
public sealed class FertilityField
{
    /// <summary>
    /// Logistic recovery rate. Raised from 0.02, which could not keep up with even
    /// one plant: a cell settles at <c>ceiling - drain/RegrowthRate</c>, so at 0.02
    /// any drain above ~0.012/s pinned the soil at zero. Measured with no creatures
    /// at all, mean fertility fell from 0.5 to 0.02 in 4500 ticks - nothing to do
    /// with grazing.
    /// </summary>
    private const float RegrowthRate = 0.08f;

    private readonly float[] _value;
    private readonly float[] _ceiling;
    private readonly int _plantResolution;

    public FertilityField(int resolution, int plantResolution, ref Pcg32 rng)
    {
        Resolution = resolution;
        _plantResolution = plantResolution;

        _value = new float[resolution * resolution];
        _ceiling = new float[resolution * resolution];

        float perSoilCell = (float)plantResolution / resolution;
        _drainPerCell = 1f / MathF.Max(1f, perSoilCell * perSoilCell);

        Seed(ref rng);
    }

    public int Resolution { get; }

    /// <summary>Fertility under a plant cell.</summary>
    public float At(int plantIndex) => _value[ToOwnIndex(plantIndex)];

    public float AtCell(int x, int y) => _value[y * Resolution + x];

    /// <summary>
    /// Mean soil quality. The one number that separates a world that is merely
    /// grazed from one whose soil is running down - from the plant count alone the
    /// two look identical until it is too late to recover.
    /// </summary>
    public float Mean()
    {
        float total = 0f;
        for (int i = 0; i < _value.Length; i++) total += _value[i];
        return _value.Length == 0 ? 0f : total / _value.Length;
    }

    /// <summary>
    /// Plant cells are finer than fertility cells, so several plants share one patch
    /// of soil. The drain is scaled by that ratio: it is a rate per unit area, and
    /// charging every plant cell the full amount made a fully planted soil cell
    /// drain four times as fast as one plant ever should.
    /// </summary>
    private readonly float _drainPerCell;

    public void Drain(int plantIndex, float amount)
    {
        int i = ToOwnIndex(plantIndex);
        _value[i] = MathF.Max(0f, _value[i] - amount * _drainPerCell);
    }

    /// <summary>Logistic regrowth toward each cell's own ceiling.</summary>
    public void Step(float dt)
    {
        for (int i = 0; i < _value.Length; i++)
            _value[i] += RegrowthRate * dt * (_ceiling[i] - _value[i]);
    }

    /// <summary>Maps a plant-grid index onto this coarser grid.</summary>
    private int ToOwnIndex(int plantIndex)
    {
        int px = plantIndex % _plantResolution;
        int py = plantIndex / _plantResolution;

        int x = px * Resolution / _plantResolution;
        int y = py * Resolution / _plantResolution;
        return y * Resolution + x;
    }

    /// <summary>
    /// Value noise: a few octaves of smoothed random lattice, summed. Cheap, and
    /// it produces broad regions of good and poor ground rather than the salt-and-
    /// pepper that raw per-cell randomness would give.
    /// </summary>
    private void Seed(ref Pcg32 rng)
    {
        var lattice = new float[Resolution * Resolution];
        for (int i = 0; i < lattice.Length; i++) lattice[i] = rng.NextFloat();

        for (int y = 0; y < Resolution; y++)
        {
            for (int x = 0; x < Resolution; x++)
            {
                float value =
                    Octave(lattice, x, y, 8) * 0.55f +
                    Octave(lattice, x, y, 4) * 0.30f +
                    Octave(lattice, x, y, 2) * 0.15f;

                // Biased upward: a map that is mostly barren starves everything
                // before any behaviour has a chance to evolve.
                float ceiling = Math.Clamp(0.25f + value * 0.85f, 0.05f, 1f);

                int i = y * Resolution + x;
                _ceiling[i] = ceiling;
                _value[i] = ceiling;
            }
        }
    }

    private float Octave(float[] lattice, int x, int y, int scale)
    {
        int step = Math.Max(1, Resolution / scale);

        int x0 = x / step * step;
        int y0 = y / step * step;
        int x1 = (x0 + step) % Resolution;
        int y1 = (y0 + step) % Resolution;

        float tx = Smooth((x - x0) / (float)step);
        float ty = Smooth((y - y0) / (float)step);

        float a = lattice[y0 * Resolution + x0];
        float b = lattice[y0 * Resolution + x1];
        float c = lattice[y1 * Resolution + x0];
        float d = lattice[y1 * Resolution + x1];

        return Lerp(Lerp(a, b, tx), Lerp(c, d, tx), ty);
    }

    private static float Smooth(float t) => t * t * (3f - 2f * t);
    private static float Lerp(float a, float b, float t) => a + (b - a) * t;
}
