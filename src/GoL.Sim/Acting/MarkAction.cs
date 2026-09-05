using System;
using GoL.Sim.Core;

namespace GoL.Sim.Acting;

/// <summary>
/// Laying scent on one channel. Needs the matching gland.
/// <para>
/// One class, two instances - channel A and channel B are the same behaviour through
/// different glands, exactly as chase and flee are one signed Move. A second class
/// would be a copy differing only in an index.
/// </para>
/// </summary>
public sealed class MarkAction : ICreatureAction
{
    private readonly int _channel;

    public MarkAction(CreatureAction id)
    {
        if (id is not (CreatureAction.ScentA or CreatureAction.ScentB))
            throw new ArgumentOutOfRangeException(nameof(id), id, "Marking is ScentA or ScentB.");

        Id = id;
        _channel = id == CreatureAction.ScentA ? 0 : 1;
    }

    public CreatureAction Id { get; }

    public bool CanStart(in ActionContext ctx) => Actions.IsAvailable(Id, ctx.Genome);

    public ActionStatus Execute(in ActionContext ctx, float dt)
    {
        float amount = Id == CreatureAction.ScentA
            ? ctx.Mind.Intent.EmitScent0
            : ctx.Mind.Intent.EmitScent1;

        if (amount <= 0f) return ActionStatus.Blocked;

        // Marking happens while moving. A trail laid from a standstill is a dot, and
        // the point of a pheromone channel is the path it records - so this action
        // drives the body from the brain's intent rather than stopping it.
        Locomotion.Apply(ctx.Body, ctx.Energy, ctx.Genome, ctx.Mind.Intent, ctx.Field, dt);
        ctx.World.Environment.Emit(ctx.Body.Position, _channel, amount * dt);

        return ActionStatus.Running;
    }
}
