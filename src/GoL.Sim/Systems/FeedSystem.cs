using Microsoft.Xna.Framework;
using MonoGame.Extended.ECS;
using MonoGame.Extended.ECS.Systems;
using GoL.Sim.Components;
using GoL.Sim.Core;

namespace GoL.Sim.Systems;

/// <summary>
/// Resolves biting.
/// <para>
/// Runs after actuation so a creature bites from where it actually ended up, and
/// as its own system because contested food must be resolved in one place: two
/// creatures eating the same plant have to split it the same way regardless of
/// which entity slot they occupy.
/// </para>
/// </summary>
public sealed class FeedSystem : SimSystem
{
    private readonly SimWorld _world;

    private ComponentMapper<Body> _body = null!;
    private ComponentMapper<Energy> _energy = null!;
    private ComponentMapper<Vitals> _vitals = null!;
    private ComponentMapper<Genes> _genes = null!;
    private ComponentMapper<Mind> _mind = null!;

    public FeedSystem(SimWorld world)
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
            if (!mind.Intent.Bite) continue;

            _world.ResolveBite(
                _body.Get(id), _energy.Get(id), _genes.Get(id).Genome, mind, dt);
        }
    }
}
