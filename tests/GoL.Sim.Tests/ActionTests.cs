using System.Numerics;
using GoL.Sim.Components;
using GoL.Sim.Core;
using GoL.Sim.Genetics;
using GoL.Sim.Systems;

namespace GoL.Sim.Tests;

/// <summary>
/// The action readout. Its entire value is that it does not lie: it is a projection
/// of <see cref="Intent"/> and nothing else, so these tests pin that it stays one.
/// </summary>
public class ActionTests
{
    private static Genome Seed(ulong seed = 1, params LatentTraitId[] unlock)
    {
        var rng = new Pcg32(seed);
        var genome = Genome.CreateSeed(ref rng);
        foreach (var id in unlock) Mutator.Unlock(genome, id, ref rng);
        return genome;
    }

    private static float[] Read(Genome genome, in Intent intent)
    {
        var values = new float[Actions.Count];
        Actions.Read(genome, intent, values);
        return values;
    }

    /// <summary>
    /// The guard against a future action being added and silently ignored. Count
    /// must come from the enum, and every action must carry an explicit tie-break
    /// priority - one left out would rank 0, tie with Reproduce for the top slot,
    /// and quietly win every tie it entered.
    /// </summary>
    [Fact]
    public void Vocabulary_CoversEveryEnumMember()
    {
        Assert.Equal(Actions.All.Length, Actions.Count);

        // Touching Rank forces BuildRank, which throws if TieBreak is incomplete.
        Assert.True(Actions.Current(new float[Actions.Count], out _) == false);

        foreach (var action in Actions.All)
        {
            Assert.False(string.IsNullOrEmpty(Actions.Name(action)));
            Assert.False(string.IsNullOrEmpty(Actions.PoleName(action, 1f)));
            Assert.False(string.IsNullOrEmpty(Actions.PoleName(action, -1f)));
        }
    }

    [Fact]
    public void EveryAction_ReadsBackTheIntentItCameFrom()
    {
        var genome = Seed(2,
            LatentTraitId.SprintGland, LatentTraitId.ScentGlandA,
            LatentTraitId.ScentGlandB, LatentTraitId.Torpor);

        var intent = new Intent
        {
            Thrust = 0.7f,
            Turn = -0.3f,
            Bite = true,
            Reproduce = false,
            Sprint = true,
            Torpor = false,
            EmitScent0 = 0.25f,
            EmitScent1 = 0.5f,
        };

        var v = Read(genome, intent);

        Assert.Equal(0.7f, v[(int)CreatureAction.Move], 5);
        Assert.Equal(-0.3f, v[(int)CreatureAction.Turn], 5);
        Assert.Equal(1f, v[(int)CreatureAction.Bite]);
        Assert.Equal(0f, v[(int)CreatureAction.Reproduce]);
        Assert.Equal(1f, v[(int)CreatureAction.Sprint]);
        Assert.Equal(0f, v[(int)CreatureAction.Torpor]);
        Assert.Equal(0.25f, v[(int)CreatureAction.ScentA], 5);
        Assert.Equal(0.5f, v[(int)CreatureAction.ScentB], 5);
    }

    /// <summary>
    /// The point of the whole vocabulary. Chase and flee are not two actions; they
    /// are one signed <see cref="CreatureAction.Move"/>. If this ever splits into two
    /// enum members, the readout has started inventing a distinction the controller
    /// does not have - and would then need a heuristic to sustain it.
    /// </summary>
    [Fact]
    public void ChaseAndFlee_AreOneSignedAction()
    {
        var genome = Seed();

        var chasing = new Intent { Thrust = 0.8f };
        var fleeing = new Intent { Thrust = -0.8f };

        Assert.True(Actions.Current(Read(genome, chasing), out var forward));
        Assert.True(Actions.Current(Read(genome, fleeing), out var backward));

        Assert.Equal(CreatureAction.Move, forward);
        Assert.Equal(CreatureAction.Move, backward);

        Assert.Equal("Chase", Actions.PoleName(CreatureAction.Move, 0.8f));
        Assert.Equal("Flee", Actions.PoleName(CreatureAction.Move, -0.8f));
    }

    [Fact]
    public void TurnLeftAndRight_AreAlsoOneSignedAction()
    {
        Assert.Equal("Turn L", Actions.PoleName(CreatureAction.Turn, -0.5f));
        Assert.Equal("Turn R", Actions.PoleName(CreatureAction.Turn, 0.5f));
        Assert.True(Actions.IsSigned(CreatureAction.Turn));
        Assert.True(Actions.IsSigned(CreatureAction.Move));
    }

    [Fact]
    public void Current_IsTheGreatestMagnitude()
    {
        var genome = Seed();
        var intent = new Intent { Thrust = 0.2f, Turn = -0.9f };

        Assert.True(Actions.Current(Read(genome, intent), out var current));
        Assert.Equal(CreatureAction.Turn, current);
    }

    /// <summary>Magnitude ignores sign, or a hard reverse would rank below a gentle
    /// nudge forward and the readout would misreport every fleeing creature.</summary>
    [Fact]
    public void Current_RanksByMagnitudeNotSign()
    {
        var genome = Seed();
        var intent = new Intent { Thrust = -0.9f, Turn = 0.2f };

        Assert.True(Actions.Current(Read(genome, intent), out var current));
        Assert.Equal(CreatureAction.Move, current);
    }

