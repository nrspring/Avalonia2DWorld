using System.Collections.Generic;
using NWorld.Map.Models;
using SkiaSharp;

namespace NWorld.MapServices.MapRenderComponents.StandardRenderer
{
    /// <summary>
    /// The shore, held as a picture between frames -- see <see cref="HeldPicture"/> for what that
    /// means and what may be put through it.
    /// <para>
    /// It qualifies on both counts. It does not move: <see cref="Coastline"/> says so outright,
    /// nothing in it runs on <see cref="RenderFrame.TimeSeconds"/>, and what it draws is settled
    /// by the world, the zoom and where the view is looking. And it asks nothing of what is under
    /// it: every paint in it is an ordinary source-over stroke. So on a still map every frame
    /// after the first was redrawing an image identical to the one before it -- redrawn only
    /// because the water animating on top forces a repaint of everything beneath.
    /// </para>
    /// <para>
    /// Worth holding rather than merely trimming: the coast is a handful of strokes, most of
    /// them blurred, laid along two passes over the shore rather than one -- and a blurred stroke
    /// is paid for per pixel along every shoreline on screen. Measured on a 600x400
    /// world at sixteen pixels a tile it came to about six milliseconds of a twenty-eight
    /// millisecond frame. Held as a picture it is one blit, which measured out at half again as
    /// many frames a second at that zoom.
    /// </para>
    /// <para>
    /// <b>The shore and not the drop-off</b>, although the two are drawn together and the drop-off
    /// costs about as much. <see cref="WaterDepth"/> darkens the sea by multiplying into it, so
    /// what it puts on the canvas is a function of the water already there, and it fails the first
    /// rule on <see cref="HeldPicture"/>. It is drawn every frame, against its backdrop.
    /// </para>
    /// </summary>
    internal sealed class CoastlineCache
    {
        private readonly Coastline _coast = new();
        private readonly HeldPicture _held = new();

        /// <summary>
        /// Draws the coast: from the held picture where it still stands for what would be drawn,
        /// and by drawing it where it does not.
        /// </summary>
        public void Render(SKCanvas canvas, RenderFrame frame, IReadOnlyList<MapTile> tiles)
        {
            // Below this the coast draws nothing, so there is nothing to hold. That is also what
            // keeps the mini-map out of here, which asks for the whole world at a few pixels a
            // tile -- and which, drawing into a raster surface, brings no context to hold one on.
            if (frame.TileSize < Coastline.MinTileSize || frame.World is null ||
                frame.Gpu is not { } gpu ||
                !_held.Draw(canvas, frame, gpu, c => _coast.Render(c, frame, tiles)))
            {
                _coast.Render(canvas, frame, tiles);
            }
        }

        /// <inheritdoc cref="HeldPicture.Clear"/>
        public void Clear() => _held.Clear();
    }
}
