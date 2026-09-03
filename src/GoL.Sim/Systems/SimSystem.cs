using Microsoft.Xna.Framework;
using MonoGame.Extended.ECS;
using MonoGame.Extended.ECS.Systems;

namespace GoL.Sim.Systems;

/// <summary>
/// Base for every simulation system.
/// <para>
/// Extended's <see cref="EntityUpdateSystem"/> hands each system a
/// <see cref="GameTime"/>. The simulation must never see wall-clock time - a
/// seeded run has to reproduce exactly - so <see cref="SimWorld"/> synthesises a
/// <c>GameTime</c> carrying the fixed timestep and nothing else, and systems read
/// <see cref="Dt"/> rather than the clock.
/// </para>
/// <para>
/// Systems iterate <c>ActiveEntities</c>, which Extended orders by ascending entity
/// id (verified experimentally - see CLAUDE.md). That stable order is what makes
/// float accumulation across creatures reproducible.
/// </para>
/// </summary>
public abstract class SimSystem : EntityUpdateSystem
{
    protected SimSystem(AspectBuilder aspect) : base(aspect) { }

    /// <summary>The fixed timestep, in seconds.</summary>
    protected static float Dt(GameTime gameTime) => (float)gameTime.ElapsedGameTime.TotalSeconds;
}
