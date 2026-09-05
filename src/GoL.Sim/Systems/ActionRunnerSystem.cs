using System;
using Microsoft.Xna.Framework;
using MonoGame.Extended.ECS;
using MonoGame.Extended.ECS.Systems;
using GoL.Sim.Acting;
using GoL.Sim.Components;
using GoL.Sim.Core;

namespace GoL.Sim.Systems;

/// <summary>
/// Runs one action per creature. The first system that writes to the world -
/// everything before it only read, so every creature decided against the same state.
/// <para>
/// Replaces <c>ActuateSystem</c> and <c>FeedSystem</c>, which between them applied
/// thrust, turning, scent and biting as four unrelated effects of the same
/// <see cref="Intent"/>. Nothing there owned an action, so nothing was ever in a
/// position to notice a creature biting at empty air.
/// </para>
/// </summary>
public sealed class ActionRunnerSystem : SimSystem
{
    private readonly SimWorld _world;

    /// <summary>One slot per <see cref="CreatureAction"/>; null where no behaviour
    /// has been written yet. Indexed by the enum so lookup is an array read.</summary>
    private readonly ICreatureAction?[] _table = new ICreatureAction?[Actions.Count];

    /// <summary>Selection values, reused every creature. The tick allocates nothing.</summary>
    private readonly float[] _wants = new float[Actions.Count];

    private ComponentMapper<Body> _body = null!;
    private ComponentMapper<Energy> _energy = null!;
    private ComponentMapper<Vitals> _vitals = null!;
    private ComponentMapper<Genes> _genes = null!;
    private ComponentMapper<Mind> _mind = null!;
    private ComponentMapper<Sight> _sight = null!;
    private ComponentMapper<Doing> _doing = null!;

    public ActionRunnerSystem(SimWorld world, params ICreatureAction[] actions)
        : base(Aspect.All(typeof(Body), typeof(Mind), typeof(Genes), typeof(Doing)))
    {
        _world = world;
        foreach (var action in actions) _table[(int)action.Id] = action;
    }

    /// <summary>
    /// Least time a running action keeps the body before another may take it.
    /// <para>
    /// Moved here from the readout, which is where it always belonged. It was
    /// smoothing a label over a creature that was in fact changing its mind sixty
    /// times a second; the flicker was real and the label was only reporting it.
    /// Holding the <i>action</i> steady fixes the behaviour, and the readout needs no
    /// smoothing of its own.
    /// </para>
    /// <para>
    /// It does not apply to an action that reported <see cref="ActionStatus.Done"/>
    /// or <see cref="ActionStatus.Blocked"/>: a finished meal should not pin the body
    /// for another third of a second.
    /// </para>
    /// </summary>
    private const float MinimumDwell = 0.35f;

    public override void Initialize(IComponentMapperService mappers)
    {
        _body = mappers.GetMapper<Body>();
        _energy = mappers.GetMapper<Energy>();
        _vitals = mappers.GetMapper<Vitals>();
        _genes = mappers.GetMapper<Genes>();
        _mind = mappers.GetMapper<Mind>();
        _sight = mappers.GetMapper<Sight>();
        _doing = mappers.GetMapper<Doing>();
    }

    public override void Update(GameTime gameTime)
    {
        float dt = Dt(gameTime);

        foreach (int id in ActiveEntities)
        {
            var vitals = _vitals.Get(id);
            if (!vitals.Alive) continue;

            var mind = _mind.Get(id);
            var doing = _doing.Get(id);
            var genes = _genes.Get(id);

            var ctx = new ActionContext(
                _world, id, _body.Get(id), _energy.Get(id), vitals,
                genes, mind, _sight.Get(id), doing);

            // Selection reads the brain's INTENT, never the body.
            //
            // The readout deliberately reads Move and Turn off the body instead, so
            // it reports what a creature is visibly doing rather than what it asked
            // for. Selecting on those same values would latch: the body is moving
            // because Move ran last tick, so Move reads high, so Move wins again -
            // for as long as momentum lasts, entirely independently of the brain.
            // The symptom would be actions running to completion instead of
            // switching, which is exactly the outcome this step is aiming at, so the
            // bug would have read as success.
            Actions.Read(genes.Genome, mind.Intent, _wants);

            var chosen = Choose(doing);

            if (chosen is not { } want || _table[(int)want] is not { } action)
            {
                doing.Clear();
                continue;
            }

            bool switching = !doing.Active || doing.Action != want;

            if (switching)
            {
                doing.Action = want;
                doing.Active = true;
                doing.HeldFor = 0f;
                doing.HasTarget = false;

                if (!action.CanStart(in ctx))
                {
                    // Reported, not hidden. A creature that cannot start eating must
                    // read differently from one that is feeding - inferring which
                    // from the screen is what this whole step exists to end.
                    doing.Status = ActionStatus.Blocked;
                    continue;
                }
            }

            doing.Status = action.Execute(in ctx, dt);
            doing.HeldFor += dt;
        }
    }

    /// <summary>Which action should have the body, or null for idle.</summary>
    private CreatureAction? Choose(Doing doing)
    {
        // The lab pins an action so it can be watched in isolation. Nothing on the
        // board ever sets this.
        if (doing.Forced is { } forced) return forced;

        // An action still running keeps the body unless a challenger clearly beats
        // it, and for at least MinimumDwell. One that finished or could not start
        // holds nothing - so the next tick picks freshly.
        bool committed = doing.Active && doing.Status == ActionStatus.Running;

        var showing = committed ? doing.Action : (CreatureAction?)null;

        if (!Actions.Current(_wants, out var current, showing)) return null;

        if (committed && current != doing.Action && doing.HeldFor < MinimumDwell)
            return doing.Action;

        return current;
    }
}
