using System;
using NWorld.Map.Models;
using SkiaSharp;

namespace NWorld.MapServices.MapRenderComponents
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
        /// Only sound for a paint whose shader is anchored to the canvas rather than to the
        /// rect being filled, which is true of every shader here: they are built with a scale
        /// or a repeat and no translation, so what lands on a pixel depends on where the pixel
        /// is and not on which draw covered it. Given that, any decomposition of an area into
        /// rects paints identical pixels -- verified by checksum, one rect per tile against one
        /// per row.
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

        private static void Draw(SKCanvas canvas, SKPaint paint, int x, int y, int length, int tileSize) =>
            canvas.DrawRect(
                SKRect.Create(x * tileSize, y * tileSize, length * tileSize, tileSize),
                paint);
    }
}
