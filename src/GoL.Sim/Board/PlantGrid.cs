using System;
using GoL.Sim.Core;

namespace GoL.Sim.Board;

/// <summary>
/// The board's vegetation, as a grid rather than as entities.
/// <para>
/// At most one plant per cell, so lookup is <c>O(1)</c> indexing and there is no
/// second spatial structure to rebuild. Tens of thousands of cells as ECS entities
/// would be a large regression for no gain - "prefer ECS" is not "everything is an
/// entity"; dense uniform data stays in arrays.
/// </para>
/// <para>
/// Growth is <b>bucketed</b>: the grid is split into 16 buckets and one is swept per
/// tick, so a full pass completes every 16 ticks at a flat cost that does not depend
/// on how much is growing. Touching every cell every frame would dominate the tick.
/// </para>
/// </summary>
public sealed class PlantGrid
{
    /// <summary>Ticks to sweep the whole grid. Growth is scaled by this, so the
    /// bucketing is invisible in the resulting growth rate.</summary>
    public const int BucketCount = 16;

    private readonly PlantKind[] _kind;
    private readonly float[] _energy;
    private readonly float[] _maturity;
    private readonly float _worldSize;
    private readonly bool _toroidal;

    public PlantGrid(float worldSize, int resolution, bool toroidal)
    {
        _worldSize = worldSize;
        _toroidal = toroidal;

        Resolution = resolution;
        CellSize = worldSize / resolution;

        int cells = resolution * resolution;
        _kind = new PlantKind[cells];
        _energy = new float[cells];
        _maturity = new float[cells];
    }

    public int Resolution { get; }
    public float CellSize { get; }
    public int CellCount => _kind.Length;

    public PlantKind KindAt(int index) => _kind[index];
    public float EnergyAt(int index) => _energy[index];

    /// <summary>Cells currently holding a plant. The soak runner's early warning:
    /// a world being stripped shows here long before it shows in the population.</summary>
    public int LiveCount()
    {
        int n = 0;
        for (int i = 0; i < _kind.Length; i++) if (_kind[i] != PlantKind.None) n++;
        return n;
    }
    public float MaturityAt(int index) => _maturity[index];

    public Vector2 CentreOf(int index)
    {
        int x = index % Resolution;
        int y = index / Resolution;
        return new Vector2((x + 0.5f) * CellSize, (y + 0.5f) * CellSize);
    }

    public int IndexAt(Vector2 position)
    {
        int x = Wrap((int)MathF.Floor(position.X / CellSize));
        int y = Wrap((int)MathF.Floor(position.Y / CellSize));
        return x < 0 || y < 0 ? -1 : y * Resolution + x;
    }

    public void Plant(int index, PlantKind kind, float energy, float maturity = 0f)
    {
        _kind[index] = kind;
        _energy[index] = energy;
        _maturity[index] = maturity;
    }

    public void Clear(int index)
    {
        _kind[index] = PlantKind.None;
        _energy[index] = 0f;
        _maturity[index] = 0f;
    }

    /// <summary>Takes energy from a plant, clearing the cell if it is stripped.</summary>
    public float Consume(int index, float amount)
    {
        float taken = MathF.Min(amount, _energy[index]);
        _energy[index] -= taken;

        // Eaten is eaten - for every kind, not just carrion. A stripped plant used
        // to stay in place at zero energy and refill from its roots, which left an
        // invisible green a creature could sit on forever, grazing regrowth for less
        // than its own upkeep. Clearing the cell means food has to be found again,
        // which is what makes foraging a behaviour worth evolving.
        if (_energy[index] <= 0.05f) Clear(index);

        return taken;
    }

    /// <summary>
    /// Grows and spreads one bucket. Called once per tick with the bucket that is
    /// due, so every cell is visited once per <see cref="BucketCount"/> ticks.
    /// </summary>
    public void StepBucket(int bucket, FertilityField fertility, float dt, ref Pcg32 rng)
    {
        // Growth is charged for the whole sweep interval, so the bucketing does not
        // change how fast anything actually grows.
        float sweepDt = dt * BucketCount;

        for (int index = bucket; index < _kind.Length; index += BucketCount)
        {
            var kind = _kind[index];
            if (kind == PlantKind.None) continue;

            var spec = PlantSpecs.Get(kind);

            if (kind == PlantKind.Carrion)
            {
                // Corpses rot rather than grow.
                _energy[index] -= 2.0f * sweepDt;
                if (_energy[index] <= 0.05f) Clear(index);
                continue;
            }

            float soil = fertility.At(index);

            // A plant that has exhausted its own ground dies off. Without this
            // nothing removes a plant except being eaten, so vegetation only ever
            // spreads and the map carpets over - food everywhere, no reason to
            // forage, and the "sit on grass and bud forever" strategy wins.
            // Reusing MinFertility means the die-off line is the same line that
            // decides where the kind may grow at all, so a patch that drains its
            // soil below what it needs vacates the ground and lets it recover:
            // boom-and-bust patches that move, which is what the fertility field
            // was built for.
            if (soil < spec.MinFertility * DieOffMargin)
            {
                Clear(index);
                continue;
            }

            _energy[index] = MathF.Min(spec.MaxEnergy, _energy[index] + spec.GrowthRate * sweepDt * soil);
            _maturity[index] = MathF.Min(1f, _maturity[index] + spec.MaturityRate * sweepDt);

            fertility.Drain(index, spec.FertilityDrain * sweepDt);

            if (_maturity[index] >= 1f && rng.Chance(spec.SpreadChance))
                TrySpread(index, kind, spec, fertility, ref rng);
        }

        AmbientSeed(bucket, fertility, sweepDt, ref rng);
    }

