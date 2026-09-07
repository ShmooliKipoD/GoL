using System;
using System.Numerics;
using GoL.Sim.Acting;
using GoL.Sim.Brains;
using GoL.Sim.Core;
using GoL.Sim.Genetics;

namespace GoL.Sim.Components;

/// <summary>
/// Components are <b>reference types</b> throughout. That is not a style choice:
/// MonoGame.Extended's <c>ComponentMapper&lt;T&gt;</c> constrains <c>T : class</c>.
/// The per-tick path stays allocation-free; birth allocates one object per
/// component, which is the trade this ECS imposes.
/// </summary>
public sealed class Body
{
    public Vector2 Position;
    public float Heading;

    /// <summary>Signed forward speed, units per second.</summary>
    public float Speed;

    /// <summary>Signed turn rate, radians per second.</summary>
    public float AngularVelocity;

    public float Radius;

    public Vector2 Forward => new(MathF.Cos(Heading), MathF.Sin(Heading));

    /// <summary>Body mass, normalized so a mid-sized creature is about 1.</summary>
    public float Mass
    {
        get { float r = Radius / 8f; return r * r; }
    }
}

/// <summary>Stored energy, what it is worth, and what has been eaten to get it.</summary>
public sealed class Energy
{
    public float Current;
    public float Maximum;

    /// <summary>Calories absorbed over this creature's whole life.</summary>
    public float LifetimeIntake;

    /// <summary>Calories absorbed this tick. Cleared by <c>SenseSystem</c> at the
    /// top of the tick, alongside <see cref="Mind.BitThisTick"/>.</summary>
    public float IntakeThisTick;

    /// <summary>
    /// Smoothed intake, energy per second. Smoothed because the raw per-tick value
    /// is either one bite's worth or zero, which flickers far too fast to read.
    /// </summary>
    public float IntakeRate;

    public float Fraction => Maximum > 0f ? Math.Clamp(Current / Maximum, 0f, 1f) : 0f;

    /// <summary>
    /// Absorbs food. <b>The only way energy is ever credited</b> - both environments
    /// used to clamp by hand, and two copies of that is how the two feeding paths
    /// would have drifted apart. Recording intake here also makes the readout
    /// physically unable to disagree with the energy it is reporting on.
    /// </summary>
    public void Gain(float amount)
    {
        if (amount <= 0f) return;

        float before = Current;
        Current = MathF.Min(Maximum, Current + amount);

        // Credit what was actually absorbed, not what was offered: a full creature
        // biting a plant gains nothing, and the readout should say so.
        float absorbed = Current - before;
        LifetimeIntake += absorbed;
        IntakeThisTick += absorbed;
    }

    /// <summary>Folds this tick's intake into the smoothed rate.</summary>
    public void TrackIntake(float dt)
    {
        if (dt <= 0f) return;

        // A long window on purpose. Bites are discrete now - a mouthful every
        // BiteInterval - so a fast filter reads near zero between them and spikes on
        // the tick one lands. Two seconds of memory reports what a creature is
        // actually living on.
        const float smoothing = 0.5f;
        float instant = IntakeThisTick / dt;
        IntakeRate += (instant - IntakeRate) * MathF.Min(1f, smoothing * dt);
    }
}

/// <summary>Age, poison and breeding history - everything that tracks a life.</summary>
public sealed class Vitals
{
    public float Age;
    public float ToxinLoad;
    public float LastBirthTime = float.NegativeInfinity;
    public bool Alive = true;

    /// <summary>Tick of the most recent birth, or -1 for never.
    /// <para>
    /// A tick counter rather than a comparison against <see cref="LastBirthTime"/>:
    /// that is a float, and "did this happen this tick" written as float equality is
    /// exact today but silently becomes "never" the moment simulation time
    /// accumulates differently. A tick count is exact by construction.
    /// </para></summary>
    public long LastBirthTick = -1;

    /// <summary>Simulation tick this creature was born on. Part of the key for its
    /// random stream, so slot reuse cannot make two creatures share a sequence.</summary>
    public long BirthTick;
}

/// <summary>The heritable design. Everything else about a creature derives from it.</summary>
public sealed class Genes
{
    public required Genome Genome { get; init; }

    public float Trait(TraitAxis axis) => Genome.Trait(axis);
    public bool Has(LatentTraitId id) => Genome.Has(id);
}

/// <summary>The compiled brain, its node-id map, and its working buffers.</summary>
public sealed class Mind
{
    public required Brain Brain { get; set; }
    public required SensorLayout Layout { get; set; }

