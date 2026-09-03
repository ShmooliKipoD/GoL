using GoL.Render;

namespace GoL.Ui.Tests;

public class MenuListTests
{
    private const float Dt = 1f / 60f;

    private static MenuList ThreeItems(Action<string> log) => new(new[]
    {
        new MenuEntry("New Game", () => log("new")),
        new MenuEntry("Configuration", () => log("config")),
        new MenuEntry("Exit", () => log("exit")),
    });

    [Fact]
    public void Down_MovesSelection_AndWrapsPastTheEnd()
    {
        var menu = ThreeItems(_ => { });

        Assert.Equal(0, menu.SelectedIndex);
        menu.Update(Dt, new MenuInput(Down: true));
        Assert.Equal(1, menu.SelectedIndex);
        menu.Update(Dt, new MenuInput(Down: true));
        Assert.Equal(2, menu.SelectedIndex);

        // Wrapping is the behaviour the spec's menu inherits from QiLight.
        menu.Update(Dt, new MenuInput(Down: true));
        Assert.Equal(0, menu.SelectedIndex);
    }

    [Fact]
    public void Up_FromFirstItem_WrapsToLast()
    {
        var menu = ThreeItems(_ => { });
        menu.Update(Dt, new MenuInput(Up: true));
        Assert.Equal(2, menu.SelectedIndex);
    }

    [Fact]
    public void Accept_InvokesOnlyTheSelectedEntry()
    {
        var fired = new List<string>();
        var menu = ThreeItems(fired.Add);

        menu.Update(Dt, new MenuInput(Down: true));
        menu.Update(Dt, new MenuInput(Accept: true));

        Assert.Equal(new[] { "config" }, fired);
    }

    [Fact]
    public void Update_ReportsWhetherItConsumedTheInput()
    {
        var menu = ThreeItems(_ => { });

        // Consumed: the host screen must not also act on this keypress.
        Assert.True(menu.Update(Dt, new MenuInput(Down: true)));

        // Not consumed: Cancel is the host screen's business, and Left/Right mean
        // nothing on a row with no adjuster.
        Assert.False(menu.Update(Dt, new MenuInput(Cancel: true)));
        Assert.False(menu.Update(Dt, new MenuInput(Left: true)));
        Assert.False(menu.Update(Dt, MenuInput.None));
    }

    [Fact]
    public void AdjustableEntry_StepsOnLeftAndRight_AndDoesNotActivate()
    {
        int value = 5;
        bool activated = false;

        var menu = new MenuList(new[]
        {
            new MenuEntry("Speed",
                OnActivate: () => activated = true,
                ValueText: () => value.ToString(),
                OnAdjust: d => value += d),
        });

        menu.Update(Dt, new MenuInput(Right: true));
        Assert.Equal(6, value);

        menu.Update(Dt, new MenuInput(Left: true));
        menu.Update(Dt, new MenuInput(Left: true));
        Assert.Equal(4, value);

        Assert.False(activated);
    }

    [Fact]
    public void SelectLast_ArmsTheFinalEntry()
    {
        var menu = ThreeItems(_ => { });
        menu.SelectLast();
        Assert.Equal(menu.Count - 1, menu.SelectedIndex);
    }

    [Fact]
    public void EmptyMenu_IsRejected()
        => Assert.Throws<ArgumentException>(() => new MenuList(Array.Empty<MenuEntry>()));
}
