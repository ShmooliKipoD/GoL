using System;
using System.Numerics;
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
    /// <summary>Reads the brain's effector outputs into an <see cref="Intent"/>.</summary>
    public static Intent ReadIntent(Creature creature, ReadOnlySpan<float> effectors)
    {
        var layout = creature.Layout;
        var genome = creature.Genome;

        var intent = new Intent
        {
            Thrust = Read(effectors, layout.Action(InnateAction.Thrust)),
            Turn = Read(effectors, layout.Action(InnateAction.Turn)),
            Bite = Read(effectors, layout.Action(InnateAction.Bite)) > 0.5f,
            Reproduce = Read(effectors, layout.Action(InnateAction.Reproduce)) > 0.5f,
        };

        if (genome.Has(LatentTraitId.SprintGland))
        {
            int slot = LatentTraitCatalog.Get(LatentTraitId.SprintGland).Slot;
            intent.Sprint = Read(effectors, layout.EffectorIndexOf(NodeIds.Effector(slot, 0))) > 0.5f;
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
    public static void Apply(Creature creature, in Intent intent, ISenseField field, float dt)
    {
        var genome = creature.Genome;

        float maxSpeed = Metabolism.EffectiveMaxSpeed(creature, intent.Sprint);
        float thrust = Math.Clamp(intent.Thrust, -1f, 1f);

        // Reverse is slower than forward - a creature that backs away as fast as it
        // charges makes fleeing and pursuing the same manoeuvre.
        float target = thrust >= 0f
            ? thrust * maxSpeed
            : thrust * maxSpeed * Metabolism.ReverseFactor;

        // Ease toward the target rather than snapping: instant velocity changes make
        // every creature twitch, and mass should mean something.
        const float responsiveness = 6f;
        creature.Speed += (target - creature.Speed) * MathF.Min(1f, responsiveness * dt);

        float turnRate = genome.Trait(TraitAxis.TurnRate);
        creature.AngularVelocity = Math.Clamp(intent.Turn, -1f, 1f) * turnRate;
        creature.Heading = Senses.WrapAngle(creature.Heading + creature.AngularVelocity * dt);

        var forward = new Vector2(MathF.Cos(creature.Heading), MathF.Sin(creature.Heading));
        creature.Position = field.Wrap(creature.Position + forward * (creature.Speed * dt));

        creature.Energy -= Metabolism.MovementCost(creature, intent.Sprint) * dt;
    }

    /// <summary>Charges upkeep, ages the creature, and drains any toxin load.</summary>
    public static void Tick(Creature creature, float baseRate, float dt)
    {
        creature.Age += dt;
        creature.Energy -= Metabolism.BaseCost(creature, baseRate) * dt;

        if (creature.ToxinLoad > 0f)
        {
            creature.Energy -= Metabolism.ToxinDrain * creature.ToxinLoad * dt;
            creature.ToxinLoad *= MathF.Max(0f, 1f - Metabolism.ToxinDecay * dt);
            if (creature.ToxinLoad < 0.01f) creature.ToxinLoad = 0f;
        }

        if (creature.Energy <= 0f || creature.Age > Metabolism.MaxAge)
        {
            creature.Energy = MathF.Max(0f, creature.Energy);
            creature.Alive = false;
        }
    }

    /// <summary>All four gates a birth must pass.</summary>
    public static bool CanReproduce(Creature creature, in Intent intent, float simTime)
    {
        if (!intent.Reproduce) return false;
        if (creature.Age < creature.Genome.Trait(TraitAxis.MatureAge)) return false;
        if (simTime - creature.LastBirthTime < Metabolism.BirthCooldown) return false;

        float threshold = creature.Genome.Trait(TraitAxis.ReproduceThreshold);
        return creature.EnergyFraction >= threshold;
    }

    private static float Read(ReadOnlySpan<float> effectors, int index) =>
        index >= 0 && index < effectors.Length ? effectors[index] : 0f;
}
