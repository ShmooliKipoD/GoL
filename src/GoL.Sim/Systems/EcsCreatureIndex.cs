using System;
using GoL.Sim.Components;
using GoL.Sim.Core;
using GoL.Sim.Genetics;

namespace GoL.Sim.Systems;

/// <summary>
/// Finds creatures for the environment's sense queries.
/// <para>
/// The environment cannot enumerate creatures itself - the ECS owns them - so the
/// simulation supplies this bridge. It walks <see cref="SimWorld.Living"/>, which
/// is ascending by entity id, so results are emitted in a stable order and any
/// float accumulated over them is reproducible.
/// </para>
/// <para>
/// Brute force for now. The board's spatial hash replaces the scan in Step 3b
/// behind this same interface.
/// </para>
/// </summary>
public sealed class EcsCreatureIndex : ICreatureIndex
{
    private readonly SimWorld _world;

    public EcsCreatureIndex(SimWorld world) => _world = world;

    public int Query(Vector2 centre, float radius, int excludeCreatureId, Span<Percept> results)
    {
        var field = _world.Field;
        int count = 0;

        var living = _world.Living;
        for (int i = 0; i < living.Count; i++)
        {
            int id = living[i];
            if (id == excludeCreatureId) continue;
            if (count >= results.Length) return count;

            var body = _world.Get<Body>(id);
            var offset = field.Offset(centre, body.Position);

            float reach = radius + body.Radius;
            if (offset.LengthSquared() > reach * reach) continue;

            results[count++] = new Percept(
                body.Position, body.Radius, SeenKind.Creature,
                _world.Get<Genes>(id).Genome.Normalized(TraitAxis.Hue), id);
        }

        return count;
    }
}
