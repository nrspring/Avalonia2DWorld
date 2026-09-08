using System.Threading.Tasks;
using NWorld.Map.Models;

namespace NWorld.MapServices.MapRenderComponents.StandardRenderer.RenderingFunctions
{
    /// <summary>
    /// A carrack: three masts, and as much of the tile as a ship may have.
    /// <para>
    /// Held short of the tile edge rather than run out to it, and by more than the drawing needs.
    /// A hull that reached the edge would touch the hull on the next tile, and two ships touching
    /// read as one longer ship -- the same trap <see cref="RenderFort"/> keeps its margin to
    /// avoid, and a worse one here, because a fleet is drawn in a line far more often than forts
    /// are.
    /// </para>
    /// <para>
    /// Slender for her length, which is the other half of the size reading. She is not merely
    /// longer than the cog; she is a different proportion, and a shape the eye can tell from the
    /// boat's without measuring either.
    /// </para>
    /// </summary>
    public static class RenderLargeShip
    {
        private static readonly Vessel Carrack = new(new VesselStyle(Length: 0.86f, Beam: 0.30f, Masts: 3));

        public static Task Render(TileRenderContext context) => Carrack.Render(context);

        /// <inheritdoc cref="Vessel.Prewarm"/>
        public static Task Prewarm(int tileSize) => Carrack.Prewarm(tileSize);

        /// <inheritdoc cref="Vessel.ClearCache"/>
        public static void ClearCache() => Carrack.ClearCache();
    }
}
