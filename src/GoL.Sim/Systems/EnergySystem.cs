using Microsoft.Xna.Framework;
using MonoGame.Extended.ECS;
using MonoGame.Extended.ECS.Systems;
using GoL.Sim.Components;
using GoL.Sim.Core;

namespace GoL.Sim.Systems;

/// <summary>
/// Charges upkeep, ages everything and marks the dead. Runs after feeding so a
/// creature that ate this tick is credited before being charged.
/// </summary>
public sealed class EnergySystem : SimSystem
{
    private readonly SimWorld _world;

    private ComponentMapper<Body> _body = null!;
    private ComponentMapper<Energy> _energy = null!;
    private ComponentMapper<Vitals> _vitals = null!;
    private ComponentMapper<Genes> _genes = null!;
    private ComponentMapper<Mind> _mind = null!;

    public EnergySystem(SimWorld world)
        : base(Aspect.All(typeof(Body), typeof(Energy), typeof(Vitals), typeof(Genes), typeof(Mind)))
        => _world = world;

    public override void Initialize(IComponentMapperService mappers)
    {
        _body = mappers.GetMapper<Body>();
        _energy = mappers.GetMapper<Energy>();
        _vitals = mappers.GetMapper<Vitals>();
        _genes = mappers.GetMapper<Genes>();
        _mind = mappers.GetMapper<Mind>();
    }

    public override void Update(GameTime gameTime)
    {
        float dt = Dt(gameTime);
        float baseRate = _world.Config.BaseMetabolicRate;

        foreach (int id in ActiveEntities)
        {
            var vitals = _vitals.Get(id);
            if (!vitals.Alive) continue;

            var mind = _mind.Get(id);

            Locomotion.Tick(
                _body.Get(id), _energy.Get(id), vitals,
                _genes.Get(id).Genome, mind.Brain, baseRate, dt, mind.Intent.Torpor);
        }
    }
}
