using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using GoL.Sim;
using GoL.Sim.Board;
using GoL.Sim.Core;
using GoL.Sim.Genetics;
using GoL.Sim.Systems;

// Soak runner. References GoL.Sim ONLY - no MonoGame app, no GraphicsDevice, no
// window, and deliberately runnable with no DYLD_FALLBACK_LIBRARY_PATH. That is the
// standing proof that the simulation never needs graphics to run; if this ever stops
// working, the core has acquired a display dependency.
//
//   dotnet run --project tools/GoL.Headless -- --ticks 10000 --seed 42

int ticks = ArgInt(args, "--ticks", 6000);
int seed = ArgInt(args, "--seed", 42);
int creatures = ArgInt(args, "--creatures", 40);
bool lab = Array.IndexOf(args, "--lab") >= 0;

// Grant every latent attribute to the starting population. Not a simulation mode -
// a way to prove the latent rows of the behaviour histogram actually report. Left
// alone they read 0.00% forever, because a 6000-tick run unlocks nothing, and a row
// that is always zero is indistinguishable from a row that is broken.
bool unlockAll = Array.IndexOf(args, "--unlock-all") >= 0;

var config = new SimConfig
{
    Seed = seed,
    WorldSize = lab ? 340f : 1024f,
    TicksPerSecond = 60,
    PlantDensity = 0.18f,
};

IEnvironment environment;
float worldSize;

if (lab)
{
    var labEnv = new LabEnvironment(config);
    labEnv.SeedPlants(ArgInt(args, "--plants", 60));
    environment = labEnv;
    worldSize = labEnv.WorldSize;
}
else
{
    var board = new BoardEnvironment(config);
    environment = board;
    worldSize = board.WorldSize;
}

var world = new SimWorld(config, environment);

var rng = new Pcg32((ulong)seed, StreamId.World);
for (int i = 0; i < creatures; i++)
{
    var genome = Genome.CreateSeed(ref rng);

    if (unlockAll)
    {
        // Repeatedly, so prerequisites open up as it goes.
        while (Mutator.UnlockRandomTrait(genome, ref rng)) { }
    }

    world.Spawn(
        genome,
        new Vector2(rng.NextFloat(0f, worldSize), rng.NextFloat(0f, worldSize)),
        rng.NextFloat(0f, MathF.Tau));
}

Console.WriteLine($"{(lab ? "lab" : "board")}  seed {seed}  {creatures} creatures  "
    + $"world {worldSize:F0}  {ticks} ticks{(unlockAll ? "  (all attributes granted)" : "")}");
Console.WriteLine($"{"tick",8} {"pop",6} {"gen",5} {"mean E",9} {"in/s",7} {"net/s",7} "
    + $"{"plants",7} {"soil",6}  attributes");

var stopwatch = Stopwatch.StartNew();
int report = Math.Max(1, ticks / 10);

// Creature-ticks spent on each action, plus the two signs of Move counted apart.
// Accumulated over the whole run rather than sampled, so a rare action still shows.
var histogram = new long[Actions.Count];
long chasing = 0, fleeing = 0, idle = 0, total = 0;

// How often a creature changes what it is doing. "Its state fluctuates way too
// fast" is otherwise an eyeball judgement; this makes it a number, and it is the
// evidence that motor commitment actually did something.
// Indexed by entity id, holding the previous action + 1 so 0 means "not seen yet".
var previousAction = new int[1024];
long switches = 0, switchSamples = 0;

// How often an open mouth actually reaches food. A creature can hold Bite wide
// open and starve, because the gate says "I want to eat" and nothing says "there
// is anything here" - the readout calls that Biting rather than Feeding, and this
// turns the distinction into a number.
long mouthOpen = 0, bitesConnected = 0, creatureTicks = 0;

for (int tick = 1; tick <= ticks; tick++)
{
    world.Step();
    Accumulate(world, histogram, ref chasing, ref fleeing, ref idle, ref total);
    CountSwitches(world, ref previousAction, ref switches, ref switchSamples);

    foreach (int id in world.Living)
    {
        creatureTicks++;
        var m = world.Get<GoL.Sim.Components.Mind>(id);
        if (m.BitThisTick) bitesConnected++;
        if (m.Intent.Bite) mouthOpen++;
    }
    if (tick % report == 0) Report(world, tick, environment);
}

stopwatch.Stop();

Console.WriteLine();
Console.WriteLine($"{ticks} ticks in {stopwatch.ElapsedMilliseconds} ms "
    + $"({ticks / Math.Max(1.0, stopwatch.Elapsed.TotalSeconds):F0} ticks/s)");
Console.WriteLine($"world hash: {world.Hash():X16}");

PrintBehaviour(histogram, chasing, fleeing, idle, total);

if (creatureTicks > 0)
{
    // Bites are discrete, so counting "open-mouth ticks that landed" would just
    // measure the chewing gap. What matters is how often a mouthful actually
    // arrives, and how much of the time the mouth is open hopefully.
    Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
        "  {0,-40} {1,6:F2}", "bites landed per creature-minute",
        (double)bitesConnected / creatureTicks * config.TicksPerSecond * 60.0));

    Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
        "  {0,-40} {1,6:F1}%", "of the time with its mouth open",
        100.0 * mouthOpen / creatureTicks));
}

