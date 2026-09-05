using System.Numerics;
using GoL.Sim.Board;
using GoL.Sim.Components;
using GoL.Sim.Core;
using GoL.Sim.Genetics;
using GoL.Sim.Systems;

namespace GoL.Sim.Tests;

/// <summary>
/// Motor commitment. A creature was watched flickering between biting and turning
/// several times a second; these pin the three body properties that stopped it.
/// The brain still decides everything - nothing here scripts behaviour.
/// </summary>
public class CommitmentTests
{
    /// <summary>
    /// The twitch, in miniature. A brain output resting near the threshold used to
    /// flip the gate every tick; with hysteresis it opens once and stays open.
    /// </summary>
    [Fact]
    public void AGateNearTheThreshold_StopsChattering()
    {
        bool open = false;
        int flips = 0;

        // Hovering either side of GateOpen, as a real effector does.
        float[] wobble = { 0.55f, 0.45f, 0.52f, 0.48f, 0.51f, 0.49f, 0.53f, 0.47f };

        foreach (float v in wobble)
        {
            bool next = Locomotion.Gate(v, open);
            if (next != open) flips++;
            open = next;
        }

        Assert.Equal(1, flips);
        Assert.True(open, "it should have opened once and held");
    }

    /// <summary>Hysteresis must not become a latch that sticks: an output that
    /// genuinely falls away still closes the gate.</summary>
    [Fact]
    public void AGateStillCloses_WhenTheBrainReallyLetsGo()
    {
        Assert.True(Locomotion.Gate(0.9f, false));
        Assert.True(Locomotion.Gate(0.3f, true), "should hold through the band");
        Assert.False(Locomotion.Gate(0.1f, true), "but let go below the close point");
        Assert.False(Locomotion.Gate(0.3f, false), "and not reopen inside the band");
    }

    /// <summary>Turning now eases like speed. A body cannot reverse its turn within
    /// a sixtieth of a second, and that instant reversal was the visible twitch.</summary>
    [Fact]
    public void Turning_EasesRatherThanSnapping()
    {
        var (world, creature) = Lab();
        float turnRate = creature.Trait(TraitAxis.TurnRate);

        creature.Body.AngularVelocity = 0f;
        var hard = new Intent { Turn = 1f };

        Locomotion.Apply(creature.Body, creature.Energy, creature.Genome, hard, world.Field, 1f / 60f);
        float afterOne = creature.Body.AngularVelocity;

        Assert.True(afterOne > 0f, "it must start turning");
        Assert.True(afterOne < turnRate * 0.5f, "but must not arrive in a single tick");

        for (int i = 0; i < 120; i++)
            Locomotion.Apply(creature.Body, creature.Energy, creature.Genome, hard, world.Field, 1f / 60f);

        Assert.Equal(turnRate, creature.Body.AngularVelocity, 2);
    }

    /// <summary>An int defaults to 0, and 0 is a valid plant id and grid index - so
    /// without the explicit initialiser every newborn starts latched onto whatever
    /// occupies cell zero.</summary>
    [Fact]
    public void AFreshMind_IsNotLatchedOntoAnything()
    {
        var rng = new Pcg32(1);
        var genome = Genome.CreateSeed(ref rng);
        var brain = GoL.Sim.Brains.Brain.Compile(genome);

        var mind = new Mind { Brain = brain, Layout = new SensorLayout(brain) };

        Assert.Equal(-1, mind.BiteTarget);
    }

    /// <summary>
    /// The heart of "eat until the green is done": the mouth keeps the plant it
    /// started on even when a closer one appears, and lets go once it is finished.
    /// </summary>
    [Fact]
    public void TheBite_StaysOnItsPlant_ThenLetsGo()
    {
        var config = new SimConfig { Seed = 5, WorldSize = 340f };
        var lab = new LabEnvironment(config);
        var world = new SimWorld(config, lab);

        var far = lab.AddPlant(new Vector2(112f, 100f), 30f);
        var near = lab.AddPlant(new Vector2(104f, 100f), 30f);

        var rng = new Pcg32(5);
        var genome = Genome.CreateSeed(ref rng);
        var brain = GoL.Sim.Brains.Brain.Compile(genome);
        var mind = new Mind { Brain = brain, Layout = new SensorLayout(brain) };
        mind.EnsureBuffers();

        var body = new Body { Position = new Vector2(100f, 100f), Radius = 14f, Heading = 0f };
        var energy = new Energy { Current = 0f, Maximum = 10000f };

        // Start on the far plant by hiding the near one behind a first bite.
        mind.BiteTarget = far.Id;
        lab.ResolveBite(world, body, energy, genome, mind, 1f / 60f);
        Assert.Equal(far.Id, mind.BiteTarget);

        float nearBefore = near.Energy;

        // Keep biting: it must finish the far plant, not switch to the nearer one.
        // 30 energy at BiteRate 22/s takes about 82 ticks, so give it room.
        for (int i = 0; i < 200; i++)
        {
            if (mind.BiteTarget != far.Id) break;
            lab.ResolveBite(world, body, energy, genome, mind, 1f / 60f);
        }

        Assert.Equal(nearBefore, near.Energy, 3);
        Assert.True(far.Energy < Metabolism.WorthBiting, "the far plant should be eaten out");
    }

