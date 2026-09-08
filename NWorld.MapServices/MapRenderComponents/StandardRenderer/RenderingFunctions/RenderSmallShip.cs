using System.Threading.Tasks;
using NWorld.Map.Models;

namespace NWorld.MapServices.MapRenderComponents.StandardRenderer.RenderingFunctions
{
    /// <summary>
    /// A boat: one mast, and not much hull under it.
    /// <para>
    /// The smallest of the three, and the one that has to stay a ship at the sizes where the
    /// other two still have room to spare. Short and relatively broad for her length, because a
    /// small hull drawn to the same slender proportion as a carrack comes out as a splinter --
    /// the little ones are beamier for their length in life, and here they have to be.
    /// </para>
    /// </summary>
    public static class RenderSmallShip
    {
        private static readonly Vessel Boat = new(new VesselStyle(Length: 0.46f, Beam: 0.21f, Masts: 1));

        public static Task Render(TileRenderContext context) => Boat.Render(context);

        /// <inheritdoc cref="Vessel.Prewarm"/>
        public static Task Prewarm(int tileSize) => Boat.Prewarm(tileSize);

        /// <inheritdoc cref="Vessel.ClearCache"/>
        public static void ClearCache() => Boat.ClearCache();
    }
}
