using GoL.Render;

namespace GoL.Ui.Tests;

/// <summary>
/// The sandbox's pause menu.
/// <para>
/// The behaviour worth pinning is that it is <b>modal</b>. The screen underneath
/// binds A, N and Space to its own overlays, and <c>InputMap</c> maps those same keys
/// to Left, No and Accept - so an overlay that reported only the frames it acted on
/// would let one keypress drive the menu and the arena at once.
/// </para>
/// </summary>
public class PauseOverlayTests
{
    private const float Dt = 1f / 60f;

    private static PauseOverlay Overlay(Action<string> log, Func<string>? value = null,
        Action<int>? adjust = null) => new("Sandbox", new[]
    {
        new MenuEntry("Resume", () => log("resume")),
        new MenuEntry("Arena size", ValueText: value ?? (() => "340"), OnAdjust: adjust),
        new MenuEntry("Back to Main Menu", () => log("menu")),
    });

    [Fact]
    public void Closed_ConsumesNothing()
    {
        var overlay = Overlay(_ => { });

        Assert.False(overlay.IsOpen);
        Assert.False(overlay.Update(Dt, new MenuInput(Down: true)));
        Assert.False(overlay.Update(Dt, new MenuInput(Accept: true)));
    }

    [Fact]
    public void Open_SwallowsEveryFrame_EvenOnesItIgnores()
    {
        var overlay = Overlay(_ => { });
        overlay.Open();

        // Y and N mean nothing to a menu, and it must still not let them through.
        Assert.True(overlay.Update(Dt, new MenuInput(Yes: true)));
        Assert.True(overlay.Update(Dt, MenuInput.None));
    }

    [Fact]
    public void Cancel_Closes()
    {
        var overlay = Overlay(_ => { });
        overlay.Open();

        Assert.True(overlay.Update(Dt, new MenuInput(Cancel: true)));
        Assert.False(overlay.IsOpen);
    }

    [Fact]
    public void Activate_FiresTheSelectedRow()
    {
        var fired = new List<string>();
        var overlay = Overlay(fired.Add);
        overlay.Open();

        overlay.Update(Dt, new MenuInput(Accept: true));
        Assert.Equal(new[] { "resume" }, fired);

        overlay.Update(Dt, new MenuInput(Down: true));
        overlay.Update(Dt, new MenuInput(Down: true));
        overlay.Update(Dt, new MenuInput(Accept: true));
        Assert.Equal(new[] { "resume", "menu" }, fired);
    }

    /// <summary>Left and Right reach the value row, which is how the arena is resized.</summary>
    [Fact]
    public void ValueRow_AdjustsOnLeftAndRight()
    {
        var steps = new List<int>();
        var overlay = Overlay(_ => { }, adjust: steps.Add);
        overlay.Open();

        overlay.Update(Dt, new MenuInput(Down: true));       // onto "Arena size"
        overlay.Update(Dt, new MenuInput(Right: true));
        overlay.Update(Dt, new MenuInput(Left: true));

        Assert.Equal(new[] { 1, -1 }, steps);
    }
}
