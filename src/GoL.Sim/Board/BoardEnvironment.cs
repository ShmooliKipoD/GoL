using System;
using GoL.Sim.Components;
using GoL.Sim.Core;
using GoL.Sim.Genetics;
using GoL.Sim.Systems;

namespace GoL.Sim.Board;

/// <summary>
/// The real world: vegetation on a grid, ground fertility, diffusing scent, and a
/// spatial hash over the creatures.
/// <para>
/// Replaces <see cref="LabEnvironment"/> behind the same <see cref="IEnvironment"/>
/// and <see cref="ISenseField"/>, with no change to any system or to creature code -
/// which is what the Step 2 seam existed to make possible.
/// </para>
/// </summary>
public sealed class BoardEnvironment : IEnvironment, ISenseField
{
    /// <summary>Scent channels: two for creature glands, one reserved for blightcap.</summary>
    public const int ScentChannels = 3;

    private readonly PlantGrid _plants;
    private readonly FertilityField _fertility;
    private readonly PheromoneField _scent;
    private readonly SpatialHash _creatures;

    private Pcg32 _rng;
    private int[] _queryScratch = new int[256];

    public BoardEnvironment(SimConfig config)
    {
        WorldSize = config.WorldSize;
        Toroidal = config.Toroidal;

        _rng = new Pcg32((ulong)(config.Seed == 0 ? 20260903 : config.Seed), StreamId.Plants);

        int plantResolution = Math.Max(16, (int)(WorldSize / 8f));
        _plants = new PlantGrid(WorldSize, plantResolution, Toroidal);
        _fertility = new FertilityField(64, plantResolution, ref _rng);
        _scent = new PheromoneField(WorldSize, 128, ScentChannels, Toroidal);

        // Cell size a little above the largest sensible body, so a query sweeps a
        // 3x3 neighbourhood rather than a wide block.
        _creatures = new SpatialHash(WorldSize, 32f, Toroidal);

        Seed(config.PlantDensity);
    }

    public ISenseField SenseField => this;

    public float WorldSize { get; }
    public bool Toroidal { get; }

    public PlantGrid Plants => _plants;
    public FertilityField Fertility => _fertility;
    public PheromoneField Scent => _scent;

    /// <summary>Set by the simulation: the environment cannot enumerate creatures
    /// itself, since the ECS owns them.</summary>
    public ICreatureIndex Creatures { get; set; } = EmptyCreatureIndex.Instance;

    private long _tick;

    // --- IEnvironment ---

    public void Step(SimWorld world, float dt)
    {
        _tick++;

        // Diffusion cost is fixed and population-independent, so it runs at a
        // quarter rate. Emission still happens every tick.
        if (_tick % PheromoneField.StepInterval == 0)
            _scent.Step(dt * PheromoneField.StepInterval);

        if (_tick % PlantGrid.BucketCount == 0)
            _fertility.Step(dt * PlantGrid.BucketCount);

        // One bucket per tick: a flat cost that does not grow with how much is
        // growing, and a full sweep every 16 ticks.
        _plants.StepBucket((int)(_tick % PlantGrid.BucketCount), _fertility, dt, ref _rng);

        RebuildCreatureIndex(world);
    }

    /// <summary>
    /// Rebuilt after the previous tick's movement and before this tick's sensing.
    /// A mid-loop rebuild would let queries see a torn index.
    /// </summary>
    private void RebuildCreatureIndex(SimWorld world)
    {
        _creatures.Clear();

        var living = world.Living;
        for (int i = 0; i < living.Count; i++)
        {
            int id = living[i];
            var body = world.Get<Body>(id);
            _creatures.Add(id, body.Position, body.Radius);
        }

        _creatures.Build();
        CreatureLookup = world;
    }

    /// <summary>
    /// How the sense queries resolve an entity id back to its components. Set on the
    /// first <see cref="Step"/>, which runs before any sensing in the same tick.
    /// </summary>
    public SimWorld? CreatureLookup { get; private set; }

    public void Emit(Vector2 position, int channel, float amount)
        => _scent.Emit(position, channel, amount);

