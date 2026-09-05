using GoL.Sim.Core;

namespace GoL.Sim.Acting;

/// <summary>
/// One thing a creature can do, from beginning to end.
/// <para>
/// Before this interface, execution was smeared across four systems - actuation did
/// thrust and scent, feeding did biting, lifecycle did breeding - and nothing owned
/// "eating" as a thing with a start, a middle and an end. That is why a creature
/// could bite air indefinitely: no code was ever in a position to notice that the
/// biting was not achieving anything.
/// </para>
/// <para>
/// <b>An action owns the body while it runs.</b> Exactly one runs at a time, and it
/// drives the motor primitives directly. <c>Eat</c> steers itself toward its meal
/// rather than delegating to <c>Move</c>; two actions sharing the body would be the
/// creature-states problem arriving a step early.
/// </para>
/// <para>
/// <b>What an action may not do is choose.</b> It may execute what the brain picked
/// competently - closing the distance to a plant the creature already decided to eat
/// is execution. Deciding to eat rather than flee is the brain's, and must never
/// migrate in here, or finding food stops being something a lineage evolves and the
/// open-ended evolution this project exists for goes with it.
/// </para>
/// </summary>
public interface ICreatureAction
{
    /// <summary>Which effector this action is the behaviour for.</summary>
    CreatureAction Id { get; }

    /// <summary>Whether there is any point starting: is there a plant in sight, is
    /// this creature old enough to breed. Checked before the action takes the body,
    /// so a creature is never reported as doing something it cannot do.</summary>
    bool CanStart(in ActionContext ctx);

    /// <summary>
    /// Advances the action by one step.
    /// <para>
    /// <b>An action that returns <see cref="ActionStatus.Blocked"/> must not have
    /// moved the body.</b> The runner falls through to the next-best action on a
    /// block, and that one will drive the body itself - two actions moving one
    /// creature in a single tick would charge it for the movement twice.
    /// </para>
    /// </summary>
    ActionStatus Execute(in ActionContext ctx, float dt);
}
