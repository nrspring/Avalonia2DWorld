using System.Threading.Tasks;
using NWorld.Map.Models;
using SkiaSharp;

namespace NWorld.MapServices.MapRenderComponents.StandardRenderer.RenderingFunctions
{
    /// <summary>
    /// Water too deep to see the bottom of: a much darker, bluer, colder surface than
    /// <see cref="RenderWater"/>.
    /// <para>
    /// It draws the same wave field, at the same phase, from the same block -- see
    /// <see cref="WaterStyle"/> -- so crests run unbroken across a shallow/deep boundary and
    /// only the colour steps, the way a real drop-off looks. What changes is everything to do
    /// with light: less of it comes back, so the palette is darker and further towards blue,
    /// the glints are dimmer and broader, and there is much less foam, which is also what
    /// makes the depth legible at a glance.
    /// </para>
    /// <para>
    /// A gentler <see cref="WaterStyle.Steepness"/> is doing something slightly different from
    /// the rest: it does not change where the crests are, only how hard they catch the light,
    /// so the deep reads as a longer, lazier swell while still lining up exactly with the
    /// shallows next to it.
    /// </para>
    /// </summary>
    public static class RenderDeepWater
    {
        private static readonly AnimatedWater Surface = new(new WaterStyle(
            // Its own seed, so the broad depth variation out here is unrelated to the shallows'
            // rather than a darker copy of the same shapes.
            Seed: 0x5D13A70Bu,
            Trough: new SKColor(0x07, 0x1B, 0x33),
            Body: new SKColor(0x10, 0x33, 0x57),
            Crest: new SKColor(0x1E, 0x55, 0x80),
            Highlight: new SKColor(0xA8, 0xC8, 0xDE),
            Steepness: 0.040f,
            Shine: 34f,
            GlintStrength: 0.42f,
            FoamStrength: 0.18f,
            TurbidityStrength: 0.10f,
            DepthLattice: 7,
            DepthStrength: 38f));

        public static Task Render(TileRenderContext context) => Surface.Render(context);

        /// <inheritdoc cref="AnimatedWater.Prewarm"/>
        public static Task Prewarm(int tileSize) => Surface.Prewarm(tileSize);

        /// <inheritdoc cref="AnimatedWater.ClearCache"/>
        public static void ClearCache() => Surface.ClearCache();
    }
}
