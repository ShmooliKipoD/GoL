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

    /// <summary>Always. A creature can always try to move; whether it gets anywhere
    /// is the body's business.</summary>
    public bool CanStart(in ActionContext ctx) => true;

    public ActionStatus Execute(in ActionContext ctx, float dt)
    {
        Locomotion.Apply(ctx.Body, ctx.Energy, ctx.Genome, ctx.Mind.Intent, ctx.Field, dt);

        // Travelling has no natural end. It runs until the brain wants something
        // else badly enough to take the body, which is the runner's call - reporting
        // Done here every tick would make Move unable to hold a heading at all.
        return ActionStatus.Running;
    }
}
