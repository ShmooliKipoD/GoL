using GoL.Render;
using GoL.Sim.Board;
using GoL.Sim.Components;
using GoL.Sim.Core;
using GoL.Sim.Genetics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MonoGame.Extended;
using MonoGame.Extended.Input;
using GoL.Sim.Systems;
using SimVector2 = System.Numerics.Vector2;
using XnaVector2 = Microsoft.Xna.Framework.Vector2;

namespace GoL.App.Screens;

/// <summary>
/// The world: a populated board with a pan-and-zoom camera. The Step 3 checkpoint,
/// and what "New Game" opens.
/// </summary>
public sealed class BoardScreen : GolScreen
{
    private const float PanSpeed = 400f;

    private readonly BoardEnvironment _board;
    private readonly SimWorld _world;
    private readonly BoardRenderer _boardRenderer = new();
    private readonly CreatureRenderer _creatures = new();
    private readonly SenseOverlayRenderer _senses = new();
    private readonly InspectorRenderer _inspector = new();

    private BoardCamera _camera = null!;
    /// <summary>
    /// Overlays start off; GOL_OVERLAYS=all turns them all on at launch. macOS
    /// blocks synthetic keystrokes, so without this the overlay draw paths cannot
    /// be exercised without a human pressing keys.
    /// </summary>
    private OverlayFlags _overlays =
        string.Equals(Environment.GetEnvironmentVariable("GOL_OVERLAYS"), "all",
            StringComparison.OrdinalIgnoreCase)
            ? OverlayFlags.All
            : OverlayFlags.None;

    /// <summary>Keep the inspector pinned to <i>something</i> living. Set only by
    /// GOL_OVERLAYS, so the panels can be exercised without a human clicking - and
    /// deliberately not on during normal play, where silently jumping to a new
    /// creature when yours dies would be surprising.</summary>
    private readonly bool _autoSelect =
        string.Equals(Environment.GetEnvironmentVariable("GOL_OVERLAYS"), "all",
            StringComparison.OrdinalIgnoreCase);

    private int _selectedId = -1;
    private bool _follow;
    private bool _paused;
    private float _accumulator;
    private int _speed = 1;

    public BoardScreen(GolGame game) : base(game)
    {
        _board = new BoardEnvironment(Gol.Config);
        _world = new SimWorld(Gol.Config, _board);
        Populate();
    }

    private void Populate()
    {
        // A stream of its own, so changing how the world is populated cannot shift
        // what any creature later draws.
        var rng = new Pcg32((ulong)(Gol.Config.Seed == 0 ? 20260903 : Gol.Config.Seed), StreamId.World);

        for (int i = 0; i < Gol.Config.InitialPopulation; i++)
        {
            _world.Spawn(
                Genome.CreateSeed(ref rng),
                new SimVector2(
                    rng.NextFloat(0f, _board.WorldSize),
                    rng.NextFloat(0f, _board.WorldSize)),
                rng.NextFloat(0f, MathF.Tau));
        }
    }

    public override void LoadContent()
    {
        base.LoadContent();
        _camera = new BoardCamera(_board.WorldSize, ViewWidth, ViewHeight);
    }

    public override void Update(GameTime gameTime)
    {
        float frameTime = (float)gameTime.ElapsedGameTime.TotalSeconds;

        var kb = KeyboardExtended.GetState();
        var mouse = MouseExtended.GetState();
        var input = InputMap.ReadMenu(kb);

        if (input.Cancel) { Gol.ShowScreen(new MainMenuScreen(Gol)); return; }

        _camera.ViewportWidth = ViewWidth;
        _camera.ViewportHeight = ViewHeight;

        HandleOverlayKeys(kb);
        HandleSpeedKeys(kb);
        HandleCamera(kb, mouse, frameTime);
        HandleSelection(mouse);

        StepSimulation(frameTime);

        // Has to wait for the first step: Living is published by LifecycleSystem,
        // so it is still empty in the constructor - which is why the equivalent
        // check there never selected anything. Re-pins when the subject dies, or
        // the panels would disappear for good the first time one starved.
        if (_autoSelect && (_selectedId < 0 || !IsAlive(_selectedId)) && _world.Living.Count > 0)
            _selectedId = _world.Living[0];

        if (_follow && _selectedId >= 0 && IsAlive(_selectedId))
            _camera.FollowWrapped(ToXna(_world.Get<Body>(_selectedId).Position), 0.12f);
    }

    /// <summary>
    /// Advances whole fixed steps only. Real time accumulates here; the simulation
    /// never sees a clock, which is what keeps a seeded run reproducible.
    /// </summary>
    private void StepSimulation(float frameTime)
    {
        float dt = Gol.Config.SecondsPerTick;

        if (_paused) return;

        _accumulator += frameTime;

        // Bounded, so a slow frame cannot spiral into an ever-growing catch-up debt.
        int budget = 8 * _speed;
        while (_accumulator >= dt && budget-- > 0)
        {
            for (int i = 0; i < _speed && budget-- > 0; i++) _world.Step();
            _accumulator -= dt;
        }
    }

