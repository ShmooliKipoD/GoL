using GoL.Render;
using Microsoft.Xna.Framework;

namespace GoL.Ui.Tests;

/// <summary>
/// The sandbox's projection, both ways.
/// <para>
/// These exist because the inverse is what a click depends on, and a click that
/// lands in the wrong place is not obviously a projection bug when you are looking
/// at it - a green appears, just not where you pointed, which reads as the arena
/// being off rather than the arithmetic.
/// </para>
/// </summary>
public class ArenaViewTests
{
    [Fact]
    public void ScreenToWorld_InvertsWorldToScreen()
    {
        var view = new ArenaView(3.5f, new Vector2(120f, -40f));
        var world = new Vector2(212.5f, 77.25f);

        var round = view.ScreenToWorld(view.WorldToScreen(world));

        Assert.Equal(world.X, round.X, 3);
        Assert.Equal(world.Y, round.Y, 3);
    }

    [Fact]
    public void WorldOrigin_LandsOnTheOffset()
    {
        var view = new ArenaView(2f, new Vector2(64f, 32f));

        Assert.Equal(new Vector2(64f, 32f), view.WorldToScreen(Vector2.Zero));
        Assert.Equal(Vector2.Zero, view.ScreenToWorld(new Vector2(64f, 32f)));
    }

    /// <summary>
    /// Zoom must not shift the point under the cursor's world reading - scale divides,
    /// it does not translate. A sign or ordering slip here shows up as clicks drifting
    /// further off the further you are from the origin, which is exactly the kind of
    /// bug that gets blamed on the mouse.
    /// </summary>
    [Fact]
    public void Scale_DividesRatherThanTranslates()
    {
        var view = new ArenaView(4f, new Vector2(10f, 10f));

        Assert.Equal(new Vector2(25f, 50f), view.ScreenToWorld(new Vector2(110f, 210f)));
    }
}
