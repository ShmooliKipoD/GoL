using GoL.Render;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoGame.Extended.Screens;

namespace GoL.App.Screens;

/// <summary>
/// Shared plumbing for every GoL screen: the sprite batch, the UI font wrapped in
/// a <see cref="TextRenderer"/>, and typed access back to the game for switching
/// screens. Screens hold their own state and are unloaded when swapped away.
/// </summary>
public abstract class GolScreen : GameScreen
{
    protected GolScreen(GolGame game) : base(game) => Gol = game;

    protected GolGame Gol { get; }
    protected SpriteBatch Batch { get; private set; } = null!;
    protected TextRenderer Text { get; private set; } = null!;

    protected float ViewWidth => GraphicsDevice.Viewport.Width;
    protected float ViewHeight => GraphicsDevice.Viewport.Height;
    protected Vector2 ViewCenter => new(ViewWidth / 2f, ViewHeight / 2f);

    public override void LoadContent()
    {
        base.LoadContent();
        Batch = new SpriteBatch(GraphicsDevice);
        Text = new TextRenderer(Batch, Content.Load<SpriteFont>("Fonts/UiFont"));
    }

    /// <summary>Opens a sprite batch configured for crisp pixel text.
    /// PointClamp is one of the three things monogram needs; the other two -
    /// integer scales and whole-pixel positions - are handled by TextRenderer.</summary>
    protected void BeginUi() => Batch.Begin(samplerState: SamplerState.PointClamp);

    protected void EndUi() => Batch.End();

    public override void UnloadContent()
    {
        Batch?.Dispose();
        base.UnloadContent();
    }
}
