using GoL.Sim.Components;
using GoL.Sim.Genetics;

namespace GoL.Sim.Core;

/// <summary>
/// A read-only handle to one creature's components, for code outside the ECS -
/// renderers, the inspector panels, tests.
/// <para>
/// A plain readonly struct rather than a <c>ref struct</c> (which
/// <see cref="SenseSubject"/> is) because callers legitimately store one: the lab
/// keeps a handle to the creature it is following. It holds references to
/// components the ECS owns, so it stays valid only while that entity lives.
/// </para>
/// <para>
/// Presentation reads through this and never writes. That is what keeps
/// <c>GoL.Render</c> from acquiring simulation logic by accident.
/// </para>
/// </summary>
public readonly struct CreatureView
{
    public CreatureView(
        int id, Body body, Energy energy, Vitals vitals, Genes genes, Mind mind, Sight sight,
        Behaviour behaviour)
    {
        Id = id;
        Body = body;
        Energy = energy;
        Vitals = vitals;
        Genes = genes;
        Mind = mind;
        Sight = sight;
        Behaviour = behaviour;
    }

    public int Id { get; }
    public Body Body { get; }
    public Energy Energy { get; }
    public Vitals Vitals { get; }
    public Genes Genes { get; }
    public Mind Mind { get; }
    public Sight Sight { get; }
    public Behaviour Behaviour { get; }

    public Genome Genome => Genes.Genome;
    public Vector2 Position => Body.Position;
    public float Heading => Body.Heading;
    public float Radius => Body.Radius;
    public float EnergyFraction => Energy.Fraction;
    public float Age => Vitals.Age;
    public bool Alive => Vitals.Alive;

    public Eye ForwardEye => Sight.Forward;
    public Eye? RearEye => Sight.Rear;

    public Intent LastIntent => Mind.Intent;
    public bool BitThisTick => Mind.BitThisTick;

    /// <summary>What this creature is doing, as a name. "Idle" when nothing clears
    /// the deadband.</summary>
    public string ActionLabel => Behaviour.Label();

    /// <summary>
    /// Energy per second this creature burns just existing, before movement. Paired
    /// with <see cref="Components.Energy.IntakeRate"/> it answers the only question
    /// that matters while watching one forage: is it winning or losing?
    /// </summary>
    public float UpkeepRate => Metabolism.BaseCost(
        Body, Genome, Mind.Brain, Vitals, SimConfig.DefaultBaseMetabolicRate,
        Mind.Intent.Torpor);

    public Vector2 Forward => Body.Forward;
    public Vector2 NosePosition => Body.Position + Body.Forward * Body.Radius;
    public float MouthRange => Body.Radius + Genes.Trait(TraitAxis.MouthReach);

    public float Trait(TraitAxis axis) => Genes.Trait(axis);
}
