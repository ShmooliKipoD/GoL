using GoL.App.Screens;
using GoL.Sim;
using Microsoft.Xna.Framework;
using MonoGame.Extended.Input;
using MonoGame.Extended.Screens;

namespace GoL.App;

/// <summary>
/// Desktop entry point. Owns the graphics device, the MonoGame.Extended
/// <see cref="ScreenManager"/> and the live configuration; all behaviour lives
/// in the screens.
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

    /// <summary>
    /// The live settings. Screens edit this; a run reads it once at world
    /// construction so the simulation never sees it change underneath.
    /// </summary>
    public SimConfig Config { get; set; } = new();

    /// <summary>Where settings are persisted. One instance for the process.</summary>
    public SimConfigStore ConfigStore { get; } = new();

    protected override void Initialize()
    {
        // Title is set AFTER base.Initialize(): MonoGame creates the window during
        // that call and stamps it with the assembly name, so setting it in the
        // constructor is silently overwritten.
        base.Initialize();
        Window.Title = "GoL";
    }

    protected override void LoadContent()
    {
        base.LoadContent();
        Config = ConfigStore.Load();
        _screens.LoadScreen(StartScreen());
    }

    /// <summary>
    /// Normally the main menu. Set GOL_SCREEN to jump straight somewhere during
    /// development - macOS blocks synthetic keystrokes, so clicking through the
    /// menus to reach a screen cannot be scripted, and this is how a screen gets
    /// looked at directly. Unknown values fall back to the menu.
    /// </summary>
    private GameScreen StartScreen() =>
        Environment.GetEnvironmentVariable("GOL_SCREEN")?.Trim().ToLowerInvariant() switch
        {
            "config" => new ConfigScreen(this),
            "lab" => new CreatureLabScreen(this),
            "board" => new BoardScreen(this),
            _ => new MainMenuScreen(this),
        };

    protected override void Update(GameTime gameTime)
    {
        // KeyboardExtended and MouseExtended hold static previous/current state, so
        // WasKeyPressed only works if these are called EXACTLY once per frame.
        // Never call them and edges never fire (menus look dead); call them twice
        // and edges are lost intermittently (menus look like they skip keys).
        // They must run before base.Update(), which is what ticks the ScreenManager
        // component and therefore the screens.
        KeyboardExtended.Update();
        MouseExtended.Update();

        base.Update(gameTime);
    }

    /// <summary>
    /// Switches the active screen. Note this UNLOADS the outgoing screen, so
    /// anything that must preserve the state beneath it - the exit prompt, the
    /// pause menu - is an overlay drawn by its host screen, not a screen.
    /// </summary>
    public void ShowScreen(GameScreen screen) => _screens.LoadScreen(screen);
}
