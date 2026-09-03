using System;
using System.Numerics;
using GoL.Sim.Components;
using GoL.Sim.Genetics;

namespace GoL.Sim.Core;

/// <summary>
/// The components one creature's senses need, gathered into a single value so the
/// sensing code takes one parameter rather than seven.
/// <para>
/// A <c>readonly ref struct</c>: it exists only for the duration of a call, holds
/// references to components the ECS already owns, and must never be stored. That
/// keeps sensing allocation-free while still reading naturally.
/// </para>
/// </summary>
public readonly ref struct SenseSubject
{
    public SenseSubject(
        int id, Body body, Energy energy, Vitals vitals, Genes genes, Mind mind, Sight sight)
    {
        Id = id;
        Body = body;
        Energy = energy;
        Vitals = vitals;
        Genes = genes;
        Mind = mind;
        Sight = sight;
    }

    public int Id { get; }
    public Body Body { get; }
    public Energy Energy { get; }
    public Vitals Vitals { get; }
    public Genes Genes { get; }
    public Mind Mind { get; }
    public Sight Sight { get; }

    public Genome Genome => Genes.Genome;
    public Vector2 Position => Body.Position;
    public float Heading => Body.Heading;

    /// <summary>Where the nose samples pheromones - just in front of the body.</summary>
    public Vector2 NosePosition => Body.Position + Body.Forward * Body.Radius;

    /// <summary>How far the mouth reaches from the body centre.</summary>
    public float MouthRange => Body.Radius + Genes.Trait(TraitAxis.MouthReach);
}
