using System;

namespace GoL.Sim.Board;

/// <summary>
/// A uniform spatial hash over the world, rebuilt once per tick.
/// <para>
/// Built as a <b>counting sort into flat arrays</b> rather than an array of lists:
/// two linear passes, no per-cell allocation, and nothing to garbage-collect. After
/// the first rebuild it never allocates again, which matters because this runs
/// every tick for the whole population.
/// </para>
/// <para>
/// Cell coordinates wrap, so a query near the edge naturally reaches round the
/// world seam without a special case.
/// </para>
/// </summary>
public sealed class SpatialHash
{
    private readonly int _resolution;
    private readonly float _worldSize;
    private readonly float _cellSize;
    private readonly bool _toroidal;

    private int[] _cellStart;
    private int[] _cellCount;
    private int[] _items;
    private float[] _x;
    private float[] _y;
    private float[] _radius;

    private int _count;

    public SpatialHash(float worldSize, float cellSize, bool toroidal)
    {
        _worldSize = worldSize;
        _toroidal = toroidal;

        _resolution = Math.Max(1, (int)MathF.Ceiling(worldSize / cellSize));
        _cellSize = worldSize / _resolution;

        int cells = _resolution * _resolution;
        _cellStart = new int[cells + 1];
        _cellCount = new int[cells];
        _items = new int[256];
        _x = new float[256];
        _y = new float[256];
        _radius = new float[256];
    }

    public int Resolution => _resolution;
    public float CellSize => _cellSize;

    /// <summary>Largest radius inserted, so a query can widen its cell sweep enough
    /// to catch big things whose centres sit in a neighbouring cell.</summary>
    public float MaxRadius { get; private set; }

    public void Clear()
    {
        _count = 0;
        MaxRadius = 0f;
        Array.Clear(_cellCount, 0, _cellCount.Length);
    }

    /// <summary>Stages one entity. Call <see cref="Build"/> once all are added.</summary>
    public void Add(int id, Vector2 position, float radius)
    {
        EnsureCapacity(_count + 1);

        _items[_count] = id;
        _x[_count] = position.X;
        _y[_count] = position.Y;
        _radius[_count] = radius;
        _count++;

        if (radius > MaxRadius) MaxRadius = radius;
        _cellCount[CellIndex(position)]++;
    }

    /// <summary>Turns the staged entities into the sorted bucket layout.</summary>
    public void Build()
    {
        // Prefix sum: cellStart[i] is where cell i's run begins.
        int running = 0;
        for (int i = 0; i < _cellCount.Length; i++)
        {
            _cellStart[i] = running;
            running += _cellCount[i];
        }
        _cellStart[^1] = running;

        EnsureSorted(_count);
        Array.Copy(_cellStart, _cursor, _cellCount.Length);

        for (int i = 0; i < _count; i++)
        {
            int cell = CellIndex(new Vector2(_x[i], _y[i]));
            int slot = _cursor[cell]++;

            _sortedId[slot] = _items[i];
            _sortedX[slot] = _x[i];
            _sortedY[slot] = _y[i];
            _sortedR[slot] = _radius[i];
        }
    }

    private int[] _cursor = Array.Empty<int>();
    private int[] _sortedId = Array.Empty<int>();
    private float[] _sortedX = Array.Empty<float>();
    private float[] _sortedY = Array.Empty<float>();
    private float[] _sortedR = Array.Empty<float>();

    /// <summary>
    /// Ids whose bodies overlap the circle, written to <paramref name="results"/>
    /// in <b>ascending id</b>. That ordering is not cosmetic: callers accumulate
    /// floats over these results, and float addition is not associative, so an
    /// unstable order would make a seeded run irreproducible.
    /// </summary>
    public int Query(Vector2 centre, float radius, int excludeId, Span<int> results)
    {
        int count = 0;

        // Widen by the largest body so a big creature whose centre is a cell away
        // is still found.
        float reach = radius + MaxRadius;
        int span = Math.Max(1, (int)MathF.Ceiling(reach / _cellSize));

        int cx = CellCoord(centre.X);
        int cy = CellCoord(centre.Y);

        for (int dy = -span; dy <= span; dy++)
        {
            for (int dx = -span; dx <= span; dx++)
            {
                int x = WrapCell(cx + dx);
                int y = WrapCell(cy + dy);
                if (x < 0 || y < 0) continue;

                int cell = y * _resolution + x;
                int from = _cellStart[cell];
                int to = _cellStart[cell] + _cellCount[cell];

                for (int i = from; i < to; i++)
                {
                    int id = _sortedId[i];
                    if (id == excludeId) continue;

                    float ox = Delta(_sortedX[i] - centre.X);
                    float oy = Delta(_sortedY[i] - centre.Y);

                    float combined = radius + _sortedR[i];
                    if (ox * ox + oy * oy > combined * combined) continue;

                    if (count >= results.Length) return count;
                    results[count++] = id;
                }
            }
        }

        // Cells are visited in spatial order, so sort into id order before returning.
        results[..count].Sort();
        return count;
    }

    private float Delta(float d)
    {
        if (!_toroidal) return d;

        float half = _worldSize * 0.5f;
        if (d > half) d -= _worldSize;
        else if (d < -half) d += _worldSize;
        return d;
    }

    private int CellCoord(float v)
    {
        int c = (int)MathF.Floor(v / _cellSize);
        return _toroidal ? WrapCell(c) : Math.Clamp(c, 0, _resolution - 1);
    }

    private int WrapCell(int c)
    {
        if (!_toroidal) return c >= 0 && c < _resolution ? c : -1;

        c %= _resolution;
        return c < 0 ? c + _resolution : c;
    }

    private int CellIndex(Vector2 position) =>
        CellCoord(position.Y) * _resolution + CellCoord(position.X);

    private void EnsureCapacity(int needed)
    {
        if (_items.Length >= needed) return;

        int size = Math.Max(needed, _items.Length * 2);
        Array.Resize(ref _items, size);
        Array.Resize(ref _x, size);
        Array.Resize(ref _y, size);
        Array.Resize(ref _radius, size);
    }

    private void EnsureSorted(int needed)
    {
        if (_cursor.Length < _cellCount.Length) _cursor = new int[_cellCount.Length];
        if (_sortedId.Length >= needed) return;

        int size = Math.Max(needed, _sortedId.Length * 2);
        _sortedId = new int[size];
        _sortedX = new float[size];
        _sortedY = new float[size];
        _sortedR = new float[size];
    }
}
