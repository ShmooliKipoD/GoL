using System.Numerics;
using GoL.Sim.Acting;
using GoL.Sim.Components;
using GoL.Sim.Core;
using GoL.Sim.Genetics;
using GoL.Sim.Systems;

namespace GoL.Sim.Tests;

/// <summary>
/// The Sandbox's "go here": a waypoint on <see cref="Doing"/> that only
/// <c>MoveAction</c> reads.
/// <para>
/// It is an override in the same family as <see cref="Doing.Forced"/> - it stands in
/// for the brain's wanting, never for the body - so what these pin is that it steers
/// and then <b>lets go</b>. A waypoint that outlived its arrival would leave the
/// creature permanently unable to travel anywhere its brain chose.
/// </para>
/// </summary>
public class WaypointTests
{
    private const float Dt = 1f / 60f;

    private static (SimWorld World, int Id, Doing Doing) Subject(Vector2 at, float heading = 0f)
    {
        var config = new SimConfig { Seed = 5, WorldSize = 340f };
        var world = new SimWorld(config, new SandboxEnvironment(config));

        var rng = new Pcg32(1);
        int id = world.Spawn(Genome.CreateSeed(ref rng), at, heading);

        return (world, id, world.Get<Doing>(id));
    }

    /// <summary>
    /// Facing away from the destination, the creature must turn toward it.
    /// <para>
    /// Against a control that differs <i>only</i> in having no waypoint, rather than
    /// against its own starting heading. A brain left to itself turns constantly, so
    /// "the error got smaller" would have passed on a coin flip and told us nothing
    /// about whether the steering ran at all.
    /// </para>
    /// </summary>
    [Fact]
    public void Waypoint_TurnsTheCreatureTowardIt()
    {
        // Facing +x, told to go to +y: a quarter turn to make.
        var goal = new Vector2(170f, 260f);

        var (steered, steeredId, steeredDoing) = Subject(new Vector2(170f, 170f));
        steeredDoing.Forced = CreatureAction.Move;
        steeredDoing.Waypoint = goal;
        steeredDoing.HasWaypoint = true;

        var (control, controlId, controlDoing) = Subject(new Vector2(170f, 170f));
        controlDoing.Forced = CreatureAction.Move;

        for (int i = 0; i < 30; i++) { steered.Step(); control.Step(); }

        float steeredError = BearingError(steered, steeredId, goal);
        float controlError = BearingError(control, controlId, goal);

        Assert.True(steeredError < controlError,
            $"steered {steeredError:F3} rad off, unsteered {controlError:F3}");

        // And not merely better than a wanderer - actually pointing at the thing.
        Assert.True(steeredError < 0.1f, $"steered should be lined up, is {steeredError:F3} rad off");
    }

    /// <summary>How far off the creature is from facing <paramref name="goal"/>.</summary>
    private static float BearingError(SimWorld world, int id, Vector2 goal)
    {
        var body = world.Get<Body>(id);
        var offset = goal - body.Position;

        return MathF.Abs(Senses.WrapAngle(MathF.Atan2(offset.Y, offset.X) - body.Heading));
    }

    /// <summary>Already lined up, it should close the gap.</summary>
    [Fact]
    public void Waypoint_ClosesTheDistance()
    {
        var start = new Vector2(120f, 170f);
        var goal = new Vector2(220f, 170f);

        var (world, id, doing) = Subject(start);
        doing.Forced = CreatureAction.Move;
        doing.Waypoint = goal;
        doing.HasWaypoint = true;

        float before = Vector2.Distance(start, goal);
        for (int i = 0; i < 120; i++) world.Step();
        float after = Vector2.Distance(world.Get<Body>(id).Position, goal);

        Assert.True(after < before - 1f, $"distance should shrink, went {before:F1} -> {after:F1}");
    }

    /// <summary>
    /// Arriving releases the creature. Without this the flag latches and the brain
    /// never gets the body back - the failure would look like a creature that can
    /// only ever stand on one spot.
    /// </summary>
    [Fact]
    public void Waypoint_ClearsOnArrival()
    {
        var (world, id, doing) = Subject(new Vector2(170f, 170f));
        doing.Forced = CreatureAction.Move;

        // Inside the arrival radius from the outset, so it clears on the first tick
        // it is looked at rather than depending on how fast this genome walks.
        doing.Waypoint = new Vector2(170.5f, 170f);
        doing.HasWaypoint = true;

        world.Step();

        Assert.False(world.Get<Doing>(id).HasWaypoint);
    }

    /// <summary>
    /// An arriving creature must still be driven exactly once on that tick, by the
    /// brain. Two actions moving one body in a tick charges it twice, which is the
    /// rule <c>ActionStatus.Blocked</c> exists to protect - and the arrival branch is
    /// the one place Move hands the tick back mid-flight.
    /// </summary>
    [Fact]
    public void ArrivalTick_StillReportsMoveRunning()
    {
        var (world, id, doing) = Subject(new Vector2(170f, 170f));
        doing.Forced = CreatureAction.Move;
        doing.Waypoint = new Vector2(170.5f, 170f);
        doing.HasWaypoint = true;

        world.Step();

        var after = world.Get<Doing>(id);
        Assert.Equal(CreatureAction.Move, after.Action);
        Assert.Equal(ActionStatus.Running, after.Status);
    }

    /// <summary>
    /// The board sets no waypoint, so Move with the flag clear has to behave exactly
    /// as it did before the flag existed. This is the guard on the whole feature being
    /// invisible outside the Sandbox.
    /// </summary>
    [Fact]
    public void WithoutAWaypoint_MoveIsUnchanged()
    {
        var (a, aId, aDoing) = Subject(new Vector2(170f, 170f), heading: 0.4f);
        aDoing.Forced = CreatureAction.Move;

        var (b, bId, bDoing) = Subject(new Vector2(170f, 170f), heading: 0.4f);
        bDoing.Forced = CreatureAction.Move;

        // One of them is handed a waypoint and then has it taken away again, which
        // must leave no trace at all.
        bDoing.Waypoint = new Vector2(200f, 200f);
        bDoing.HasWaypoint = false;

        for (int i = 0; i < 60; i++) { a.Step(); b.Step(); }

        Assert.Equal(a.Get<Body>(aId).Position, b.Get<Body>(bId).Position);
        Assert.Equal(a.Get<Body>(aId).Heading, b.Get<Body>(bId).Heading);
    }
}
