using System.Threading.Tasks;
using NWorld.Map.Models;

namespace NWorld.MapServices.MapRenderComponents.StandardRenderer.RenderingFunctions
{
    /// <summary>
    /// A cog: two masts, and half again the boat's length.
    /// <para>
    /// The middle of the three, which is the hardest of them to draw, because middling is not a
    /// shape. What makes her legible is that she is never judged alone: one yard is a boat and
    /// three is a carrack, so two is read as the one between them the moment either of the
    /// others is anywhere on the map.
    /// </para>
    /// </summary>
    public static class RenderMediumShip
    {
        private static readonly Vessel Cog = new(new VesselStyle(Length: 0.67f, Beam: 0.26f, Masts: 2));

        public static Task Render(TileRenderContext context) => Cog.Render(context);

        /// <inheritdoc cref="Vessel.Prewarm"/>
        public static Task Prewarm(int tileSize) => Cog.Prewarm(tileSize);

        /// <inheritdoc cref="Vessel.ClearCache"/>
        public static void ClearCache() => Cog.ClearCache();
    }
}