    /// <summary>
    /// The owner's complaint as a number. Parked beside a green, a creature should
    /// take the whole plant - not the ~0.2 energy a single flickering tick yields.
    /// </summary>
    [Fact]
    public void ParkedBesideAGreen_ACreatureEatsItOut()
    {
        var config = new SimConfig { Seed = 8, WorldSize = 340f };
        var lab = new LabEnvironment(config);
        var world = new SimWorld(config, lab);

        var plant = lab.AddPlant(new Vector2(108f, 100f), 35f);

        var rng = new Pcg32(8);
        var genome = Genome.CreateSeed(ref rng);
        var brain = GoL.Sim.Brains.Brain.Compile(genome);
        var mind = new Mind { Brain = brain, Layout = new SensorLayout(brain) };
        mind.EnsureBuffers();

        var body = new Body { Position = new Vector2(100f, 100f), Radius = 12f, Heading = 0f };
        var energy = new Energy { Current = 0f, Maximum = 10000f };

        for (int i = 0; i < 300; i++)
            lab.ResolveBite(world, body, energy, genome, mind, 1f / 60f);

        float expected = 35f * genome.Trait(TraitAxis.DigestGrass);

        Assert.True(energy.LifetimeIntake > expected * 0.9f,
            $"expected about {expected:0.0} from a whole plant, got {energy.LifetimeIntake:0.0}");
        Assert.False(plant.Alive);
    }

    /// <summary>A consumed green stays gone for a while and comes back as a seedling
    /// - not instantly, elsewhere, at full energy, which is why nothing ever
    /// appeared to run out.</summary>
    [Fact]
    public void AConsumedGreen_StaysGone_ThenReturnsAsASeedling()
    {
        var config = new SimConfig { Seed = 12, WorldSize = 340f };
        var lab = new LabEnvironment(config);
        var world = new SimWorld(config, lab);

        var plant = lab.AddPlant(new Vector2(108f, 100f), 35f);

        var rng = new Pcg32(12);
        var genome = Genome.CreateSeed(ref rng);
        var brain = GoL.Sim.Brains.Brain.Compile(genome);
        var mind = new Mind { Brain = brain, Layout = new SensorLayout(brain) };
        mind.EnsureBuffers();

        var body = new Body { Position = new Vector2(100f, 100f), Radius = 12f, Heading = 0f };
        var energy = new Energy { Current = 0f, Maximum = 10000f };

        for (int i = 0; i < 300; i++)
            lab.ResolveBite(world, body, energy, genome, mind, 1f / 60f);

        Assert.False(plant.Alive);
        Assert.True(plant.RespawnAt > 0f);

        // Well short of the delay: still gone.
        for (int i = 0; i < 60; i++) world.Step();
        Assert.False(plant.Alive);

        // And well past it: back, but small.
        for (int i = 0; i < 600; i++) world.Step();
        Assert.True(plant.Alive, "the arena must not empty");
        Assert.True(plant.Energy < plant.MaxEnergy * 0.6f,
            "it should come back as a seedling, not a full plant");
    }

    private static (SimWorld World, CreatureView Subject) Lab(ulong seed = 1)
    {
        var config = new SimConfig { Seed = (int)seed, WorldSize = 340f };
        var world = new SimWorld(config, new LabEnvironment(config));

        var rng = new Pcg32(seed);
        return (world, world.View(world.Spawn(Genome.CreateSeed(ref rng), new Vector2(50f, 50f), 0f)));
    }
}
