using GoL.Render;
using GoL.Sim.Components;
using GoL.Sim.Core;
using GoL.Sim.Genetics;
using GoL.Sim.Systems;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MonoGame.Extended;
using MonoGame.Extended.Input;
using SimVector2 = System.Numerics.Vector2;
using XnaVector2 = Microsoft.Xna.Framework.Vector2;

namespace GoL.App.Screens;

/// <summary>
/// An arena you build in: one creature, bare ground, and every overlay.
/// <para>
/// It began as the Step 2 checkpoint - somewhere the genome, the senses and the
/// energy model could be <i>seen</i> working before an ecosystem was layered on top.
/// It now starts empty instead of pre-seeded, because watching a creature handle
/// food you placed where you placed it answers questions that a random scatter
/// cannot: the scatter is a sample of the board, and the board already exists.
/// </para>
/// </summary>
public sealed class SandboxScreen : GolScreen
{
    /// <summary>Margin around the arena, in pixels.</summary>
    private const float ArenaMargin = 28f;

    private const float MinZoom = 0.6f;
    private const float MaxZoom = 8f;

    /// <summary>
    /// The arena sides the pause menu offers.
    /// <para>
    /// Not the config screen's world-size row: that one steps 512..8192 for the
    /// board, and every value it can produce is far too large to see a creature's
    /// mouth arc in. These four are the sandbox's own ladder, each roughly double the
    /// last, so cycling changes the character of the space rather than nudging it.
    /// </para>
    /// </summary>
    private static readonly float[] ArenaSizes = { 240f, 340f, 680f, 1360f };

    private readonly CreatureRenderer _creatures = new();
    private readonly SenseOverlayRenderer _senses = new();
    private readonly InspectorRenderer _inspector = new();
    private readonly PauseOverlay _pause;

    /// <summary>Rebuilt whenever the arena is, so neither is readonly. Nothing outside
    /// this screen holds either one - the renderers take the world as an argument and
    /// cache nothing - which is what makes rebuilding in place safe.</summary>
    private SandboxEnvironment _env = null!;
    private SimWorld _world = null!;

    /// <summary>Index into <see cref="ArenaSizes"/>. Survives a rebuild; that is the
    /// point of it living on the screen rather than being passed to a fresh one.</summary>
    private int _sizeIndex = 1;

    /// <summary>Drives genomes. A field rather than a local so each respawn gets a new
    /// creature instead of the same doomed one over and over.</summary>
    private Pcg32 _rng;

    private int _subjectId;
    private OverlayFlags _overlays = OverlayFlags.Vision | OverlayFlags.Attributes | OverlayFlags.Actions;
    private bool _paused;
    private float _accumulator;

    /// <summary>Last frame's projection, so a click is tested against the picture the
    /// operator was actually looking at when they made it.</summary>
    private ArenaView _view;

    /// <summary>Zoom relative to fit-the-arena. Starts closer than that because the
    /// sandbox is for inspecting one creature's anatomy, not for surveying a field.</summary>
    private float _zoom = 3f;

    /// <summary>Keep the subject centred. Turned off to survey the whole arena.</summary>
    private bool _follow = true;

    public SandboxScreen(GolGame game) : base(game)
    {
        _pause = new PauseOverlay("Sandbox", new[]
        {
            new MenuEntry("Resume", ClosePause),
            new MenuEntry("Arena size",
                ValueText: () => $"{ArenaSizes[_sizeIndex]:F0}",
                OnAdjust: CycleSize),
            new MenuEntry("Back to Main Menu", () => Gol.ShowScreen(new MainMenuScreen(Gol))),
        });

        Rebuild();
    }

    /// <summary>A method rather than <c>() =&gt; _pause.Close()</c> inline above: the
    /// lambda would capture the field on the way to assigning it, which nullable flow
    /// analysis rightly cannot see through.</summary>
    private void ClosePause() => _pause.Close();