    public void ResolveBite(
        SimWorld world, Body body, Energy energy, Genome genome, Mind mind, float dt)
    {
        float reach = body.Radius + genome.Trait(TraitAxis.MouthReach);
        float arc = genome.Trait(TraitAxis.MouthArc);

        int best = FindBiteTarget(body, reach, arc);
        if (best < 0) return;

        var kind = _plants.KindAt(best);

        float take = _plants.Consume(best, Metabolism.BiteRate * dt);
        if (take <= 0f) return;

        float digestibility = PlantSpecs.Digestibility(kind, genome);
        energy.Current = MathF.Min(energy.Maximum, energy.Current + take * digestibility);

        mind.BitThisTick = true;
    }

    /// <summary>Nearest plant inside the mouth arc, or -1.</summary>
    private int FindBiteTarget(Body body, float reach, float arc)
    {
        int span = Math.Max(1, (int)MathF.Ceiling(reach / _plants.CellSize));

        int centre = _plants.IndexAt(body.Position);
        if (centre < 0) return -1;

        int cx = centre % _plants.Resolution;
        int cy = centre / _plants.Resolution;

        int best = -1;
        float bestDistance = float.MaxValue;

        for (int dy = -span; dy <= span; dy++)
        {
            for (int dx = -span; dx <= span; dx++)
            {
                int index = NeighbourIndex(cx + dx, cy + dy);
                if (index < 0) continue;

                var kind = _plants.KindAt(index);
                if (kind == PlantKind.None) continue;
                if (_plants.EnergyAt(index) <= 0.05f) continue;

                var offset = Offset(body.Position, _plants.CentreOf(index));
                float distance = offset.Length() - PlantSpecs.Get(kind).Radius;
                if (distance > reach) continue;

                // The mouth is an arc, not a ring: it must face its food.
                float relative = Senses.WrapAngle(MathF.Atan2(offset.Y, offset.X) - body.Heading);
                if (MathF.Abs(relative) > arc) continue;

                if (distance < bestDistance) { bestDistance = distance; best = index; }
            }
        }

        return best;
    }

    private int NeighbourIndex(int x, int y)
    {
        int r = _plants.Resolution;

        if (Toroidal)
        {
            x %= r; if (x < 0) x += r;
            y %= r; if (y < 0) y += r;
        }
        else if (x < 0 || y < 0 || x >= r || y >= r) return -1;

        return y * r + x;
    }

    /// <summary>Leaves a corpse. Carrion is what makes carnivory pay off before a
    /// lineage can reliably hunt.</summary>
    public void OnDeath(Vector2 position, float radius, float remainingEnergy)
    {
        int index = _plants.IndexAt(position);
        if (index < 0) return;
        if (_plants.KindAt(index) != PlantKind.None) return;

        float mass = (radius / 8f) * (radius / 8f);
        _plants.Plant(index, PlantKind.Carrion, remainingEnergy * 0.6f + mass * 8f, maturity: 1f);
    }

    // --- ISenseField ---

    public int Query(Vector2 centre, float radius, int excludeCreatureId, Span<Percept> results)
    {
        int count = QueryPlants(centre, radius, results);
        count += QueryCreatures(centre, radius, excludeCreatureId, results[count..]);
        return count;
    }

    public int RayCastPlant(
        Vector2 origin, Vector2 direction, float maxDistance,
        out float distance, out float radius)
    {
        int index = _plants.RayCast(Wrap(origin), direction, maxDistance, out distance);
        radius = index < 0 ? 0f : PlantSpecs.Get(_plants.KindAt(index)).Radius;
        return index;
    }

