using GoL.Sim.Components;
using GoL.Sim.Genetics;
using GoL.Sim.Systems;

namespace GoL.Sim.Core;

/// <summary>
/// The world creatures live in: its plants, its fields, and its spatial index.
/// <para>
/// Separate from <see cref="ISenseField"/>, which is only the read-only slice a
/// creature can perceive. This is the writable side - growth, decay, eating and
/// death - and only the simulation's systems touch it.
/// </para>
/// <para>
/// The seam exists so the Creature Lab's hand-placed plants and the real board can
/// both drive the same systems. If swapping one for the other requires changing a
/// system, the seam was drawn in the wrong place.
/// </para>
/// </summary>
public interface IEnvironment
{
    /// <summary>What creatures may perceive.</summary>
    ISenseField SenseField { get; }

    /// <summary>Advances plants, fertility and pheromones, and rebuilds the spatial
    /// index. Called first in the tick, so every creature perceives the same state.</summary>
    void Step(SimWorld world, float dt);

    /// <summary>Deposits pheromone.</summary>
    void Emit(Vector2 position, int channel, float amount);

    /// <summary>Resolves one creature's bite against whatever is in its mouth arc.</summary>
    void ResolveBite(SimWorld world, Body body, Energy energy, Genome genome, Mind mind, float dt);

    /// <summary>Called when a creature dies, so the environment can leave a corpse.</summary>
    void OnDeath(Vector2 position, float radius, float remainingEnergy);
}
