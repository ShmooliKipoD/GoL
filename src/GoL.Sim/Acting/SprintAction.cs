using GoL.Sim.Core;

namespace GoL.Sim.Acting;

/// <summary>
/// Sprinting, which is <b>a modifier on movement rather than an action of its own</b>.
/// There is no such thing as sprinting while standing still.
/// <para>
/// So this does not exist as a class. <c>Metabolism.EffectiveMaxSpeed</c> reads
/// <c>Intent.Sprint</c> and raises the ceiling for whatever action is driving the
/// body - Move, Eat closing on a green, Mark laying a trail - and charges the higher
/// movement cost through the same path.
/// </para>
/// <para>
/// It still appears in <see cref="CreatureAction"/>, because that vocabulary is the
/// effector set and Sprint is an effector the brain drives. What it does not get is
/// an <see cref="ICreatureAction"/>: the runner leaves its slot empty, and selecting
/// it falls through to the movement it is a modifier on. This file exists to say so,
/// because an empty slot in the table looks exactly like an unfinished one.
/// </para>
/// </summary>
internal static class SprintIsAModifier
{
}
