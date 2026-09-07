using System;
using System.Numerics;

namespace GoL.Sim.Core;

/// <summary>Something a creature can perceive.</summary>
/// <param name="Position">World position.</param>
/// <param name="Radius">Physical radius, so vision can account for size.</param>
/// <param name="Kind">Plant or creature.</param>
/// <param name="Hue">Body hue, meaningful only for creatures.</param>
/// <param name="Id">Identifier within its kind, for the eater to look up.</param>
public readonly record struct Percept(
    Vector2 Position, float Radius, SeenKind Kind, float Hue, int Id);

/// <summary>
/// The world as a creature's senses can reach it.
/// <para>
/// This is the seam between the creature (Step 2) and the board (Step 3). The sandbox
/// backs it with brute-force scans over a handful of hand-placed plants; the real
/// world backs it with a spatial hash, plant grid and diffusing pheromone field.
/// Swapping one for the other must not require touching creature code - if it
/// does, this interface was drawn in the wrong place.
/// </para>
/// <para>
/// Every query writes into a caller-owned span and returns a count. No
/// <c>IEnumerable</c>, no LINQ: this runs for every creature every tick and must
/// not allocate.
/// </para>
/// </summary>
public interface ISenseField
{
    /// <summary>Side length of the world.</summary>
    float WorldSize { get; }

    /// <summary>Whether positions wrap at the edges.</summary>
    bool Toroidal { get; }

    /// <summary>Everything perceivable within <paramref name="radius"/> of a point,
    /// excluding the creature doing the looking. Results are ordered by id, which
    /// keeps float accumulation over them reproducible.</summary>
    int Query(Vector2 centre, float radius, int excludeCreatureId, Span<Percept> results);

    /// <summary>Creatures only, without sweeping the vegetation grid.</summary>
    int QueryCreatures(Vector2 centre, float radius, int excludeCreatureId, Span<Percept> results);

    /// <summary>
    /// Marches the vegetation grid along a ray and reports the first plant hit.
    /// <para>
    /// This exists for performance, and the difference is not marginal. Gathering
    /// every plant within eye range means sweeping the square that encloses it -
    /// roughly 1200 cells per creature per tick at a 130-unit range - and that scan
    /// was measured as the simulation's dominant cost. A ray per vision bin visits
    /// on the order of sixteen cells instead, and yields nearest-hit-per-bin and
    /// occlusion for free, which is exactly what the eye needs anyway.
    /// </para>
    /// </summary>
    /// <returns>The plant's cell index, or -1 if the ray hits nothing.</returns>
    int RayCastPlant(
        Vector2 origin, Vector2 direction, float maxDistance,
        out float distance, out float radius);

    /// <summary>Pheromone strength on a channel at a point, 0..1.</summary>
    float SampleScent(Vector2 position, int channel);

    /// <summary>Ground fertility beneath a point, 0..1.</summary>
    float SampleFertility(Vector2 position);

    /// <summary>Shortest offset from a to b, taking wrapping into account. Every
    /// distance in the simulation must go through this or creatures will fail to
    /// see things just across a world seam.</summary>
    Vector2 Offset(Vector2 from, Vector2 to);

    /// <summary>Brings a position back inside the world.</summary>
    Vector2 Wrap(Vector2 position);
}
