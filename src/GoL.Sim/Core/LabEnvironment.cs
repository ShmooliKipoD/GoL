using System;
using System.Collections.Generic;
using GoL.Sim.Components;
using GoL.Sim.Genetics;
using GoL.Sim.Systems;

namespace GoL.Sim.Core;

/// <summary>A morsel of food in the lab.</summary>
public sealed class LabPlant
{
    public int Id;
    public Vector2 Position;
    public float Radius = 4f;
    public float Energy = 35f;
    public float MaxEnergy = 35f;
    public bool Alive => Energy > 0.05f;

    /// <summary>Simulation time this plant comes back, or -1 if it is not gone.
    /// <para>
    /// A consumed green used to reappear instantly, elsewhere, at full energy - so
    /// nothing ever visibly ran out. It now stays gone for a while and returns as a
    /// seedling that has to grow.
    /// </para></summary>
    public float RespawnAt = -1f;
}

/// <summary>
/// A minimal environment for the Creature Lab: a few hand-placed plants,
/// brute-force queries, and a pheromone grid that decays but does not diffuse.
/// <para>
/// <b>Deliberately throwaway.</b> The board environment replaces it behind the same
/// <see cref="IEnvironment"/> and <see cref="ISenseField"/>. If that swap requires
/// changing a system, the seam was drawn in the wrong place.
/// </para>
/// </summary>
public sealed class LabEnvironment : IEnvironment, ISenseField
{
    private const int ScentChannels = 2;
    private const int ScentGrid = 32;

    private readonly float[] _scent = new float[ScentChannels * ScentGrid * ScentGrid];
    private readonly List<LabPlant> _plants = new();
    private Pcg32 _rng;
    private int _nextPlantId;

    public LabEnvironment(SimConfig config)
    {
        // A small arena: the lab is for inspecting one creature, so the view can be
        // scaled up enough to see its eyes and mouth arc.
        WorldSize = MathF.Min(config.WorldSize, 340f);
        Toroidal = config.Toroidal;
        _rng = new Pcg32((ulong)(config.Seed == 0 ? 12345 : config.Seed), StreamId.Plants);
    }

    public ISenseField SenseField => this;

    public float WorldSize { get; }
    public bool Toroidal { get; }

    public IReadOnlyList<LabPlant> Plants => _plants;

    public LabPlant AddPlant(Vector2 position, float energy = 35f)
    {
        var plant = new LabPlant
        {
            Id = _nextPlantId++,
            Position = position,
            Energy = energy,
            MaxEnergy = energy,
        };
        _plants.Add(plant);
        return plant;
    }

    public void SeedPlants(int count)
    {
        for (int i = 0; i < count; i++)
            AddPlant(new Vector2(_rng.NextFloat(0f, WorldSize), _rng.NextFloat(0f, WorldSize)));
    }

    // --- IEnvironment ---

    public void Step(SimWorld world, float dt)
    {
        DecayScent(dt);

        foreach (var plant in _plants)
        {
            if (plant.RespawnAt >= 0f)
            {
                if (world.SimTime < plant.RespawnAt) continue;

                // Back as a seedling somewhere new, not as a full plant in place.
                plant.Position = new Vector2(
                    _rng.NextFloat(0f, WorldSize), _rng.NextFloat(0f, WorldSize));
                plant.Energy = plant.MaxEnergy * 0.15f;
                plant.RespawnAt = -1f;
                continue;
            }

            // Only living plants regrow. This used to run over every plant in the
            // list including stripped ones, so an eaten green silently came back
            // from zero - the same zombie-green problem the board grid had.
            if (!plant.Alive) continue;
            plant.Energy = MathF.Min(plant.MaxEnergy, plant.Energy + 1.2f * dt);
        }
    }

    public void ResolveBite(
        SimWorld world, Body body, Energy energy, Genome genome, Mind mind, float dt)
    {
        float reach = body.Radius + genome.Trait(TraitAxis.MouthReach);
        float arc = genome.Trait(TraitAxis.MouthArc);

        // Finish what the mouth started, while it lasts and stays in reach.
        LabPlant? best = Latched(mind.BiteTarget, body, reach, arc);
        float bestDistance = float.MaxValue;

        if (best is null) foreach (var plant in _plants)
        {
            if (plant.Energy < Metabolism.WorthBiting) continue;

            var offset = Offset(body.Position, plant.Position);
            float distance = offset.Length() - plant.Radius;
            if (distance > reach) continue;

            float relative = Senses.WrapAngle(MathF.Atan2(offset.Y, offset.X) - body.Heading);
            if (MathF.Abs(relative) > arc) continue;

            if (distance < bestDistance) { bestDistance = distance; best = plant; }
        }

        mind.BiteTarget = best?.Id ?? -1;
        if (best is null) return;

        float take = MathF.Min(Metabolism.BiteRate * dt, best.Energy);
        best.Energy -= take;
        energy.Gain(take * genome.Trait(TraitAxis.DigestGrass));
        mind.BitThisTick = true;

        // Eaten greens are gone. One seeds elsewhere later so the arena cannot
        // empty - the lab exists to demonstrate feeding, repeatedly.
        if (!best.Alive)
        {
            best.Energy = 0f;
            best.RespawnAt = world.SimTime + RespawnDelay;
            mind.BiteTarget = -1;
        }
    }