if (switchSamples > 0)
{
    double perSecond = (double)switches / switchSamples * config.TicksPerSecond;
    Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
        "  {0,-40} {1,6:F2}", "action switches per creature-second", perSecond));
}

// Population collapse is the normal first outcome of any energy tuning, so say so
// plainly rather than reporting a clean run over an empty world.
if (world.Population == 0)
{
    Console.WriteLine("POPULATION COLLAPSED - the energy constants need retuning.");
    return 1;
}

return 0;

// The best evidence available that evolution is producing coherent strategies
// rather than noise. Because the vocabulary is the effector set, this is a direct
// distribution over what the brains actually emit - there is no predicate in
// between that could be miscalibrated and fake either result.
//
// Read it with the failure modes in mind: ~100% idle, or an even spread across
// every action, both mean the brains are not doing anything.
static void PrintBehaviour(long[] histogram, long chasing, long fleeing, long idle, long total)
{
    Console.WriteLine();
    Console.WriteLine($"behaviour over {total} creature-ticks");

    if (total == 0) return;

    // Move is reported split by sign as well as whole: chase and flee are one
    // signed action, but a chase-heavy and a flee-heavy population are not the
    // same population, and the aggregate alone would hide the difference.
    for (int i = 0; i < Actions.Count; i++)
    {
        var action = (CreatureAction)i;
        string label = Actions.Name(action);

        if (action == CreatureAction.Move)
        {
            label += $" (chase {100.0 * chasing / total,5:F1}%  flee {100.0 * fleeing / total,5:F1}%)";
        }

        Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
            "  {0,-40} {1,6:F2}%", label, 100.0 * histogram[i] / total));
    }

    Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
        "  {0,-40} {1,6:F2}%", "idle", 100.0 * idle / total));
}

static void Accumulate(
    SimWorld world, long[] histogram,
    ref long chasing, ref long fleeing, ref long idle, ref long total)
{
    var living = world.Living;

    for (int i = 0; i < living.Count; i++)
    {
        var behaviour = world.Get<GoL.Sim.Components.Behaviour>(living[i]);
        total++;

        if (!behaviour.Active) { idle++; continue; }

        histogram[(int)behaviour.Current]++;

        if (behaviour.Current == CreatureAction.Move)
        {
            if (behaviour.Value(CreatureAction.Move) < 0f) fleeing++; else chasing++;
        }
    }
}

/// <summary>
/// Counts how often each creature's current action changes. A high number is the
/// twitching: a brain output resting near a gate threshold flips it every tick, and
/// the creature chatters between biting and turning instead of doing either.
/// </summary>
static void CountSwitches(SimWorld world, ref int[] previous, ref long switches, ref long samples)
{
    var living = world.Living;

    for (int i = 0; i < living.Count; i++)
    {
        int id = living[i];
        if (id >= previous.Length) Array.Resize(ref previous, Math.Max(id + 1, previous.Length * 2));

        var behaviour = world.Get<GoL.Sim.Components.Behaviour>(id);

        // Idle is a state like any other here - flipping in and out of it counts.
        int current = behaviour.Active ? (int)behaviour.Current + 2 : 1;

        if (previous[id] != 0)
        {
            samples++;
            if (previous[id] != current) switches++;
        }

        previous[id] = current;
    }
}

static void Report(SimWorld world, int tick, IEnvironment environment)
{
    float meanEnergy = 0f, meanIntake = 0f;
    var living = world.Living;

    for (int i = 0; i < living.Count; i++)
    {
        var energy = world.Get<GoL.Sim.Components.Energy>(living[i]);
        meanEnergy += energy.Current;
        meanIntake += energy.IntakeRate;
    }

    if (living.Count > 0) { meanEnergy /= living.Count; meanIntake /= living.Count; }

    // Upkeep is not reported directly, so net is derived from what intake has to
    // beat: a population whose net is negative is dying, whatever the count says.
    float meanUpkeep = 0f;
    for (int i = 0; i < living.Count; i++)
    {
        int id = living[i];
        meanUpkeep += Metabolism.BaseCost(
            world.Get<GoL.Sim.Components.Body>(id),
            world.Get<GoL.Sim.Components.Genes>(id).Genome,
            world.Get<GoL.Sim.Components.Mind>(id).Brain,
            world.Get<GoL.Sim.Components.Vitals>(id),
            world.Config.BaseMetabolicRate);
    }
    if (living.Count > 0) meanUpkeep /= living.Count;

    // Plant count and soil are the early warning. A stripped world shows here long
    // before it shows in the population, and soil separates "grazed" from
    // "running down" - which look identical from the plant count alone.
    string plants = "-", soil = "-";
    if (environment is BoardEnvironment board)
    {
        plants = board.Plants.LiveCount().ToString(CultureInfo.InvariantCulture);
        soil = board.Fertility.Mean().ToString("F2", CultureInfo.InvariantCulture);
    }

    string attributes = world.UnlockTimeline.Count == 0
        ? "-"
        : string.Join(" ", world.UnlockTimeline.Keys.OrderBy(k => (int)k));

    Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
        "{0,8} {1,6} {2,5} {3,9:F1} {4,7:F2} {5,7:F2} {6,7} {7,6}  {8}",
        tick, world.Population, world.MaxGeneration, meanEnergy,
        meanIntake, meanIntake - meanUpkeep, plants, soil, attributes));
}

static int ArgInt(string[] args, string name, int fallback)
{
    int i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out int value)
        ? value
        : fallback;
}