    /// <summary>Chance per empty cell per sweep that a kind appears from nowhere.</summary>
    private const float AmbientSeedChance = 0.00004f;

    /// <summary>
    /// How far below its own <c>MinFertility</c> a plant is allowed to sit before it
    /// dies. Below 1 so the die-off line is under the line for seeding, leaving a
    /// band where a plant survives on ground it could not have colonised - without
    /// that gap, cells flicker between planted and cleared on the boundary.
    /// </summary>
    private const float DieOffMargin = 0.8f;

    /// <summary>
    /// A floor against extinction. Spread only ever fills a cell next to a living
    /// plant, so once greens became destructible the last plant of a kind could be
    /// eaten and that kind would be gone for the rest of the run - quietly turning
    /// the locked niches (bramble, blightcap) into dead content.
    /// <para>
    /// Deliberately rare: spread from living plants stays the normal mechanism, and
    /// this is only the path back from zero. Soil still decides what can grow where,
    /// so it cannot put a plant somewhere the world would not otherwise support.
    /// </para>
    /// </summary>
    private void AmbientSeed(int bucket, FertilityField fertility, float sweepDt, ref Pcg32 rng)
    {
        for (int index = bucket; index < _kind.Length; index += BucketCount)
        {
            if (_kind[index] != PlantKind.None) continue;
            if (!rng.Chance(AmbientSeedChance * sweepDt)) continue;

            float soil = fertility.At(index);

            // Ascending kind order, so which kind wins is reproducible.
            foreach (var spec in PlantSpecs.Seedable)
            {
                if (soil < spec.MinFertility || soil > spec.MaxFertility) continue;

                Plant(index, spec.Kind, spec.MaxEnergy * 0.15f);
                break;
            }
        }
    }

    /// <summary>Seeds one of the eight neighbouring cells, if it is empty and the
    /// soil suits this kind.</summary>
    private void TrySpread(int index, PlantKind kind, PlantSpec spec, FertilityField fertility, ref Pcg32 rng)
    {
        int x = index % Resolution;
        int y = index / Resolution;

        int direction = rng.NextInt(8);
        int dx = direction switch { 0 or 3 or 5 => -1, 2 or 4 or 7 => 1, _ => 0 };
        int dy = direction switch { 0 or 1 or 2 => -1, 5 or 6 or 7 => 1, _ => 0 };

        int nx = Wrap(x + dx);
        int ny = Wrap(y + dy);
        if (nx < 0 || ny < 0) return;

        int target = ny * Resolution + nx;
        if (_kind[target] != PlantKind.None) return;

        float soil = fertility.At(target);
        if (soil < spec.MinFertility || soil > spec.MaxFertility) return;

        // A seedling, not a copy: it has to grow and mature before it can spread on.
        Plant(target, kind, spec.MaxEnergy * 0.15f);
    }

    /// <summary>
    /// Walks the grid cell by cell along a ray (a DDA march) and returns the first
    /// occupied cell, or -1.
    /// <para>
    /// Chosen over gathering everything in range because the cost depends on how far
    /// the ray travels, not on the area it could have covered: about sixteen cells
    /// for a long ray, against roughly twelve hundred for the equivalent square
    /// sweep. It also gives occlusion for nothing - the march stops at the first
    /// hit, which is precisely what a vision bin wants to report.
    /// </para>
    /// </summary>
    public int RayCast(Vector2 origin, Vector2 direction, float maxDistance, out float distance)
    {
        distance = maxDistance;

        float dx = direction.X;
        float dy = direction.Y;
        if (dx * dx + dy * dy < 1e-9f) return -1;

        int x = (int)MathF.Floor(origin.X / CellSize);
        int y = (int)MathF.Floor(origin.Y / CellSize);

        int stepX = dx > 0f ? 1 : -1;
        int stepY = dy > 0f ? 1 : -1;

        // Distance along the ray to the next cell boundary on each axis, and the
        // distance between successive boundaries.
        float invX = MathF.Abs(dx) < 1e-9f ? float.MaxValue : 1f / MathF.Abs(dx);
        float invY = MathF.Abs(dy) < 1e-9f ? float.MaxValue : 1f / MathF.Abs(dy);

        float deltaX = invX * CellSize;
        float deltaY = invY * CellSize;

        float nextBoundaryX = (dx > 0f ? (x + 1) * CellSize - origin.X : origin.X - x * CellSize);
        float nextBoundaryY = (dy > 0f ? (y + 1) * CellSize - origin.Y : origin.Y - y * CellSize);

        float travelX = nextBoundaryX * invX;
        float travelY = nextBoundaryY * invY;

        float travelled = 0f;

        // A generous but finite bound: a ray cannot legitimately cross more cells
        // than this, and an unbounded loop here would hang the tick.
        int maxSteps = (int)(maxDistance / CellSize) * 2 + 4;

        for (int step = 0; step < maxSteps && travelled <= maxDistance; step++)
        {
            int cx = Wrap(x);
            int cy = Wrap(y);

            if (cx < 0 || cy < 0) return -1;   // left a non-wrapping world

            int index = cy * Resolution + cx;
            if (_kind[index] != PlantKind.None && _energy[index] > 0.05f)
            {
                distance = travelled;
                return index;
            }

            if (travelX < travelY)
            {
                travelled = travelX;
                travelX += deltaX;
                x += stepX;
            }
            else
            {
                travelled = travelY;
                travelY += deltaY;
                y += stepY;
            }
        }

        return -1;
    }

    private int Wrap(int c)
    {
        if (!_toroidal) return c >= 0 && c < Resolution ? c : -1;

        c %= Resolution;
        return c < 0 ? c + Resolution : c;
    }
}
