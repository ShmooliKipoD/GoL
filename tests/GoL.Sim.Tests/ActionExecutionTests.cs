using System.Numerics;
using GoL.Sim.Acting;
using GoL.Sim.Components;
using GoL.Sim.Core;
using GoL.Sim.Genetics;
using GoL.Sim.Systems;

namespace GoL.Sim.Tests;

/// <summary>
/// Actions as things a creature <i>does</i>, with a beginning, a middle and an end.
/// <para>
/// These exist because a creature was watched holding its mouth open at empty air
/// while every readout reported it as eating. Nothing owned "eating", so nothing was
/// ever in a position to notice the biting was achieving nothing. The contract these
/// tests pin is that an action which cannot make progress has to say so.
/// </para>
/// </summary>
public class ActionExecutionTests
{
    private static (SimWorld World, LabEnvironment Lab) MakeLab(int seed = 5)
    {
        var config = new SimConfig { Seed = seed, WorldSize = 340f };
        var lab = new LabEnvironment(config);
        return (new SimWorld(config, lab), lab);
    }

    private static Genome Seed(ulong seed = 1)
    {
        var rng = new Pcg32(seed);
        return Genome.CreateSeed(ref rng);
    }

    /// <summary>
    /// The runner's <c>Aspect</c> requires <see cref="Doing"/>, and so does the
    /// readout. If <c>Spawn</c> ever stops attaching it, <c>ActiveEntities</c> is
    /// empty and every creature goes inert - which is indistinguishable on screen
    /// and in the soak report from creatures that simply have no actions registered.
    /// Cheap to assert, and it fails loudly instead of silently.
    /// </summary>
    [Fact]
    public void Spawn_AttachesDoing()
    {
        var (world, _) = MakeLab();
        int id = world.Spawn(Seed(), new Vector2(100f, 100f), 0f);

        var doing = world.Get<Doing>(id);

        Assert.NotNull(doing);
        Assert.False(doing.Active);
        Assert.Null(doing.Forced);

        // The read-only handle the renderers and lab go through must carry it too.
        Assert.Equal(Actions.IdleLabel, world.View(id).ActionLabel);
    }

    /// <summary>
    /// <c>LifecycleSystem</c> is the only thing that publishes <see cref="SimWorld.Living"/>,
    /// and <c>BoardEnvironment</c>, the creature index and the soak report all read
    /// it. Gutting that system's decision-making while accidentally dropping the
    /// publish would empty the list and read as a population of zero everywhere -
    /// again indistinguishable from creatures that are merely inert.
    /// </summary>
    [Fact]
    public void Living_IsPublished_EvenWithNoActionsRunning()
    {
        var (world, _) = MakeLab();

        for (int i = 0; i < 4; i++)
            world.Spawn(Seed((ulong)(i + 1)), new Vector2(60f + i * 30f, 100f), 0f);

        world.Step();

        Assert.Equal(4, world.Population);
        Assert.Equal(4, world.Living.Count);

        // Ascending by entity id - anything accumulated over this list depends on it.
        for (int i = 1; i < world.Living.Count; i++)
            Assert.True(world.Living[i] > world.Living[i - 1]);
    }

    /// <summary>
    /// The state of the world partway through Step 3g: execution is stripped and the
    /// actions are being written back one at a time. A creature still senses and
    /// thinks - its brain emits an intent every tick - but nothing acts on it.
    /// <para>
    /// This test is <b>expected to be replaced</b> as actions land, and that is the
    /// point: it is the marker for how much of the rebuild is done.
    /// </para>
    /// </summary>
    [Fact]
    public void WithNoActionsRegistered_CreaturesAreInert()
    {
        var (world, _) = MakeLab();
        int id = world.Spawn(Seed(), new Vector2(170f, 170f), 0f);

        var body = world.Get<Body>(id);
        var start = body.Position;

        for (int i = 0; i < 120; i++) world.Step();

        Assert.Equal(start, body.Position);
        Assert.Equal(0f, body.Speed);
        Assert.False(world.Get<Doing>(id).Active);
        Assert.Equal(0f, world.Get<Energy>(id).LifetimeIntake);
    }
}
