using System.Numerics;
using GoL.Sim;
using GoL.Sim.Board;
using GoL.Sim.Core;
using GoL.Sim.Genetics;
using GoL.Sim.Systems;

namespace GoL.Sim.Tests;

public class BoardTests
{
    private static (SimWorld World, BoardEnvironment Board) Build(int seed = 5, float size = 512f)
    {
        var config = new SimConfig { Seed = seed, WorldSize = size, Toroidal = true, PlantDensity = 0.2f };
        var board = new BoardEnvironment(config);
        return (new SimWorld(config, board), board);
    }

    [Fact]
    public void TheBoardStartsWithVegetation_OfSeveralKinds()
    {
        var (_, board) = Build();
        var found = new HashSet<PlantKind>();

        for (int i = 0; i < board.Plants.CellCount; i++)
        {
            var kind = board.Plants.KindAt(i);
            if (kind != PlantKind.None) found.Add(kind);
        }

        Assert.True(found.Count >= 3, $"only {found.Count} plant kinds seeded");
    }

    /// <summary>
    /// The locked niche, and the reason the CelluloseGut attribute is worth its
    /// upkeep. If this ever passes for a creature without the gut, the whole
    /// open-ended-attribute story loses its most concrete payoff.
    /// </summary>
    [Fact]
    public void Bramble_IsWorthlessWithoutACelluloseGut_AndFoodWithOne()
    {
        var rng = new Pcg32(1);
        var genome = Genome.CreateSeed(ref rng);

        Assert.Equal(0f, PlantSpecs.Digestibility(PlantKind.Bramble, genome));

        Mutator.Unlock(genome, LatentTraitId.CelluloseGut, ref rng);

        Assert.True(PlantSpecs.Digestibility(PlantKind.Bramble, genome) > 0f);
    }

    [Fact]
    public void Blightcap_PoisonsTheUnprotected_AndBarelyTouchesTheResistant()
    {
        var rng = new Pcg32(2);
        var vulnerable = Genome.CreateSeed(ref rng);
        var resistant = Genome.CreateSeed(ref rng);
        Mutator.Unlock(resistant, LatentTraitId.ToxinResistance, ref rng);

        float unprotected = PlantSpecs.Toxicity(PlantKind.Blightcap, vulnerable);
        float protectedDose = PlantSpecs.Toxicity(PlantKind.Blightcap, resistant);

        Assert.True(unprotected > 0f);
        Assert.True(protectedDose < unprotected);
    }

    [Fact]
    public void Carnivores_DigestCarrionFarBetterThanOthers()
    {
        var rng = new Pcg32(3);
        var grazer = Genome.CreateSeed(ref rng);
        var hunter = Genome.CreateSeed(ref rng);
        Mutator.Unlock(hunter, LatentTraitId.Carnivory, ref rng);

        Assert.True(
            PlantSpecs.Digestibility(PlantKind.Carrion, hunter)
            > PlantSpecs.Digestibility(PlantKind.Carrion, grazer));
    }

    [Fact]
    public void ADeadCreature_LeavesCarrion()
    {
        var (_, board) = Build();

        // Somewhere empty, so the corpse is not rejected for landing on a plant.
        int index = -1;
        for (int i = 0; i < board.Plants.CellCount; i++)
        {
            if (board.Plants.KindAt(i) == PlantKind.None) { index = i; break; }
        }
        Assert.True(index >= 0);

        board.OnDeath(board.Plants.CentreOf(index), radius: 8f, remainingEnergy: 20f);

        Assert.Equal(PlantKind.Carrion, board.Plants.KindAt(index));
    }

    [Fact]
    public void PlantsGrowBack_AfterBeingPartlyGrazed()
    {
        var (world, board) = Build();

        int index = FindPlant(board, PlantKind.Grass);

        // Partly, not wholly. A plant eaten to nothing is now removed rather than
        // left in place at zero energy to refill from its roots - that behaviour
        // left an invisible green a creature could sit on forever. Grazing still
        // has to be survivable, though, or every nibble would destroy a plant.
        board.Plants.Consume(index, board.Plants.EnergyAt(index) * 0.5f);

        float stripped = board.Plants.EnergyAt(index);
        Assert.Equal(PlantKind.Grass, board.Plants.KindAt(index));

        for (int i = 0; i < 600; i++) world.Step();

        Assert.True(board.Plants.EnergyAt(index) > stripped, "grass never regrew");
    }

