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

            Run(in ctx, doing, dt);
        }
    }

    /// <summary>
    /// Picks an action and runs it, falling through to the next-best when the one it
    /// wanted most cannot start.
    /// <para>
    /// The fall-through is the point. A creature whose brain says "eat" with nothing
    /// in range must not freeze - moving is how it finds food in the first place.
    /// Without this, a top-ranked action that cannot start left the creature idle,
    /// and the whole population sat still wanting to bite at nothing.
    /// </para>
    /// </summary>
    private void Run(in ActionContext ctx, Doing doing, float dt)
    {
        // The sandbox pins an action so it can be watched in isolation, and pinning
        // deliberately does NOT fall through: watching a forced Eat fail to start is
        // exactly the diagnostic it exists for. Nothing on the board sets this.
        if (doing.Forced is { } forced)
        {
            Start(in ctx, doing, forced, dt, fallThrough: false);
            return;
        }

        // An action still running keeps the body unless a challenger clearly beats
        // it, and for at least MinimumDwell. One that finished or could not start
        // holds nothing, so the next tick picks freshly.
        bool committed = doing.Active && doing.Status == ActionStatus.Running;
        var showing = committed ? doing.Action : (CreatureAction?)null;

        if (!Actions.Current(_wants, out var want, showing))
        {
            doing.Clear();
            return;
        }

        if (committed && want != doing.Action && doing.HeldFor < MinimumDwell)
            want = doing.Action;

        Start(in ctx, doing, want, dt, fallThrough: true);
    }

    /// <summary>Starts <paramref name="want"/>, or the best thing that can start
    /// instead of it.</summary>
    private void Start(
        in ActionContext ctx, Doing doing, CreatureAction want, float dt, bool fallThrough)
    {
        int tried = 0;

        while (true)
        {
            tried |= 1 << (int)want;

            if (_table[(int)want] is { } action)
            {
                // A fresh start, and CanStart gates every one of them.
                //
                // Note the third clause. Comparing only the action id was a bug with
                // an unusually quiet failure: an Eat that reported Done kept its slot,
                // so the next tick "continued" it rather than starting it, its
                // per-meal progress never reset, and it reported Done again forever.
                // The creature ate one green and then stood over the empty ground for
                // the rest of its life, still reporting Bite. A finished action is
                // finished - running it again is a new start.
                bool switching = !doing.Active
                    || doing.Action != want
                    || doing.Status != ActionStatus.Running;

                if (switching)
                {
                    doing.Action = want;
                    doing.Active = true;
                    doing.HeldFor = 0f;
                    doing.HasTarget = false;
                }

                if (!switching || action.CanStart(in ctx))
                {
                    var status = action.Execute(in ctx, dt);
                    doing.Status = status;
                    doing.HeldFor += dt;

                    // Blocked from Execute falls through exactly like a failed
                    // CanStart. Only CanStart used to, which left a creature that
                    // could start eating but then could not proceed - a green in
                    // reach that turned out not to be chewable - burning the whole
                    // tick on nothing. It stood still holding a stale speed while
                    // the soak faithfully reported it as biting.
                    //
                    // This is why an action that returns Blocked must not have
                    // driven the body: whatever runs next will drive it, and two
                    // actions moving one creature in one tick would charge it twice.
                    if (status != ActionStatus.Blocked) return;
                }
                else
                {
                    // Reported, not hidden. A creature that cannot start eating must
                    // read differently on screen from one that is feeding - having to
                    // infer which is what this whole step exists to end.
                    doing.Status = ActionStatus.Blocked;
                }
            }
            else if (!fallThrough)
            {
                // No behaviour written for it yet. Still worth showing as blocked
                // rather than as idle, so a half-built table is visible in the sandbox.
                doing.Action = want;
                doing.Active = true;
                doing.Status = ActionStatus.Blocked;
                return;
            }

            if (!fallThrough || !NextBest(tried, out want))
            {
                if (!doing.Active) doing.Clear();
                return;
            }
        }
    }

    /// <summary>The largest remaining action not already tried.</summary>
    private bool NextBest(int tried, out CreatureAction next)
    {
        next = default;
        float best = Actions.Deadband;
        bool found = false;

        for (int i = 0; i < Actions.Count; i++)
        {
            if ((tried & (1 << i)) != 0) continue;

            float magnitude = MathF.Abs(_wants[i]);
            if (magnitude < best) continue;

            best = magnitude;
            next = (CreatureAction)i;
            found = true;
        }

        return found;
    }

}
