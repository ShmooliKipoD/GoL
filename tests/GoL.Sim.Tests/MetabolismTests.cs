using System.Numerics;
using GoL.Sim;
using GoL.Sim.Core;
using GoL.Sim.Genetics;

namespace GoL.Sim.Tests;

/// <summary>
/// The energy model. Every constant in it is a guess that will be retuned, so these
/// tests pin the <i>relationships</i> that must hold for selection to mean anything
/// - not the numbers, which are free to move.
/// </summary>
public class MetabolismTests
{
    private static Creature Make(ulong seed = 1, Action<Genome>? configure = null)
    {
        var rng = new Pcg32(seed);
        var genome = Genome.CreateSeed(ref rng);
        configure?.Invoke(genome);
        return new Creature(0, genome, new Vector2(50f, 50f), 0f, seed, 0);
    }

    private static void SetTrait(Genome genome, TraitAxis axis, float value)
    {
        var range = Traits.Range(axis);
        genome.TraitValues[(int)axis] = Math.Clamp((value - range.Min) / (range.Max - range.Min), 0f, 1f);
    }

    [Fact]
    public void BiggerBodies_CostMoreToRun()
    {
        var small = Make(configure: g => SetTrait(g, TraitAxis.BodyRadius, 4f));
        var large = Make(configure: g => SetTrait(g, TraitAxis.BodyRadius, 13f));

        Assert.True(
            Metabolism.BaseCost(large, 0.35f) > Metabolism.BaseCost(small, 0.35f),
            "size must cost something, or every creature evolves to be huge");
    }

    [Fact]
    public void UnlockedAttributes_AddUpkeep()
    {
        var plain = Make(5);
        var equipped = Make(5);

        var rng = new Pcg32(9);
        Mutator.Unlock(equipped.Genome, LatentTraitId.ArmorPlating, ref rng);
        equipped.Rebuild();

        Assert.True(
            Metabolism.BaseCost(equipped, 0.35f) > Metabolism.BaseCost(plain, 0.35f),
            "a free attribute is never selected against, so all lineages would collect every one");
    }

    /// <summary>
    /// Quadratic, not linear. If movement cost were linear in speed there would be
    /// no reason ever to cruise, and "always flat out" would dominate.
    /// </summary>
    [Fact]
    public void MovementCost_GrowsFasterThanSpeed()
    {
        var creature = Make();
        float maxSpeed = creature.Genome.Trait(TraitAxis.MaxSpeed);

        creature.Speed = maxSpeed * 0.5f;
        float half = Metabolism.MovementCost(creature, sprinting: false);

        creature.Speed = maxSpeed;
        float full = Metabolism.MovementCost(creature, sprinting: false);

        Assert.True(full > half * 2f,
            "doubling speed must cost more than twice as much");
    }

    [Fact]
    public void Sprinting_CostsMoreThanTheSpeedItBuys()
    {
        var creature = Make(configure: g =>
        {
            var rng = new Pcg32(2);
            Mutator.Unlock(g, LatentTraitId.SprintGland, ref rng);
        });
        creature.Rebuild();

        float speedGain = Metabolism.EffectiveMaxSpeed(creature, sprinting: true)
                        / Metabolism.EffectiveMaxSpeed(creature, sprinting: false);

        creature.Speed = creature.Genome.Trait(TraitAxis.MaxSpeed);
        float costRatio = Metabolism.MovementCost(creature, sprinting: true)
                        / Metabolism.MovementCost(creature, sprinting: false);

        Assert.True(costRatio > speedGain,
            "sprinting must be a trade-off, not a free win");
    }

    [Fact]
    public void Senescence_RaisesUpkeepOnlyAfterMaturity()
    {
        var creature = Make(configure: g => SetTrait(g, TraitAxis.MatureAge, 50f));

        creature.Age = 10f;
        Assert.Equal(1f, Metabolism.AgeFactor(creature), 4);

        creature.Age = 50f;
        Assert.Equal(1f, Metabolism.AgeFactor(creature), 4);

        creature.Age = 200f;
        float old = Metabolism.AgeFactor(creature);

        creature.Age = 300f;
        Assert.True(Metabolism.AgeFactor(creature) > old,
            "upkeep must keep climbing with age");
    }

    [Fact]
    public void StarvedCreature_Dies()
    {
        var creature = Make();
        creature.Energy = 0.01f;

        for (int i = 0; i < 120 && creature.Alive; i++)
            Locomotion.Tick(creature, baseRate: 0.35f, dt: 1f / 60f);

        Assert.False(creature.Alive);
        Assert.Equal(0f, creature.Energy);
    }

    [Fact]
    public void Reproduction_RequiresIntent_Age_Energy_AndCooldown()
    {
        var creature = Make(configure: g => SetTrait(g, TraitAxis.MatureAge, 20f));
        var willing = new Intent { Reproduce = true };

        creature.Age = 5f;
        creature.Energy = creature.MaxEnergy;
        Assert.False(Locomotion.CanReproduce(creature, willing, 100f), "too young");

        creature.Age = 50f;
        creature.Energy = creature.MaxEnergy * 0.1f;
        Assert.False(Locomotion.CanReproduce(creature, willing, 100f), "too poor");

        creature.Energy = creature.MaxEnergy;
        Assert.False(
            Locomotion.CanReproduce(creature, new Intent { Reproduce = false }, 100f),
            "the brain never asked");

        Assert.True(Locomotion.CanReproduce(creature, willing, 100f));

        creature.LastBirthTime = 99f;
        Assert.False(Locomotion.CanReproduce(creature, willing, 100f), "still on cooldown");
    }

    [Fact]
    public void Toxin_DrainsEnergy_ThenDecaysAway()
    {
        var creature = Make();
        creature.Energy = creature.MaxEnergy;
        creature.ToxinLoad = 20f;

        float before = creature.Energy;
        for (int i = 0; i < 60; i++) Locomotion.Tick(creature, 0.35f, 1f / 60f);

        Assert.True(creature.Energy < before, "poison must actually hurt");
        Assert.True(creature.ToxinLoad < 20f, "and must wear off");
    }

    [Fact]
    public void Reverse_IsSlowerThanForward()
    {
        var creature = Make();
        var world = new LabWorld(new SimConfig { Seed = 1, WorldSize = 340f });

        creature.Speed = 0f;
        for (int i = 0; i < 120; i++)
            Locomotion.Apply(creature, new Intent { Thrust = 1f }, world, 1f / 60f);
        float forward = creature.Speed;

        creature.Speed = 0f;
        for (int i = 0; i < 120; i++)
            Locomotion.Apply(creature, new Intent { Thrust = -1f }, world, 1f / 60f);
        float backward = MathF.Abs(creature.Speed);

        // Otherwise fleeing and charging are the same manoeuvre.
        Assert.True(backward < forward);
    }
}
