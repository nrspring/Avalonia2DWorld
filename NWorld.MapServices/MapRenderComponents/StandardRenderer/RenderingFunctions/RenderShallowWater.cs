using System.Threading.Tasks;
using NWorld.Map.Models;
using SkiaSharp;

namespace NWorld.MapServices.MapRenderComponents.StandardRenderer.RenderingFunctions
{
    /// <summary>
    /// The sea over a shelf: close enough to the shore that the bottom is still doing some of
    /// the work, so it is lighter and greener than <see cref="RenderDeepWater"/> and lighter
    /// again than the open water further out.
    /// <para>
    /// It draws the same wave field at the same phase as the deep does -- see
    /// <see cref="WaterStyle"/> -- so crests run unbroken across the drop-off and only the
    /// colour steps, which is what a shelf edge actually looks like from above. The green in
    /// the palette is the point of the whole thing: blue over blue reads as one sea at two
    /// brightnesses, and it is the shift towards sand and weed that reads as <em>shallow</em>.
    /// </para>
    /// <para>
    /// Its own component rather than sharing the river's, though the two are both wadeable
    /// water and could have been drawn alike. A tile's ground is what the passes read the map
    /// back out of, and a shared type would leave them unable to tell a river mouth from the
    /// bay it runs into -- which matters, because a river pass has to know where the rivers
    /// are and would otherwise find one wrapped round every coast on the map.
    /// </para>
    /// </summary>
    public static class RenderShallowWater
    {
        private static readonly AnimatedWater Surface = new(new WaterStyle(
            // Its own seed, so the mottling over the shelf is not the same shapes as the
            // deep's in a paler colour.
            Seed: 0x9C41E2B7u,
            Trough: new SKColor(0x1A, 0x51, 0x6B),
            Body: new SKColor(0x2E, 0x84, 0x9B),
            Crest: new SKColor(0x5C, 0xB4, 0xB8),
            Highlight: new SKColor(0xDF, 0xF2, 0xEC),
            Steepness: 0.048f,
            Shine: 46f,

            // More foam than the deep and less than a river: this is water breaking on a shelf
            // rather than either open ocean or a current with banks either side of it.
            GlintStrength: 0.62f,
            FoamStrength: 0.34f,

            // The one that says shallow more than the palette does. Turbidity is the bottom
            // showing through, and the bottom only shows through where there is not much water
            // over it.
            TurbidityStrength: 0.26f,
            DepthLattice: 8,
            DepthStrength: 30f));

        /// <summary>The colour of the sea over a shelf at the mean surface.</summary>
        /// <inheritdoc cref="AnimatedWater.Body" path="/summary"/>
        public static SKColor Body => Surface.Body;

        public static Task Render(TileRenderContext context) => Surface.Render(context);

        /// <inheritdoc cref="AnimatedWater.Prewarm"/>
        public static Task Prewarm(int tileSize) => Surface.Prewarm(tileSize);

        /// <inheritdoc cref="AnimatedWater.ClearCache"/>
        public static void ClearCache() => Surface.ClearCache();
    }
}
