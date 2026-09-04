using System.Numerics;
using GoL.Sim.Board;
using GoL.Sim.Components;
using GoL.Sim.Core;
using GoL.Sim.Genetics;
using GoL.Sim.Systems;

namespace GoL.Sim.Tests;

/// <summary>
/// Eating. These exist because a creature was watched biting a green and gaining
/// nothing visible, and it took arithmetic rather than observation to work out why.
/// </summary>
public class FeedingTests
{
    private static (SimWorld World, BoardEnvironment Board) MakeBoard(int seed = 3)
    {
        var config = new SimConfig { Seed = seed, WorldSize = 256f, PlantDensity = 0.2f };
        var board = new BoardEnvironment(config);
        return (new SimWorld(config, board), board);
    }

    private static Genome Seed(ulong seed = 1)
    {
        var rng = new Pcg32(seed);
        return Genome.CreateSeed(ref rng);
    }

    /// <summary>
    /// The owner's actual request: an eaten green is gone. It used to stay in place
    /// at zero energy and refill from its roots, which left an invisible plant a
    /// creature could sit on forever, grazing regrowth for less than its own upkeep.
    /// </summary>
    [Fact]
    public void FullyEatenPlant_IsRemoved()
    {
        var (_, board) = MakeBoard();
        var plants = board.Plants;

        int index = FindPlant(plants);
        plants.Plant(index, PlantKind.Grass, 5f);

        // Strip it.
        while (plants.EnergyAt(index) > 0f) plants.Consume(index, 1f);

        Assert.Equal(PlantKind.None, plants.KindAt(index));
        Assert.Equal(0f, plants.EnergyAt(index));
    }

    [Fact]
    public void PartlyEatenPlant_SurvivesAndKeepsItsKind()
    {
        var (_, board) = MakeBoard();
        var plants = board.Plants;

        int index = FindPlant(plants);
        plants.Plant(index, PlantKind.Grass, 10f);

        float taken = plants.Consume(index, 3f);

        Assert.Equal(3f, taken, 4);
        Assert.Equal(PlantKind.Grass, plants.KindAt(index));
        Assert.True(plants.EnergyAt(index) > 0f, "grazing must not be all-or-nothing");
    }

    /// <summary>The readout must not be able to disagree with the energy it reports
    /// on - which is why both are written by the same method.</summary>
    [Fact]
    public void Gaining_CreditsEnergyAndIntakeByTheSameAmount()
    {
        var energy = new Energy { Current = 10f, Maximum = 100f };

        energy.Gain(7.5f);

        Assert.Equal(17.5f, energy.Current, 4);
        Assert.Equal(7.5f, energy.LifetimeIntake, 4);
        Assert.Equal(7.5f, energy.IntakeThisTick, 4);
    }

    /// <summary>A full creature biting a plant absorbs nothing, and the readout has
    /// to say so rather than reporting what was offered.</summary>
    [Fact]
    public void Gaining_RecordsWhatWasAbsorbed_NotWhatWasOffered()
    {
        var energy = new Energy { Current = 98f, Maximum = 100f };

        energy.Gain(50f);

        Assert.Equal(100f, energy.Current, 4);
        Assert.Equal(2f, energy.LifetimeIntake, 4);
    }

    [Fact]
    public void IntakeRate_DecaysWhenNothingIsEaten()
    {
        var energy = new Energy { Current = 10f, Maximum = 100f };

        energy.IntakeThisTick = 0.2f;
        for (int i = 0; i < 60; i++) energy.TrackIntake(1f / 60f);
        float fed = energy.IntakeRate;

        Assert.True(fed > 0f, "feeding must register");

        energy.IntakeThisTick = 0f;
        for (int i = 0; i < 300; i++) energy.TrackIntake(1f / 60f);

        Assert.True(energy.IntakeRate < fed * 0.1f, "the rate must not latch on");
    }

    /// <summary>A nearly stripped plant that happens to be nearest must not be
    /// chosen over a full one behind it, or the creature works through scraps while
    /// the meal goes untouched.</summary>
    [Fact]
    public void ADrainedPlant_IsNotWorthBiting()
    {
        var (_, board) = MakeBoard();
        var plants = board.Plants;

        // Clear the world first: the board seeds plants everywhere, and a bite that
        // simply found a different one would pass this test for the wrong reason.
        for (int i = 0; i < plants.Resolution * plants.Resolution; i++) plants.Clear(i);

        int index = plants.Resolution * (plants.Resolution / 2) + plants.Resolution / 2;
        plants.Plant(index, PlantKind.Grass, Metabolism.WorthBiting * 0.5f);

        var genome = Seed();
        var body = new Body { Position = plants.CentreOf(index), Radius = 6f, Heading = 0f };
        var energy = new Energy { Current = 10f, Maximum = 100f };
        var mind = MakeMind(genome);

        board.ResolveBite(null!, body, energy, genome, mind, 1f / 60f);

        Assert.False(mind.BitThisTick, "scraps are not a meal");
        Assert.Equal(10f, energy.Current, 4);

        // The control: the same creature, same place, a plant worth eating.
        plants.Plant(index, PlantKind.Grass, 20f);
        board.ResolveBite(null!, body, energy, genome, mind, 1f / 60f);

        Assert.True(mind.BitThisTick, "a full plant in reach must still be eaten");
        Assert.True(energy.Current > 10f);
    }

