using System;
using Microsoft.Xna.Framework;
using MonoGame.Extended.ECS;
using MonoGame.Extended.ECS.Systems;
using GoL.Sim.Components;
using GoL.Sim.Core;

namespace GoL.Sim.Systems;

/// <summary>
/// Fills every creature's sensor vector from the world.
/// <para>
/// <b>Read-only with respect to the world, and separate from acting on purpose.</b>
/// Every creature senses against the same frozen state. If creature 0 moved before
/// creature 1 sensed, the run would depend on the order the entity list happens to
/// be in, and reproducibility from a seed would be gone.
/// </para>
/// </summary>
public sealed class SenseSystem : SimSystem
{
    private readonly SimWorld _world;
    private readonly Senses _senses = new();

    private ComponentMapper<Body> _body = null!;
    private ComponentMapper<Energy> _energy = null!;
    private ComponentMapper<Vitals> _vitals = null!;
    private ComponentMapper<Genes> _genes = null!;
    private ComponentMapper<Mind> _mind = null!;
    private ComponentMapper<Sight> _sight = null!;

    public SenseSystem(SimWorld world)
        : base(Aspect.All(typeof(Body), typeof(Mind), typeof(Genes), typeof(Sight)))
        => _world = world;

    public override void Initialize(IComponentMapperService mappers)
    {
        _body = mappers.GetMapper<Body>();
        _energy = mappers.GetMapper<Energy>();
        _vitals = mappers.GetMapper<Vitals>();
        _genes = mappers.GetMapper<Genes>();
        _mind = mappers.GetMapper<Mind>();
        _sight = mappers.GetMapper<Sight>();
    }

    public override void Update(GameTime gameTime)
    {
        foreach (int id in ActiveEntities)
        {
            var vitals = _vitals.Get(id);
            if (!vitals.Alive) continue;

            var mind = _mind.Get(id);
            mind.BitThisTick = false;
            _energy.Get(id).IntakeThisTick = 0f;

            var subject = new SenseSubject(
                id, _body.Get(id), _energy.Get(id), vitals,
                _genes.Get(id), mind, _sight.Get(id));

            _senses.Sample(subject, _world.Field, _world.SimTime,
                mind.Sensors.AsSpan(0, mind.Brain.SensorCount));
        }
    }
}
