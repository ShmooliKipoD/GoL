namespace GoL.Sim.Core;

/// <summary>
/// Keeps the world's random sequences independent of one another.
/// <para>
/// Two PCG32 streams with the same seed but different ids do not correlate, so
/// seeding plants does not shift what creatures draw, and adding a creature does
/// not shift the world's own sequence. Without this, any change to how many random
/// values one subsystem consumes would silently alter every other subsystem's
/// results - and a seeded run would stop being reproducible across refactors.
/// </para>
/// </summary>
public static class StreamId
{
    public const ulong World = 1;
    public const ulong Plants = 2;
    public const ulong Creature = 3;
    public const ulong Respawn = 4;
}