    /// <summary>Seconds a consumed green stays gone before one returns elsewhere.
    /// Long enough that the arena visibly thins where a creature has been feeding.
    /// </summary>
    private const float RespawnDelay = 6f;

    /// <summary>The latched plant, if it is still worth finishing and in reach.</summary>
    private LabPlant? Latched(int id, Body body, float reach, float arc)
    {
        if (id < 0 || id >= _plants.Count) return null;

        var plant = _plants[id];

        // Any energy at all keeps the latch - WorthBiting governs what a mouth will
        // pick, not whether it finishes what it started.
        if (plant.Energy <= 0f) return null;

        var offset = Offset(body.Position, plant.Position);
        if (offset.Length() - plant.Radius > reach) return null;

        float relative = Senses.WrapAngle(MathF.Atan2(offset.Y, offset.X) - body.Heading);
        return MathF.Abs(relative) <= arc ? plant : null;
    }

    /// <summary>The lab leaves no corpse; carrion is a board concern.</summary>
    public void OnDeath(Vector2 position, float radius, float remainingEnergy) { }

    // --- ISenseField ---

    public int Query(Vector2 centre, float radius, int excludeCreatureId, Span<Percept> results)
    {
        int count = 0;

        // Plants in id order - a stable order, so any float accumulated over these
        // results is reproducible.
        foreach (var plant in _plants)
        {
            if (!plant.Alive) continue;
            if (count >= results.Length) return count;

            var offset = Offset(centre, plant.Position);
            float reach = radius + plant.Radius;
            if (offset.LengthSquared() > reach * reach) continue;

            results[count++] = new Percept(plant.Position, plant.Radius, SeenKind.Plant, 0f, plant.Id);
        }

        count += QueryCreatures(centre, radius, excludeCreatureId, results[count..]);
        return count;
    }

    public int QueryCreatures(Vector2 centre, float radius, int excludeId, Span<Percept> results)
        => Creatures.Query(centre, radius, excludeId, results);

    /// <summary>Brute force over a dozen plants. The board's grid march is what this
    /// stands in for; at this scale the difference does not matter.</summary>
    public int RayCastPlant(
        Vector2 origin, Vector2 direction, float maxDistance,
        out float distance, out float radius)
    {
        distance = maxDistance;
        radius = 0f;
        int hit = -1;

        foreach (var plant in _plants)
        {
            if (!plant.Alive) continue;

            var offset = Offset(origin, plant.Position);
            float along = offset.X * direction.X + offset.Y * direction.Y;
            if (along <= 0f || along > maxDistance) continue;

            // Perpendicular distance from the plant's centre to the ray.
            float perpX = offset.X - direction.X * along;
            float perpY = offset.Y - direction.Y * along;
            if (perpX * perpX + perpY * perpY > plant.Radius * plant.Radius) continue;

            if (along < distance)
            {
                distance = along;
                radius = plant.Radius;
                hit = plant.Id;
            }
        }

        return hit;
    }

    /// <summary>How creatures are found. Set by the simulation at start-up, because
    /// the environment does not own the entities - the ECS does.</summary>
    public ICreatureIndex Creatures { get; set; } = EmptyCreatureIndex.Instance;

    public float SampleScent(Vector2 position, int channel)
    {
        if (channel < 0 || channel >= ScentChannels) return 0f;
        var (x, y) = Cell(position);
        return _scent[Index(channel, x, y)];
    }

    /// <summary>Flat in the lab. A real fertility field is a board feature.</summary>
    public float SampleFertility(Vector2 position) => 0.5f;

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

    // --- scent field ---

    public void Emit(Vector2 position, int channel, float amount)
    {
        if (channel < 0 || channel >= ScentChannels) return;
        var (x, y) = Cell(position);
        int i = Index(channel, x, y);
        _scent[i] = MathF.Min(1f, _scent[i] + amount);
    }

    private void DecayScent(float dt)
    {
        float keep = MathF.Max(0f, 1f - 0.35f * dt);
        for (int i = 0; i < _scent.Length; i++)
        {
            _scent[i] *= keep;
            if (_scent[i] < 1e-4f) _scent[i] = 0f;   // denormals would crawl
        }
    }

    public float ScentAt(int channel, int x, int y) => _scent[Index(channel, x, y)];
    public int ScentResolution => ScentGrid;
    public float ScentCellSize => WorldSize / ScentGrid;

    private (int X, int Y) Cell(Vector2 position)
    {
        var wrapped = Wrap(position);
        int x = Math.Clamp((int)(wrapped.X / WorldSize * ScentGrid), 0, ScentGrid - 1);
        int y = Math.Clamp((int)(wrapped.Y / WorldSize * ScentGrid), 0, ScentGrid - 1);
        return (x, y);
    }

    private static int Index(int channel, int x, int y) =>
        channel * ScentGrid * ScentGrid + y * ScentGrid + x;
}

/// <summary>
/// How an environment finds creatures. The ECS owns the entities, so the
/// environment cannot enumerate them itself - the simulation supplies this.
/// </summary>
public interface ICreatureIndex
{
    int Query(Vector2 centre, float radius, int excludeCreatureId, Span<Percept> results);
}

internal sealed class EmptyCreatureIndex : ICreatureIndex
{
    public static readonly EmptyCreatureIndex Instance = new();
    public int Query(Vector2 c, float r, int exclude, Span<Percept> results) => 0;
}