    /// <summary>A gate and a full-thrust move both read 1. The tie-break puts the
    /// discrete event first, so a creature that is both biting and charging reports
    /// the bite.</summary>
    [Fact]
    public void Current_BreaksTiesTowardDiscreteEvents()
    {
        var genome = Seed();
        var intent = new Intent { Thrust = 1f, Bite = true };

        Assert.True(Actions.Current(Read(genome, intent), out var current));
        Assert.Equal(CreatureAction.Bite, current);
    }

    [Fact]
    public void NothingHappening_IsIdle_AndNotAnEnumMember()
    {
        var genome = Seed();
        var intent = new Intent { Thrust = 0.01f, Turn = -0.02f };

        Assert.False(Actions.Current(Read(genome, intent), out _));
        Assert.DoesNotContain(Actions.All, a => Actions.Name(a) == Actions.IdleLabel);
    }

    /// <summary>
    /// The guard that keeps "what can this creature do" honest. A brain output can
    /// exist for a channel the genome never unlocked, and reporting it would put an
    /// action on screen the creature cannot actually take.
    /// </summary>
    [Fact]
    public void LockedActions_AreNeverReportedActive()
    {
        var genome = Seed();

        Assert.False(genome.Has(LatentTraitId.SprintGland));
        Assert.False(genome.Has(LatentTraitId.Torpor));

        var intent = new Intent { Sprint = true, Torpor = true, EmitScent0 = 1f };
        var v = Read(genome, intent);

        Assert.Equal(0f, v[(int)CreatureAction.Sprint]);
        Assert.Equal(0f, v[(int)CreatureAction.Torpor]);
        Assert.Equal(0f, v[(int)CreatureAction.ScentA]);
        Assert.False(Actions.Current(v, out _));
    }

    [Fact]
    public void AvailableMask_TracksTheGenome()
    {
        var plain = Seed(3);
        var sprinter = Seed(3, LatentTraitId.SprintGland);

        Assert.False(Actions.IsAvailable(CreatureAction.Sprint, plain));
        Assert.True(Actions.IsAvailable(CreatureAction.Sprint, sprinter));

        // Innate actions are available to everything, always.
        foreach (var action in new[]
                 {
                     CreatureAction.Move, CreatureAction.Turn,
                     CreatureAction.Bite, CreatureAction.Reproduce,
                 })
        {
            Assert.True(Actions.IsAvailable(action, plain));
        }

        int mask = Actions.AvailableMask(sprinter);
        Assert.NotEqual(0, mask & (1 << (int)CreatureAction.Sprint));
    }

    /// <summary>A bite that connected is "Feeding"; a mouth that merely opened is
    /// "Biting". Both are outcomes the simulation records, not inferences.</summary>
    [Fact]
    public void Label_DistinguishesOutcomesFromGates()
    {
        Assert.Equal("Feeding", Actions.Label(CreatureAction.Bite, 1f, fed: true, bred: false));
        Assert.Equal("Biting", Actions.Label(CreatureAction.Bite, 1f, fed: false, bred: false));
        Assert.Equal("Breeding", Actions.Label(CreatureAction.Reproduce, 1f, false, bred: true));
        Assert.Equal("Reproduce", Actions.Label(CreatureAction.Reproduce, 1f, false, bred: false));
    }

    /// <summary>The system must publish a Behaviour for every living creature, or
    /// the panel and the soak histogram read stale values.</summary>
    [Fact]
    public void ActionSystem_PublishesBehaviourForEveryLivingCreature()
    {
        var config = new SimConfig { Seed = 4, WorldSize = 340f };
        var world = new SimWorld(config, new LabEnvironment(config));

        var rng = new Pcg32(4);
        for (int i = 0; i < 5; i++)
            world.Spawn(Genome.CreateSeed(ref rng), new Vector2(40f + i * 20f, 60f), 0f);

        for (int i = 0; i < 30; i++) world.Step();

        Assert.NotEmpty(world.Living);

        foreach (int id in world.Living)
        {
            var view = world.View(id);
            var behaviour = view.Behaviour;

            Assert.Equal(Actions.AvailableMask(view.Genome), behaviour.AvailableMask);
            Assert.Equal(
                Actions.Value(CreatureAction.Move, view.LastIntent),
                behaviour.Value(CreatureAction.Move), 5);

            Assert.False(string.IsNullOrEmpty(view.ActionLabel));
        }
    }

    /// <summary>
    /// A birth is read from the tick stamp lifecycle writes, not re-derived from the
    /// reproduction predicate - so the two can never disagree.
    /// </summary>
    [Fact]
    public void Breeding_IsReadFromTheBirthTick()
    {
        var vitals = new Vitals { LastBirthTick = 42 };

        Assert.True(vitals.LastBirthTick == 42);
        Assert.False(new Vitals().LastBirthTick == 42);
        Assert.Equal(-1, new Vitals().LastBirthTick);
    }
}