    /// <summary>
    /// Builds a fresh arena at the current size: bare ground, one creature centred.
    /// <para>
    /// The environment and the world are always replaced as a pair. They hold a
    /// back-reference to each other - <c>SimWorld</c>'s constructor hands the
    /// environment an index into its own entities - so one cannot be swapped under
    /// the other.
    /// </para>
    /// </summary>
    private void Rebuild()
    {
        _rng = new Pcg32((ulong)(Gol.Config.Seed == 0 ? 20260903 : Gol.Config.Seed));

        _env = new SandboxEnvironment(Gol.Config, ArenaSizes[_sizeIndex]);
        _world = new SimWorld(Gol.Config, _env);

        // No plants. The greens are the operator's to place, and seeding a few
        // hundred first would bury whatever arrangement they were trying to make.
        SpawnSubject();
    }

    /// <summary>
    /// Puts a new creature at the centre of the <b>existing</b> arena.
    /// <para>
    /// What happens when the subject starves, and the reason it is not a rebuild.
    /// With no food at start, starving is the ordinary outcome of walking away from
    /// the greens you laid out - and rebuilding would delete them, which is to say it
    /// would delete the experiment at the exact moment it produced a result.
    /// </para>
    /// <para>
    /// Safe outside <c>Step</c>: this is what construction does. Spawning during a
    /// tick would not be, which is why births go through a request on <c>Doing</c>.
    /// </para>
    /// </summary>
    private void SpawnSubject()
    {
        var genome = Genome.CreateSeed(ref _rng);

        // Dev affordance: start the subject with N attributes already unlocked, so
        // the trait system can be looked at without pressing U (macOS blocks
        // synthetic keystrokes, so screenshots cannot drive the UI).
        if (int.TryParse(Environment.GetEnvironmentVariable("GOL_SANDBOX_TRAITS"), out int preset))
        {
            for (int i = 0; i < preset; i++) Mutator.UnlockRandomTrait(genome, ref _rng);
        }

        float centre = _env.WorldSize * 0.5f;
        _subjectId = _world.Spawn(genome, new SimVector2(centre, centre), _rng.NextFloat(0f, MathF.Tau));
    }

    private void CycleSize(int direction)
    {
        _sizeIndex = (_sizeIndex + direction + ArenaSizes.Length) % ArenaSizes.Length;

        // A resize is a reset. Carrying the arrangement across would mean remapping
        // every plant into different bounds, and there is no honest answer for what
        // to do with the ones that fall outside the smaller square.
        Rebuild();
    }

    /// <summary>A live handle to the creature being inspected. Fetched rather than
    /// cached: the ECS owns the components, and a stale handle after a rebuild would
    /// draw the previous brain.</summary>
    private CreatureView Subject => _world.View(_subjectId);

    /// <summary>Pixels per world unit, chosen so the arena fills the height with a
    /// margin. Without this a creature is roughly ten pixels across and none of its
    /// anatomy is legible.</summary>
    private float ViewScale =>
        MathF.Max(0.1f, (ViewHeight - ArenaMargin * 2f) / _env.WorldSize) * _zoom;

    private ArenaView CurrentView()
    {
        float scale = ViewScale;
        return new ArenaView(scale, CentreOffset(scale));
    }

    public override void Update(GameTime gameTime)
    {
        float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
        var kb = KeyboardExtended.GetState();
        var mouse = MouseExtended.GetState();
        var input = InputMap.ReadMenu(kb);

        // Before the sim advances, so it matches the frame that was drawn - which is
        // the frame the operator aimed at. Recomputing it after the step would put a
        // click a few pixels behind a moving subject while the view follows it.
        _view = CurrentView();

        if (_pause.Update(dt, input)) return;
        if (input.Cancel) { _pause.Open(); return; }

        HandleOverlayKeys(kb);
        HandleMouse(kb, mouse);

        if (kb.WasKeyPressed(Keys.Space)) _paused = !_paused;
        if (kb.WasKeyPressed(Keys.R)) Rebuild();
        if (kb.WasKeyPressed(Keys.F)) _follow = !_follow;

        HandleZoom(kb, mouse);

        // A fixed timestep, accumulated from real time. The simulation never sees
        // wall-clock time - that is what keeps a seeded run reproducible.
        float step = Gol.Config.SecondsPerTick;

        if (_paused)
        {
            if (kb.WasKeyPressed(Keys.OemPeriod)) { _world.Step(); EnsureSubject(); }
            return;
        }

        // Real time accumulates here; the simulation only ever advances in whole
        // fixed steps, which is what keeps a seeded run reproducible.
        _accumulator += dt;
        int guard = 0;
        while (_accumulator >= step && guard++ < 8)
        {
            _world.Step();
            _accumulator -= step;
        }

        EnsureSubject();
    }

