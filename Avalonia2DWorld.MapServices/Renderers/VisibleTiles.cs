using System;
using SkiaSharp;

namespace Avalonia2DWorld.MapServices.Renderers
{
    /// <summary>
    /// Which tiles a canvas can actually show. Every renderer needs this and none of them
    /// needs a different answer, so it lives here rather than once per renderer.
    /// </summary>
    internal static class VisibleTiles
    {
        /// <summary>
        /// The tiles the canvas clip can show, in tile coordinates and inclusive.
        /// <para>
        /// Taken from the clip rather than passed in, so this holds for whatever the caller
        /// set up: the map pass gets the control's bounds, and the mini-map pass -- which
        /// scales the whole map into a corner -- gets bounds that take in all of it, and so
        /// keeps every tile.
        /// </para>
        /// <para>
        /// A tile wider than the clip on every side. Nothing here knows how far past its own
        /// square a component may draw, and a highlight that overhangs by a pixel would
        /// otherwise vanish as its tile crossed the edge.
        /// </para>
        /// </summary>
        public static (int MinX, int MinY, int MaxX, int MaxY) For(SKCanvas canvas, int tileSize)
        {
            var clip = canvas.LocalClipBounds;

            // An empty clip means nothing is visible, and an inverted range drops every tile.
            if (clip.Width <= 0 || clip.Height <= 0)
                return (0, 0, -1, -1);

            return (
                ToTile(clip.Left, tileSize) - 1,
                ToTile(clip.Top, tileSize) - 1,
                ToTile(clip.Right, tileSize) + 1,
                ToTile(clip.Bottom, tileSize) + 1);
        }

        /// <summary>
        /// A pixel coordinate to the tile that covers it, clamped rather than cast: an
        /// unbounded clip comes back as a rect of nearly infinite floats, and casting that to
        /// an int is undefined enough to invert the range and hide the whole map.
        /// </summary>
        private static int ToTile(float pixels, int tileSize) =>
            (int)Math.Clamp(MathF.Floor(pixels / tileSize), int.MinValue / 2, int.MaxValue / 2);
    }
}
