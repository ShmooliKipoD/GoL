using System;
using System.Numerics;
using GoL.Sim.Core;
using GoL.Sim.Genetics;

namespace GoL.Sim.Acting;

/// <summary>
/// Eating: choose a green, close the distance, bite it out, finish.
/// <para>
/// This is the action the whole step was written for. A creature was watched holding
/// its mouth open with nothing in front of it, gaining nothing, while every readout
/// reported it as feeding - because "bite" was a gate applied wherever it happened to
/// be read, and approaching food was something the brain had to arrange separately.
/// Biting out of reach is no longer possible: getting there is <i>part of eating</i>.
/// </para>
/// </summary>
public sealed class EatAction : ICreatureAction
{
    /// <summary>The mouth gate. Eating is the behaviour behind that effector - the
    /// vocabulary is the effector set, so this is not a new member.</summary>
    public CreatureAction Id => CreatureAction.Bite;

    /// <summary>
    /// How hard the creature steers toward its meal, in turn-rate per radian of
    /// error. High enough to close on a green, low enough that the eased turn still
    /// reads as a turn rather than a snap.
    /// </summary>
    private const float SteerGain = 2.5f;

    /// <summary>Beyond this much heading error, close the angle before closing the
    /// distance. Charging at ninety degrees off just orbits the plant.</summary>
    private const float ApproachArc = 1.0f;

    public bool CanStart(in ActionContext ctx)
    {
        // Something already in the mouth is reason enough.
        if (ctx.Mind.BiteTarget >= 0) return true;

        // Otherwise it has to be able to SEE food.
        //
        // Deliberately sight, not a "nearest plant" query on the environment. An
        // environment oracle would give a creature with terrible eyes exactly the
        // same food-finding as one with excellent eyes, and vision would stop being
        // something a lineage evolves - which is the whole point of the project.
        if (!Acquire(in ctx, out var target)) return false;

        ctx.Doing.Target = target;
        ctx.Doing.HasTarget = true;
        ctx.Doing.Progress = 0;
        return true;
    }

    public ActionStatus Execute(in ActionContext ctx, float dt)
    {
        var mind = ctx.Mind;

        // Bite first, from wherever the body actually is. ResolveBite owns the
        // mouth-reach and arc test and the latch onto a particular plant, so this
        // action never second-guesses whether something is chewable.
        ctx.World.ResolveBite(ctx.Body, ctx.Energy, ctx.Genome, mind, dt);

        if (mind.BitThisTick) ctx.Doing.Progress++;

        if (mind.BiteTarget >= 0)
        {
            // In the mouth. Stop, so a creature does not chew its way straight past
            // the plant it is eating.
            Drive(in ctx, thrust: 0f, turn: 0f, dt);
            return ActionStatus.Running;
        }

        // Nothing in the mouth. If this meal landed bites and the plant is gone, it
        // was eaten out - which is what finishing looks like.
        if (ctx.Doing.Progress > 0) return ActionStatus.Done;

        // Still on the way. Re-acquire only when there is nothing to head for: a
        // creature that re-picked every tick nibbled whatever drifted closest
        // instead of finishing a meal, which is the bug Mind.BiteTarget was written
        // to fix and it would come straight back here.
        if (!ctx.Doing.HasTarget)
        {
            if (!Acquire(in ctx, out var reacquired)) return ActionStatus.Blocked;
            ctx.Doing.Target = reacquired;
            ctx.Doing.HasTarget = true;
        }

        var offset = ctx.Field.Offset(ctx.Position, ctx.Doing.Target);
        float distance = offset.Length();

        if (distance <= 0.001f)
        {
            // Arrived, and the mouth found nothing. Whatever was seen is not here.
            ctx.Doing.HasTarget = false;
            return ActionStatus.Blocked;
        }

        float bearing = Senses.WrapAngle(MathF.Atan2(offset.Y, offset.X) - ctx.Heading);

        // In reach, and yet ResolveBite took nothing. Either the green is too
        // depleted to be worth picking, or it sits just outside the mouth arc - an
        // eye and a mouth are different shapes, so seeing something is not the same
        // as being able to bite it.
        //
        // This branch used to set HasTarget false and return Running WITHOUT driving
        // the body, which froze the creature solid: it stood still, re-acquired the
        // same inedible green next tick, and did it again forever. Four of eight
        // creatures were stuck like that by tick 500, each holding a stale speed it
        // could no longer act on, and the soak reported them as 78% "Bite" the whole
        // time. An action that cannot make progress has to say so - that is the
        // entire premise of ActionStatus, and here was the first place it was
        // quietly violated.
        if (distance <= ctx.MouthRange)
        {
            float arc = ctx.Genes.Trait(TraitAxis.MouthArc);

            // Lining up might still bring it into the mouth.
            if (MathF.Abs(bearing) > arc)
            {
                Drive(in ctx, thrust: 0f, turn: Math.Clamp(bearing * SteerGain, -1f, 1f), dt);
                return ActionStatus.Running;
            }

            // Squarely in the mouth and still not chewable. Nothing more to try.
            ctx.Doing.HasTarget = false;
            return ActionStatus.Blocked;
        }

        // Turn onto it before charging, or a creature badly off-bearing just orbits.
        float turn = Math.Clamp(bearing * SteerGain, -1f, 1f);
        float thrust = MathF.Abs(bearing) > ApproachArc ? 0.2f : 1f;

        Drive(in ctx, thrust, turn, dt);
        return ActionStatus.Running;
    }

    /// <summary>
    /// The nearest plant this creature can actually see, as a world position.
    /// <para>
    /// A <b>position</b>, because that is all an eye can give: bins carry a distance
    /// and a kind, never an identity. So approach is by position from sight, while
    /// biting stays by id against the mouth-reach latch - two different questions
    /// that only look like one.
    /// </para>
    /// </summary>
    private static bool Acquire(in ActionContext ctx, out Vector2 target)
    {
        target = default;

        float best = float.MaxValue;
        bool found = false;

        Scan(ctx.Sight.Forward, in ctx, ref best, ref target, ref found);
        if (ctx.Sight.Rear is { } rear) Scan(rear, in ctx, ref best, ref target, ref found);

        return found;
    }

    private static void Scan(
        Eye eye, in ActionContext ctx, ref float best, ref Vector2 target, ref bool found)
    {
        for (int bin = 0; bin < eye.BinCount; bin++)
        {
            if (eye.Kind[bin] != SeenKind.Plant) continue;

            float distance = eye.Distance[bin];
            if (distance >= best) continue;

            best = distance;
            target = ctx.Field.Wrap(ctx.Position + eye.BinDirection(ctx.Heading, bin) * distance);
            found = true;
        }
    }

    /// <summary>
    /// Drives the motor primitives directly, from an intent this action builds
    /// rather than the brain's.
    /// <para>
    /// That is what "an action owns the body while it runs" means. Eat steers itself
    /// instead of delegating to <see cref="MoveAction"/>; two actions sharing the
    /// body at once would be the creature-states problem arriving a step early.
    /// Sprint and Torpor still come from the brain, because whether to spend that
    /// energy is a decision, not a manoeuvre.
    /// </para>
    /// </summary>
    private static void Drive(in ActionContext ctx, float thrust, float turn, float dt)
    {
        var intent = ctx.Mind.Intent;
        intent.Thrust = thrust;
        intent.Turn = turn;

        Locomotion.Apply(ctx.Body, ctx.Energy, ctx.Genome, intent, ctx.Field, dt);
    }
}
