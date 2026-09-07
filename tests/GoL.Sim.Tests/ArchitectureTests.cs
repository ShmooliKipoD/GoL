using System.Numerics;
using System.Reflection;
using GoL.Sim;
using GoL.Sim.Core;
using GoL.Sim.Genetics;
using GoL.Sim.Systems;

namespace GoL.Sim.Tests;

/// <summary>
/// Guards the rule that actually matters: <b>the simulation must never need
/// graphics to run.</b>
/// <para>
/// This used to be "GoL.Sim references no MonoGame assembly at all". That is no
/// longer the rule - the core uses MonoGame.Extended's ECS, which was verified to
/// build and run with no GraphicsDevice, no window and no
/// DYLD_FALLBACK_LIBRARY_PATH. The reference is fine; <i>needing a display</i> is
/// not. See CLAUDE.md.
/// </para>
/// </summary>
public class ArchitectureTests
{
    /// <summary>
    /// The complete set of non-BCL assemblies the simulation is allowed to pull in.
    /// Deliberately an allowlist: a new dependency should be a decision someone
    /// makes on purpose, not something that arrives with a convenient `using`.
    /// </summary>
    private static readonly string[] AllowedThirdParty =
    {
        "MonoGame.Extended",   // ECS only
        "MonoGame.Framework",  // types Extended's API exposes (GameTime, and no more)
    };

    [Fact]
    public void SimAssembly_PullsInNothingUnexpected()
    {
        var unexpected = typeof(SimWorld).Assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .Where(n => !n.StartsWith("System", StringComparison.Ordinal)
                     && !n.StartsWith("Microsoft.CSharp", StringComparison.Ordinal)
                     && n != "netstandard"
                     && n != "mscorlib"
                     && !AllowedThirdParty.Contains(n))
            .ToArray();

        Assert.True(unexpected.Length == 0,
            $"GoL.Sim gained an unvetted dependency: {string.Join(", ", unexpected)}");
    }

    /// <summary>
    /// The rule stated directly: build a world, populate it and run it, with no
    /// graphics device anywhere. If the simulation ever starts needing a display,
    /// this is what stops compiling or throws.
    /// </summary>
    [Fact]
    public void AWorld_RunsWithoutAnyGraphicsDevice()
    {
        var config = new SimConfig { Seed = 99, WorldSize = 340f, TicksPerSecond = 60 };
        var environment = new SandboxEnvironment(config);
        var world = new SimWorld(config, environment);

        environment.SeedPlants(12);

        var rng = new Pcg32(99);
        for (int i = 0; i < 8; i++)
        {
            world.Spawn(
                Genome.CreateSeed(ref rng),
                new Vector2(rng.NextFloat(0f, 340f), rng.NextFloat(0f, 340f)),
                rng.NextFloat(0f, MathF.Tau));
        }

        for (int tick = 0; tick < 600; tick++) world.Step();

        Assert.Equal(600, world.Tick);
        Assert.True(world.SimTime > 0f);
    }

    /// <summary>
    /// The simulation must never read a clock. <see cref="SimWorld.Step"/> taking no
    /// arguments is what enforces it - the host accumulates real time and calls this
    /// an integer number of times, so a seeded run reproduces exactly.
    /// </summary>
    [Fact]
    public void Step_TakesNoTimeArgument()
    {
        var step = typeof(SimWorld).GetMethod(nameof(SimWorld.Step), BindingFlags.Public | BindingFlags.Instance);

        Assert.NotNull(step);
        Assert.Empty(step!.GetParameters());
    }
}
