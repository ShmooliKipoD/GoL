using System;
using Microsoft.Xna.Framework;
using MonoGame.Extended;

namespace GoL.Render;

/// <summary>
/// Pan and zoom over the board.
/// <para>
/// Hand-rolled rather than MonoGame.Extended's <c>OrthographicCamera</c>, for one
/// reason: this world <b>wraps</b>. Following a creature across the seam needs the
/// view to move continuously rather than jumping the width of the map, and that
/// means owning the focus arithmetic. The matrix it produces is an ordinary
/// <c>SpriteBatch</c> transform either way.
/// </para>
/// </summary>
public sealed class BoardCamera
{
    private const float MinZoom = 0.15f;
    private const float MaxZoom = 8f;

    private readonly float _worldSize;

    public BoardCamera(float worldSize, float viewportWidth, float viewportHeight)
    {
        _worldSize = worldSize;
        ViewportWidth = viewportWidth;
        ViewportHeight = viewportHeight;
        Focus = new Vector2(worldSize * 0.5f, worldSize * 0.5f);
    }

    public float ViewportWidth { get; set; }
    public float ViewportHeight { get; set; }

    /// <summary>World point held at the centre of the screen.</summary>
    public Vector2 Focus;

    public float Zoom { get; private set; } = 1f;

    public Matrix View =>
        Matrix.CreateTranslation(-Focus.X, -Focus.Y, 0f)
        * Matrix.CreateScale(Zoom, Zoom, 1f)
        * Matrix.CreateTranslation(ViewportWidth * 0.5f, ViewportHeight * 0.5f, 0f);

    /// <summary>The world rectangle currently visible, for culling.</summary>
    public RectangleF VisibleWorld
    {
        get
        {
            float w = ViewportWidth / Zoom;
            float h = ViewportHeight / Zoom;
            return new RectangleF(Focus.X - w * 0.5f, Focus.Y - h * 0.5f, w, h);
        }
    }

    public Vector2 ScreenToWorld(Vector2 screen) =>
        new(
            (screen.X - ViewportWidth * 0.5f) / Zoom + Focus.X,
            (screen.Y - ViewportHeight * 0.5f) / Zoom + Focus.Y);

    public Vector2 WorldToScreen(Vector2 world) =>
        new(
            (world.X - Focus.X) * Zoom + ViewportWidth * 0.5f,
            (world.Y - Focus.Y) * Zoom + ViewportHeight * 0.5f);

    public void Pan(Vector2 worldDelta) => Focus += worldDelta;

    /// <summary>
    /// Zooms about a screen point, keeping the world point under it fixed.
    /// <para>
    /// Zooming about the screen centre instead is the common shortcut and it feels
    /// broken: the thing you are pointing at slides away as you zoom toward it.
    /// </para>
    /// </summary>
    public void ZoomAt(Vector2 screenAnchor, float factor)
    {
        var before = ScreenToWorld(screenAnchor);
        Zoom = Math.Clamp(Zoom * factor, MinZoom, MaxZoom);
        var after = ScreenToWorld(screenAnchor);

        Focus += before - after;
    }

    /// <summary>
    /// Moves the focus toward a target the short way round a wrapping world.
    /// <para>
    /// Following a creature that crosses the seam would otherwise whip the camera
    /// across the entire map. Taking the shorter offset keeps the motion continuous.
    /// </para>
    /// </summary>
    public void FollowWrapped(Vector2 target, float smoothing)
    {
        var delta = target - Focus;

        float half = _worldSize * 0.5f;
        if (delta.X > half) delta.X -= _worldSize;
        else if (delta.X < -half) delta.X += _worldSize;
        if (delta.Y > half) delta.Y -= _worldSize;
        else if (delta.Y < -half) delta.Y += _worldSize;

        Focus += delta * Math.Clamp(smoothing, 0f, 1f);
        Focus = WrapFocus(Focus);
    }

    private Vector2 WrapFocus(Vector2 focus)
    {
        float x = focus.X % _worldSize;
        float y = focus.Y % _worldSize;
        if (x < 0f) x += _worldSize;
        if (y < 0f) y += _worldSize;
        return new Vector2(x, y);
    }
}
