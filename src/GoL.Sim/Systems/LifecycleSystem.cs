using System.Collections.Generic;
using Microsoft.Xna.Framework;
using MonoGame.Extended.ECS;
using MonoGame.Extended.ECS.Systems;
using GoL.Sim.Components;
using GoL.Sim.Core;

namespace GoL.Sim.Systems;

/// <summary>
/// Births and deaths, in that order and last in the tick.
/// <para>
/// Last on purpose. Creating or destroying entities mid-loop changes the collection
/// other systems are iterating, and a newborn that sensed and acted on its birth
/// tick would have done so against a half-updated world. Both are collected into
/// lists here and applied once the pass is complete.
/// </para>
/// </summary>
public sealed class LifecycleSystem : SimSystem
{
    private readonly SimWorld _world;
    private readonly List<int> _dead = new();
    private readonly List<int> _parents = new();
    private readonly List<int> _living = new();

    private ComponentMapper<Body> _body = null!;
    private ComponentMapper<Energy> _energy = null!;
    private ComponentMapper<Vitals> _vitals = null!;
    private ComponentMapper<Genes> _genes = null!;
    private ComponentMapper<Mind> _mind = null!;

    public LifecycleSystem(SimWorld world)
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
        _dead.Clear();
        _parents.Clear();
        _living.Clear();

        int maxGeneration = 0;

        foreach (int id in ActiveEntities)
        {
            var vitals = _vitals.Get(id);

            if (!vitals.Alive) { _dead.Add(id); continue; }

            // ActiveEntities is ascending by id, so this list is too - which keeps
            // anything accumulated over it reproducible.
            _living.Add(id);

            int generation = _genes.Get(id).Genome.Generation;
            if (generation > maxGeneration) maxGeneration = generation;

            if (Locomotion.CanReproduce(
                    _energy.Get(id), vitals, _genes.Get(id).Genome,
                    _mind.Get(id).Intent, _world.SimTime))
            {
                _parents.Add(id);
            }
        }

        // Births before deaths, so a parent that dies this tick still leaves issue -
        // and so a freed entity slot cannot be handed to its own offspring, which
        // would give the child its parent's random stream.
        foreach (int id in _parents)
            _world.Reproduce(id, _body.Get(id), _energy.Get(id), _vitals.Get(id), _genes.Get(id));

        foreach (int id in _dead)
            _world.Kill(id, _body.Get(id), _energy.Get(id));

        _world.PublishLiving(_living, maxGeneration);
    }
}
