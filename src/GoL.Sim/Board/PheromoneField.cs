using System;

namespace GoL.Sim.Board;

/// <summary>
/// Diffusing, decaying scent over a grid, several channels deep.
/// <para>
/// <b>Stepped every fourth tick, not every tick.</b> Diffusion cost is fixed by the
/// grid size and does not depend on population, so at 60 Hz it would be the single
/// largest cost in the simulation - dwarfing every brain put together. Emission
/// still happens every tick; only the diffuse-and-decay pass is sub-rate.
/// </para>
/// </summary>
public sealed class PheromoneField
{
    /// <summary>Ticks between diffusion passes.</summary>
    public const int StepInterval = 4;

    /// <summary>
    /// Diffusion coefficient per pass. An explicit four-neighbour Laplacian is
    /// <b>only stable for D &lt;= 0.25</b>; above that the field oscillates and
    /// blows up into a checkerboard, which is a baffling thing to debug later. The
    /// constructor asserts it rather than leaving it to be rediscovered.
    /// </summary>
    private const float Diffusion = 0.18f;

    /// <summary>Decay per second. Gives scent a half-life of roughly two seconds.</summary>
    private const float Decay = 0.35f;

    /// <summary>Values below this are flushed to zero. Without it the tails of the
    /// field fill with denormal floats and the diffusion pass slows by an order of
    /// magnitude.</summary>
    private const float DenormalFloor = 1e-4f;

    private float[] _value;
    private float[] _scratch;
    private readonly float _worldSize;
    private readonly bool _toroidal;

    public PheromoneField(float worldSize, int resolution, int channels, bool toroidal)
    {
        if (Diffusion > 0.25f)
            throw new InvalidOperationException("Explicit diffusion is unstable above 0.25.");

        _worldSize = worldSize;
        _toroidal = toroidal;

        Resolution = resolution;
        Channels = channels;
        CellSize = worldSize / resolution;

        _value = new float[channels * resolution * resolution];
        _scratch = new float[_value.Length];
    }

    public int Resolution { get; }
    public int Channels { get; }
    public float CellSize { get; }

    public float At(int channel, int x, int y) => _value[Index(channel, x, y)];

    /// <summary>Deposits scent at a world position.</summary>
    public void Emit(Vector2 position, int channel, float amount)
    {
        if (channel < 0 || channel >= Channels) return;

        var (x, y) = Cell(position);
        int i = Index(channel, x, y);
        _value[i] = MathF.Min(1f, _value[i] + amount);
    }

    /// <summary>Strength at a world position.</summary>
    public float Sample(Vector2 position, int channel)
    {
        if (channel < 0 || channel >= Channels) return 0f;

        var (x, y) = Cell(position);
        return _value[Index(channel, x, y)];
    }

    /// <summary>
    /// One diffuse-and-decay pass. <paramref name="dt"/> is the interval since the
    /// last pass, not one tick.
    /// <para>
    /// Split into an interior fast path and an edge path. The obvious version calls
    /// a wrapping helper for all four neighbours of every cell, and the modulo
    /// arithmetic in that helper - run tens of thousands of times per pass, for a
    /// wrap that only actually applies on the boundary - was measured as the
    /// simulation's largest fixed cost once vision had been fixed. Interior cells
    /// index their neighbours directly.
    /// </para>
    /// </summary>
    public void Step(float dt)
    {
        int plane = Resolution * Resolution;
        int last = Resolution - 1;
        float keep = MathF.Max(0f, 1f - Decay * dt);

        var value = _value;
        var scratch = _scratch;

        for (int channel = 0; channel < Channels; channel++)
        {
            int offset = channel * plane;

            // Interior: every neighbour is in range, so no wrapping is needed.
            for (int y = 1; y < last; y++)
            {
                int row = offset + y * Resolution;

                for (int x = 1; x < last; x++)
                {
                    int i = row + x;

                    float centre = value[i];
                    float laplacian =
                        value[i - 1] + value[i + 1] +
                        value[i - Resolution] + value[i + Resolution] -
                        4f * centre;

                    float next = (centre + Diffusion * laplacian) * keep;
                    scratch[i] = next < DenormalFloor ? 0f : next;
                }
            }

            // Edges, where wrapping actually matters.
            for (int y = 0; y < Resolution; y++)
            {
                bool edgeRow = y == 0 || y == last;

                for (int x = 0; x < Resolution; x += edgeRow ? 1 : last)
                {
                    int i = offset + y * Resolution + x;

                    float centre = value[i];
                    float laplacian =
                        Neighbour(offset, x - 1, y) +
                        Neighbour(offset, x + 1, y) +
                        Neighbour(offset, x, y - 1) +
                        Neighbour(offset, x, y + 1) -
                        4f * centre;

                    float next = (centre + Diffusion * laplacian) * keep;
                    scratch[i] = next < DenormalFloor ? 0f : next;

                    if (last == 0) break;
                }
            }
        }

        // Swap rather than copy: the old buffer becomes next pass's scratch.
        (_value, _scratch) = (scratch, value);
    }

    private float Neighbour(int offset, int x, int y)
    {
        int cx = WrapCell(x);
        int cy = WrapCell(y);

        // A non-wrapping world has no scent beyond its edge; treating the outside
        // as zero makes the boundary absorb rather than reflect.
        if (cx < 0 || cy < 0) return 0f;
        return _value[offset + cy * Resolution + cx];
    }

    private int WrapCell(int c)
    {
        if (!_toroidal) return c >= 0 && c < Resolution ? c : -1;

        c %= Resolution;
        return c < 0 ? c + Resolution : c;
    }

    private (int X, int Y) Cell(Vector2 position)
    {
        int x = WrapCell((int)MathF.Floor(position.X / CellSize));
        int y = WrapCell((int)MathF.Floor(position.Y / CellSize));

        return (Math.Clamp(x < 0 ? 0 : x, 0, Resolution - 1),
                Math.Clamp(y < 0 ? 0 : y, 0, Resolution - 1));
    }

    private int Index(int channel, int x, int y) =>
        channel * Resolution * Resolution + y * Resolution + x;
}
