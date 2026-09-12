using System;
using Avalonia2DWorld.Map.Models;
using SkiaSharp;

namespace Avalonia2DWorld.MapServices.MapRenderComponents.StandardRenderer
{
    /// <summary>
    /// Something drawn once and then kept, to be blitted back on the frames that would have
    /// drawn it again identically.
    /// <para>
    /// The map repaints continuously because the water moves. Everything else on it does not, so
    /// any pass that does not read the clock is redrawn every frame to produce the image it
    /// produced last frame. This is the machinery for not doing that; what may be put through it
    /// is the caller's judgement, and it is a narrow judgement -- see the two rules below.
    /// </para>
    /// <para>
    /// <b>Only what does not read its backdrop.</b> The held picture is built over transparency
    /// and composited afterwards, so a pass that blends with what is underneath it -- a multiply,
    /// a screen -- gets a different answer inside here than it would on the canvas, and gets it
    /// silently. <see cref="WaterDepth"/> is the standing example: held on its own it multiplies
    /// into nothing, and the drop-off comes back as a pale halo measuring 207 of 255 against the
    /// sea it was meant to darken. Either hold the backdrop with it, or do not hold it.
    /// </para>
    /// <para>
    /// <b>Only what does not read the clock</b>, unless the caller means to stop it -- which is
    /// what the zoomed-out map does deliberately, and says so where it asks for that.
    /// </para>
    /// <para>
    /// Held in <b>device</b> pixels at the exact transform it was drawn under, and blitted back
    /// one pixel to one with the matrix reset. That is not a detail: a picture held in map pixels
    /// and left to the canvas to place lands at a fractional offset under any display scaling,
    /// and the resample softens every hard edge in it -- on the coast it showed as a one-pixel
    /// seam tracing every shore on the map. Held this way the kept frame is the drawn frame, to
    /// the pixel, which has been checked by comparing the two with the water stopped.
    /// </para>
    /// <para>
    /// The price is no reuse while the view is moving, since a pan changes the transform and
    /// every changed transform is a rebuild. That costs nothing worth having: a frame that is
    /// panning is drawing a different picture anyway, so there was never a picture to reuse.
    /// What is held is the still map, which is what a map mostly is.
    /// </para>
    /// <para>
    /// One thread at a time, like everything else a renderer owns.
    /// </para>
    /// </summary>
    internal sealed class HeldPicture
    {
        /// <summary>
        /// Past this the picture is not worth holding and the caller draws straight to the
        /// canvas. A window-sized surface on any ordinary display is far inside it; this is here
        /// so that an enormous one cannot turn a saving into a hundred megabytes of texture.
        /// </summary>
        private const long MaxPixels = 16L * 1024 * 1024;

        private SKSurface? _surface;

        /// <summary>Where the picture goes back, in device pixels, and how big it is.</summary>
        private SKRectI _clip;

        /// <summary>
        /// The transform the picture was drawn under. Anything at all different here -- a pan of
        /// half a pixel, a zoom, the window moved to a screen at another scaling -- means the held
        /// pixels are not the pixels that would be drawn now.
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
        /// Blits the held picture, drawing <paramref name="paint"/> into a new one first if what
        /// is held is not what would be drawn now. False where nothing could be held -- too large,
        /// or the device would not give a surface -- and then the caller must draw as it always
        /// did, having had nothing drawn for it.
        /// <para>
        /// <paramref name="with"/> is how the picture goes back on, for a caller that wants
        /// something other than laying it over what is there: the water's drop-off holds a mask
        /// and blits it through <see cref="SKBlendMode.DstIn"/>, so what is held is not a picture
        /// of the map at all but the shape another draw is cut to.
        /// </para>
        /// </summary>
        public bool Draw(
            SKCanvas canvas, RenderFrame frame, GRContext gpu, Action<SKCanvas> paint, SKPaint? with = null)
        {
            var clip = canvas.DeviceClipBounds;
            var matrix = canvas.TotalMatrix;

            if (clip.Width <= 0 || clip.Height <= 0)
                return true;

            if (!Holds(gpu, frame, clip, matrix) && !Rebuild(gpu, frame, clip, matrix, paint))
                return false;

            // Straight to device pixels, which is the one placement that cannot resample: the
            // picture was rasterised under this very transform, so the canvas must not apply it a
            // second time.
            var checkpoint = canvas.Save();

            try
            {
                canvas.ResetMatrix();
                _surface!.Draw(canvas, _clip.Left, _clip.Top, with);
            }
            finally
            {
                canvas.RestoreToCount(checkpoint);
            }

            return true;
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
        /// Exact equality, deliberately. There is no tolerance worth having here: a transform that
        /// differs at all draws different pixels, and one that differs by less than a pixel is
        /// precisely the case that would show as a shimmer under a slow pan.
        /// </summary>
        private static bool Same(SKMatrix a, SKMatrix b) =>
            a.ScaleX == b.ScaleX && a.SkewX == b.SkewX && a.TransX == b.TransX &&
            a.SkewY == b.SkewY && a.ScaleY == b.ScaleY && a.TransY == b.TransY &&
            a.Persp0 == b.Persp0 && a.Persp1 == b.Persp1 && a.Persp2 == b.Persp2;

        private bool Rebuild(
            GRContext gpu, RenderFrame frame, SKRectI clip, SKMatrix matrix, Action<SKCanvas> paint)
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
                // surface. What is drawn then goes on working in map pixels and needs to know
                // nothing about being held -- and the clip lands exactly over the surface, which
                // is what VisibleTiles reads to decide how much of the world to walk.
                canvas.SetMatrix(SKMatrix.Concat(
                    SKMatrix.CreateTranslation(-clip.Left, -clip.Top), matrix));

                paint(canvas);
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
