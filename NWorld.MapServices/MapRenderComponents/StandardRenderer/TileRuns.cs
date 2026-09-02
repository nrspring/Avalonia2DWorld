using System;
using NWorld.Map.Models;
using SkiaSharp;

namespace NWorld.MapServices.MapRenderComponents.StandardRenderer
{
    /// <summary>
    /// Covers a batch of tiles with a paint, using as few rects as the batch allows.
    /// </summary>
    internal static class TileRuns
    {
        /// <summary>
        /// Fills every tile in <paramref name="tiles"/> with <paramref name="paint"/>, one rect
        /// per run of horizontally adjacent tiles.
        /// <para>
        /// Only sound for a paint whose colour at a pixel depends on where that pixel is and
        /// not on which draw covered it: a solid colour, or a shader anchored to the canvas
        /// rather than to the rect being filled. Every shader passed here is built with a
        /// scale or a repeat and no translation, so all of them qualify. Given that, any
        /// decomposition of an area into rects paints identical pixels -- verified by
        /// checksum, one rect per tile against one per row.
        /// </para>
        /// <para>
        /// A translucent paint additionally needs antialiasing off. Runs share their edges
        /// exactly, and an antialiased edge gives both sides partial coverage, which a
        /// translucent fill shows as a seam.
        /// </para>
        /// <para>
        /// Runs are found by walking the batch in the order the renderer collected it and
        /// extending while the next tile is the previous one's right-hand neighbour. A batch
        /// that arrives in reading order collapses to about one rect per row; one that arrives
        /// shuffled still draws correctly, just with more rects.
        /// </para>
        /// </summary>
        public static void Fill(SKCanvas canvas, ReadOnlySpan<TilePlacement> tiles, int tileSize, SKPaint paint)
        {
            if (tiles.Length == 0)
                return;

            var runX = tiles[0].X;
            var runY = tiles[0].Y;
            var runLength = 1;

            for (var i = 1; i < tiles.Length; i++)
            {
                var tile = tiles[i];

                if (tile.Y == runY && tile.X == runX + runLength)
                {
                    runLength++;
                    continue;
                }

                Draw(canvas, paint, runX, runY, runLength, tileSize);
                runX = tile.X;
                runY = tile.Y;
                runLength = 1;
            }

            Draw(canvas, paint, runX, runY, runLength, tileSize);
        }

        /// <summary>
        /// Fills every tile with the paint <paramref name="paintFor"/> chooses for it, one rect
        /// per run of horizontally adjacent tiles that were given the same one.
        /// <para>
        /// A tile whose paint comes back null is not drawn, and ends the run it was in. That is
        /// what lets a caller cover only some of a batch -- the elevation shade covers the high
        /// ground and leaves everything at sea level and flat land alone -- without first
        /// having to sort the batch into the tiles it wants and the tiles it does not.
        /// </para>
        /// <para>
        /// Paints are compared by reference, so a caller has to hand back the same instance for
        /// tiles meant to share a run. Choosing from a table rather than building one per tile
        /// is what makes that true, and it is far the cheaper way round in any case.
        /// </para>
        /// <para>
        /// Everything said above about <see cref="Fill(SKCanvas, ReadOnlySpan{TilePlacement}, int, SKPaint)"/>
        /// holds here too: the same constraint on what a paint may be, and the same run-finding.
        /// </para>
        /// </summary>
        public static void Fill(
            SKCanvas canvas,
            ReadOnlySpan<TilePlacement> tiles,
            int tileSize,
            Func<TilePlacement, SKPaint?> paintFor)
        {
            if (tiles.Length == 0)
                return;

            var runPaint = paintFor(tiles[0]);
            var runX = tiles[0].X;
            var runY = tiles[0].Y;
            var runLength = 1;

            for (var i = 1; i < tiles.Length; i++)
            {
                var tile = tiles[i];
                var paint = paintFor(tile);

                if (ReferenceEquals(paint, runPaint) && tile.Y == runY && tile.X == runX + runLength)
                {
                    runLength++;
                    continue;
                }

                if (runPaint is not null)
                    Draw(canvas, runPaint, runX, runY, runLength, tileSize);

                runPaint = paint;
                runX = tile.X;
                runY = tile.Y;
                runLength = 1;
            }

            if (runPaint is not null)
                Draw(canvas, runPaint, runX, runY, runLength, tileSize);
        }

        private static void Draw(SKCanvas canvas, SKPaint paint, int x, int y, int length, int tileSize) =>
            canvas.DrawRect(
                SKRect.Create(x * tileSize, y * tileSize, length * tileSize, tileSize),
                paint);
    }
}
