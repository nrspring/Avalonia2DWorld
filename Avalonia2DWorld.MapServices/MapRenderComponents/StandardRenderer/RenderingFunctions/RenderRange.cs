using System.Threading.Tasks;
using Avalonia2DWorld.Map.Models;
using SkiaSharp;

namespace Avalonia2DWorld.MapServices.MapRenderComponents.StandardRenderer.RenderingFunctions
{
    /// <summary>
    /// Washes a tile orange to show it is in range: a flat translucent square, no outline.
    /// <para>
    /// Deliberately not built like <see cref="RenderHover"/> and <see cref="RenderSelected"/>,
    /// which outline a single tile each. Range covers a whole region at once, and outlining
    /// every tile in it would draw the grid the ground renderers work so hard to hide. A wash
    /// has no interior edges, so a region of any shape reads as one area.
    /// </para>
    /// <para>
    /// It is also the one overlay that needs nothing cached. Hover and selected have a shape,
    /// so their sprite depends on the tile size and there is one per zoom level; a flat colour
    /// is the same colour at every zoom, so this is a single paint and there is no
    /// <c>Prewarm</c> or <c>ClearCache</c> to offer.
    /// </para>
    /// </summary>
    public static class RenderRange
    {
        /// <summary>
        /// Translucent enough to read the ground and anything standing on it -- range is shown
        /// on many tiles at once, and it has to inform rather than obscure.
        /// </summary>
        private static readonly SKPaint Wash = new()
        {
            Color = new SKColor(0xF7, 0x8B, 0x1E, 96),
            BlendMode = SKBlendMode.SrcOver,

            // Must stay off. Coalescing gives runs that share an edge exactly; antialiased,
            // both sides of that edge would get partial coverage and the translucent fill
            // would show a seam along it -- a faint grid over the region, which is the one
            // thing this is shaped to avoid.
            IsAntialias = false,
        };

        public static Task Render(TileRenderContext context)
        {
            var canvas = context.Canvas;
            var tileSize = context.TileSize;
            var tiles = context.Tiles;

            if (canvas is null || tileSize <= 0 || tiles.Length == 0)
                return Task.CompletedTask;

            TileRuns.Fill(canvas, tiles, tileSize, Wash);

            return Task.CompletedTask;
        }
    }
}
