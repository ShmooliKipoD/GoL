using System;
using GoL.Sim.Genetics;

namespace GoL.Sim.Core;

/// <summary>
/// Everything a creature can do. The vocabulary <b>is the effector set</b> - one
/// entry per output the brain actually drives, mirroring <see cref="InnateAction"/>
/// and the latent effector channels.
/// <para>
/// Note what is <i>not</i> here: chasing and fleeing are not two actions. They are
/// one signed <see cref="Move"/> - chase is positive thrust, flee is negative. The
/// brain is a continuous controller, not a planner; it emits a thrust every tick and
/// never chooses between named behaviours. Splitting one effector into two named
/// entries would invent a distinction the controller does not have, and would then
/// need a "is something in view?" heuristic to tell them apart. Reading the sign
/// needs no heuristic and cannot drift from the real model. The same goes for
/// <see cref="Turn"/>: left and right are one action.
/// </para>
/// </summary>
public enum CreatureAction
{
    /// <summary>Signed thrust. Negative is flee, positive is chase.</summary>
    Move,

    /// <summary>Signed turn. Negative is left, positive is right.</summary>
    Turn,

    /// <summary>Mouth gate.</summary>
    Bite,

    /// <summary>Breeding gate. Still subject to age, cooldown and energy.</summary>
    Reproduce,

    Sprint,
    ScentA,
    ScentB,
    Torpor,
}

/// <summary>
/// Reads a creature's actions off its <see cref="Intent"/>. Pure projection: no
/// inference, no new sensing, no simulation state of its own. That is what keeps it
/// a readout rather than a second behaviour model that could drift from the real one.
/// </summary>
public static class Actions
{
    public static readonly CreatureAction[] All = Enum.GetValues<CreatureAction>();

    /// <summary>Derived from the enum, never hardcoded. A literal here would leave
    /// a newly added action out of <see cref="Read"/>, out of
    /// <see cref="AvailableMask"/> and out of every buffer sized by it - silently,
    /// since nothing would fail to compile.</summary>
    public static readonly int Count = All.Length;

    /// <summary>Below this magnitude an action does not count as happening. Without
    /// it every creature would read as permanently turning, because a brain output
    /// is essentially never exactly zero.</summary>
    public const float Deadband = 0.05f;

    /// <summary>
    /// Tie-break when two actions share the top magnitude. Discrete events ahead of
    /// continuous ones, so a creature that is both biting and cruising reports the
    /// bite. Fixed and documented: an unordered "pick one" would flicker between
    /// equally-true states and make the readout useless for spotting patterns.
    /// </summary>
    /// <summary>
    /// Every action, in tie-break order. <b>A new action must be added here too</b> -
    /// <see cref="BuildRank"/> enforces it rather than trusting the reminder, because
    /// a missing entry would leave that action at rank 0, tied with
    /// <see cref="CreatureAction.Reproduce"/> for top priority, and it would quietly
    /// win every tie it entered.
    /// </summary>
    private static readonly CreatureAction[] TieBreak =
    {
        CreatureAction.Reproduce, CreatureAction.Bite, CreatureAction.Torpor,
        CreatureAction.Sprint, CreatureAction.ScentA, CreatureAction.ScentB,
        CreatureAction.Move, CreatureAction.Turn,
    };

    private static readonly int[] Rank = BuildRank();

    /// <summary>Display name. Ignores sign - see <see cref="PoleName"/>.</summary>
    public static string Name(CreatureAction action) => action switch
    {
        CreatureAction.Move => "Move",
        CreatureAction.Turn => "Turn",
        CreatureAction.Bite => "Bite",
        CreatureAction.Reproduce => "Reproduce",
        CreatureAction.Sprint => "Sprint",
        CreatureAction.ScentA => "Scent A",
        CreatureAction.ScentB => "Scent B",
        CreatureAction.Torpor => "Torpor",
        _ => action.ToString(),
    };

    /// <summary>
    /// What this action looks like at the sign it is currently at. "Chase" and
    /// "Flee" are human words for the two signs of a thrust - the creature is
    /// advancing or reversing and has no notion of a pursuer.
    /// </summary>
    public static string PoleName(CreatureAction action, float value) => action switch
    {
        CreatureAction.Move => value < 0f ? "Flee" : "Chase",
        CreatureAction.Turn => value < 0f ? "Turn L" : "Turn R",
        _ => Name(action),
    };