    /// <summary>
    /// This creature's own sensor and effector vectors.
    /// <para>
    /// Per-creature rather than a shared scratch buffer, because sensing and
    /// thinking are two separate passes over every entity: a shared buffer would
    /// hold only the last creature's readings by the time thinking began. Sized at
    /// birth and on rebuild, so the tick itself never allocates.
    /// </para>
    /// </summary>
    public float[] Sensors { get; private set; } = Array.Empty<float>();
    public float[] Effectors { get; private set; } = Array.Empty<float>();

    /// <summary>What the brain decided last tick. Read by the actuation system and
    /// by the overlays - the mouth band flares from this.</summary>
    public Intent Intent;

    /// <summary>Whether a bite actually connected, as opposed to the mouth merely
    /// being open. Cleared at the start of each tick.</summary>
    public bool BitThisTick;

    /// <summary>
    /// What the mouth is currently working on: a grid cell on the board, a plant id
    /// in the sandbox. <b>-1 for nothing</b>, and that initialiser is load-bearing - an
    /// int defaults to 0, which is a perfectly valid index in both, so every newborn
    /// would otherwise start latched onto whatever occupies cell zero.
    /// <para>
    /// Without this the mouth re-picked the nearest plant every tick, so a creature
    /// nibbled whatever drifted closest instead of finishing a meal.
    /// </para>
    /// </summary>
    public int BiteTarget = -1;

    /// <summary>Seconds until this mouth can take another bite. A bite is an event,
    /// so there has to be a gap between them - otherwise "chew" is just a drain
    /// again, one tick at a time.</summary>
    public float BiteCooldown;

    /// <summary>Recompiles after the genome changed in place. Normally a genome is
    /// fixed for a lifetime; the sandbox uses this to grant an attribute on demand.</summary>
    public void Rebuild(Genome genome)
    {
        Brain.Recompile(genome);
        Layout = new SensorLayout(Brain);
        EnsureBuffers();
    }

    /// <summary>Grows the working buffers to fit the brain. Buffers only ever grow,
    /// so a recycled entity slot reuses them.</summary>
    public void EnsureBuffers()
    {
        if (Sensors.Length < Brain.SensorCount) Sensors = new float[Brain.SensorCount];
        if (Effectors.Length < Brain.EffectorCount) Effectors = new float[Brain.EffectorCount];
    }
}

/// <summary>
/// What this creature is doing, as a readout over its effectors.
/// <para>
/// Derived presentation state, kept off <see cref="Mind"/> so that component does
/// not become a bag of unrelated fields. Written only by
/// <c>ActionSystem</c> and read by renderers, the soak runner and tests - nothing
/// in the simulation ever reads it back, or the readout would start driving
/// behaviour instead of describing it.
/// </para>
/// </summary>
public sealed class Behaviour
{
    /// <summary>Every action's value this tick, indexed by <see cref="CreatureAction"/>.
    /// Signed for Move and Turn; zero for anything this genome cannot do.</summary>
    public readonly float[] Values = new float[Actions.Count];

    /// <summary>The action with the greatest magnitude. Only meaningful when
    /// <see cref="Active"/>.</summary>
    public CreatureAction Current;

    /// <summary>False when nothing cleared the deadband - the idle state.</summary>
    public bool Active;

    /// <summary>Seconds the current action has been showing. The headline label is a
    /// summary for a human, and a summary that changes ten times a second is not one
    /// - the per-action bars keep reporting every value continuously, so holding this
    /// steady hides nothing.</summary>
    public float HeldFor;

    /// <summary>Actions this genome can perform, as a bitmask over
    /// <see cref="CreatureAction"/>.</summary>
    public int AvailableMask;

    /// <summary>A bite connected this tick, as opposed to the mouth merely opening.</summary>
    public bool Fed;

    /// <summary>A birth happened this tick.</summary>
    public bool Bred;

    public float Value(CreatureAction action) => Values[(int)action];

    public bool Can(CreatureAction action) => (AvailableMask & (1 << (int)action)) != 0;

    // Label() lived here until Step 3g. CreatureView composes the label from Doing
    // instead, because Doing knows what actually ran and this component only mirrors
    // it - two places building the same string is how they drift apart.
}

/// <summary>
/// What this creature is actually doing right now, as opposed to what its brain
/// asked for.
/// <para>
/// This is the single source of truth for the current action. <see cref="Behaviour"/>
/// used to <i>infer</i> it from whichever effector had the largest magnitude, because
/// nothing in the simulation knew. Now something does, and a readout that guessed
/// alongside a runner that knew would eventually disagree - the panel claiming
/// <c>Eat</c> while the body executed <c>Move</c> is the worst kind of bug to chase.
/// </para>
/// </summary>
public sealed class Doing
{
    /// <summary>The action running this tick. Only meaningful when <see cref="Active"/>.</summary>
    public CreatureAction Action;

    /// <summary>False when nothing is running - the idle state.</summary>
    public bool Active;

