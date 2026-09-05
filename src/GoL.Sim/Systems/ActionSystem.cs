using Microsoft.Xna.Framework;
using MonoGame.Extended.ECS;
using MonoGame.Extended.ECS.Systems;
using GoL.Sim.Components;
using GoL.Sim.Core;
using GoL.Sim.Genetics;

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

    private ComponentMapper<Body> _body = null!;
    private ComponentMapper<Vitals> _vitals = null!;
    private ComponentMapper<Genes> _genes = null!;
    private ComponentMapper<Mind> _mind = null!;
    private ComponentMapper<Behaviour> _behaviour = null!;

    public ActionSystem(SimWorld world)
        : base(Aspect.All(typeof(Body), typeof(Vitals), typeof(Genes), typeof(Mind), typeof(Behaviour)))
        => _world = world;

    public override void Initialize(IComponentMapperService mappers)
    {
        _body = mappers.GetMapper<Body>();
        _vitals = mappers.GetMapper<Vitals>();
        _genes = mappers.GetMapper<Genes>();
        _mind = mappers.GetMapper<Mind>();
        _behaviour = mappers.GetMapper<Behaviour>();
    }

    /// <summary>
    /// Least time an action stays on screen before another may replace it. A
    /// creature cruising while turning gently has two effectors at nearly the same
    /// magnitude, and whichever is momentarily larger changes many times a second -
    /// so the label flickered over a creature that was plainly moving smoothly.
    /// <para>
    /// A bite that connects or a birth overrides it: those are events worth
    /// interrupting for, and they are over in a tick if not shown at once.
    /// </para>
    /// </summary>
    private const float MinimumDwell = 0.35f;

    public override void Update(GameTime gameTime)
    {
        float dt = Dt(gameTime);

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

            var body = _body.Get(id);

            Actions.Read(
                genome, mind.Intent,
                body.Speed, Metabolism.EffectiveMaxSpeed(genome, mind.Intent.Sprint),
                body.AngularVelocity, genome.Trait(TraitAxis.TurnRate),
                behaviour.Values);

            // Hand the action already showing back in, so it keeps its place unless
            // something clearly beats it.
            var showing = behaviour.Active ? behaviour.Current : (CreatureAction?)null;
            var wasCurrent = behaviour.Current;
            bool wasActive = behaviour.Active;

            bool nowActive = Actions.Current(behaviour.Values, out var nowCurrent, showing);

            // Idle counts as a state here. A creature drifting around the deadband
            // slips in and out of it constantly, and treating that as "no action"
            // rather than as a change let the flicker straight back through.
            bool changed = nowActive != wasActive || (nowActive && nowCurrent != wasCurrent);
            bool salient = behaviour.Fed || behaviour.Bred;

            if (changed && behaviour.HeldFor < MinimumDwell && !salient)
            {
                // Too soon. Keep showing what it was, and let the bars carry the
                // detail this label is deliberately not trying to.
                nowActive = wasActive;
                nowCurrent = wasCurrent;
                changed = false;
            }

            behaviour.Active = nowActive;
            behaviour.Current = nowCurrent;
            behaviour.HeldFor = changed ? 0f : behaviour.HeldFor + dt;
        }
    }
}
