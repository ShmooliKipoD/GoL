namespace GoL.Sim.Acting;

/// <summary>
/// How an action's attempt to execute went this tick.
/// <para>
/// The distinction between <see cref="Blocked"/> and <see cref="Running"/> is the
/// whole reason this type exists. Before it, a creature could hold its mouth open
/// forever with nothing in front of it and every readout would report it as eating -
/// the mouth was open, so "Bite" was true. An action that cannot make progress now
/// has to say so, and the runner can hand the body to something else.
/// </para>
/// </summary>
public enum ActionStatus
{
    /// <summary>Under way and making progress. Keep it.</summary>
    Running,

    /// <summary>Finished successfully - the plant is eaten, the birth happened.</summary>
    Done,

    /// <summary>Cannot proceed: nothing to eat, not old enough to breed. Not a
    /// failure of the action so much as of the moment, so the runner drops it and
    /// lets the next-best action have the body.</summary>
    Blocked,
}
