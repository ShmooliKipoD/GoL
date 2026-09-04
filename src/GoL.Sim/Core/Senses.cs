using System;
using System.Numerics;
using GoL.Sim.Components;
using GoL.Sim.Genetics;

namespace GoL.Sim.Core;

/// <summary>
/// Fills a creature's sensor vector from the world. Read-only with respect to the
/// world: sensing happens for every creature before any of them act, so all brains
/// perceive the same frozen state.
/// </summary>
public sealed class Senses
{
    // Scratch, owned for the object's life. Sensing runs for every creature every
    // tick, so nothing here may allocate.
    private Percept[] _percepts = new Percept[256];

    /// <summary>
    /// Writes <paramref name="creature"/>'s sensor values into
    /// <paramref name="destination"/>, indexed by the brain's dense sensor order.
    /// </summary>
    public void Sample(
        in SenseSubject creature,
        ISenseField field,
        float simTime,
        Span<float> destination)
    {
        destination.Clear();

        var genome = creature.Genome;
        var layout = creature.Mind.Layout;

        UpdateEyes(creature, field);

        // --- Innate senses ---
        Write(destination, layout, NodeIds.InnateSlot, (int)InnateSense.Energy,
            creature.Energy.Fraction);

        Write(destination, layout, NodeIds.InnateSlot, (int)InnateSense.Age,
            Math.Clamp(creature.Vitals.Age / Metabolism.MaxAge, 0f, 1f));

        float maxSpeed = genome.Trait(TraitAxis.MaxSpeed);
        Write(destination, layout, NodeIds.InnateSlot, (int)InnateSense.Speed,
            maxSpeed > 0f ? Math.Clamp(creature.Body.Speed / maxSpeed, -1f, 1f) : 0f);

        float turnRate = genome.Trait(TraitAxis.TurnRate);
        Write(destination, layout, NodeIds.InnateSlot, (int)InnateSense.Turn,
            turnRate > 0f ? Math.Clamp(creature.Body.AngularVelocity / turnRate, -1f, 1f) : 0f);

        Write(destination, layout, NodeIds.InnateSlot, (int)InnateSense.Fertility,
            field.SampleFertility(creature.Position));

        Write(destination, layout, NodeIds.InnateSlot, (int)InnateSense.Scent0,
            field.SampleScent(creature.NosePosition, 0));

        // A free rhythm source. Without it, evolving any gait at all first requires
        // evolving a recurrent oscillator from scratch - a steep first step that
        // most lineages never take.
        Write(destination, layout, NodeIds.InnateSlot, (int)InnateSense.Oscillator,
            MathF.Sin(simTime * (2f * MathF.PI / 1.5f)));

        SampleMouth(creature, field, layout, destination);

        // --- Vision, forward eye ---
        WriteEye(destination, layout, NodeIds.VisionSlot, creature.Sight.Forward, genome);

        // --- Latent senses ---
        if (genome.Has(LatentTraitId.RearEye))
        {
            WriteEye(destination, layout,
                LatentTraitCatalog.Get(LatentTraitId.RearEye).Slot, creature.Sight.Rear!, genome);
        }

        if (genome.Has(LatentTraitId.ColorVision))
        {
            int slot = LatentTraitCatalog.Get(LatentTraitId.ColorVision).Slot;
            var eye = creature.Sight.Forward;
            for (int bin = 0; bin < eye.BinCount; bin++)
                Write(destination, layout, slot, bin, eye.Hue[bin]);
        }

        if (genome.Has(LatentTraitId.SecondNostril))
        {
            Write(destination, layout,
                LatentTraitCatalog.Get(LatentTraitId.SecondNostril).Slot, 0,
                field.SampleScent(creature.NosePosition, 1));
        }

        if (genome.Has(LatentTraitId.ScentGradient))
            SampleScentGradient(creature, field, layout, destination);

        if (genome.Has(LatentTraitId.ToxinResistance))
        {
            Write(destination, layout,
                LatentTraitCatalog.Get(LatentTraitId.ToxinResistance).Slot, 0,
                Math.Clamp(creature.Vitals.ToxinLoad / 50f, 0f, 1f));
        }
    }

