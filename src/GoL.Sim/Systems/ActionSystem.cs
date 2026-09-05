using Microsoft.Xna.Framework;
using MonoGame.Extended.ECS;
using MonoGame.Extended.ECS.Systems;
using GoL.Sim.Acting;
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
    private ComponentMapper<Doing> _doing = null!;

    public ActionSystem(SimWorld world)
        : base(Aspect.All(typeof(Body), typeof(Vitals), typeof(Genes), typeof(Mind), typeof(Behaviour), typeof(Doing)))
        => _world = world;

    public override void Initialize(IComponentMapperService mappers)
    {
        _body = mappers.GetMapper<Body>();
        _vitals = mappers.GetMapper<Vitals>();
        _genes = mappers.GetMapper<Genes>();
        _mind = mappers.GetMapper<Mind>();
        _behaviour = mappers.GetMapper<Behaviour>();
        _doing = mappers.GetMapper<Doing>();
    }


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

            // Mirror what the runner actually ran. This used to infer the current
            // action from the largest effector magnitude, with a dwell and a
            // takeover margin, because nothing in the simulation knew. Doing knows,
            // so the guess is gone - two things that each claimed to know the
            // current action would eventually disagree, and a panel reporting Eat
            // over a body executing Move is the worst kind of bug to chase.
            var doing = _doing.Get(id);

            bool changed = doing.Active != behaviour.Active
                || (doing.Active && doing.Action != behaviour.Current);

            behaviour.Active = doing.Active;
            behaviour.Current = doing.Action;
            behaviour.HeldFor = changed ? 0f : behaviour.HeldFor + dt;
        }
    }
}