    private int QueryPlants(Vector2 centre, float radius, Span<Percept> results)
    {
        int span = Math.Max(1, (int)MathF.Ceiling((radius + 5f) / _plants.CellSize));

        int origin = _plants.IndexAt(centre);
        if (origin < 0) return 0;

        int cx = origin % _plants.Resolution;
        int cy = origin / _plants.Resolution;

        int count = 0;

        // Ascending cell index, which is a stable order - so anything accumulated
        // over these results is reproducible.
        for (int dy = -span; dy <= span; dy++)
        {
            for (int dx = -span; dx <= span; dx++)
            {
                if (count >= results.Length) return count;

                int index = NeighbourIndex(cx + dx, cy + dy);
                if (index < 0) continue;

                var kind = _plants.KindAt(index);
                if (kind == PlantKind.None || _plants.EnergyAt(index) <= 0.05f) continue;

                var position = _plants.CentreOf(index);
                float plantRadius = PlantSpecs.Get(kind).Radius;

                var offset = Offset(centre, position);
                float reach = radius + plantRadius;
                if (offset.LengthSquared() > reach * reach) continue;

                results[count++] = new Percept(position, plantRadius, SeenKind.Plant, 0f, index);
            }
        }

        return count;
    }

    public int QueryCreatures(Vector2 centre, float radius, int excludeId, Span<Percept> results)
    {
        var world = CreatureLookup;
        if (world is null) return 0;

        if (_queryScratch.Length < results.Length) _queryScratch = new int[results.Length];

        int found = _creatures.Query(centre, radius, excludeId, _queryScratch);
        int count = 0;

        for (int i = 0; i < found && count < results.Length; i++)
        {
            int id = _queryScratch[i];
            var body = world.Get<Body>(id);

            results[count++] = new Percept(
                body.Position, body.Radius, SeenKind.Creature,
                world.Get<Genes>(id).Genome.Normalized(TraitAxis.Hue), id);
        }

        return count;
    }

    public float SampleScent(Vector2 position, int channel) => _scent.Sample(position, channel);

    public float SampleFertility(Vector2 position)
    {
        int index = _plants.IndexAt(position);
        return index < 0 ? 0f : _fertility.At(index);
    }

    public Vector2 Offset(Vector2 from, Vector2 to)
    {
        var delta = to - from;
        if (!Toroidal) return delta;

        // The shorter way round, or creatures go blind at the world seam.
        float half = WorldSize * 0.5f;
        if (delta.X > half) delta.X -= WorldSize;
        else if (delta.X < -half) delta.X += WorldSize;
        if (delta.Y > half) delta.Y -= WorldSize;
        else if (delta.Y < -half) delta.Y += WorldSize;
        return delta;
    }

    public Vector2 Wrap(Vector2 position)
    {
        if (!Toroidal)
        {
            return new Vector2(
                Math.Clamp(position.X, 0f, WorldSize),
                Math.Clamp(position.Y, 0f, WorldSize));
        }

        float x = position.X % WorldSize;
        float y = position.Y % WorldSize;
        if (x < 0f) x += WorldSize;
        if (y < 0f) y += WorldSize;
        return new Vector2(x, y);
    }

    /// <summary>
    /// Scatters starting vegetation. Each kind is seeded only where its soil suits
    /// it, so the initial map already has the structure the spread rules maintain.
    /// </summary>
    private void Seed(float density)
    {
        int target = (int)(_plants.CellCount * Math.Clamp(density, 0f, 0.9f));

        for (int i = 0; i < target; i++)
        {
            int index = _rng.NextInt(_plants.CellCount);
            if (_plants.KindAt(index) != PlantKind.None) continue;

            float soil = _fertility.At(index);
            var kind = PickKind(soil, ref _rng);
            var spec = PlantSpecs.Get(kind);

            if (soil < spec.MinFertility || soil > spec.MaxFertility) continue;

            _plants.Plant(index, kind, spec.MaxEnergy * _rng.NextFloat(0.3f, 1f),
                maturity: _rng.NextFloat());
        }
    }

    private static PlantKind PickKind(float soil, ref Pcg32 rng)
    {
        float roll = rng.NextFloat();

        // Poor ground grows blightcap and bramble; good ground grows fruit.
        if (soil < 0.25f) return roll < 0.5f ? PlantKind.Blightcap : PlantKind.Bramble;
        if (soil > 0.55f && roll < 0.12f) return PlantKind.Fruit;
        return roll < 0.75f ? PlantKind.Grass : PlantKind.Bramble;
    }
}
