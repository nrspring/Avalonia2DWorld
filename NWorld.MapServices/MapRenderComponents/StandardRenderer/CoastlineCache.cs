using System;
using System.Collections.Generic;
using NWorld.Map.Models;
using SkiaSharp;

namespace NWorld.MapServices.MapRenderComponents.StandardRenderer
{
    /// <summary>
    /// The shore, held as a picture between frames.
    /// <para>
    /// It does not move. <see cref="Coastline"/> says so outright: nothing in it runs on
    /// <see cref="RenderFrame.TimeSeconds"/>, and what it draws is settled by the world, the
    /// zoom and where the view is looking. So on a still map every frame after the first redrew,
    /// at some cost, an image identical to the one before it -- redrawn only because the water
    /// animating on top forces a repaint of everything under it.
    /// </para>
    /// <para>
    /// The cost is worth avoiding rather than merely reducing: the coast is three blurred
    /// strokes, and a blurred stroke is paid for per pixel over the length of every shoreline on
    /// screen. Measured on a 600x400 world at sixteen pixels a tile it came to about six
    /// milliseconds of a twenty-eight millisecond frame. Held as a picture it is one blit.
    /// </para>
    /// <para>
    /// <b>The shore and not the drop-off</b>, although the two are drawn together and the
    /// drop-off costs about as much. <see cref="WaterDepth"/> darkens the sea by multiplying into
    /// it -- a <c>SaveLayer</c> composited with <see cref="SKBlendMode.Multiply"/> -- so what it
    /// puts on the canvas is a function of the water already there. Held apart from that water it
    /// multiplies into nothing, and the pale halo that comes back along every drop-off is not a
    /// rounding error but the whole of the effect gone wrong: it measured 207 of 255 against the
    /// sea it should have darkened. Anything that reads its backdrop has to be drawn against its
    /// backdrop. The coast asks nothing of what is under it -- every paint in it is an ordinary
    /// source-over stroke -- which is exactly what makes it safe to hold, and the test for
    /// whether anything else may join it later.
    /// </para>
    /// <para>
    /// Held in <b>device</b> pixels, at the exact transform it was drawn under, and blitted back
    /// one pixel to one with the matrix reset. That is not a detail either: a picture held in map
    /// pixels and left to the canvas to place lands at a fractional offset under any display
    /// scaling, and the resample softens the ink line along the shore -- a one-pixel seam tracing
    /// every coast on the map. Held this way the kept frame is the drawn frame, to the pixel,
    /// which has been checked by comparing the two with the water stopped.
    /// </para>
    /// <para>
    /// The price is no reuse while the view is moving, since a pan changes the transform and
    /// every changed transform is a rebuild. That costs nothing worth having: a frame that is
    /// panning is drawing a different picture anyway, so there was never a picture to reuse.
    /// What is held is the still map, which is what a map mostly is.
    /// </para>
    /// </summary>
    internal sealed class CoastlineCache
    {
        /// <summary>
        /// Past this the picture is not worth holding and the coast draws straight to the canvas.
        /// A window-sized surface on any ordinary display is far inside it; this is here so that
        /// an enormous one cannot turn a saving into a hundred megabytes of texture.
        /// </summary>
        private const long MaxPixels = 16L * 1024 * 1024;

        private readonly Coastline _coast = new();

        private SKSurface? _surface;

        /// <summary>Where the picture goes back, in device pixels, and how big it is.</summary>
        private SKRectI _clip;

        /// <summary>
        /// The transform the picture was drawn under. Anything at all different here -- a pan of
        /// half a pixel, a zoom, the window moved to a screen at another scaling -- means the
        /// held pixels are not the pixels that would be drawn now.
        /// </summary>
        private SKMatrix _matrix;

        /// <summary>
        /// The context the picture was built on, held to be compared and never to be used. A lost
        /// device gives a new context, and everything made on the old one is rubbish that happens
        /// to still answer its properties.
        /// </summary>
        private GRContext? _context;

        /// <summary>
        /// The grid the picture was built from. Compared by reference, which is exactly right
        /// here: an edit publishes a new grid rather than writing to the old one, so a different
        /// reference is the definition of a map that has changed.
        /// </summary>
        private TileGrid? _world;

        private int _tileSize;

