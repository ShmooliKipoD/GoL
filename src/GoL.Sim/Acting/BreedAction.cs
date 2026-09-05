using GoL.Sim.Core;

namespace GoL.Sim.Acting;

/// <summary>
/// Producing offspring.
/// <para>
/// The decision moved here out of <c>LifecycleSystem</c>, which still performs the
/// birth - creating entities mid-iteration is exactly what that system exists to
/// defer - but no longer decides. What it does now is act on a request.
/// </para>
/// </summary>
public sealed class BreedAction : ICreatureAction
{
    public CreatureAction Id => CreatureAction.Reproduce;

    /// <summary>
    /// Whether this body can bear young: old enough, past the cooldown, and with
    /// energy to spare.
    /// <para>
    /// Deliberately not the brain's gate as well. Selecting this action is what
    /// expresses wanting to - the runner only picks Reproduce when that effector
    /// clears the deadband - so testing it again here would ask the same question
    /// twice, and would leave the lab unable to force a birth on a genome whose gate
    /// happens never to open.
    /// </para>
    /// <para>
    /// Reported as <see cref="ActionStatus.Blocked"/> when the gates do not pass, so
    /// a creature that wants to breed and cannot falls through to something it can
    /// do. It used to be a silent predicate inside a lifecycle loop, which meant a
    /// creature could spend its whole tick on a birth it was never going to get.
    /// </para>
    /// </summary>
    public bool CanStart(in ActionContext ctx) => Locomotion.CanBear(
        ctx.Energy, ctx.Vitals, ctx.Genome, ctx.World.SimTime);

    public ActionStatus Execute(in ActionContext ctx, float dt)
    {
        // Re-checked rather than assumed. CanStart runs once when the action takes
        // the body; the gates can close underneath it - a bite of energy spent, the
        // cooldown restarting after a birth - and breeding twice off one decision
        // would hand a creature a free child.
        if (!CanStart(in ctx)) return ActionStatus.Blocked;

        // A request, not a call. The ECS cannot create entities while it is
        // iterating them, so LifecycleSystem performs this last in the tick.
        ctx.Doing.WantsBirth = true;

        // Standing still to do it. A creature that keeps charging while giving birth
        // leaves its newborn behind at speed, and Reproduce is a discrete event
        // rather than something to be done on the move.
        Locomotion.Apply(ctx.Body, ctx.Energy, ctx.Genome, StandStill(in ctx), ctx.Field, dt);

        return ActionStatus.Done;
    }

    private static Intent StandStill(in ActionContext ctx)
    {
        var intent = ctx.Mind.Intent;
        intent.Thrust = 0f;
        intent.Turn = 0f;
        return intent;
    }
}
