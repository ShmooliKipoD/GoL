using Microsoft.Xna.Framework;
using MonoGame.Extended.ECS;
using MonoGame.Extended.ECS.Systems;

namespace GoL.Sim.Systems;

/// <summary>
/// Advances the world's grids - pheromones, fertility, plant growth - and rebuilds
/// the spatial index.
/// <para>
/// Runs <b>first</b>, so every creature senses the same field state, and the index
/// is rebuilt after the previous tick's movement but before this tick's sensing.
/// A mid-loop rebuild would let queries see a torn index.
/// </para>
/// <para>
/// Not an <c>EntityUpdateSystem</c>: fields are dense grids, not entities. Making
/// tens of thousands of plant cells into entities would be a large regression for
/// no gain.
/// </para>
/// </summary>
public sealed class FieldSystem : UpdateSystem
{
    private readonly SimWorld _world;

    public FieldSystem(SimWorld world) => _world = world;

    public override void Update(GameTime gameTime)
        => _world.StepFields((float)gameTime.ElapsedGameTime.TotalSeconds);
}
