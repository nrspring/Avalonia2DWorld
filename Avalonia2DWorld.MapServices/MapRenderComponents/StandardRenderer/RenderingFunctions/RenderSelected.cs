using System.Threading.Tasks;
using Avalonia2DWorld.Map.Models;
using SkiaSharp;

namespace Avalonia2DWorld.MapServices.MapRenderComponents.StandardRenderer.RenderingFunctions
{
    /// <summary>
    /// Marks the tile the player has chosen, in yellow. Identical in shape to
    /// <see cref="RenderHover"/> and differing only in hue, which is the point: the two mean
    /// the same kind of thing and should read as one family, so nothing but the colour tells
    /// them apart.
    /// <para>
    /// A selection outlasts the cursor and a hover does not, so the two can land on the same
    /// tile at once. Give selected the lower layer of the pair and the hover will draw over
    /// it, which is the right way round -- the mark that follows the mouse should be the one
    /// that answers it.
    /// </para>
    /// </summary>
    public static class RenderSelected
    {
        private static readonly TileHighlight Highlight = new(new HighlightStyle(
            // Warmer and more saturated than the grass it usually sits on, which is what keeps
            // a yellow legible over green.
            Outline: new SKColor(0xFF, 0xD1, 0x1A),
            Shadow: new SKColor(0x4A, 0x35, 0x02),
            OutlineAlpha: 236,
            ShadowAlpha: 150,
            OutlineFraction: 1f / 16f,
            ShadowFraction: 0.30f));

        public static Task Render(TileRenderContext context) => Highlight.Render(context);

        /// <inheritdoc cref="TileHighlight.Prewarm"/>
        public static Task Prewarm(int tileSize) => Highlight.Prewarm(tileSize);

        /// <inheritdoc cref="TileHighlight.ClearCache"/>
        public static void ClearCache() => Highlight.ClearCache();
    }
}
