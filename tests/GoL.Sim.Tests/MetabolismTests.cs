using System.Numerics;
using GoL.Sim;
using GoL.Sim.Components;
using GoL.Sim.Core;
using GoL.Sim.Genetics;
using GoL.Sim.Systems;

namespace GoL.Sim.Tests;

/// <summary>
/// The energy model. Every constant in it is a guess that will be retuned, so these
/// tests pin the <i>relationships</i> that must hold for selection to mean anything
/// - not the numbers, which are free to move.
/// </summary>
public class MetabolismTests
{
    /// <summary>Spawns one creature into a real world and hands back a view of it,
    /// so these tests exercise the same components the systems do.</summary>
    private static (SimWorld World, CreatureView Subject) Make(
        ulong seed = 1, Action<Genome>? configure = null)
    {
        var config = new SimConfig { Seed = (int)seed, WorldSize = 340f };
        var env = new LabEnvironment(config);
        var world = new SimWorld(config, env);

        var rng = new Pcg32(seed);
        var genome = Genome.CreateSeed(ref rng);
        configure?.Invoke(genome);

        return (world, world.View(world.Spawn(genome, new Vector2(50f, 50f), 0f)));
    }

    private static float Cost(CreatureView c) =>
        Metabolism.BaseCost(c.Body, c.Genome, c.Mind.Brain, c.Vitals, 0.35f);

    private static void Tick(CreatureView c) =>
        Locomotion.Tick(c.Body, c.Energy, c.Vitals, c.Genome, c.Mind.Brain, 0.35f, 1f / 60f);

    private static void SetTrait(Genome genome, TraitAxis axis, float value)
    {
        var range = Traits.Range(axis);
        genome.TraitValues[(int)axis] = Math.Clamp((value - range.Min) / (range.Max - range.Min), 0f, 1f);
    }

    [Fact]
    public void BiggerBodies_CostMoreToRun()
    {
        var (_, small) = Make(configure: g => SetTrait(g, TraitAxis.BodyRadius, 4f));
        var (_, large) = Make(configure: g => SetTrait(g, TraitAxis.BodyRadius, 13f));

        Assert.True(
            Cost(large) > Cost(small),
            "size must cost something, or every creature evolves to be huge");
    }

    [Fact]
    public void UnlockedAttributes_AddUpkeep()
    {
        var (_, plain) = Make(5);
        var (_, equipped) = Make(5);

        var rng = new Pcg32(9);
        Mutator.Unlock(equipped.Genome, LatentTraitId.ArmorPlating, ref rng);
        equipped.Mind.Rebuild(equipped.Genome);

        Assert.True(
            Cost(equipped) > Cost(plain),
            "a free attribute is never selected against, so all lineages would collect every one");
    }

    /// <summary>
    /// Quadratic, not linear. If movement cost were linear in speed there would be
    /// no reason ever to cruise, and "always flat out" would dominate.
    /// </summary>
    [Fact]
    public void MovementCost_GrowsFasterThanSpeed()
    {
        var (_, creature) = Make();
        float maxSpeed = creature.Trait(TraitAxis.MaxSpeed);

        creature.Body.Speed = maxSpeed * 0.5f;
        float half = Metabolism.MovementCost(creature.Body, creature.Genome, sprinting: false);

        creature.Body.Speed = maxSpeed;
        float full = Metabolism.MovementCost(creature.Body, creature.Genome, sprinting: false);

        Assert.True(full > half * 2f,
            "doubling speed must cost more than twice as much");
    }

    [Fact]
    public void Sprinting_CostsMoreThanTheSpeedItBuys()
    {
        var (_, creature) = Make(configure: g =>
        {
            var rng = new Pcg32(2);
            Mutator.Unlock(g, LatentTraitId.SprintGland, ref rng);
        });

        float speedGain = Metabolism.EffectiveMaxSpeed(creature.Genome, sprinting: true)
                        / Metabolism.EffectiveMaxSpeed(creature.Genome, sprinting: false);

        creature.Body.Speed = creature.Trait(TraitAxis.MaxSpeed);
        float costRatio = Metabolism.MovementCost(creature.Body, creature.Genome, sprinting: true)
                        / Metabolism.MovementCost(creature.Body, creature.Genome, sprinting: false);

        Assert.True(costRatio > speedGain,
            "sprinting must be a trade-off, not a free win");
    }