    /// <summary>
    /// Replaces the subject if the last step killed it.
    /// <para>
    /// Called after <b>every</b> step, single-stepping included, and that is not
    /// belt-and-braces: death destroys the entity outright, so a dead
    /// <c>_subjectId</c> is not a creature reading <c>Alive == false</c> - it is a
    /// handle to nothing, and the next <c>Draw</c> is the one that finds out. Stepping
    /// a paused creature into starvation used to reach exactly that.
    /// </para>
    /// </summary>
    private void EnsureSubject()
    {
        if (!_world.Living.Contains(_subjectId)) SpawnSubject();
    }

    /// <summary>
    /// The two click bindings. Both go through the sandbox rather than the renderer:
    /// <c>GoL.Render</c> computes the projection and never mutates what it is handed.
    /// </summary>
    private void HandleMouse(KeyboardStateExtended kb, MouseStateExtended mouse)
    {
        // WasButtonPressed returns bool? - hence "is not true" rather than a negation.
        if (mouse.WasButtonPressed(MouseButton.Left) is not true) return;

        var world = _view.ScreenToWorld(new XnaVector2(mouse.X, mouse.Y));
        var target = new SimVector2(world.X, world.Y);

        // Outside the arena, both clicks are refused rather than wrapped.
        //
        // A toroidal world would happily take the coordinate - Wrap folds it in and
        // Offset already goes the short way round - but the result reads as a bug:
        // click just past the right edge and the creature sets off to the LEFT, or a
        // green appears on the far side of the arena from where you pointed. The
        // rectangle on screen is the world, and nothing outside it is a place.
        if (target.X < 0f || target.Y < 0f
            || target.X > _env.WorldSize || target.Y > _env.WorldSize) return;

        // P is held rather than toggled: a placement mode would need its own indicator
        // and its own way of being left, and dropping greens is a thing you do in
        // bursts and then stop.
        if (kb.IsKeyDown(Keys.P))
        {
            _env.AddPlant(target);
            return;
        }

        // Consumed only by MoveAction, so it takes hold when Move is what is running -
        // pinned with O, or chosen by the brain on its own.
        var doing = _world.Get<Doing>(_subjectId);
        doing.Waypoint = target;
        doing.HasWaypoint = true;
    }

    private void HandleZoom(KeyboardStateExtended kb, MouseStateExtended mouse)
    {
        // Both the main row and the numpad, since laptop keyboards differ on which
        // one a bare "+" produces.
        if (kb.WasKeyPressed(Keys.OemPlus) || kb.WasKeyPressed(Keys.Add))
            _zoom = MathF.Min(MaxZoom, _zoom * 1.25f);

        if (kb.WasKeyPressed(Keys.OemMinus) || kb.WasKeyPressed(Keys.Subtract))
            _zoom = MathF.Max(MinZoom, _zoom / 1.25f);

        int wheel = mouse.DeltaScrollWheelValue;
        if (wheel != 0)
            _zoom = Math.Clamp(_zoom * MathF.Pow(1.12f, -wheel / 120f), MinZoom, MaxZoom);
    }