    /// <summary>Recomputes what each eye can see. Also what the V overlay draws.</summary>
    private void UpdateEyes(in SenseSubject creature, ISenseField field)
    {
        var genome = creature.Genome;

        var forward = creature.Sight.Forward;
        forward.HeadingOffset = 0f;
        forward.Range = genome.Trait(TraitAxis.EyeRange);
        forward.HalfFov = genome.Trait(TraitAxis.EyeHalfFov);
        forward.Clear();

        Eye? rear = null;
        if (genome.Has(LatentTraitId.RearEye))
        {
            rear = creature.Sight.EnsureRear();
            rear.HeadingOffset = MathF.PI;
            rear.Range = forward.Range * Vision.RearRangeFactor;
            rear.HalfFov = forward.HalfFov;
            rear.Clear();
        }

        float reach = MathF.Max(forward.Range, rear?.Range ?? 0f);

        // Creatures come from the spatial index: there are few of them, and a
        // creature's angular width matters, so each is placed into every bin it
        // visually covers.
        int count = QueryCreaturesOnly(field, creature.Position, reach, creature.Id);
        for (int i = 0; i < count; i++)
        {
            ref readonly var p = ref _percepts[i];
            var offset = field.Offset(creature.Position, p.Position);
            float distance = offset.Length();
            if (distance <= 1e-4f) continue;

            CastInto(forward, creature.Body.Heading, offset, distance, p);
            if (rear is not null) CastInto(rear, creature.Body.Heading, offset, distance, p);
        }

        // Vegetation is a dense grid, so it is ray-marched one ray per bin rather
        // than gathered. Sweeping the square enclosing eye range was measured as the
        // simulation's dominant cost; marching costs what the ray actually travels.
        CastPlantRays(forward, creature.Body.Heading, creature.Position, field);
        if (rear is not null) CastPlantRays(rear, creature.Body.Heading, creature.Position, field);
    }

    /// <summary>One ray per vision bin. The march stops at the first plant, so
    /// occlusion falls out for free.</summary>
    private static void CastPlantRays(Eye eye, float bodyHeading, Vector2 origin, ISenseField field)
    {
        for (int bin = 0; bin < eye.BinCount; bin++)
        {
            var direction = eye.BinDirection(bodyHeading, bin);

            int hit = field.RayCastPlant(origin, direction, eye.Range, out float distance, out float radius);
            if (hit < 0) continue;

            eye.Report(bin, MathF.Max(0f, distance - radius), SeenKind.Plant, 0f);
        }
    }

    /// <summary>
    /// Places one percept into whichever bin it falls in. The cone test is done on
    /// the angle rather than with a dot-product shortcut because the bin index is
    /// needed anyway - and an object's angular half-width is added so a large
    /// nearby object fills the bins it visually covers rather than a single one.
    /// </summary>
    private static void CastInto(Eye eye, float bodyHeading, Vector2 offset, float distance, in Percept p)
    {
        float edge = distance - p.Radius;
        if (edge >= eye.Range) return;
        if (edge < 0f) edge = 0f;

        float angle = MathF.Atan2(offset.Y, offset.X);
        float relative = WrapAngle(angle - bodyHeading - eye.HeadingOffset);

        // Angular half-width of the object at this distance.
        float halfWidth = distance > p.Radius
            ? MathF.Asin(Math.Clamp(p.Radius / distance, 0f, 1f))
            : MathF.PI * 0.5f;

        int lo = eye.BinFor(relative - halfWidth);
        int hi = eye.BinFor(relative + halfWidth);

        // Entirely outside the cone on the same side.
        if (lo < 0 && hi < 0)
        {
            if (relative - halfWidth > eye.HalfFov || relative + halfWidth < -eye.HalfFov) return;
            lo = 0;
            hi = eye.BinCount - 1;
        }

        if (lo < 0) lo = 0;
        if (hi < 0) hi = eye.BinCount - 1;

        for (int bin = lo; bin <= hi; bin++)
            eye.Report(bin, edge, p.Kind, p.Hue);
    }

    private static void WriteEye(
        Span<float> destination, SensorLayout layout, int slot, Eye eye, Genome genome)
    {
        for (int bin = 0; bin < eye.BinCount; bin++)
        {
            int channel = bin * Vision.ChannelsPerBin;

            // Closeness rather than distance: 1 means touching, 0 means nothing
            // there. A brain reads "something is close" more directly than
            // "something is 87 units away", and an empty bin reads as 0.
            float closeness = eye.Kind[bin] == SeenKind.None
                ? 0f
                : 1f - Math.Clamp(eye.Distance[bin] / MathF.Max(eye.Range, 1e-3f), 0f, 1f);

            Write(destination, layout, slot, channel + 0, closeness);
            Write(destination, layout, slot, channel + 1, eye.Kind[bin] == SeenKind.Plant ? 1f : 0f);
            Write(destination, layout, slot, channel + 2, eye.Kind[bin] == SeenKind.Creature ? 1f : 0f);
        }
    }

