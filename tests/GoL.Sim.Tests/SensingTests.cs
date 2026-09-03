using System.Numerics;
using GoL.Sim;
using GoL.Sim.Core;
using GoL.Sim.Genetics;

namespace GoL.Sim.Tests;

/// <summary>
/// Vision geometry. These are the tests that catch a creature that cannot see food
/// sitting in front of it - a failure that looks like "evolution isn't working"
/// rather than like a bug.
/// </summary>
public class SensingTests
{
    private const float Arena = 340f;

    /// <summary>A creature at the arena centre facing +X, with known eye geometry.</summary>
    private static (LabWorld World, Creature Subject) Lab(
        float eyeRange = 100f, float halfFov = 0.5f, float heading = 0f)
    {
        var config = new SimConfig { Seed = 1, WorldSize = Arena, Toroidal = true };
        var world = new LabWorld(config);

        var rng = new Pcg32(42);
        var genome = Genome.CreateSeed(ref rng);

        SetTrait(genome, TraitAxis.EyeRange, eyeRange);
        SetTrait(genome, TraitAxis.EyeHalfFov, halfFov);

        float centre = world.WorldSize * 0.5f;
        var subject = world.Spawn(genome, new Vector2(centre, centre), heading);
        return (world, subject);
    }

    /// <summary>Sets a trait to a real value by inverting its normalized range.</summary>
    private static void SetTrait(Genome genome, TraitAxis axis, float value)
    {
        var range = Traits.Range(axis);
        genome.TraitValues[(int)axis] = Math.Clamp((value - range.Min) / (range.Max - range.Min), 0f, 1f);
    }

    private static void Look(LabWorld world, Creature subject)
    {
        var senses = new Senses();
        var buffer = new float[subject.Brain.SensorCount];
        senses.Sample(subject, world, subject.Layout, 0f, buffer);
    }

    private static bool SeesAnything(Eye eye)
    {
        foreach (var kind in eye.Kind) if (kind != SeenKind.None) return true;
        return false;
    }

    [Fact]
    public void SeesAPlantDirectlyAhead()
    {
        var (world, subject) = Lab();
        world.AddPlant(subject.Position + new Vector2(50f, 0f));

        Look(world, subject);

        Assert.True(SeesAnything(subject.ForwardEye));

        // Directly ahead lands in a middle bin, not at an edge.
        int middle = Vision.BinCount / 2;
        Assert.Equal(SeenKind.Plant, subject.ForwardEye.Kind[middle]);
    }

    [Fact]
    public void DoesNotSeeAPlantDirectlyBehind()
    {
        var (world, subject) = Lab();
        world.AddPlant(subject.Position + new Vector2(-50f, 0f));

        Look(world, subject);

        Assert.False(SeesAnything(subject.ForwardEye));
    }

    [Theory]
    [InlineData(0.95f, true)]   // just inside range
    [InlineData(1.60f, false)]  // beyond range
    public void RangeIsRespected(float distanceFactor, bool expected)
    {
        const float Range = 100f;
        var (world, subject) = Lab(eyeRange: Range);
        world.AddPlant(subject.Position + new Vector2(Range * distanceFactor, 0f));

        Look(world, subject);

        Assert.Equal(expected, SeesAnything(subject.ForwardEye));
    }

    [Theory]
    [InlineData(0.4f, true)]    // inside a 0.5 rad half-FOV
    [InlineData(1.2f, false)]   // well outside it
    public void FieldOfViewIsRespected(float angle, bool expected)
    {
        const float HalfFov = 0.5f;
        var (world, subject) = Lab(halfFov: HalfFov);

        world.AddPlant(subject.Position + new Vector2(
            MathF.Cos(angle) * 50f, MathF.Sin(angle) * 50f));

        Look(world, subject);

        Assert.Equal(expected, SeesAnything(subject.ForwardEye));
    }

    /// <summary>
    /// A near object must hide a far one in the same bin. Without this the brain
    /// would receive the distance of whichever object happened to be scanned last.
    /// </summary>
    [Fact]
    public void NearerObjectOccludesFartherOneInTheSameBin()
    {
        var (world, subject) = Lab();
        world.AddPlant(subject.Position + new Vector2(80f, 0f));
        world.AddPlant(subject.Position + new Vector2(20f, 0f));

        Look(world, subject);

        int middle = Vision.BinCount / 2;
        Assert.Equal(SeenKind.Plant, subject.ForwardEye.Kind[middle]);
        Assert.True(subject.ForwardEye.Distance[middle] < 30f,
            "the eye reported the far plant over the near one");
    }

    [Fact]
    public void SeesAcrossTheWorldSeam_WhenToroidal()
    {
        var config = new SimConfig { Seed = 1, WorldSize = Arena, Toroidal = true };
        var world = new LabWorld(config);

        var rng = new Pcg32(7);
        var genome = Genome.CreateSeed(ref rng);
        SetTrait(genome, TraitAxis.EyeRange, 100f);
        SetTrait(genome, TraitAxis.EyeHalfFov, 0.5f);

        // Just inside the right edge, facing +X - so the plant just inside the LEFT
        // edge is a short hop away round the seam, not a world away.
        var subject = world.Spawn(genome, new Vector2(world.WorldSize - 10f, 50f), 0f);
        world.AddPlant(new Vector2(10f, 50f));

        Look(world, subject);

        Assert.True(SeesAnything(subject.ForwardEye),
            "wrapping was ignored, so the creature is blind at the seam");
    }

    [Fact]
    public void RearEye_ExistsOnlyOnceTheTraitIsUnlocked()
    {
        var (world, subject) = Lab();
        world.AddPlant(subject.Position + new Vector2(-50f, 0f));

        Look(world, subject);
        Assert.Null(subject.RearEye);

        var rng = new Pcg32(3);
        Mutator.Unlock(subject.Genome, LatentTraitId.RearEye, ref rng);
        subject.Rebuild();

        Look(world, subject);

        Assert.NotNull(subject.RearEye);
        Assert.True(SeesAnything(subject.RearEye!),
            "the rear eye should see what is behind the creature");
    }

    [Fact]
    public void MouthArcIsRespected_ANearbyPlantBehindIsNotInReach()
    {
        var (world, subject) = Lab();
        SetTrait(subject.Genome, TraitAxis.MouthReach, 5f);
        SetTrait(subject.Genome, TraitAxis.MouthArc, 0.4f);

        // Touching the body, but behind it.
        world.AddPlant(subject.Position + new Vector2(-(subject.Radius + 1f), 0f));

        Look(world, subject);

        int contact = subject.Layout.IndexOf(
            NodeIds.Sensor(NodeIds.InnateSlot, (int)InnateSense.MouthContact));

        var buffer = new float[subject.Brain.SensorCount];
        new Senses().Sample(subject, world, subject.Layout, 0f, buffer);

        Assert.Equal(0f, buffer[contact]);
    }
}
