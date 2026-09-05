using System;

namespace NWorld.Map.Models
{
    /// <summary>
    /// A point on the map in pixels rather than in tiles, so it can fall anywhere -- including
    /// inside a tile, or on the line between two of them.
    /// <para>
    /// What <see cref="TileCoordinate"/> is not, and deliberately: a tile coordinate names a
    /// square, and there is no way to say "a third of the way across it". Anything placed on
    /// the map by eye rather than by square -- a label above all -- needs the finer address,
    /// and needs it to survive a zoom.
    /// </para>
    /// <para>
    /// Which is what <see cref="PixelsPerTile"/> is for. A pixel on screen is worth a different
    /// amount of map at every zoom level, so "pixels" alone would name a different place on
    /// every notch of the wheel. This space is pinned to the tiles instead: one tile is always
    /// <see cref="PixelsPerTile"/> of these, whatever the view is doing, and a point stored in
    /// it stays over the same piece of ground for ever. <see cref="Canvas"/> converts to what
    /// the renderer is actually drawing at.
    /// </para>
    /// </summary>
    /// <param name="X">Distance from the map's left edge, in map pixels.</param>
    /// <param name="Y">Distance from the map's top edge, in map pixels.</param>
    public readonly record struct MapPixel(double X, double Y)
    {
        /// <summary>
        /// How many map pixels one tile is worth. The whole of this space's meaning.
        /// <para>
        /// 32 because that is <see cref="MapViewOptions.TileSize"/>'s default: at the zoom the
        /// map opens at, a map pixel is a screen pixel and the numbers in a saved file are the
        /// ones somebody would have measured off the picture. It is a scale and not a limit --
        /// the coordinates are doubles and nothing rounds to a tile.
        /// </para>
        /// <para>
        /// Never change it. It is written into every saved position, and halving it would move
        /// every label on every map ever saved.
        /// </para>
        /// </summary>
        public const int PixelsPerTile = 32;

        /// <summary>The point at a position given in tiles and fractions of a tile.</summary>
        public static MapPixel FromTiles(double x, double y) =>
            new(x * PixelsPerTile, y * PixelsPerTile);

        /// <summary>
        /// This point in the coordinates a render function draws in, where a tile is
        /// <paramref name="tileSize"/> pixels across. Undoes <see cref="FromTiles"/> at the
        /// zoom actually in force.
        /// </summary>
        public (float X, float Y) Canvas(int tileSize) => (
            (float)(X * tileSize / PixelsPerTile),
            (float)(Y * tileSize / PixelsPerTile));

        /// <summary>Which tile this point falls in.</summary>
        public TileCoordinate Tile() => new(
            (int)Math.Floor(X / PixelsPerTile),
            (int)Math.Floor(Y / PixelsPerTile));
    }
}
