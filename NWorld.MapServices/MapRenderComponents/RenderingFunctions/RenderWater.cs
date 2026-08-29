using System.Threading.Tasks;
using NWorld.Map.Models;
using SkiaSharp;

namespace NWorld.MapServices.MapRenderComponents.RenderingFunctions
{
    /// <summary>
    /// Open, wadeable water: mid blue-green, bright glints, a fair amount of foam on the
    /// crests. The waves themselves come from <see cref="AnimatedWater"/> and are shared with
    /// <see cref="RenderDeepWater"/>, so a shoreline between the two is a change of colour
    /// across one unbroken surface.
    /// </summary>
    public static class RenderWater
    {
        private static readonly AnimatedWater Surface = new(new WaterStyle(
            Seed: 0x2B7F91C5u,
            Trough: new SKColor(0x1B, 0x45, 0x66),
            Body: new SKColor(0x2F, 0x76, 0x9C),
            Crest: new SKColor(0x51, 0xA6, 0xBC),
            Highlight: new SKColor(0xDC, 0xEF, 0xF3),
            Steepness: 0.055f,
            Shine: 55f,
            GlintStrength: 0.8f,
            FoamStrength: 0.5f,
            TurbidityStrength: 0.14f,
            DepthLattice: 9,
            DepthStrength: 44f));

        public static Task Render(TileRenderContext context) => Surface.Render(context);

        /// <inheritdoc cref="AnimatedWater.Prewarm"/>
        public static Task Prewarm(int tileSize) => Surface.Prewarm(tileSize);

        /// <inheritdoc cref="AnimatedWater.ClearCache"/>
        public static void ClearCache() => Surface.ClearCache();
    }
}
