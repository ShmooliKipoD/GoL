using GoL.App.Screens;
using Microsoft.Xna.Framework;
using MonoGame.Extended.Screens;

namespace GoL.App;

/// <summary>
/// Desktop entry point. Owns the graphics device and the MonoGame.Extended
/// <see cref="ScreenManager"/>; all actual behaviour lives in the screens.
/// </summary>
public sealed class GolGame : Game
{
    private readonly GraphicsDeviceManager _graphics;
    private readonly ScreenManager _screens;

    public GolGame()
    {
        _graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = 1280,
            PreferredBackBufferHeight = 800,
        };

        Content.RootDirectory = "Content";
        IsMouseVisible = true;
        Window.AllowUserResizing = true;

        _screens = new ScreenManager();
        Components.Add(_screens);
    }

    protected override void Initialize()
    {
        // Set in Initialize, not the constructor: MonoGame creates the window
        // during base.Initialize() and stamps it with the assembly name, so a
        // title set earlier is silently overwritten.
        base.Initialize();
        Window.Title = "GoL";
    }

    protected override void LoadContent()
    {
        base.LoadContent();
        _screens.LoadScreen(new SmokeTestScreen(this));
    }

    /// <summary>Switches the active screen. Screens call this, not each other.</summary>
    public void ShowScreen(GameScreen screen) => _screens.LoadScreen(screen);
}