    /// <summary>
    /// The full label for a current action, refined by what actually happened:
    /// a bite that connected reads "Feeding" rather than "Biting", and a gate that
    /// produced a birth reads "Breeding". Both are outcomes the simulation already
    /// records, not inferences.
    /// </summary>
    public static string Label(CreatureAction action, float value, bool fed, bool bred) =>
        action switch
        {
            CreatureAction.Bite => fed ? "Feeding" : "Biting",
            CreatureAction.Reproduce => bred ? "Breeding" : "Reproduce",
            CreatureAction.ScentA or CreatureAction.ScentB => "Marking",
            _ => PoleName(action, value),
        };

    /// <summary>What a creature doing nothing is called. Not an enum member: idle is
    /// the absence of an action, not one of them.</summary>
    public const string IdleLabel = "Idle";

    /// <summary>True for the two actions whose sign carries meaning.</summary>
    public static bool IsSigned(CreatureAction action) =>
        action is CreatureAction.Move or CreatureAction.Turn;

    /// <summary>The attribute this action needs, or null if it is innate.</summary>
    public static LatentTraitId? Requires(CreatureAction action) => action switch
    {
        CreatureAction.Sprint => LatentTraitId.SprintGland,
        CreatureAction.ScentA => LatentTraitId.ScentGlandA,
        CreatureAction.ScentB => LatentTraitId.ScentGlandB,
        CreatureAction.Torpor => LatentTraitId.Torpor,
        _ => null,
    };

    /// <summary>Whether this genome can perform the action at all.</summary>
    public static bool IsAvailable(CreatureAction action, Genome genome)
    {
        var required = Requires(action);
        return required is null || genome.Has(required.Value);
    }

    /// <summary>Every action this genome can perform, as a bitmask.</summary>
    public static int AvailableMask(Genome genome)
    {
        int mask = 0;
        for (int i = 0; i < Count; i++)
            if (IsAvailable((CreatureAction)i, genome)) mask |= 1 << i;
        return mask;
    }

    /// <summary>
    /// This action's current value. Signed for <see cref="CreatureAction.Move"/> and
    /// <see cref="CreatureAction.Turn"/>; gates read 0 or 1.
    /// </summary>
    public static float Value(CreatureAction action, in Intent intent) => action switch
    {
        CreatureAction.Move => Math.Clamp(intent.Thrust, -1f, 1f),
        CreatureAction.Turn => Math.Clamp(intent.Turn, -1f, 1f),
        CreatureAction.Bite => intent.Bite ? 1f : 0f,
        CreatureAction.Reproduce => intent.Reproduce ? 1f : 0f,
        CreatureAction.Sprint => intent.Sprint ? 1f : 0f,
        CreatureAction.ScentA => Math.Clamp(intent.EmitScent0, 0f, 1f),
        CreatureAction.ScentB => Math.Clamp(intent.EmitScent1, 0f, 1f),
        CreatureAction.Torpor => intent.Torpor ? 1f : 0f,
        _ => 0f,
    };

    /// <summary>Fills <paramref name="destination"/> with every action's value,
    /// zeroing any this genome cannot perform.</summary>
    public static void Read(Genome genome, in Intent intent, Span<float> destination)
    {
        for (int i = 0; i < Count; i++)
        {
            var action = (CreatureAction)i;
            destination[i] = IsAvailable(action, genome) ? Value(action, intent) : 0f;
        }
    }

    /// <summary>
    /// The action a creature is most doing: the greatest magnitude this tick, ties
    /// broken by <see cref="TieBreak"/>. Returns false when nothing clears the
    /// deadband - that is the idle state, and it is deliberately not an enum member.
    /// </summary>
    public static bool Current(ReadOnlySpan<float> values, out CreatureAction current)
    {
        current = default;
        float best = Deadband;
        bool found = false;

        for (int i = 0; i < Count; i++)
        {
            float magnitude = MathF.Abs(values[i]);
            if (magnitude < Deadband) continue;

            if (!found || magnitude > best ||
                (magnitude == best && Rank[i] < Rank[(int)current]))
            {
                best = magnitude;
                current = (CreatureAction)i;
                found = true;
            }
        }

        return found;
    }

    private static int[] BuildRank()
    {
        if (TieBreak.Length != Count)
        {
            throw new InvalidOperationException(
                $"TieBreak lists {TieBreak.Length} of {Count} actions. Every action needs "
                + "an explicit priority; one left out would rank 0 and win every tie.");
        }

        var rank = new int[Count];
        for (int i = 0; i < TieBreak.Length; i++) rank[(int)TieBreak[i]] = i;
        return rank;
    }
}
