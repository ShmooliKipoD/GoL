using System;
using System.Numerics;
using GoL.Sim.Components;
using GoL.Sim.Genetics;

namespace GoL.Sim.Core;

/// <summary>What a brain decided to do this tick, before any of it is applied.</summary>
public struct Intent
{
    public float Thrust;
    public float Turn;
    public bool Bite;
    public bool Reproduce;
    public bool Sprint;
    public bool Torpor;
    public float EmitScent0;
    public float EmitScent1;
}

/// <summary>
/// Turns brain outputs into movement, and charges for it.
/// <para>
/// Deliberately split from sensing: every creature senses and thinks against the
/// same frozen world, and only then does anything move. If creature 0 moved before
/// creature 1 sensed, the entire run would depend on the order the list happens to
/// be in, and reproducibility from a seed would be gone.
/// </para>
/// </summary>
public static class Locomotion
{
    /// <summary>A gate opens here.</summary>
    public const float GateOpen = 0.5f;

    /// <summary>And, once open, does not close until here.
    /// <para>
    /// The gap is the whole point. A bare threshold re-evaluated every tick makes a
    /// brain output resting near it chatter at the tick rate - the creature flutters
    /// its mouth open and shut sixty times a second instead of taking a bite. A
    /// Schmitt trigger costs one comparison and gives a body that commits to what it
    /// started.
    /// </para></summary>
    public const float GateClose = 0.2f;

    /// <summary>Opens above <see cref="GateOpen"/>, closes below <see cref="GateClose"/>.</summary>
    public static bool Gate(float value, bool wasOpen) =>
        wasOpen ? value > GateClose : value > GateOpen;

    /// <summary>Reads the brain's effector outputs into an <see cref="Intent"/>.</summary>
    public static Intent ReadIntent(
        Genome genome, SensorLayout layout, ReadOnlySpan<float> effectors, in Intent previous)
    {
        var intent = new Intent
        {
            Thrust = Read(effectors, layout.Action(InnateAction.Thrust)),
            Turn = Read(effectors, layout.Action(InnateAction.Turn)),
            Bite = Gate(Read(effectors, layout.Action(InnateAction.Bite)), previous.Bite),

            // Reproduce is hysteretic too, which was worth checking rather than
            // assuming: unlike the motor gates it feeds CanReproduce, so a gate
            // latched open could in principle mean breeding every BirthCooldown
            // forever. Measured on the board at 12000 ticks, seed 11: population
            // 149 plain against 146 hysteretic - no real difference, because a birth
            // still has to pass age, cooldown and an energy-fraction threshold, and
            // an open gate creates none of those.
            Reproduce = Gate(Read(effectors, layout.Action(InnateAction.Reproduce)), previous.Reproduce),
        };

        if (genome.Has(LatentTraitId.SprintGland))
        {
            int slot = LatentTraitCatalog.Get(LatentTraitId.SprintGland).Slot;
            intent.Sprint = Gate(
                Read(effectors, layout.EffectorIndexOf(NodeIds.Effector(slot, 0))), previous.Sprint);
        }

        if (genome.Has(LatentTraitId.Torpor))
        {
            int slot = LatentTraitCatalog.Get(LatentTraitId.Torpor).Slot;
            intent.Torpor = Gate(
                Read(effectors, layout.EffectorIndexOf(NodeIds.Effector(slot, 0))), previous.Torpor);
        }

        if (genome.Has(LatentTraitId.ScentGlandA))
        {
            int slot = LatentTraitCatalog.Get(LatentTraitId.ScentGlandA).Slot;
            intent.EmitScent0 = MathF.Max(0f, Read(effectors, layout.EffectorIndexOf(NodeIds.Effector(slot, 0))));
        }

        if (genome.Has(LatentTraitId.ScentGlandB))
        {
            int slot = LatentTraitCatalog.Get(LatentTraitId.ScentGlandB).Slot;
            intent.EmitScent1 = MathF.Max(0f, Read(effectors, layout.EffectorIndexOf(NodeIds.Effector(slot, 0))));
        }

        return intent;
    }