    private void SampleMouth(
        in SenseSubject creature, ISenseField field, SensorLayout layout, Span<float> destination)
    {
        float reach = creature.MouthRange;
        int count = QueryAll(field, creature.Position, reach, creature.Id);

        float bestCloseness = 0f;
        float bestKind = 0f;
        float mouthArc = creature.Genome.Trait(TraitAxis.MouthArc);

        for (int i = 0; i < count; i++)
        {
            ref readonly var p = ref _percepts[i];
            var offset = field.Offset(creature.Position, p.Position);
            float distance = offset.Length() - p.Radius;
            if (distance > reach) continue;
            if (distance < 0f) distance = 0f;

            // The mouth is an arc, not a ring: a creature must face food to eat it.
            float relative = WrapAngle(MathF.Atan2(offset.Y, offset.X) - creature.Body.Heading);
            if (MathF.Abs(relative) > mouthArc) continue;

            float closeness = 1f - Math.Clamp(distance / MathF.Max(reach, 1e-3f), 0f, 1f);
            if (closeness <= bestCloseness) continue;

            bestCloseness = closeness;
            bestKind = p.Kind == SeenKind.Creature ? 1f : -1f;
        }

        Write(destination, layout, NodeIds.InnateSlot, (int)InnateSense.MouthContact, bestCloseness);
        Write(destination, layout, NodeIds.InnateSlot, (int)InnateSense.MouthKind, bestKind);

        if (creature.Genome.Has(LatentTraitId.Carnivory))
        {
            Write(destination, layout,
                LatentTraitCatalog.Get(LatentTraitId.Carnivory).Slot, 0,
                bestKind > 0f ? bestCloseness : 0f);
        }
    }

    /// <summary>Four taps around the nose give the direction a scent strengthens in -
    /// the difference between smelling something and knowing where it is.</summary>
    private static void SampleScentGradient(
        in SenseSubject creature, ISenseField field, SensorLayout layout, Span<float> destination)
    {
        int slot = LatentTraitCatalog.Get(LatentTraitId.ScentGradient).Slot;
        float reach = MathF.Max(creature.Genome.Trait(TraitAxis.NoseRadius), 1f);

        var nose = creature.NosePosition;
        var forward = creature.Body.Forward;
        var left = new Vector2(-forward.Y, forward.X);

        for (int channel = 0; channel < 2; channel++)
        {
            float ahead = field.SampleScent(field.Wrap(nose + forward * reach), channel);
            float behind = field.SampleScent(field.Wrap(nose - forward * reach), channel);
            float port = field.SampleScent(field.Wrap(nose + left * reach), channel);
            float starboard = field.SampleScent(field.Wrap(nose - left * reach), channel);

            Write(destination, layout, slot, channel * 2 + 0, Math.Clamp(ahead - behind, -1f, 1f));
            Write(destination, layout, slot, channel * 2 + 1, Math.Clamp(port - starboard, -1f, 1f));
        }
    }

    private static void Write(
        Span<float> destination, SensorLayout layout, int slot, int channel, float value)
    {
        int index = layout.IndexOf(NodeIds.Sensor(slot, channel));
        if (index >= 0) destination[index] = value;
    }

    /// <summary>
    /// Queries into the scratch buffer, growing it if the result filled it exactly -
    /// which is the only signal the field has that it may have truncated. The buffer
    /// stays grown, so this allocates during warm-up and never again.
    /// </summary>
    private int QueryAll(ISenseField field, Vector2 centre, float radius, int excludeId)
    {
        while (true)
        {
            int count = field.Query(centre, radius, excludeId, _percepts);
            if (count < _percepts.Length) return count;

            // Truncation would silently blind the creature to whatever sorted last.
            _percepts = new Percept[_percepts.Length * 2];
        }
    }

    private int QueryCreaturesOnly(ISenseField field, Vector2 centre, float radius, int excludeId)
    {
        while (true)
        {
            int count = field.QueryCreatures(centre, radius, excludeId, _percepts);
            if (count < _percepts.Length) return count;

            _percepts = new Percept[_percepts.Length * 2];
        }
    }

    /// <summary>Folds an angle into (-pi, pi].</summary>
    public static float WrapAngle(float angle)
    {
        const float TwoPi = MathF.PI * 2f;
        angle %= TwoPi;
        if (angle > MathF.PI) angle -= TwoPi;
        else if (angle <= -MathF.PI) angle += TwoPi;
        return angle;
    }
}
