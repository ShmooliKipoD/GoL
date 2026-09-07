using System;
using GoL.Sim.Core;

namespace GoL.Sim.Acting;

/// <summary>
/// Travelling on the current heading.
/// <para>
/// <b>One signed action, not two.</b> Positive thrust advances, negative backs away;
/// chase and flee are the two poles of the same effector. The brain is a continuous
/// controller, not a planner - it emits a thrust every tick and never chooses between
/// named behaviours. Splitting this into Chase and Flee would invent a distinction
/// the controller does not have, and would then need an "is something in view?"
/// heuristic to tell them apart. Reading the sign needs no heuristic and cannot drift
/// from the real model.
/// </para>
/// </summary>
public sealed class MoveAction : ICreatureAction
{
    public CreatureAction Id => CreatureAction.Move;

    /// <summary>How hard it steers onto a waypoint, in turn-rate per radian of error.
    /// The same gain <c>EatAction</c> approaches a green with, deliberately: walking
    /// to a spot and closing on a meal are the same manoeuvre, and two different
    /// numbers would make the sandbox misreport how travel actually looks.</summary>
    private const float SteerGain = 2.5f;

    /// <summary>Beyond this much heading error, close the angle before the distance.
    /// Charging at ninety degrees off just orbits the destination.</summary>
    private const float ApproachArc = 1.0f;

    /// <summary>Always. A creature can always try to move; whether it gets anywhere
    /// is the body's business.</summary>
    public bool CanStart(in ActionContext ctx) => true;

    public ActionStatus Execute(in ActionContext ctx, float dt)
    {
        // A waypoint the Sandbox pinned. Steering to it rather than obeying the brain
        // is exactly the same kind of override as Doing.Forced: it stands in for the
        // creature's wanting, never for its body. Everything below still goes through
        // Locomotion, so the trip costs what a trip costs and a slow creature is
        // still slow.
        if (ctx.Doing.HasWaypoint && SteerToWaypoint(in ctx, dt)) return ActionStatus.Running;

        Locomotion.Apply(ctx.Body, ctx.Energy, ctx.Genome, ctx.Mind.Intent, ctx.Field, dt);

        // Travelling has no natural end. It runs until the brain wants something
        // else badly enough to take the body, which is the runner's call - reporting
        // Done here every tick would make Move unable to hold a heading at all.
        return ActionStatus.Running;
    }

    /// <summary>
    /// Drives one step toward <c>Doing.Waypoint</c>. Returns false once it has
    /// arrived, having driven nothing - the caller then falls through to the brain on
    /// the same tick, so arrival costs no stalled frame and the body is still driven
    /// exactly once.
    /// </summary>
    private static bool SteerToWaypoint(in ActionContext ctx, float dt)
    {
        // Through the field, so the short way round a toroidal seam is the way taken.
        var offset = ctx.Field.Offset(ctx.Position, ctx.Doing.Waypoint);
        float distance = offset.Length();

        // Close enough is arrived. A point target would leave the creature circling
        // it forever, overshooting by whatever it travels in one tick.
        if (distance <= ArrivalRadius(in ctx))
        {
            ctx.Doing.HasWaypoint = false;
            return false;
        }

        float bearing = Senses.WrapAngle(MathF.Atan2(offset.Y, offset.X) - ctx.Heading);
        float turn = Math.Clamp(bearing * SteerGain, -1f, 1f);
        float thrust = MathF.Abs(bearing) > ApproachArc ? 0.2f : 1f;

        // A local copy: the brain's intent is not ours to rewrite, and ThinkSystem
        // would overwrite it next tick anyway. Sprint and Torpor ride along untouched,
        // because whether to spend that energy stays the creature's decision.
        var intent = ctx.Mind.Intent;
        intent.Thrust = thrust;
        intent.Turn = turn;

        Locomotion.Apply(ctx.Body, ctx.Energy, ctx.Genome, intent, ctx.Field, dt);
        return true;
    }

    /// <summary>How near counts as there. Scaled by the body so a large creature is
    /// not asked to place its centre more precisely than a small one.</summary>
    private static float ArrivalRadius(in ActionContext ctx) =>
        MathF.Max(2f, ctx.Body.Radius);
}
