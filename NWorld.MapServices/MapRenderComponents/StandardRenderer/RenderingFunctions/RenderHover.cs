using System.Threading.Tasks;
using NWorld.Map.Models;
using SkiaSharp;

namespace NWorld.MapServices.MapRenderComponents.StandardRenderer.RenderingFunctions
{
    /// <summary>
    /// Marks the tile under the cursor, in red. Transient and follows the mouse, so it is the
    /// louder of the two marks -- see <see cref="TileHighlight"/> for the shape, which it
    /// shares with <see cref="RenderSelected"/>.
    /// </summary>
    public static class RenderHover
    {
        private static readonly TileHighlight Highlight = new(new HighlightStyle(
            Outline: new SKColor(0xF2, 0x3B, 0x2E),
            Shadow: new SKColor(0x4A, 0x08, 0x06),
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