    private void HandleOverlayKeys(KeyboardStateExtended kb)
    {
        if (kb.WasKeyPressed(Keys.V)) _overlays = _overlays.Toggle(OverlayFlags.Vision);
        if (kb.WasKeyPressed(Keys.N)) _overlays = _overlays.Toggle(OverlayFlags.Smell);
        if (kb.WasKeyPressed(Keys.M)) _overlays = _overlays.Toggle(OverlayFlags.Mouth);
        if (kb.WasKeyPressed(Keys.B)) _overlays = _overlays.Toggle(OverlayFlags.Brain);
        if (kb.WasKeyPressed(Keys.A)) _overlays = _overlays.Toggle(OverlayFlags.Attributes);
        if (kb.WasKeyPressed(Keys.G)) _overlays = _overlays.Toggle(OverlayFlags.Pheromone);
        if (kb.WasKeyPressed(Keys.K)) _overlays = _overlays.Toggle(OverlayFlags.Actions);

        if (kb.WasKeyPressed(Keys.F1))
            _overlays = _overlays == OverlayFlags.All ? OverlayFlags.None : OverlayFlags.All;

        // Force a mutation, so the trait system is demonstrable without waiting for
        // a rare unlock to happen on its own.
        if (kb.WasKeyPressed(Keys.U)) UnlockOne();

        if (kb.WasKeyPressed(Keys.O)) CycleForcedAction();
    }

    /// <summary>
    /// Pins the subject to one action, or releases it back to its brain.
    /// <para>
    /// <b>A sandbox affordance and nothing else</b> - the board never forces an action.
    /// It exists because an unevolved brain simply never chooses the action you want
    /// to watch, which makes checking actions one at a time impossible otherwise.
    /// </para>
    /// <para>
    /// Forcing stands in for the brain's wanting, not for the creature's body: a
    /// forced action still has to pass its own <c>CanStart</c>, so forcing Rest on a
    /// creature without Torpor reads "blocked" rather than resting. Forced actions
    /// also do not fall through to something else, because watching a pinned action
    /// fail is the diagnostic this key is for.
    /// </para>
    /// </summary>
    private void CycleForcedAction()
    {
        var doing = _world.Get<Doing>(_subjectId);

        doing.Forced = doing.Forced is null
            ? Actions.All[0]
            : (int)doing.Forced.Value + 1 < Actions.Count
                ? (CreatureAction)((int)doing.Forced.Value + 1)
                : null;
    }

    private void UnlockOne()
    {
        var subject = Subject;
        ref var rng = ref _world.Get<RandomSource>(_subjectId).Rng;

        Mutator.UnlockRandomTrait(subject.Genome, ref rng);

        // The brain gained nodes, so the compiled form and the sensor map are stale.
        subject.Mind.Rebuild(subject.Genome);
    }

    public override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(Palette.Background);

        var view = CurrentView();

        Batch.Begin(samplerState: SamplerState.PointClamp, transformMatrix: view.Transform);

        DrawArena();
        if (_overlays.Has(OverlayFlags.Pheromone)) DrawScentField();
        DrawPlants();
        DrawWaypoint();

        var subject = Subject;
        _senses.Draw(Batch, subject, _world.Field, _overlays);
        _creatures.Draw(Batch, subject);

        Batch.End();

        BeginUi();
        DrawPanels();

        // Screen space, so it is projected by hand: the sandbox has no BoardCamera and
        // draws the world through the plain scale-and-offset transform above.
        if (_overlays.Has(OverlayFlags.Actions))
        {
            var head = view.WorldToScreen(new XnaVector2(subject.Position.X, subject.Position.Y));
            head.Y -= subject.Radius * view.Scale + 6f;

            InspectorRenderer.DrawActionLabel(Batch, Text, subject, head);
        }