    private void HandleOverlayKeys(KeyboardStateExtended kb)
    {
        if (kb.WasKeyPressed(Keys.V)) _overlays = _overlays.Toggle(OverlayFlags.Vision);
        if (kb.WasKeyPressed(Keys.N)) _overlays = _overlays.Toggle(OverlayFlags.Smell);
        if (kb.WasKeyPressed(Keys.M)) _overlays = _overlays.Toggle(OverlayFlags.Mouth);
        if (kb.WasKeyPressed(Keys.B)) _overlays = _overlays.Toggle(OverlayFlags.Brain);
        if (kb.WasKeyPressed(Keys.A)) _overlays = _overlays.Toggle(OverlayFlags.Attributes);
        if (kb.WasKeyPressed(Keys.G)) _overlays = _overlays.Toggle(OverlayFlags.Fertility);
        if (kb.WasKeyPressed(Keys.H)) _overlays = _overlays.Toggle(OverlayFlags.Pheromone);
        if (kb.WasKeyPressed(Keys.K)) _overlays = _overlays.Toggle(OverlayFlags.Actions);

        if (kb.WasKeyPressed(Keys.F1))
            _overlays = _overlays == OverlayFlags.All ? OverlayFlags.None : OverlayFlags.All;

        if (kb.WasKeyPressed(Keys.F)) _follow = !_follow;
    }

    private void HandleSpeedKeys(KeyboardStateExtended kb)
    {
        if (kb.WasKeyPressed(Keys.Space)) _paused = !_paused;
        if (kb.WasKeyPressed(Keys.OemPeriod) && _paused) _world.Step();

        if (kb.WasKeyPressed(Keys.D1)) _speed = 1;
        if (kb.WasKeyPressed(Keys.D2)) _speed = 4;
        if (kb.WasKeyPressed(Keys.D3)) _speed = 16;
    }

    private void HandleCamera(KeyboardStateExtended kb, MouseStateExtended mouse, float dt)
    {
        var pan = XnaVector2.Zero;
        if (kb.IsKeyDown(Keys.Left) || kb.IsKeyDown(Keys.A)) pan.X -= 1f;
        if (kb.IsKeyDown(Keys.Right) || kb.IsKeyDown(Keys.D)) pan.X += 1f;
        if (kb.IsKeyDown(Keys.Up) || kb.IsKeyDown(Keys.W)) pan.Y -= 1f;
        if (kb.IsKeyDown(Keys.Down) || kb.IsKeyDown(Keys.S)) pan.Y += 1f;

        if (pan != XnaVector2.Zero)
        {
            _follow = false;
            // Divided by zoom so a pan covers the same amount of SCREEN per second
            // however far in you are - panning at high zoom otherwise crawls.
            _camera.Pan(pan * (PanSpeed * dt / _camera.Zoom));
        }

        if (mouse.MiddleButton == ButtonState.Pressed)
        {
            var drag = new XnaVector2(mouse.DeltaPosition.X, mouse.DeltaPosition.Y);
            if (drag != XnaVector2.Zero)
            {
                _follow = false;
                _camera.Pan(-drag / _camera.Zoom);
            }
        }

        int wheel = mouse.DeltaScrollWheelValue;
        if (wheel != 0)
        {
            // Zoom about the cursor, not the screen centre.
            var anchor = new XnaVector2(mouse.X, mouse.Y);
            _camera.ZoomAt(anchor, MathF.Pow(1.15f, -wheel / 120f));
        }

        if (kb.WasKeyPressed(Keys.OemPlus) || kb.WasKeyPressed(Keys.Add))
            _camera.ZoomAt(ViewCenter, 1.25f);
        if (kb.WasKeyPressed(Keys.OemMinus) || kb.WasKeyPressed(Keys.Subtract))
            _camera.ZoomAt(ViewCenter, 1f / 1.25f);
    }

    /// <summary>Click a creature to pin the inspector overlays to it.</summary>
    private void HandleSelection(MouseStateExtended mouse)
    {
        if (mouse.WasButtonPressed(MouseButton.Left) is not true) return;

        var world = _camera.ScreenToWorld(new XnaVector2(mouse.X, mouse.Y));
        var target = new SimVector2(world.X, world.Y);

        int best = -1;
        float bestDistance = float.MaxValue;

        foreach (int id in _world.Living)
        {
            var body = _world.Get<Body>(id);
            float distance = _board.Offset(target, body.Position).Length();

            // A generous pick radius, so a small creature at low zoom is still
            // clickable without pixel-hunting.
            float pick = MathF.Max(body.Radius, 12f / _camera.Zoom);
            if (distance > pick || distance >= bestDistance) continue;

            bestDistance = distance;
            best = id;
        }

        _selectedId = best;
        if (best >= 0) _overlays |= OverlayFlags.Attributes | OverlayFlags.Actions;
    }