        /// <summary>
        /// Draws the coast: from the held picture where it still stands for what would be drawn,
        /// and by drawing it where it does not.
        /// </summary>
        public void Render(SKCanvas canvas, RenderFrame frame, IReadOnlyList<MapTile> tiles)
        {
            // Below this the coast draws nothing, so there is nothing to hold. That is also what
            // keeps the mini-map out of here, which asks for the whole world at a few pixels a
            // tile.
            if (frame.TileSize < Coastline.MinTileSize || frame.World is null || frame.Gpu is not { } gpu)
            {
                _coast.Render(canvas, frame, tiles);
                return;
            }

            var clip = canvas.DeviceClipBounds;
            var matrix = canvas.TotalMatrix;

            if (clip.Width <= 0 || clip.Height <= 0)
                return;

            if (!Holds(gpu, frame, clip, matrix) && !Rebuild(gpu, frame, tiles, clip, matrix))
            {
                // Nothing held and nothing built -- too large to hold, or the device refused the
                // surface. The map still has a coast.
                _coast.Render(canvas, frame, tiles);
                return;
            }

            // Straight to device pixels, which is the one placement that cannot resample: the
            // picture was rasterised under this very transform, so the canvas must not apply it a
            // second time.
            var checkpoint = canvas.Save();

            try
            {
                canvas.ResetMatrix();
                _surface!.Draw(canvas, _clip.Left, _clip.Top, null);
            }
            finally
            {
                canvas.RestoreToCount(checkpoint);
            }
        }

        /// <summary>Drops the picture. The next frame builds another.</summary>
        public void Clear()
        {
            _surface?.Dispose();
            _surface = null;
            _clip = SKRectI.Empty;
            _context = null;
            _world = null;
        }

        /// <summary>Whether what is held is, pixel for pixel, what would be drawn.</summary>
        private bool Holds(GRContext gpu, RenderFrame frame, SKRectI clip, SKMatrix matrix) =>
            _surface is not null &&
            ReferenceEquals(_context, gpu) &&
            ReferenceEquals(_world, frame.World) &&
            _tileSize == frame.TileSize &&
            _clip == clip &&
            Same(_matrix, matrix);

        /// <summary>
        /// Exact equality, deliberately. There is no tolerance worth having here: a transform
        /// that differs at all draws different pixels, and one that differs by less than a pixel
        /// is precisely the case that would show as a shimmering coast under a slow pan.
        /// </summary>
        private static bool Same(SKMatrix a, SKMatrix b) =>
            a.ScaleX == b.ScaleX && a.SkewX == b.SkewX && a.TransX == b.TransX &&
            a.SkewY == b.SkewY && a.ScaleY == b.ScaleY && a.TransY == b.TransY &&
            a.Persp0 == b.Persp0 && a.Persp1 == b.Persp1 && a.Persp2 == b.Persp2;

        private bool Rebuild(
            GRContext gpu,
            RenderFrame frame,
            IReadOnlyList<MapTile> tiles,
            SKRectI clip,
            SKMatrix matrix)
        {
            if ((long)clip.Width * clip.Height > MaxPixels)
                return false;

            // Kept where it fits. A pan rebuilds at exactly the size the last one was, so after
            // the first frame this is a clear and a redraw rather than a new texture.
            if (_surface is null || _clip.Width != clip.Width || _clip.Height != clip.Height ||
                !ReferenceEquals(_context, gpu))
            {
                _surface?.Dispose();
                _surface = SKSurface.Create(
                    gpu,
                    budgeted: true,
                    new SKImageInfo(clip.Width, clip.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
            }

            if (_surface is null)
            {
                _clip = SKRectI.Empty;
                return false;
            }

            var canvas = _surface.Canvas;
            var checkpoint = canvas.Save();

            try
            {
                canvas.Clear(SKColors.Transparent);

                // The drawn transform, moved so that the corner of the clip is the corner of the
                // surface. The coast then goes on drawing in map pixels and needs to know nothing
                // about being held -- and the clip lands exactly over the surface, which is what
                // VisibleTiles reads to decide how much of the world to walk.
                canvas.SetMatrix(SKMatrix.Concat(
                    SKMatrix.CreateTranslation(-clip.Left, -clip.Top), matrix));

                _coast.Render(canvas, frame, tiles);
            }
            finally
            {
                canvas.RestoreToCount(checkpoint);
            }

            _context = gpu;
            _world = frame.World;
            _tileSize = frame.TileSize;
            _clip = clip;
            _matrix = matrix;

            return true;
        }
    }
}
