using GoL.Sim;

namespace GoL.Sim.Tests;

public class ArchitectureTests
{
    /// <summary>
    /// The one architectural rule that must not break: the simulation core stays
    /// free of the graphics stack, so it can be tested headlessly and soaked for
    /// 10k ticks without a window. This test will fail exactly once - the day
    /// someone adds a convenient `using Microsoft.Xna.Framework;` for Vector2.
    /// Use System.Numerics.Vector2 instead and convert at the render boundary.
    /// </summary>
    [Fact]
    public void SimAssembly_DoesNotReferenceGraphicsStack()
    {
        var referenced = typeof(SimInfo).Assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .ToArray();

        var offenders = referenced
            .Where(n => n.StartsWith("MonoGame", StringComparison.OrdinalIgnoreCase)
                     || n.StartsWith("Microsoft.Xna", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"GoL.Sim must stay headless but references: {string.Join(", ", offenders)}");
    }
}