    private bool IsAlive(int id) => _world.Living.Contains(id);

    public override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(Palette.Background);

        var view = _camera.VisibleWorld;

        Batch.Begin(samplerState: SamplerState.PointClamp, transformMatrix: _camera.View);

        if (_overlays.Has(OverlayFlags.Fertility)) _boardRenderer.DrawFertility(Batch, _board, view);
        if (_overlays.Has(OverlayFlags.Pheromone)) _boardRenderer.DrawPheromones(Batch, _board, view);

        _boardRenderer.DrawPlants(Batch, _board, view);
        DrawCreatures(view);

        Batch.End();

        BeginUi();
        DrawHud();
        DrawPanels();
        EndUi();
    }

    private void DrawCreatures(RectangleF view)
    {
        // Culled generously and computed once: a creature just off screen may still
        // have overlay geometry - a vision cone - reaching into the view.
        var cull = new RectangleF(
            view.X - 160f, view.Y - 160f, view.Width + 320f, view.Height + 320f);

        foreach (int id in _world.Living)
        {
            var creature = _world.View(id);
            var position = ToXna(creature.Position);

            if (!cull.Contains(position)) continue;

            if (id == _selectedId)
                _senses.Draw(Batch, creature, _board, _overlays);

            _creatures.Draw(Batch, creature);
        }

        if (_selectedId >= 0 && IsAlive(_selectedId)) DrawSelectionRing();
    }

    private void DrawSelectionRing()
    {
        var body = _world.Get<Body>(_selectedId);
        Batch.DrawCircle(ToXna(body.Position), body.Radius + 4f, 16, Palette.Accent, 1.5f);
    }

    private void DrawPanels()
    {
        if (_selectedId < 0 || !IsAlive(_selectedId)) return;

        var creature = _world.View(_selectedId);

        if (_overlays.Has(OverlayFlags.Attributes))
            _inspector.DrawAttributes(Batch, Text, creature, new XnaVector2(12f, 12f));

        if (_overlays.Has(OverlayFlags.Brain))
        {
            const float width = 380f;
            const float height = 260f;
            _inspector.DrawBrain(Batch, Text, creature,
                new RectangleF(ViewWidth - width - 12f, 12f, width, height));
        }

        if (_overlays.Has(OverlayFlags.Actions))
        {
            // Below the brain panel when both are up, so neither is hidden.
            float y = _overlays.Has(OverlayFlags.Brain) ? 284f : 12f;
            _inspector.DrawActions(Batch, Text, creature, new XnaVector2(ViewWidth - 312f, y));

            // Screen space: the label must not scale with zoom, or it is unreadable
            // at both ends of the range. The camera does the projection here; the
            // sandbox, which has no camera, projects by hand.
            var head = _camera.WorldToScreen(ToXna(creature.Position));
            head.Y -= creature.Radius * _camera.Zoom + 6f;

            InspectorRenderer.DrawActionLabel(Batch, Text, creature, head);
        }
    }

    private void DrawHud()
    {
        float lineHeight = Text.LineHeight(2f);

        string speed = _paused ? "PAUSED" : $"{_speed}x";
        string stats = $"tick {_world.Tick}   pop {_world.Population}   "
                     + $"gen {_world.MaxGeneration}   zoom {_camera.Zoom:0.00}   {speed}";

        // The payoff of the whole design belongs on screen, not in a log.
        string unlocks = _world.UnlockTimeline.Count == 0
            ? "attributes: none yet"
            : "attributes: " + string.Join("  ",
                _world.UnlockTimeline.OrderBy(kv => kv.Value)
                    .Select(kv => $"{LatentTraitCatalog.Get(kv.Key).Name} @{kv.Value:0}s"));

        Batch.FillRectangle(
            new RectangleF(0f, ViewHeight - lineHeight * 3f - 14f, ViewWidth, lineHeight * 3f + 14f),
            Palette.Background * 0.85f);

        float y = ViewHeight - lineHeight * 3f - 8f;
        Text.DrawCentered(stats, ViewCenter.X, y, Palette.Ink, 2f);
        Text.DrawCentered(unlocks, ViewCenter.X, y + lineHeight, Palette.Warning, 2f);
        Text.DrawCentered(
            "click select   V N M B A overlays   K actions   G soil   H scent   1/2/3 speed   F follow   Esc menu",
            ViewCenter.X, y + lineHeight * 2f, Palette.InkDim, 2f);
    }

    private static XnaVector2 ToXna(SimVector2 v) => new(v.X, v.Y);
}
