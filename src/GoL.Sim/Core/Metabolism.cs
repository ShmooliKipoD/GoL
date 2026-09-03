using System;
using GoL.Sim.Brains;
using GoL.Sim.Components;
using GoL.Sim.Genetics;

namespace GoL.Sim.Core;

/// <summary>
/// What living costs and what eating pays.
/// <para>
/// Every constant here is a guess and will be retuned. The failure modes are stark
/// and fast: too expensive and everything starves within a minute; too cheap and
/// "sit still on grass and bud forever" dominates, so brains never grow and the
/// whole evolutionary premise goes nowhere. They live here as one block rather than
/// scattered through the tick, so retuning is a single readable edit.
/// </para>
/// </summary>
public static class Metabolism
{
    /// <summary>Hard lifespan cap. Creatures normally die well before this - not
    /// from a cutoff but because senescence raises upkeep past what they can earn,
    /// which is both more natural and one fewer special case.</summary>
    public const float MaxAge = 320f;

    /// <summary>Energy per second per unit of body mass.</summary>
    public const float SizeCost = 0.30f;

    /// <summary>Energy per second per brain node plus connection. A bigger brain is
    /// a real cost, so topology only grows where it earns its keep.</summary>
    public const float BrainCost = 0.0016f;

    /// <summary>Coefficient on movement cost.</summary>
    public const float MoveCost = 1.60f;

    /// <summary>Coefficient on turning cost.</summary>
    public const float TurnCost = 0.22f;

    /// <summary>How steeply upkeep climbs after maturity.</summary>
    public const float SenescenceRate = 2.0f;

    /// <summary>Speed multiplier while the sprint gland is firing.</summary>
    public const float SprintSpeedFactor = 1.9f;

    /// <summary>Movement cost multiplier while sprinting. Far above the speed gain,
    /// so sprinting is a real trade-off rather than free.</summary>
    public const float SprintCostFactor = 4.5f;

    /// <summary>Reverse runs at this fraction of top speed.</summary>
    public const float ReverseFactor = 0.4f;

    /// <summary>Armour's speed penalty.</summary>
    public const float ArmorSpeedFactor = 0.85f;

    /// <summary>Energy per second a bite draws from its target.</summary>
    public const float BiteRate = 22f;

    /// <summary>Fraction of a creature's energy lost as overhead when reproducing.</summary>
    public const float BirthOverhead = 0.12f;

    /// <summary>Fraction of a parent's energy handed to the offspring.</summary>
    public const float OffspringShare = 0.42f;

    /// <summary>Minimum seconds between births.</summary>
    public const float BirthCooldown = 3f;

    public const float ToxinDecay = 0.25f;
    public const float ToxinDrain = 0.9f;

    /// <summary>
    /// Energy per second burned by simply being this creature: a base rate, plus
    /// body size, plus brain complexity, plus the upkeep of every unlocked trait -
    /// all scaled by the genome's metabolism and by age.
    /// </summary>
    public static float BaseCost(
        Body body, Genome genome, Brain brain, Vitals vitals, float baseRate)
    {
        float structural =
            baseRate
            + SizeCost * body.Mass
            + BrainCost * (brain.NodeCount + brain.ConnCount)
            + genome.LatentUpkeep();

        return structural * genome.Trait(TraitAxis.Metabolism) * AgeFactor(vitals, genome);
    }

    /// <summary>
    /// Upkeep multiplier from age. Flat until maturity, then rising quadratically -
    /// so old creatures die of being unable to pay for themselves rather than of
    /// hitting an arbitrary limit.
    /// </summary>
    public static float AgeFactor(Vitals vitals, Genome genome)
    {
        float mature = genome.Trait(TraitAxis.MatureAge);
        if (vitals.Age <= mature) return 1f;

        float span = MathF.Max(MaxAge - mature, 1f);
        float t = (vitals.Age - mature) / span;
        return 1f + SenescenceRate * t * t;
    }

    /// <summary>
    /// Energy per second spent moving. Quadratic in speed, so cruising is cheap and
    /// flat-out is expensive - which is what gives speed a genuine trade-off rather
    /// than making "always maximum" the obvious strategy.
    /// </summary>
    public static float MovementCost(Body body, Genome genome, bool sprinting)
    {
        float maxSpeed = MathF.Max(genome.Trait(TraitAxis.MaxSpeed), 1e-3f);
        float speedNorm = MathF.Abs(body.Speed) / maxSpeed;

        float turnRate = MathF.Max(genome.Trait(TraitAxis.TurnRate), 1e-3f);
        float turnNorm = MathF.Abs(body.AngularVelocity) / turnRate;

        float sprintMultiplier = sprinting ? SprintCostFactor : 1f;

        return MoveCost * body.Mass * speedNorm * speedNorm * sprintMultiplier
             + TurnCost * body.Mass * turnNorm;
    }

    /// <summary>Top speed after armour and sprint modifiers.</summary>
    public static float EffectiveMaxSpeed(Genome genome, bool sprinting)
    {
        float speed = genome.Trait(TraitAxis.MaxSpeed);
        if (genome.Has(LatentTraitId.ArmorPlating)) speed *= ArmorSpeedFactor;
        if (sprinting) speed *= SprintSpeedFactor;
        return speed;
    }
}
