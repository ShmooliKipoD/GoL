using Microsoft.Xna.Framework;
using MonoGame.Extended.ECS;
using MonoGame.Extended.ECS.Systems;
using GoL.Sim.Components;
using GoL.Sim.Core;

namespace GoL.Sim.Systems;

/// <summary>
/// The first system that writes to the world. Everything before it only read, so
/// every creature decided against the same state.
/// </summary>
public sealed class ActuateSystem : SimSystem
{
    private readonly SimWorld _world;

    private ComponentMapper<Body> _body = null!;
    private ComponentMapper<Energy> _energy = null!;
    private ComponentMapper<Vitals> _vitals = null!;
    private ComponentMapper<Genes> _genes = null!;
    private ComponentMapper<Mind> _mind = null!;

    public ActuateSystem(SimWorld world)
        : base(Aspect.All(typeof(Body), typeof(Mind), typeof(Genes)))
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

        foreach (int id in ActiveEntities)
        {
            if (!_vitals.Get(id).Alive) continue;

            var mind = _mind.Get(id);
            var genome = _genes.Get(id).Genome;

            Locomotion.Apply(_body.Get(id), _energy.Get(id), genome, mind.Intent, _world.Field, dt);
            _world.EmitScent(_body.Get(id).Position, mind.Intent, dt);
        }
    }
}
