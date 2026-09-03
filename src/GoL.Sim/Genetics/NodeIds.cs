namespace GoL.Sim.Genetics;

/// <summary>
/// Allocates brain node ids by <b>derivation</b> rather than by a counter.
/// <para>
/// A sensor's id is computed from which trait owns it and which channel it is,
/// so the same sensor has the same id in every genome in every run - for free,
/// with no registry to keep in sync and no counter whose value could leak into
/// results. Unlocking a trait simply inserts the ids that trait declares.
/// </para>
/// <para>
/// Only hidden neurons get counter-allocated ids, and only within one genome.
/// Reproduction is asexual, so hidden ids never need to mean anything across
/// genomes.
/// </para>
/// </summary>
public static class NodeIds
{
    /// <summary>Constant 1.0 input, so neurons can learn an offset.</summary>
    public const int Bias = 0;

    public const int SensorBase = 1;
    public const int EffectorBase = 4096;
    public const int HiddenBase = 8192;

    /// <summary>Channels reserved per trait. Generous, because running out would
    /// mean renumbering every saved genome.</summary>
    public const int SlotStride = 32;

    /// <summary>Trait slot owning the innate senses every creature has.</summary>
    public const int InnateSlot = 0;

    /// <summary>Trait slot owning the forward eye's vision bins. Vision needs more
    /// than one stride, so it gets slots 1 and 2 to itself.</summary>
    public const int VisionSlot = 1;

    /// <summary>First slot available to the latent trait catalog.</summary>
    public const int FirstLatentSlot = 4;

    public static int Sensor(int traitSlot, int channel) =>
        SensorBase + traitSlot * SlotStride + channel;

    public static int Effector(int traitSlot, int channel) =>
        EffectorBase + traitSlot * SlotStride + channel;

    public static bool IsSensor(int id) => id >= SensorBase && id < EffectorBase;
    public static bool IsEffector(int id) => id >= EffectorBase && id < HiddenBase;
    public static bool IsHidden(int id) => id >= HiddenBase;
    public static bool IsBias(int id) => id == Bias;

    /// <summary>True if the node can be the target of a connection. Sensors and the
    /// bias node are driven by the world, so nothing may write to them.</summary>
    public static bool CanReceive(int id) => !IsSensor(id) && !IsBias(id);
}

/// <summary>Innate sensor channels, present on every creature.</summary>
public enum InnateSense
{
    /// <summary>Energy as a fraction of this creature's maximum.</summary>
    Energy,

    /// <summary>Age as a fraction of maximum lifespan.</summary>
    Age,

    /// <summary>Current forward speed, signed, relative to top speed.</summary>
    Speed,

    /// <summary>Current turn rate, signed, relative to top turn rate.</summary>
    Turn,

    /// <summary>Closeness of the nearest food inside the bite arc, 0 if none.</summary>
    MouthContact,

    /// <summary>What is in the bite arc: -1 plant, +1 creature, 0 nothing.</summary>
    MouthKind,

    /// <summary>Collided with another creature last tick.</summary>
    Bump,

    /// <summary>Ground fertility beneath the body.</summary>
    Fertility,

    /// <summary>Pheromone channel 0 strength at the nose.</summary>
    Scent0,

    /// <summary>A fixed 1.5s sine oscillator. A free rhythm generator - without one,
    /// evolving any gait at all requires first evolving a recurrent oscillator.</summary>
    Oscillator,
}

/// <summary>Innate effector channels, present on every creature.</summary>
public enum InnateAction
{
    /// <summary>Forward drive, -1..1. Negative reverses, at reduced speed.</summary>
    Thrust,

    /// <summary>Turn drive, -1..1.</summary>
    Turn,

    /// <summary>Above 0.5 the mouth is open and bites this tick.</summary>
    Bite,

    /// <summary>Above 0.5 the creature attempts to reproduce - still gated on
    /// energy, age and cooldown.</summary>
    Reproduce,
}
