using GoL.Sim.Core;

namespace GoL.Sim.Acting;

/// <summary>
/// Torpor: cheap, nearly immobile, half-blind. Needs the Torpor attribute.
/// <para>
/// The discount is applied by the energy system, which is where upkeep is charged -
/// this action's job is to hold the state, not to reprice it. What it does own is the
/// standing still, because resting while charging across the map would be neither.
/// </para>
/// </summary>
public sealed class RestAction : ICreatureAction
{
    public CreatureAction Id => CreatureAction.Torpor;

    /// <summary>The runner never selects an action this genome cannot perform -
    /// unavailable actions read zero - so reaching here means the attribute is
    /// present. Checked anyway, because the sandbox can force any action at all.</summary>
    public bool CanStart(in ActionContext ctx) =>
        Actions.IsAvailable(CreatureAction.Torpor, ctx.Genome);

    public ActionStatus Execute(in ActionContext ctx, float dt)
    {
        // Locomotion damps thrust and turn by TorporMoveFactor when the intent's
        // torpor flag is set, so resting costs manoeuvrability at exactly the moment
        // it saves energy. Passing the brain's intent through unchanged keeps that.
        Locomotion.Apply(ctx.Body, ctx.Energy, ctx.Genome, ctx.Mind.Intent, ctx.Field, dt);

        // Resting has no natural end; the brain lets go of it.
        return ActionStatus.Running;
    }
}
