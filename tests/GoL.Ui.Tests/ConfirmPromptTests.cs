using GoL.Render;

namespace GoL.Ui.Tests;

/// <summary>
/// The spec asks for a y/n prompt on exit where Esc also exits. That differs from
/// QiLight's three-second double-press confirm, so these tests pin the intended
/// behaviour rather than the inherited one.
/// </summary>
public class ConfirmPromptTests
{
    [Fact]
    public void ClosedPrompt_ConsumesNothing_AndNeverFires()
    {
        bool fired = false;
        var prompt = new ConfirmPrompt("Exit?", () => fired = true);

        Assert.False(prompt.IsOpen);
        Assert.False(prompt.Update(new MenuInput(Yes: true)));
        Assert.False(fired);
    }

    [Fact]
    public void Yes_Confirms_AndCloses()
    {
        bool fired = false;
        var prompt = new ConfirmPrompt("Exit?", () => fired = true);
        prompt.Open();

        Assert.True(prompt.Update(new MenuInput(Yes: true)));
        Assert.True(fired);
        Assert.False(prompt.IsOpen);
    }

    [Fact]
    public void Escape_AlsoConfirms()
    {
        bool fired = false;
        var prompt = new ConfirmPrompt("Exit?", () => fired = true);
        prompt.Open();

        prompt.Update(new MenuInput(Cancel: true));

        Assert.True(fired);
        Assert.False(prompt.IsOpen);
    }

    [Fact]
    public void No_Cancels_WithoutFiring()
    {
        bool fired = false;
        var prompt = new ConfirmPrompt("Exit?", () => fired = true);
        prompt.Open();

        Assert.True(prompt.Update(new MenuInput(No: true)));
        Assert.False(fired);
        Assert.False(prompt.IsOpen);
    }

    [Fact]
    public void OpenPrompt_IsModal_AndSwallowsUnrelatedInput()
    {
        bool fired = false;
        var prompt = new ConfirmPrompt("Exit?", () => fired = true);
        prompt.Open();

        // Navigation must not leak through to the menu underneath.
        Assert.True(prompt.Update(new MenuInput(Down: true)));
        Assert.True(prompt.Update(new MenuInput(Accept: true)));

        Assert.False(fired);
        Assert.True(prompt.IsOpen);
    }
}
