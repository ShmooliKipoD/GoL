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
    private static (SimWorld World, SandboxEnvironment Sandbox) MakeSandbox(int seed = 5)
    {
        var config = new SimConfig { Seed = seed, WorldSize = 340f };
        var sandbox = new SandboxEnvironment(config);
        return (new SimWorld(config, sandbox), sandbox);
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
        var (world, _) = MakeSandbox();
        int id = world.Spawn(Seed(), new Vector2(100f, 100f), 0f);

        var doing = world.Get<Doing>(id);

        Assert.NotNull(doing);
        Assert.False(doing.Active);
        Assert.Null(doing.Forced);

        // The read-only handle the renderers and sandbox go through must carry it too.
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
        var (world, _) = MakeSandbox();

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
        var (world, _) = MakeSandbox();
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
        var (world, _) = MakeSandbox();

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
    /// action fail to start is the diagnostic the sandbox's force key exists for, and
    /// silently running something else instead would destroy it.
    /// </summary>
    [Fact]
    public void AForcedAction_ReportsBlocked_RatherThanRunningSomethingElse()
    {
        var (world, _) = MakeSandbox();
        int id = world.Spawn(Seed(), new Vector2(170f, 170f), 0f);

        var doing = world.Get<Doing>(id);

        // Reproduce has no behaviour registered at this point in the rebuild.
        doing.Forced = CreatureAction.Reproduce;

        world.Step();

        Assert.Equal(CreatureAction.Reproduce, doing.Action);
        Assert.Equal(ActionStatus.Blocked, doing.Status);
        Assert.Contains("blocked", world.View(id).ActionLabelWithStatus);
    }

    // --- Eat -------------------------------------------------------------------

    /// <summary>Puts the subject at a known place with a forced action, so a single
    /// behaviour can be watched without waiting for an unevolved brain to pick it.</summary>
    private static (SimWorld World, SandboxEnvironment Sandbox, int Id, Doing Doing) Subject(
        Vector2 at, float heading = 0f, int seed = 5)
    {
        var (world, sandbox) = MakeSandbox(seed);
        int id = world.Spawn(Seed(), at, heading);
        var doing = world.Get<Doing>(id);
        doing.Forced = CreatureAction.Bite;
        return (world, sandbox, id, doing);
    }

    /// <summary>
    /// <b>The complaint, pinned as a contract.</b> The owner watched a creature bite
    /// with nothing in reach and gain nothing, while the readout called it feeding.
    /// An empty arena is the sharpest version of that: there is nothing to eat
    /// anywhere, and eating must say so rather than run forever.
    /// </summary>
    [Fact]
    public void Eat_WithNothingInSight_ReportsBlocked()
    {
        var (world, _, id, doing) = Subject(new Vector2(170f, 170f));

        world.Step();

        Assert.Equal(CreatureAction.Bite, doing.Action);
        Assert.Equal(ActionStatus.Blocked, doing.Status);
        Assert.Equal(0f, world.Get<Energy>(id).LifetimeIntake);

        // And it must not read like feeding.
        Assert.Contains("blocked", world.View(id).ActionLabelWithStatus);
    }

    /// <summary>
    /// Approaching is part of eating. This is what makes biting out of reach
    /// impossible rather than merely discouraged: the creature closes the distance
    /// itself instead of the brain having to arrange it separately.
    /// </summary>
    [Fact]
    public void Eat_ClosesTheDistance_ThenFeeds()
    {
        var start = new Vector2(120f, 170f);
        var (world, sandbox, id, _) = Subject(start);

        // Straight ahead - the creature is heading along +X - and well beyond any
        // mouth, so it has to travel.
        var plant = sandbox.AddPlant(new Vector2(180f, 170f));

        var body = world.Get<Body>(id);
        var energy = world.Get<Energy>(id);

        float reach = body.Radius + world.Get<Genes>(id).Trait(TraitAxis.MouthReach);
        Assert.True(60f > reach, "the plant must start out of reach or this proves nothing");

        for (int i = 0; i < 600; i++) world.Step();

        float closed = sandbox.Offset(body.Position, plant.Position).Length();

        Assert.True(closed < 60f, $"never closed on the green - still {closed:F1} away");
        Assert.True(energy.LifetimeIntake > 0f, "closed the distance but never ate");
    }

    /// <summary>
    /// Eating ends. A green has a fixed number of calories, and when they are gone
    /// the action is finished rather than continuing to work an empty patch.
    /// </summary>
    [Fact]
    public void Eat_ReportsDone_OnceTheGreenIsEatenOut()
    {
        var (world, sandbox, id, doing) = Subject(new Vector2(160f, 170f));

        // Small enough that a few mouthfuls finish it, and already in reach.
        var plant = sandbox.AddPlant(new Vector2(172f, 170f), energy: Metabolism.BiteSize * 2f);

        bool sawDone = false;

        for (int i = 0; i < 600 && !sawDone; i++)
        {
            world.Step();
            if (doing.Status == ActionStatus.Done) sawDone = true;
        }

        Assert.True(sawDone, "ate a green out and never reported Done");
        Assert.False(plant.Alive);
        Assert.True(world.Get<Energy>(id).LifetimeIntake > 0f);
    }

    /// <summary>
    /// A meal is not abandoned partway. Re-picking the nearest green every tick made
    /// a creature nibble whatever drifted closest and leave stubs behind to regrow,
    /// which is the opposite of eating one out.
    /// </summary>
    [Fact]
    public void Eat_StaysOnOneGreen_RatherThanNibblingWhateverIsNearest()
    {
        var (world, sandbox, id, _) = Subject(new Vector2(160f, 170f));

        var first = sandbox.AddPlant(new Vector2(171f, 170f));
        var second = sandbox.AddPlant(new Vector2(173f, 172f));

        var mind = world.Get<Mind>(id);

        // Run until something is latched, then confirm it stays latched while it lasts.
        for (int i = 0; i < 120 && mind.BiteTarget < 0; i++) world.Step();
        Assert.True(mind.BiteTarget >= 0, "never latched onto a green at all");

        int latched = mind.BiteTarget;
        var target = latched == first.Id ? first : second;

        for (int i = 0; i < 60; i++)
        {
            world.Step();
            if (!target.Alive) break;
            Assert.Equal(latched, mind.BiteTarget);
        }
    }

    /// <summary>
    /// An action that returns <see cref="ActionStatus.Blocked"/> must not have moved
    /// the body: the runner falls through on a block and the next action drives, so a
    /// block that also moved would charge the creature twice for one tick.
    /// <para>
    /// Not a hypothetical. Eat's "in reach but not chewable" branch returned without
    /// driving <i>and</i> without reporting a block, which froze four of eight
    /// creatures solid by tick 500 while the soak reported them as biting.
    /// </para>
    /// </summary>
    [Fact]
    public void Eat_BlockedInAnEmptyArena_LeavesTheBodyAlone()
    {
        var (world, _, id, doing) = Subject(new Vector2(170f, 170f));

        var body = world.Get<Body>(id);
        body.Speed = 12f;

        float before = body.Speed;
        world.Step();

        Assert.Equal(ActionStatus.Blocked, doing.Status);
        Assert.Equal(before, body.Speed);
    }

    // --- Breed -----------------------------------------------------------------

    /// <summary>Ages a creature past maturity and fills it up, so the only gate left
    /// is the one under test.</summary>
    private static void MakeReadyToBreed(SimWorld world, int id)
    {
        var vitals = world.Get<Vitals>(id);
        var energy = world.Get<Energy>(id);

        vitals.Age = world.Get<Genes>(id).Trait(TraitAxis.MatureAge) + 10f;
        vitals.LastBirthTime = float.NegativeInfinity;
        energy.Current = energy.Maximum;
    }

    /// <summary>
    /// A creature too young to breed must say so, not sit wanting a birth it will
    /// never get. This was a silent predicate inside the lifecycle loop; a creature
    /// could spend its whole tick on it and nothing would report that.
    /// </summary>
    [Fact]
    public void Breed_WhenTooYoung_ReportsBlocked()
    {
        var (world, _) = MakeSandbox();
        int id = world.Spawn(Seed(), new Vector2(170f, 170f), 0f);

        var doing = world.Get<Doing>(id);
        doing.Forced = CreatureAction.Reproduce;

        world.Get<Vitals>(id).Age = 0f;

        world.Step();

        Assert.Equal(ActionStatus.Blocked, doing.Status);
        Assert.False(doing.WantsBirth);
        Assert.Equal(1, world.Population);
    }

    /// <summary>Every gate passed, so a child arrives - and the decision belongs to
    /// the action while the birth itself still belongs to the lifecycle system.</summary>
    [Fact]
    public void Breed_WhenEveryGatePasses_ProducesAChild()
    {
        var (world, _) = MakeSandbox();
        int id = world.Spawn(Seed(), new Vector2(170f, 170f), 0f);

        var doing = world.Get<Doing>(id);
        doing.Forced = CreatureAction.Reproduce;

        // Intent.Reproduce is one of the four gates and comes from the brain, so the
        // run has to reach a tick where it is open.
        for (int i = 0; i < 400 && world.Population < 2; i++)
        {
            MakeReadyToBreed(world, id);
            world.Step();
        }

        Assert.True(world.Population >= 2, "never produced a child with every gate held open");
    }

    /// <summary>
    /// One decision, one child. The gates can close underneath a running action - a
    /// bite of energy spent, the cooldown restarting - and breeding twice off a
    /// single CanStart would hand a creature a free offspring.
    /// </summary>
    [Fact]
    public void Breed_DoesNotProduceTwoChildrenFromOneDecision()
    {
        var (world, _) = MakeSandbox();
        int id = world.Spawn(Seed(), new Vector2(170f, 170f), 0f);

        var doing = world.Get<Doing>(id);
        doing.Forced = CreatureAction.Reproduce;

        MakeReadyToBreed(world, id);

        for (int i = 0; i < 400 && world.Population < 2; i++) world.Step();
        Assert.True(world.Population >= 2, "no birth to test against");

        int after = world.Population;

        // The cooldown is now running, and nothing tops the parent back up.
        for (int i = 0; i < 60; i++) world.Step();

        Assert.Equal(after, world.Population);
        Assert.False(doing.WantsBirth);
    }

    // --- the latent actions ----------------------------------------------------

    /// <summary>A genome with one attribute granted, so a latent action can run.</summary>
    private static Genome SeedWith(LatentTraitId id, ulong seed = 1)
    {
        var rng = new Pcg32(seed);
        var genome = Genome.CreateSeed(ref rng);

        var prerequisite = LatentTraitCatalog.Get(id).Prerequisite;
        if (prerequisite is not null) Mutator.Unlock(genome, prerequisite.Value, ref rng);

        Mutator.Unlock(genome, id, ref rng);
        return genome;
    }

    /// <summary>
    /// A locked action must never run, however it is asked for. The sandbox can force
    /// any action at all, so <c>CanStart</c> is the only thing standing between a
    /// forced Rest and a creature resting without the attribute for it.
    /// </summary>
    [Fact]
    public void ALatentAction_IsBlocked_WithoutItsAttribute()
    {
        var (world, _) = MakeSandbox();
        int id = world.Spawn(Seed(), new Vector2(170f, 170f), 0f);

        var doing = world.Get<Doing>(id);
        doing.Forced = CreatureAction.Torpor;

        Assert.False(world.Get<Genes>(id).Has(LatentTraitId.Torpor));

        world.Step();

        Assert.Equal(ActionStatus.Blocked, doing.Status);
    }

    /// <summary>
    /// With the attribute unlocked, the action runs.
    /// <para>
    /// Several genomes are tried rather than one, and that is not flakiness-padding.
    /// Marking's strength is the brain's scent effector, and <c>ReadIntent</c> clamps
    /// a negative output to zero - so a genome whose scent neuron happens to sit
    /// negative can hold the gland and still never lay a trail. That is a real
    /// property of the model, not a defect in the action, and asserting against a
    /// single seed would be asserting that one arbitrary brain wants to mark.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(LatentTraitId.Torpor, CreatureAction.Torpor)]
    [InlineData(LatentTraitId.ScentGlandA, CreatureAction.ScentA)]
    [InlineData(LatentTraitId.ScentGlandB, CreatureAction.ScentB)]
    public void ALatentAction_Runs_OnceItsAttributeIsUnlocked(
        LatentTraitId trait, CreatureAction action)
    {
        bool ran = false;

        for (ulong genomeSeed = 1; genomeSeed <= 12 && !ran; genomeSeed++)
        {
            var (world, _) = MakeSandbox();
            int id = world.Spawn(SeedWith(trait, genomeSeed), new Vector2(170f, 170f), 0f);

            var doing = world.Get<Doing>(id);
            doing.Forced = action;

            for (int i = 0; i < 300 && !ran; i++)
            {
                world.Step();
                ran = doing.Action == action && doing.Status == ActionStatus.Running;
            }
        }

        Assert.True(ran, $"{action} never ran for any genome despite {trait} being unlocked");
    }

    /// <summary>
    /// Sprint is a modifier on movement, not an action - there is no such thing as
    /// sprinting while standing still. It keeps its place in the vocabulary, because
    /// that is the effector set and the brain does drive it, but it gets no
    /// behaviour: selecting it falls through to the movement it modifies.
    /// <para>
    /// Worth pinning, because an empty slot in the runner's table is indistinguishable
    /// from an unfinished one, and the obvious "fix" would be to write a SprintAction.
    /// </para>
    /// </summary>
    [Fact]
    public void Sprint_HasNoActionOfItsOwn_AndFallsThroughToMovement()
    {
        var (world, _) = MakeSandbox();
        int id = world.Spawn(SeedWith(LatentTraitId.SprintGland), new Vector2(170f, 170f), 0f);

        var doing = world.Get<Doing>(id);
        doing.Forced = CreatureAction.Sprint;

        world.Step();

        // Forced actions do not fall through, so a missing behaviour reads as blocked.
        Assert.Equal(ActionStatus.Blocked, doing.Status);

        // Unforced, the creature gets on with something it can actually do.
        doing.Forced = null;
        for (int i = 0; i < 120; i++) world.Step();

        Assert.NotEqual(CreatureAction.Sprint, doing.Action);
    }
}
