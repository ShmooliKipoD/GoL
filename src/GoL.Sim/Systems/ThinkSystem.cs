using System;
using Microsoft.Xna.Framework;
using MonoGame.Extended.ECS;
using MonoGame.Extended.ECS.Systems;
using GoL.Sim.Components;
using GoL.Sim.Core;

namespace GoL.Sim.Systems;

/// <summary>
/// Runs every brain and records what it decided. Still no world writes: actuation
/// is a separate system, so all brains think against the same frozen state.
/// </summary>
public sealed class ThinkSystem : SimSystem
{
    private ComponentMapper<Mind> _mind = null!;
    private ComponentMapper<Vitals> _vitals = null!;
    private ComponentMapper<Genes> _genes = null!;

    public ThinkSystem() : base(Aspect.All(typeof(Mind), typeof(Genes))) { }

    public override void Initialize(IComponentMapperService mappers)
    {
        _mind = mappers.GetMapper<Mind>();
        _vitals = mappers.GetMapper<Vitals>();
        _genes = mappers.GetMapper<Genes>();
    }

    public override void Update(GameTime gameTime)
    {
        foreach (int id in ActiveEntities)
        {
            if (!_vitals.Get(id).Alive) continue;

            var mind = _mind.Get(id);

            mind.Brain.Evaluate(
                mind.Sensors.AsSpan(0, mind.Brain.SensorCount),
                mind.Effectors.AsSpan(0, mind.Brain.EffectorCount));

            mind.Intent = Locomotion.ReadIntent(
                _genes.Get(id).Genome, mind.Layout,
                mind.Effectors.AsSpan(0, mind.Brain.EffectorCount));
        }
    }
}