    [Fact]
    public void VegetationSpreads_IntoEmptyGround()
    {
        var (world, board) = Build(seed: 9);
        int before = CountPlants(board);

        for (int i = 0; i < 4000; i++) world.Step();

        Assert.True(CountPlants(board) > before,
            "the board never colonised any new ground");
    }

    /// <summary>Explicit diffusion is unstable above D = 0.25; if the coefficient is
    /// ever raised past it, the field oscillates into a checkerboard instead.</summary>
    [Fact]
    public void Scent_DiffusesOutward_AndDecaysAway()
    {
        var field = new PheromoneField(512f, 64, 2, toroidal: true);
        var centre = new Vector2(256f, 256f);

        field.Emit(centre, 0, 1f);
        float peak = field.Sample(centre, 0);

        for (int i = 0; i < 20; i++) field.Step(1f / 15f);

        Assert.True(field.Sample(centre, 0) < peak, "scent never dispersed");

        // A neighbouring cell should have received some of it.
        Assert.True(field.Sample(centre + new Vector2(8f, 0f), 0) > 0f, "scent did not spread");

        for (int i = 0; i < 500; i++) field.Step(1f / 15f);

        Assert.True(field.Sample(centre, 0) < 0.001f, "scent never faded");
    }

    [Fact]
    public void SpatialHash_FindsNeighbours_AndReturnsThemInIdOrder()
    {
        var hash = new SpatialHash(512f, 32f, toroidal: true);
        hash.Clear();

        // Added out of id order deliberately: results must still come back sorted,
        // because callers accumulate floats over them.
        hash.Add(7, new Vector2(100f, 100f), 4f);
        hash.Add(2, new Vector2(110f, 100f), 4f);
        hash.Add(5, new Vector2(105f, 105f), 4f);
        hash.Add(9, new Vector2(400f, 400f), 4f);
        hash.Build();

        Span<int> results = stackalloc int[16];
        int count = hash.Query(new Vector2(105f, 102f), 20f, excludeId: -1, results);

        Assert.Equal(3, count);
        Assert.Equal(new[] { 2, 5, 7 }, results[..count].ToArray());
    }

    [Fact]
    public void SpatialHash_WrapsAcrossTheWorldSeam()
    {
        var hash = new SpatialHash(512f, 32f, toroidal: true);
        hash.Clear();
        hash.Add(1, new Vector2(5f, 50f), 4f);      // just inside the left edge
        hash.Build();

        Span<int> results = stackalloc int[8];
        int count = hash.Query(new Vector2(508f, 50f), 20f, excludeId: -1, results);

        Assert.Equal(1, count);
    }

    [Fact]
    public void RayCast_StopsAtTheNearestPlant()
    {
        var (_, board) = Build();
        var plants = board.Plants;

        // Clear a lane, then put two plants in it.
        for (int i = 0; i < plants.CellCount; i++) plants.Clear(i);

        int near = plants.IndexAt(new Vector2(100f, 50f));
        int far = plants.IndexAt(new Vector2(200f, 50f));
        plants.Plant(near, PlantKind.Grass, 10f);
        plants.Plant(far, PlantKind.Grass, 10f);

        int hit = board.RayCastPlant(
            new Vector2(20f, 50f), new Vector2(1f, 0f), 300f, out float distance, out _);

        Assert.Equal(near, hit);
        Assert.True(distance < 120f, $"ray reported the far plant (distance {distance})");
    }

    private static int FindPlant(BoardEnvironment board, PlantKind kind)
    {
        for (int i = 0; i < board.Plants.CellCount; i++)
            if (board.Plants.KindAt(i) == kind) return i;

        throw new InvalidOperationException($"no {kind} on the board");
    }

    private static int CountPlants(BoardEnvironment board)
    {
        int n = 0;
        for (int i = 0; i < board.Plants.CellCount; i++)
            if (board.Plants.KindAt(i) != PlantKind.None) n++;
        return n;
    }
}