    [Fact]
    public void Senescence_RaisesUpkeepOnlyAfterMaturity()
    {
        var (_, creature) = Make(configure: g => SetTrait(g, TraitAxis.MatureAge, 50f));

        creature.Vitals.Age = 10f;
        Assert.Equal(1f, Metabolism.AgeFactor(creature.Vitals, creature.Genome), 4);

        creature.Vitals.Age = 50f;
        Assert.Equal(1f, Metabolism.AgeFactor(creature.Vitals, creature.Genome), 4);

        creature.Vitals.Age = 200f;
        float old = Metabolism.AgeFactor(creature.Vitals, creature.Genome);

        creature.Vitals.Age = 300f;
        Assert.True(Metabolism.AgeFactor(creature.Vitals, creature.Genome) > old,
            "upkeep must keep climbing with age");
    }

    [Fact]
    public void StarvedCreature_Dies()
    {
        var (_, creature) = Make();
        creature.Energy.Current = 0.01f;

        for (int i = 0; i < 120 && creature.Alive; i++) Tick(creature);

        Assert.False(creature.Alive);
        Assert.Equal(0f, creature.Energy.Current);
    }

    [Fact]
    public void Reproduction_RequiresIntent_Age_Energy_AndCooldown()
    {
        var (_, c) = Make(configure: g => SetTrait(g, TraitAxis.MatureAge, 20f));
        var willing = new Intent { Reproduce = true };

        c.Vitals.Age = 5f;
        c.Energy.Current = c.Energy.Maximum;
        Assert.False(Locomotion.CanReproduce(c.Energy, c.Vitals, c.Genome, willing, 100f), "too young");

        c.Vitals.Age = 50f;
        c.Energy.Current = c.Energy.Maximum * 0.1f;
        Assert.False(Locomotion.CanReproduce(c.Energy, c.Vitals, c.Genome, willing, 100f), "too poor");

        c.Energy.Current = c.Energy.Maximum;
        Assert.False(
            Locomotion.CanReproduce(c.Energy, c.Vitals, c.Genome, new Intent { Reproduce = false }, 100f),
            "the brain never asked");

        Assert.True(Locomotion.CanReproduce(c.Energy, c.Vitals, c.Genome, willing, 100f));

        c.Vitals.LastBirthTime = 99f;
        Assert.False(Locomotion.CanReproduce(c.Energy, c.Vitals, c.Genome, willing, 100f), "still on cooldown");
    }

    [Fact]
    public void Toxin_DrainsEnergy_ThenDecaysAway()
    {
        var (_, creature) = Make();
        creature.Energy.Current = creature.Energy.Maximum;
        creature.Vitals.ToxinLoad = 20f;

        float before = creature.Energy.Current;
        for (int i = 0; i < 60; i++) Tick(creature);

        Assert.True(creature.Energy.Current < before, "poison must actually hurt");
        Assert.True(creature.Vitals.ToxinLoad < 20f, "and must wear off");
    }

    [Fact]
    public void Reverse_IsSlowerThanForward()
    {
        var (world, creature) = Make();

        creature.Body.Speed = 0f;
        for (int i = 0; i < 120; i++)
            Locomotion.Apply(creature.Body, creature.Energy, creature.Genome,
                new Intent { Thrust = 1f }, world.Field, 1f / 60f);
        float forward = creature.Body.Speed;

        creature.Body.Speed = 0f;
        for (int i = 0; i < 120; i++)
            Locomotion.Apply(creature.Body, creature.Energy, creature.Genome,
                new Intent { Thrust = -1f }, world.Field, 1f / 60f);
        float backward = MathF.Abs(creature.Body.Speed);

        // Otherwise fleeing and charging are the same manoeuvre.
        Assert.True(backward < forward);
    }
}