    /// <summary>Integrates one step of movement and applies its energy cost.</summary>
    public static void Apply(
        Body body, Energy energy, Genome genome, in Intent intent, ISenseField field, float dt)
    {
        float maxSpeed = Metabolism.EffectiveMaxSpeed(genome, intent.Sprint);
        float thrust = Math.Clamp(intent.Thrust, -1f, 1f);
        float turn = Math.Clamp(intent.Turn, -1f, 1f);

        // Torpor damps the controller's own outputs rather than capping the body,
        // so resting costs manoeuvrability at exactly the moment it saves energy.
        if (intent.Torpor)
        {
            thrust *= Metabolism.TorporMoveFactor;
            turn *= Metabolism.TorporMoveFactor;
        }

        // Reverse is slower than forward - a creature that backs away as fast as it
        // charges makes fleeing and pursuing the same manoeuvre.
        float target = thrust >= 0f
            ? thrust * maxSpeed
            : thrust * maxSpeed * Metabolism.ReverseFactor;

        // Ease toward the target rather than snapping: instant velocity changes make
        // every creature twitch, and mass should mean something.
        const float responsiveness = 6f;
        body.Speed += (target - body.Speed) * MathF.Min(1f, responsiveness * dt);

        // Eased, not assigned. Speed already was - "instant velocity changes make
        // every creature twitch, and mass should mean something" - and that applies
        // at least as much to turning, which is where the visible twitch came from.
        // A body cannot reverse its turn within a sixtieth of a second.
        const float turnResponsiveness = 8f;
        float turnRate = genome.Trait(TraitAxis.TurnRate);
        float targetAngular = turn * turnRate;
        body.AngularVelocity +=
            (targetAngular - body.AngularVelocity) * MathF.Min(1f, turnResponsiveness * dt);
        body.Heading = Senses.WrapAngle(body.Heading + body.AngularVelocity * dt);

        body.Position = field.Wrap(body.Position + body.Forward * (body.Speed * dt));

        energy.Current -= Metabolism.MovementCost(body, genome, intent.Sprint) * dt;
    }

    /// <summary>Charges upkeep, ages the creature, and drains any toxin load.</summary>
    public static void Tick(
        Body body, Energy energy, Vitals vitals, Genome genome, Brains.Brain brain,
        float baseRate, float dt, bool torpid = false)
    {
        vitals.Age += dt;
        energy.Current -= Metabolism.BaseCost(body, genome, brain, vitals, baseRate, torpid) * dt;

        if (vitals.ToxinLoad > 0f)
        {
            energy.Current -= Metabolism.ToxinDrain * vitals.ToxinLoad * dt;
            vitals.ToxinLoad *= MathF.Max(0f, 1f - Metabolism.ToxinDecay * dt);
            if (vitals.ToxinLoad < 0.01f) vitals.ToxinLoad = 0f;
        }

        if (energy.Current <= 0f || vitals.Age > Metabolism.MaxAge)
        {
            energy.Current = MathF.Max(0f, energy.Current);
            vitals.Alive = false;
        }
    }

    /// <summary>All four gates a birth must pass.</summary>
    public static bool CanReproduce(
        Energy energy, Vitals vitals, Genome genome, in Intent intent, float simTime)
    {
        if (!intent.Reproduce) return false;
        if (vitals.Age < genome.Trait(TraitAxis.MatureAge)) return false;
        if (simTime - vitals.LastBirthTime < Metabolism.BirthCooldown) return false;

        return energy.Fraction >= genome.Trait(TraitAxis.ReproduceThreshold);
    }

    private static float Read(ReadOnlySpan<float> effectors, int index) =>
        index >= 0 && index < effectors.Length ? effectors[index] : 0f;
}