    /// <summary>How the last <c>Execute</c> went. Shown next to the action, because
    /// a forced <c>Eat</c> with no green in range must not look on screen like one
    /// that is feeding.</summary>
    public ActionStatus Status;

    /// <summary>Seconds the current action has been running. Reset when it changes.</summary>
    public float HeldFor;

    /// <summary>
    /// Where the action is heading, in world space.
    /// <para>
    /// A <b>position, not an id</b>, and the split from <see cref="Mind.BiteTarget"/>
    /// is deliberate. An eye reports a direction and a range; it has no idea what it
    /// is looking at. So approach is by position, from sight, while biting stays by
    /// id against the existing mouth-reach latch. Trying to make one field serve both
    /// would mean either giving the eye an identity it does not have, or re-finding
    /// the target every tick - which is the nibbling bug <see cref="Mind.BiteTarget"/>
    /// was written to fix.
    /// </para>
    /// </summary>
    public Vector2 Target;

    /// <summary>How many bites this action has landed. Zero on a meal that has not
    /// started; what tells <c>EatAction</c> the difference between "the plant is
    /// eaten" and "there was never anything there".</summary>
    public int Progress;

    /// <summary>Whether <see cref="Target"/> holds anything. Not a sentinel position:
    /// the world is toroidal, so every coordinate is a legitimate destination.</summary>
    public bool HasTarget;

    /// <summary>An action pinned by the Sandbox, overriding the brain.
    /// <para>
    /// <b>A sandbox affordance and nothing else.</b> The board never sets it. It exists
    /// because an unevolved brain simply never chooses the action you want to watch,
    /// which makes "implement them one by one, checking in the sandbox" impossible
    /// otherwise.
    /// </para></summary>
    public CreatureAction? Forced;

    /// <summary>A destination pinned by the Sandbox, in world space.
    /// <para>
    /// <b>A sandbox affordance and nothing else</b>, exactly like <see cref="Forced"/>
    /// - the board never sets it, and only <c>MoveAction</c> reads it. It exists so a
    /// creature can be sent somewhere on purpose, which is the only way to watch
    /// travel over ground you chose rather than ground its brain happened to pick.
    /// </para>
    /// <para>
    /// Deliberately <b>not</b> <see cref="Target"/>. That one is <c>EatAction</c>'s
    /// latch and <c>ActionRunnerSystem</c> clears it on every action switch, which is
    /// right for something an action acquired for itself and wrong for something
    /// imposed from outside: a waypoint dropped the moment the brain blinked toward
    /// Turn would never survive long enough to walk. So this sits beside
    /// <see cref="Forced"/>, survives switches, and is cleared only on arrival.
    /// </para></summary>
    public Vector2 Waypoint;

    /// <summary>Whether <see cref="Waypoint"/> holds anything. A flag rather than a
    /// sentinel position, for the same reason as <see cref="HasTarget"/>.</summary>
    public bool HasWaypoint;

    /// <summary>
    /// Set by <c>BreedAction</c> when every gate has passed, and cleared once the
    /// birth happens.
    /// <para>
    /// A request rather than a direct call because entities cannot be created while
    /// the ECS is iterating them - <c>LifecycleSystem</c> still performs the birth,
    /// last in the tick. What moved out of that system is the <i>decision</i>.
    /// </para>
    /// </summary>
    public bool WantsBirth;

    /// <summary>Drops everything the current action was holding.
    /// <para>
    /// Note what is <i>not</i> here: <see cref="Forced"/> and <see cref="HasWaypoint"/>.
    /// Both are imposed by the Sandbox rather than acquired by an action, so neither
    /// is this method's to release - clearing them here would let a single idle tick
    /// silently undo what the operator asked for.
    /// </para></summary>
    public void Clear()
    {
        Active = false;
        Status = ActionStatus.Blocked;
        HeldFor = 0f;
        HasTarget = false;
        WantsBirth = false;
        Progress = 0;
    }
}

/// <summary>The eyes, and what they currently see.</summary>
public sealed class Sight
{
    public required Eye Forward { get; init; }

    /// <summary>Present only once the RearEye attribute is unlocked.</summary>
    public Eye? Rear { get; set; }

    public Eye EnsureRear() => Rear ??= new Eye(Vision.RearBinCount);
}

/// <summary>
/// This creature's random stream. Seeded from (worldSeed, entityId, birthTick), so
/// its sequence is a pure function of its identity and does not depend on the order
/// creatures happen to be updated in.
/// </summary>
public sealed class RandomSource
{
    public Pcg32 Rng;

    public RandomSource(ulong worldSeed, int entityId, long birthTick)
        => Rng = new Pcg32(Pcg32.Mix(worldSeed, (ulong)entityId, (ulong)birthTick), StreamId.Creature);
}
