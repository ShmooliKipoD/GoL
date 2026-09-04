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
/// One creature, a handful of plants, and every overlay - the Step 2 checkpoint.
/// <para>
/// The lab exists so the genome, the senses and the energy model can be <i>seen</i>
/// working before an entire ecosystem is layered on top. It runs on
/// <see cref="LabWorld"/>, whose sensing is deliberately throwaway and gets
/// replaced in Step 3.
/// </para>
/// </summary>
public sealed class CreatureLabScreen : GolScreen
{
    private const int PlantCount = 14;

    /// <summary>Margin around the arena, in pixels.</summary>
    private const float ArenaMargin = 28f;

    private const float MinZoom = 0.6f;
    private const float MaxZoom = 8f;

    private readonly LabEnvironment _env;
    private readonly SimWorld _world;
    private readonly CreatureRenderer _creatures = new();
    private readonly SenseOverlayRenderer _senses = new();
    private readonly InspectorRenderer _inspector = new();

    private int _subjectId;
    private OverlayFlags _overlays = OverlayFlags.Vision | OverlayFlags.Attributes | OverlayFlags.Actions;
    private bool _paused;
    private float _accumulator;

    /// <summary>Zoom relative to fit-the-arena. Starts closer than that because the
    /// lab is for inspecting one creature's anatomy, not for surveying a field.</summary>
    private float _zoom = 3f;

    /// <summary>Keep the subject centred. Turned off to survey the whole arena.</summary>
    private bool _follow = true;

    public CreatureLabScreen(GolGame game) : base(game)
    {
        _env = new LabEnvironment(Gol.Config);
        _world = new SimWorld(Gol.Config, _env);
        Populate();
    }

    private void Populate()
    {
        var rng = new Pcg32((ulong)(Gol.Config.Seed == 0 ? 20260903 : Gol.Config.Seed));

        _env.SeedPlants(PlantCount);

        var genome = Genome.CreateSeed(ref rng);

        // Dev affordance: start the subject with N attributes already unlocked, so
        // the trait system can be looked at without pressing U (macOS blocks
        // synthetic keystrokes, so screenshots cannot drive the UI).
        if (int.TryParse(Environment.GetEnvironmentVariable("GOL_LAB_TRAITS"), out int preset))
        {
            for (int i = 0; i < preset; i++) Mutator.UnlockRandomTrait(genome, ref rng);
        }

        float centre = _env.WorldSize * 0.5f;
        _subjectId = _world.Spawn(genome, new SimVector2(centre, centre), rng.NextFloat(0f, MathF.Tau));
    }

    /// <summary>Pixels per world unit, chosen so the arena fills the height with a
    /// margin. Without this a creature is roughly ten pixels across and none of its
    /// anatomy is legible.</summary>
    /// <summary>A live handle to the creature being inspected. Fetched rather than
    /// cached: the ECS owns the components, and a stale handle after a rebuild would
    /// draw the previous brain.</summary>
    private CreatureView Subject => _world.View(_subjectId);

    private float ViewScale =>
        MathF.Max(0.1f, (ViewHeight - ArenaMargin * 2f) / _env.WorldSize) * _zoom;

    public override void Update(GameTime gameTime)
    {
        var kb = KeyboardExtended.GetState();
        var input = InputMap.ReadMenu(kb);

        if (input.Cancel) { Gol.ShowScreen(new MainMenuScreen(Gol)); return; }

        HandleOverlayKeys(kb);

        if (kb.WasKeyPressed(Keys.Space)) _paused = !_paused;
        if (kb.WasKeyPressed(Keys.R)) Reset();
        if (kb.WasKeyPressed(Keys.F)) _follow = !_follow;

        HandleZoom(kb);

        // A fixed timestep, accumulated from real time. The simulation never sees
        // wall-clock time - that is what keeps a seeded run reproducible.
        float dt = Gol.Config.SecondsPerTick;
        bool step = kb.WasKeyPressed(Keys.OemPeriod);

        if (_paused)
        {
            if (step) _world.Step();
            return;
        }

        // Real time accumulates here; the simulation only ever advances in whole
        // fixed steps, which is what keeps a seeded run reproducible.
        _accumulator += (float)gameTime.ElapsedGameTime.TotalSeconds;
        int guard = 0;
        while (_accumulator >= dt && guard++ < 8)
        {
            _world.Step();
            _accumulator -= dt;
        }

        if (!Subject.Alive) Reset();
    }

    private void HandleZoom(KeyboardStateExtended kb)
    {
        // Both the main row and the numpad, since laptop keyboards differ on which
        // one a bare "+" produces.
        if (kb.WasKeyPressed(Keys.OemPlus) || kb.WasKeyPressed(Keys.Add))
            _zoom = MathF.Min(MaxZoom, _zoom * 1.25f);

        if (kb.WasKeyPressed(Keys.OemMinus) || kb.WasKeyPressed(Keys.Subtract))
            _zoom = MathF.Max(MinZoom, _zoom / 1.25f);

        int wheel = MouseExtended.GetState().DeltaScrollWheelValue;
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
    }

    private void UnlockOne()
    {
        var subject = Subject;
        ref var rng = ref _world.Get<RandomSource>(_subjectId).Rng;

        Mutator.UnlockRandomTrait(subject.Genome, ref rng);

        // The brain gained nodes, so the compiled form and the sensor map are stale.
        subject.Mind.Rebuild(subject.Genome);
    }

    private void Reset() => Gol.ShowScreen(new CreatureLabScreen(Gol));

    public override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(Palette.Background);

        float scale = ViewScale;
        var offset = CentreOffset(scale);

        Batch.Begin(samplerState: SamplerState.PointClamp, transformMatrix:
            Matrix.CreateScale(scale, scale, 1f)
            * Matrix.CreateTranslation(offset.X, offset.Y, 0f));

        DrawArena();
        if (_overlays.Has(OverlayFlags.Pheromone)) DrawScentField();
        DrawPlants();

        var subject = Subject;
        _senses.Draw(Batch, subject, _world.Field, _overlays);
        _creatures.Draw(Batch, subject);

        Batch.End();

        BeginUi();
        DrawPanels();

        // Screen space, so it is projected by hand: the lab has no BoardCamera and
        // draws the world through the plain scale-and-offset matrix above.
        if (_overlays.Has(OverlayFlags.Actions))
        {
            var head = new XnaVector2(
                subject.Position.X * scale + offset.X,
                subject.Position.Y * scale + offset.Y - subject.Radius * scale - 6f);

            InspectorRenderer.DrawActionLabel(Batch, Text, subject, head);
        }

        DrawHelp();
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
        string line2 = $"U new attribute   Space pause ({state})   . step   +/- zoom   F follow   R reset   Esc menu";

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
