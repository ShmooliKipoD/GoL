using GoL.Sim.Core;

namespace GoL.Sim.Acting;

/// <summary>
/// Rotating in place. Signed: negative is left, positive is right - one action, for
/// the same reason <see cref="MoveAction"/> is.
/// <para>
/// It goes through the same <see cref="Locomotion.Apply"/> as moving rather than
/// touching the heading itself. Turning and thrusting are one integration step of one
/// body, and two actions writing the heading by two different routes is how the
/// eased turn introduced in Step 3e would quietly come undone.
/// </para>
/// </summary>
public sealed class TurnAction : ICreatureAction
{
    public CreatureAction Id => CreatureAction.Turn;

    public bool CanStart(in ActionContext ctx) => true;

    public ActionStatus Execute(in ActionContext ctx, float dt)
    {
        Locomotion.Apply(ctx.Body, ctx.Energy, ctx.Genome, ctx.Mind.Intent, ctx.Field, dt);
        return ActionStatus.Running;
    }
}
