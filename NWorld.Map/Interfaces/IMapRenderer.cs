using NWorld.Map.Models;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Text;

namespace NWorld.Map.Interfaces
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
    }
}
