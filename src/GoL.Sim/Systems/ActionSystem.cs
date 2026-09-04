using Microsoft.Xna.Framework;
using MonoGame.Extended.ECS;
using MonoGame.Extended.ECS.Systems;
using GoL.Sim.Components;
using GoL.Sim.Core;

namespace GoL.Sim.Systems;

/// <summary>
/// Reads what each creature did into its <see cref="Behaviour"/>. Runs <b>last</b>,
/// after <see cref="LifecycleSystem"/>, so a birth can be read from the stamp
/// lifecycle just wrote rather than by re-deriving the reproduction predicate and
/// risking the two disagreeing.
/// <para>
/// Writes only to <see cref="Behaviour"/>. Nothing in the simulation reads that
/// component back - if anything did, the readout would start driving behaviour
/// instead of describing it.
/// </para>
/// </summary>
public sealed class ActionSystem : SimSystem
{
    private readonly SimWorld _world;

    private ComponentMapper<Vitals> _vitals = null!;
    private ComponentMapper<Genes> _genes = null!;
    private ComponentMapper<Mind> _mind = null!;
    private ComponentMapper<Behaviour> _behaviour = null!;

    public ActionSystem(SimWorld world)
        : base(Aspect.All(typeof(Vitals), typeof(Genes), typeof(Mind), typeof(Behaviour)))
        => _world = world;

    public override void Initialize(IComponentMapperService mappers)
    {
        _vitals = mappers.GetMapper<Vitals>();
        _genes = mappers.GetMapper<Genes>();
        _mind = mappers.GetMapper<Mind>();
        _behaviour = mappers.GetMapper<Behaviour>();
    }

    public override void Update(GameTime gameTime)
    {
        foreach (int id in ActiveEntities)
        {
            var vitals = _vitals.Get(id);

            // Whether an entity destroyed by LifecycleSystem is still visible here
            // in the same Update is not something Extended documents. Skipping the
            // dead makes the answer not matter: a deferred destroy cannot produce
            // an action for a corpse.
            if (!vitals.Alive) continue;

            var mind = _mind.Get(id);
            var genome = _genes.Get(id).Genome;
            var behaviour = _behaviour.Get(id);

            behaviour.AvailableMask = Actions.AvailableMask(genome);
            behaviour.Fed = mind.BitThisTick;
            behaviour.Bred = vitals.LastBirthTick == _world.Tick;

            Actions.Read(genome, mind.Intent, behaviour.Values);
            behaviour.Active = Actions.Current(behaviour.Values, out behaviour.Current);
        }
    }
}