        DrawHelp();
        _pause.Draw(Batch, Text, ViewWidth, ViewHeight);
        EndUi();
    }

    /// <summary>
    /// Translation that puts either the subject or the arena in the middle of the
    /// window. Rounded to whole pixels so the pixel font and thin overlay lines do
    /// not shimmer as the view moves.
    /// </summary>
    private XnaVector2 CentreOffset(float scale)
    {
        if (_follow)
        {
            return new XnaVector2(
                MathF.Round(ViewCenter.X - Subject.Position.X * scale),
                MathF.Round(ViewCenter.Y - Subject.Position.Y * scale));
        }

        return new XnaVector2(
            MathF.Round((ViewWidth - _env.WorldSize * scale) * 0.5f),
            MathF.Round((ViewHeight - _env.WorldSize * scale) * 0.5f));
    }

    private void DrawArena() =>
        Batch.DrawRectangle(new RectangleF(0f, 0f, _env.WorldSize, _env.WorldSize),
            Palette.InkDim * 0.35f, 1f);

    /// <summary>
    /// A ring where the subject was told to walk. Drawn whatever is running, not only
    /// during Move: an order that is queued behind a brain busy chewing still exists,
    /// and a marker that vanished would read as the click having been missed.
    /// </summary>
    private void DrawWaypoint()
    {
        var doing = _world.Get<Doing>(_subjectId);
        if (!doing.HasWaypoint) return;

        var at = new XnaVector2(doing.Waypoint.X, doing.Waypoint.Y);
        float radius = 4f / _zoom;

        Batch.DrawCircle(at, radius, 12, Palette.Warning, 1f / _zoom);
        Batch.DrawLine(at.X - radius, at.Y, at.X + radius, at.Y, Palette.Warning, 1f / _zoom);
        Batch.DrawLine(at.X, at.Y - radius, at.X, at.Y + radius, Palette.Warning, 1f / _zoom);
    }

    private void DrawPlants()
    {
        foreach (var plant in _env.Plants)
        {
            if (!plant.Alive) continue;

            // Radius tracks remaining energy, so a grazed plant visibly shrinks.
            float fullness = plant.Energy / plant.MaxEnergy;
            float radius = plant.Radius * (0.4f + 0.6f * fullness);

            Batch.DrawCircle(new XnaVector2(plant.Position.X, plant.Position.Y),
                radius, 10, Palette.Accent * (0.5f + 0.5f * fullness), radius);
        }
    }

    private void DrawScentField()
    {
        float cell = _env.ScentCellSize;

        for (int y = 0; y < _env.ScentResolution; y++)
        {
            for (int x = 0; x < _env.ScentResolution; x++)
            {
                float strength = _env.ScentAt(0, x, y);
                if (strength <= 0.01f) continue;

                Batch.FillRectangle(new RectangleF(x * cell, y * cell, cell, cell),
                    new Color(196, 150, 220) * (strength * 0.5f));
            }
        }
    }

    private void DrawPanels()
    {
        if (_overlays.Has(OverlayFlags.Attributes))
            _inspector.DrawAttributes(Batch, Text, Subject, new XnaVector2(12f, 12f));

        if (_overlays.Has(OverlayFlags.Brain))
        {
            float width = 380f;
            float height = 260f;
            _inspector.DrawBrain(Batch, Text, Subject,
                new RectangleF(ViewWidth - width - 12f, 12f, width, height));
        }

        if (_overlays.Has(OverlayFlags.Actions))
        {
            // Below the brain panel when both are up, so neither is hidden.
            float y = _overlays.Has(OverlayFlags.Brain) ? 284f : 12f;
            _inspector.DrawActions(Batch, Text, Subject, new XnaVector2(ViewWidth - 312f, y));
        }
    }

    private void DrawHelp()
    {
        string state = _paused ? "PAUSED" : "running";
        string line1 = "V vision   N smell   M mouth   B brain   A attributes   K actions   G scent   F1 all";
        var forced = _world.Get<Doing>(_subjectId).Forced;
        string driving = forced is null
            ? "O force action"
            : $"O forcing {Actions.Name(forced.Value).ToUpperInvariant()}";

        string line2 = $"click go here   P+click green   U new attribute   {driving}   "
            + $"Space pause ({state})   . step   +/- zoom   F follow   R reset   Esc menu";

        float lineHeight = Text.LineHeight(2f);
        float y = ViewHeight - lineHeight * 2 - 8f;

        // A backing strip: the arena's plants and rays run underneath the text and
        // pixel-font glyphs disappear against them without it.
        Batch.FillRectangle(
            new RectangleF(0f, y - 6f, ViewWidth, lineHeight * 2 + 14f),
            Palette.Background * 0.85f);

        Text.DrawCentered(line1, ViewCenter.X, y, Palette.InkDim, 2f);
        Text.DrawCentered(line2, ViewCenter.X, y + lineHeight, Palette.InkDim, 2f);
    }
}
