using Microsoft.Xna.Framework;

namespace GoL.Render;

/// <summary>
/// The sandbox's world-to-screen projection: a uniform scale and a translation.
/// <para>
/// The board projects through <see cref="BoardCamera"/>, but the sandbox has no
/// camera - it fits the whole arena to the window every frame and builds its
/// transform inline. That was fine while the projection only ever ran forwards, for
/// placing a label above a creature's head. The moment a click has to become a world
/// position it needs the inverse too, and a hand-inlined matrix has no inverse to
/// call.
/// </para>
/// <para>
/// A value rather than a camera on purpose. It holds no policy about what is centred
/// or how far it is zoomed - the screen still decides that and hands the result here
/// - so this stays pure arithmetic, testable with no graphics device at all.
/// </para>
/// </summary>
/// <param name="Scale">Pixels per world unit.</param>
/// <param name="Offset">Where world origin lands on screen, in pixels.</param>
public readonly record struct ArenaView(float Scale, Vector2 Offset)
{
    /// <summary>The transform to hand <c>SpriteBatch.Begin</c>.</summary>
    public Matrix Transform =>
        Matrix.CreateScale(Scale, Scale, 1f)
        * Matrix.CreateTranslation(Offset.X, Offset.Y, 0f);

    public Vector2 WorldToScreen(Vector2 world) => world * Scale + Offset;

    public Vector2 ScreenToWorld(Vector2 screen) => (screen - Offset) / Scale;
}