    /// <summary>The locked niche has to survive a change to food value, or the
    /// cellulose gut stops meaning anything.</summary>
    [Fact]
    public void Bramble_IsStillWorthlessWithoutTheGut()
    {
        var plain = Seed(4);
        Assert.False(plain.Has(LatentTraitId.CelluloseGut));
        Assert.Equal(0f, PlantSpecs.Digestibility(PlantKind.Bramble, plain));

        var rng = new Pcg32(4);
        var equipped = Seed(4);
        Mutator.Unlock(equipped, LatentTraitId.CelluloseGut, ref rng);
        Assert.True(PlantSpecs.Digestibility(PlantKind.Bramble, equipped) > 0f);
    }

    /// <summary>
    /// The guard on the risk destructible greens introduce. Spread only ever fills a
    /// cell beside a living plant, so without an ambient seed the last plant of a
    /// kind could be eaten and that kind would be gone for the rest of the run.
    /// </summary>
    [Fact]
    public void AmbientSeeding_RecoversAStrippedWorld()
    {
        var (world, board) = MakeBoard(seed: 9);
        var plants = board.Plants;

        for (int i = 0; i < plants.Resolution * plants.Resolution; i++) plants.Clear(i);
        Assert.Equal(0, plants.LiveCount());

        for (int i = 0; i < 20000; i++) world.Step();

        Assert.True(plants.LiveCount() > 0,
            "a stripped world must be able to come back, or grazing is irreversible");
    }

    /// <summary>Corpses come only from a death. Ambient seeding must never conjure
    /// one - carrion neither grows nor spreads.</summary>
    [Fact]
    public void AmbientSeeding_NeverProducesCarrion()
    {
        var (world, board) = MakeBoard(seed: 11);
        var plants = board.Plants;

        for (int i = 0; i < plants.Resolution * plants.Resolution; i++) plants.Clear(i);

        for (int i = 0; i < 20000; i++) world.Step();

        for (int i = 0; i < plants.Resolution * plants.Resolution; i++)
            Assert.NotEqual(PlantKind.Carrion, plants.KindAt(i));

        Assert.DoesNotContain(PlantSpecs.Seedable, s => s.Kind == PlantKind.Carrion);
    }

    /// <summary>The lab must be able to demonstrate feeding more than once, so an
    /// eaten green is replaced rather than simply removed.</summary>
    [Fact]
    public void Lab_HoldsItsPlantCountAsGreensAreEaten()
    {
        var config = new SimConfig { Seed = 5, WorldSize = 340f };
        var lab = new LabEnvironment(config);
        lab.SeedPlants(24);

        int before = lab.Plants.Count;

        var genome = Seed(5);
        var mind = MakeMind(genome);
        var energy = new Energy { Current = 0f, Maximum = 10000f };

        // Park on each plant in turn and strip it.
        for (int p = 0; p < 8; p++)
        {
            var target = lab.Plants[p];
            var body = new Body { Position = target.Position, Radius = 6f, Heading = 0f };

            for (int i = 0; i < 400; i++)
                lab.ResolveBite(null!, body, energy, genome, mind, 1f / 60f);
        }

        Assert.Equal(before, lab.Plants.Count);
        Assert.All(lab.Plants, p => Assert.True(p.Energy > 0f, "the arena must not empty"));
        Assert.True(energy.LifetimeIntake > 0f, "and the creature must actually have eaten");
    }

    /// <summary>
    /// A plant that has exhausted its own ground dies off. Constructed directly
    /// rather than waited for, because on typical soil it fires rarely - and a
    /// mechanism that is only exercised by luck is a mechanism nobody has tested.
    /// </summary>
    [Fact]
    public void APlantOnExhaustedGround_DiesOff()
    {
        var (_, board) = MakeBoard(seed: 21);
        var plants = board.Plants;
        var fertility = board.Fertility;

        int index = plants.Resolution * (plants.Resolution / 2) + plants.Resolution / 2;
        plants.Plant(index, PlantKind.Grass, 10f, maturity: 1f);

        // Strip the soil under it well below what grass needs to hold on.
        for (int i = 0; i < 500; i++) fertility.Drain(index, 1f);
        Assert.True(fertility.At(index) < PlantSpecs.Get(PlantKind.Grass).MinFertility);

        var rng = new Pcg32(21);
        for (int bucket = 0; bucket < PlantGrid.BucketCount; bucket++)
            plants.StepBucket(bucket, fertility, 1f / 60f, ref rng);

        Assert.Equal(PlantKind.None, plants.KindAt(index));
    }

    private static int FindPlant(PlantGrid plants)
    {
        for (int i = 0; i < plants.Resolution * plants.Resolution; i++)
            if (plants.KindAt(i) != PlantKind.None) return i;
        return 0;
    }

    private static Mind MakeMind(Genome genome)
    {
        var brain = GoL.Sim.Brains.Brain.Compile(genome);
        var mind = new Mind { Brain = brain, Layout = new SensorLayout(brain) };
        mind.EnsureBuffers();
        return mind;
    }
}
