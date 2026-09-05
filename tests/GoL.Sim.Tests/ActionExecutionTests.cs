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
    /// Move and Turn are registered, so a creature goes somewhere. This replaces the
    /// inertness assertion that stood here while the table was empty.
    /// </summary>
    [Fact]
    public void WithMoveRegistered_ACreatureGoesSomewhere()
    {
        var (world, _) = MakeLab();
        int id = world.Spawn(Seed(), new Vector2(170f, 170f), 0f);

        var body = world.Get<Body>(id);
        var start = body.Position;

        for (int i = 0; i < 240; i++) world.Step();

        Assert.NotEqual(start, body.Position);
        Assert.True(world.Get<Doing>(id).Active);
    }

    /// <summary>
    /// The fall-through. A creature whose brain most wants an action that cannot run
    /// must take the next-best one, not freeze.
    /// <para>
    /// Measured, not argued: with Bite winning selection and no behaviour behind it,
    /// eight creatures over 2000 ticks sat idle 86.78% of the time. Falling through
    /// to Move and Turn took that to 0.05%. A population that stands still wanting to
    /// bite at nothing is not a subtle failure, but it is a silent one.
    /// </para>
    /// </summary>
    [Fact]
    public void AnActionThatCannotRun_DoesNotFreezeTheCreature()
    {
        var (world, _) = MakeLab();

        for (int i = 0; i < 6; i++)
            world.Spawn(Seed((ulong)(i + 1)), new Vector2(60f + i * 40f, 170f), i * 0.7f);

        int idleTicks = 0, total = 0;

        for (int tick = 0; tick < 400; tick++)
        {
            world.Step();

            foreach (int id in world.Living)
            {
                total++;
                if (!world.Get<Doing>(id).Active) idleTicks++;
            }
        }

        Assert.True(total > 0);

        // Bite has no behaviour yet and wins selection often; if that idled the
        // creature this would sit near the 87% that was actually measured.
        Assert.True(
            idleTicks < total / 10,
            $"idle {idleTicks}/{total} creature-ticks - the runner is not falling through");
    }

    /// <summary>
    /// A forced action deliberately does <b>not</b> fall through. Watching a pinned
    /// action fail to start is the diagnostic the lab's force key exists for, and
    /// silently running something else instead would destroy it.
    /// </summary>
    [Fact]
    public void AForcedAction_ReportsBlocked_RatherThanRunningSomethingElse()
    {
        var (world, _) = MakeLab();
        int id = world.Spawn(Seed(), new Vector2(170f, 170f), 0f);

        var doing = world.Get<Doing>(id);

        // Reproduce has no behaviour registered at this point in the rebuild.
        doing.Forced = CreatureAction.Reproduce;

        world.Step();

        Assert.Equal(CreatureAction.Reproduce, doing.Action);
        Assert.Equal(ActionStatus.Blocked, doing.Status);
        Assert.Contains("blocked", world.View(id).ActionLabelWithStatus);
    }
}
