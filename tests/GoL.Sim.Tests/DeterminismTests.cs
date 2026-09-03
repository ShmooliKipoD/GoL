using System.Numerics;
using GoL.Sim;
using GoL.Sim.Components;
using GoL.Sim.Core;
using GoL.Sim.Genetics;
using GoL.Sim.Systems;

namespace GoL.Sim.Tests;

/// <summary>
/// Reproducibility from a seed. Without it, "did the mutation system actually do
/// something" is unanswerable and no regression here can ever be tested.
/// </summary>
public class DeterminismTests
{
    private static SimWorld Build(int seed, int creatures = 12, int plants = 30)
    {
        var config = new SimConfig { Seed = seed, WorldSize = 340f, TicksPerSecond = 60 };
        var environment = new LabEnvironment(config);
        var world = new SimWorld(config, environment);

        environment.SeedPlants(plants);

        // A dedicated stream, so changing how the world is populated cannot shift
        // what any creature later draws.
        var rng = new Pcg32((ulong)seed, StreamId.World);
        for (int i = 0; i < creatures; i++)
        {
            world.Spawn(
                Genome.CreateSeed(ref rng),
                new Vector2(
                    rng.NextFloat(0f, environment.WorldSize),
                    rng.NextFloat(0f, environment.WorldSize)),
                rng.NextFloat(0f, MathF.Tau));
        }

        return world;
    }

    private static ulong RunTo(int seed, int ticks)
    {
        var world = Build(seed);
        for (int i = 0; i < ticks; i++) world.Step();
        return world.Hash();
    }

    [Fact]
    public void SameSeed_ProducesTheSameWorld_After2000Ticks()
        => Assert.Equal(RunTo(seed: 1234, ticks: 2000), RunTo(seed: 1234, ticks: 2000));

    /// <summary>
    /// Guards against the hash being accidentally constant, which would make the
    /// test above pass while checking nothing at all.
    /// </summary>
    [Fact]
    public void DifferentSeeds_ProduceDifferentWorlds()
        => Assert.NotEqual(RunTo(seed: 1, ticks: 500), RunTo(seed: 2, ticks: 500));

    [Fact]
    public void TheWorldActuallyChanges_AsItRuns()
    {
        var world = Build(7);

        for (int i = 0; i < 50; i++) world.Step();
        ulong early = world.Hash();

        for (int i = 0; i < 500; i++) world.Step();

        Assert.NotEqual(early, world.Hash());
    }

    /// <summary>
    /// A creature's random stream is keyed on (worldSeed, entityId, birthTick), so
    /// two creatures can never share a sequence - not even when a recycled entity
    /// slot hands a newborn the id of something that just died.
    /// </summary>
    [Fact]
    public void EachCreature_GetsItsOwnRandomStream()
    {
        var world = Build(3, creatures: 6);
        var seen = new HashSet<uint>();

        foreach (int id in world.Living)
        {
            ref var rng = ref world.Get<RandomSource>(id).Rng;
            Assert.True(seen.Add(rng.NextUInt()), $"creature {id} shares a stream");
        }
    }

    /// <summary>
    /// The tick must never allocate once warmed up: at 60 Hz over hundreds of
    /// creatures, per-tick garbage would show up as periodic stalls.
    /// </summary>
    [Fact]
    public void SteadyStateTick_DoesNotAllocate()
    {
        var world = Build(11, creatures: 20);

        for (int i = 0; i < 200; i++) world.Step();   // warm up every buffer

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++) world.Step();
        long perTick = (GC.GetAllocatedBytesForCurrentThread() - before) / 100;

        // Births legitimately allocate - components are reference types, which is
        // MonoGame.Extended's ComponentMapper<T> constraint, not a choice. This
        // bound allows for the occasional birth while still catching a per-creature
        // allocation in the hot path.
        Assert.True(perTick < 4096, $"{perTick} bytes allocated per tick");
    }
}
