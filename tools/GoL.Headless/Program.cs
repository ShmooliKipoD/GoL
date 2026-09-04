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
    world.Spawn(
        Genome.CreateSeed(ref rng),
        new Vector2(rng.NextFloat(0f, worldSize), rng.NextFloat(0f, worldSize)),
        rng.NextFloat(0f, MathF.Tau));
}

Console.WriteLine($"{(lab ? "lab" : "board")}  seed {seed}  {creatures} creatures  "
    + $"world {worldSize:F0}  {ticks} ticks");
Console.WriteLine($"{"tick",8} {"pop",6} {"gen",5} {"mean E",9}  attributes");

var stopwatch = Stopwatch.StartNew();
int report = Math.Max(1, ticks / 10);

for (int tick = 1; tick <= ticks; tick++)
{
    world.Step();
    if (tick % report == 0) Report(world, tick);
}

stopwatch.Stop();

Console.WriteLine();
Console.WriteLine($"{ticks} ticks in {stopwatch.ElapsedMilliseconds} ms "
    + $"({ticks / Math.Max(1.0, stopwatch.Elapsed.TotalSeconds):F0} ticks/s)");
Console.WriteLine($"world hash: {world.Hash():X16}");

// Population collapse is the normal first outcome of any energy tuning, so say so
// plainly rather than reporting a clean run over an empty world.
if (world.Population == 0)
{
    Console.WriteLine("POPULATION COLLAPSED - the energy constants need retuning.");
    return 1;
}

return 0;

static void Report(SimWorld world, int tick)
{
    float meanEnergy = 0f;
    var living = world.Living;

    for (int i = 0; i < living.Count; i++)
        meanEnergy += world.Get<GoL.Sim.Components.Energy>(living[i]).Current;

    if (living.Count > 0) meanEnergy /= living.Count;

    string attributes = world.UnlockTimeline.Count == 0
        ? "-"
        : string.Join(" ", world.UnlockTimeline.Keys.OrderBy(k => (int)k));

    Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
        "{0,8} {1,6} {2,5} {3,9:F1}  {4}",
        tick, world.Population, world.MaxGeneration, meanEnergy, attributes));
}

static int ArgInt(string[] args, string name, int fallback)
{
    int i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out int value)
        ? value
        : fallback;
}
