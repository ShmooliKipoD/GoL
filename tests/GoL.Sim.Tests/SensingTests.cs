using System.Numerics;
using GoL.Sim;
using GoL.Sim.Core;
using GoL.Sim.Genetics;
using GoL.Sim.Systems;

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
    private static (SimWorld World, LabEnvironment Env, CreatureView Subject) Lab(
        float eyeRange = 100f, float halfFov = 0.5f, float heading = 0f)
    {
        var config = new SimConfig { Seed = 1, WorldSize = Arena, Toroidal = true };
        var env = new LabEnvironment(config);
        var world = new SimWorld(config, env);

        var rng = new Pcg32(42);
        var genome = Genome.CreateSeed(ref rng);

        SetTrait(genome, TraitAxis.EyeRange, eyeRange);
        SetTrait(genome, TraitAxis.EyeHalfFov, halfFov);

        float centre = env.WorldSize * 0.5f;
        int id = world.Spawn(genome, new Vector2(centre, centre), heading);
        return (world, env, world.View(id));
    }

    /// <summary>Sets a trait to a real value by inverting its normalized range.</summary>
    private static void SetTrait(Genome genome, TraitAxis axis, float value)
    {
        var range = Traits.Range(axis);
        genome.TraitValues[(int)axis] = Math.Clamp((value - range.Min) / (range.Max - range.Min), 0f, 1f);
    }

    /// <summary>Runs one sensing pass, exactly as SenseSystem would.</summary>
    private static void Look(SimWorld world, CreatureView subject)
    {
        var senses = new Senses();
        var mind = subject.Mind;
        var buffer = new float[mind.Brain.SensorCount];

        senses.Sample(
            new SenseSubject(subject.Id, subject.Body, subject.Energy, subject.Vitals,
                subject.Genes, mind, subject.Sight),
            world.Field, 0f, buffer);
    }

    private static bool SeesAnything(Eye eye)
    {
        foreach (var kind in eye.Kind) if (kind != SeenKind.None) return true;
        return false;
    }

    [Fact]
    public void SeesAPlantDirectlyAhead()
    {
        var (world, env, subject) = Lab();
        env.AddPlant(subject.Position + new Vector2(50f, 0f));

        Look(world, subject);

        Assert.True(SeesAnything(subject.ForwardEye));

        // Directly ahead lands in a middle bin, not at an edge.
        int middle = Vision.BinCount / 2;
        Assert.Equal(SeenKind.Plant, subject.ForwardEye.Kind[middle]);
    }

    [Fact]
    public void DoesNotSeeAPlantDirectlyBehind()
    {
        var (world, env, subject) = Lab();
        env.AddPlant(subject.Position + new Vector2(-50f, 0f));

        Look(world, subject);

        Assert.False(SeesAnything(subject.ForwardEye));
    }

    [Theory]
    [InlineData(0.95f, true)]   // just inside range
    [InlineData(1.60f, false)]  // beyond range
    public void RangeIsRespected(float distanceFactor, bool expected)
    {
        const float Range = 100f;
        var (world, env, subject) = Lab(eyeRange: Range);
        env.AddPlant(subject.Position + new Vector2(Range * distanceFactor, 0f));

        Look(world, subject);

        Assert.Equal(expected, SeesAnything(subject.ForwardEye));
    }

    [Theory]
    [InlineData(0.4f, true)]    // inside a 0.5 rad half-FOV
    [InlineData(1.2f, false)]   // well outside it
    public void FieldOfViewIsRespected(float angle, bool expected)
    {
        const float HalfFov = 0.5f;
        var (world, env, subject) = Lab(halfFov: HalfFov);

        env.AddPlant(subject.Position + new Vector2(
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
        var (world, env, subject) = Lab();
        env.AddPlant(subject.Position + new Vector2(80f, 0f));
        env.AddPlant(subject.Position + new Vector2(20f, 0f));

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
        var env = new LabEnvironment(config);
        var world = new SimWorld(config, env);

        var rng = new Pcg32(7);
        var genome = Genome.CreateSeed(ref rng);
        SetTrait(genome, TraitAxis.EyeRange, 100f);
        SetTrait(genome, TraitAxis.EyeHalfFov, 0.5f);

        // Just inside the right edge, facing +X - so the plant just inside the LEFT
        // edge is a short hop away round the seam, not a world away.
        int id = world.Spawn(genome, new Vector2(env.WorldSize - 10f, 50f), 0f);
        var subject = world.View(id);
        env.AddPlant(new Vector2(10f, 50f));

        Look(world, subject);

        Assert.True(SeesAnything(subject.ForwardEye),
            "wrapping was ignored, so the creature is blind at the seam");
    }

    [Fact]
    public void RearEye_ExistsOnlyOnceTheTraitIsUnlocked()
    {
        var (world, env, subject) = Lab();
        env.AddPlant(subject.Position + new Vector2(-50f, 0f));

        Look(world, subject);
        Assert.Null(subject.RearEye);

        var rng = new Pcg32(3);
        Mutator.Unlock(subject.Genome, LatentTraitId.RearEye, ref rng);
        subject.Mind.Rebuild(subject.Genome);

        Look(world, subject);

        Assert.NotNull(subject.RearEye);
        Assert.True(SeesAnything(subject.RearEye!),
            "the rear eye should see what is behind the creature");
    }

    [Fact]
    public void MouthArcIsRespected_ANearbyPlantBehindIsNotInReach()
    {
        var (world, env, subject) = Lab();
        SetTrait(subject.Genome, TraitAxis.MouthReach, 5f);
        SetTrait(subject.Genome, TraitAxis.MouthArc, 0.4f);

        // Touching the body, but behind it.
        env.AddPlant(subject.Position + new Vector2(-(subject.Radius + 1f), 0f));

        Look(world, subject);

        int contact = subject.Mind.Layout.IndexOf(
            NodeIds.Sensor(NodeIds.InnateSlot, (int)InnateSense.MouthContact));

        var buffer = new float[subject.Mind.Brain.SensorCount];
        new Senses().Sample(
            new SenseSubject(subject.Id, subject.Body, subject.Energy, subject.Vitals,
                subject.Genes, subject.Mind, subject.Sight),
            world.Field, 0f, buffer);

        Assert.Equal(0f, buffer[contact]);
    }
}
