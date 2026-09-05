using System.Numerics;
using GoL.Sim.Components;
using GoL.Sim.Core;
using GoL.Sim.Genetics;
using GoL.Sim.Systems;

namespace GoL.Sim.Acting;

/// <summary>
/// Everything an action needs to execute, gathered into one value.
/// <para>
/// A <c>readonly ref struct</c>, exactly like <see cref="SenseSubject"/>: it lives
/// for the duration of one call, holds references to components the ECS already
/// owns, and must never be stored. Actions stay allocation-free without having to
/// take nine parameters.
/// </para>
/// <para>
/// Shaped now for what comes after this step. Creature <i>states</i> and the
/// discrete brain both need to reach this same set of components, so they should
/// add a field here rather than reshape the contract.
/// </para>
/// </summary>
public readonly ref struct ActionContext
{
    public ActionContext(
        SimWorld world, int id, Body body, Energy energy, Vitals vitals,
        Genes genes, Mind mind, Sight sight, Doing doing)
    {
        World = world;
        Id = id;
        Body = body;
        Energy = energy;
        Vitals = vitals;
        Genes = genes;
        Mind = mind;
        Sight = sight;
        Doing = doing;
    }

    public SimWorld World { get; }
    public int Id { get; }
    public Body Body { get; }
    public Energy Energy { get; }
    public Vitals Vitals { get; }
    public Genes Genes { get; }
    public Mind Mind { get; }
    public Sight Sight { get; }

    /// <summary>What is running, and what it has latched onto.</summary>
    public Doing Doing { get; }

    public Genome Genome => Genes.Genome;

    /// <summary>What this creature may perceive. Actions steer by what the creature
    /// can actually <i>see</i> - never by asking the environment where the food is.
    /// A creature with poor eyes must be worse at finding food than one with good
    /// eyes, or vision stops being something a lineage evolves.</summary>
    public ISenseField Field => World.Field;

    public Vector2 Position => Body.Position;
    public float Heading => Body.Heading;

    /// <summary>How far the mouth reaches from the body centre.</summary>
    public float MouthRange => Body.Radius + Genes.Trait(TraitAxis.MouthReach);
}
