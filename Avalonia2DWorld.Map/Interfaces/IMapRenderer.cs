using Avalonia2DWorld.Map.Models;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Text;

namespace Avalonia2DWorld.Map.Interfaces
{
    public interface IMapRenderer
    {
        /// <summary>
        /// Draws a screenful of tiles. <paramref name="frame"/> carries the tile size and the
        /// frame's timestamp, both of which the caller samples once and reuses for the whole
        /// frame.
        /// <para>
        /// The whole visible set goes in together rather than a tile at a time, so that a
        /// component drawn on thousands of tiles can issue one batched draw instead of
        /// thousands of individual ones. Pass the tiles in reading order where that is
        /// natural: components are free to coalesce runs of adjacent tiles, and none of them
        /// require it.
        /// </para>
        /// <para>
        /// Tiles are composited layer by layer across the whole set -- every tile's layer 0,
        /// then every tile's layer 1 -- so a component that draws outside its own tile, such
        /// as a highlight or a shadow, is not overdrawn by the ground of its neighbour.
        /// </para>
        /// </summary>
        Task RenderTiles(SKCanvas canvas, RenderFrame frame, IReadOnlyList<MapTile> tiles);

        /// <summary>
        /// Draws the writing placed on the map: every label in <paramref name="labels"/>, on
        /// the same canvas and in the same coordinates the tiles were just drawn in.
        /// <para>
        /// A pass of its own rather than another layer of the one above, because a label is
        /// not attached to a tile. It is placed by <see cref="MapPixel"/> and may sit between
        /// tiles, over several of them, or over none -- so there is no tile to batch it under
        /// and no layer number that would put it anywhere sensible. It goes last, over
        /// everything the map is made of, which is what writing on a map means.
        /// </para>
        /// <para>
        /// Given the whole set, as the tiles are, and for the same reason: what is on screen
        /// is the implementation's to work out from the canvas clip. Labels are counted in
        /// dozens rather than thousands, so this is a courtesy rather than the difference
        /// batching makes to the tiles.
        /// </para>
        /// </summary>
        Task RenderLabels(SKCanvas canvas, RenderFrame frame, IReadOnlyList<MapLabel> labels);
    }
}
