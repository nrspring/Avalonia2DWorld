using System;
using System.Collections;
using System.Collections.Generic;
using NWorld.Map.Interfaces;

namespace NWorld.Map.Models
{
    /// <summary>
    /// A rectangular block of tiles in reading order, published together with the shape that
    /// makes it navigable.
    /// <para>
    /// A flat list can only be searched. Everything that wants part of a map -- the control
    /// asking for the screenful it can see, the inset asking how big the map is -- then has
    /// to walk all of it, and does so on every frame. Carrying four integers alongside the
    /// tiles turns each of those questions into arithmetic, which is what keeps the cost of a
    /// frame tied to the size of the window rather than the size of the map.
    /// </para>
    /// <para>
    /// Held as rows rather than as one array, so that an edit can replace the rows it touches
    /// and share the rest with the generation before it. That is what makes publishing a map
    /// cheap: see <see cref="TileMap"/>.
    /// </para>
    /// <para>
    /// Still an <see cref="IReadOnlyList{T}"/> over the same tiles in the same order, so
    /// anything that only wants the tiles is unaffected -- and bound by the same rule as
    /// ever: a published grid, and every row in it, is never written to again.
    /// </para>
    /// </summary>
    public sealed class TileGrid : IReadOnlyList<MapTile>, ITileRows
    {
        private readonly MapTile[][] _rows;

        /// <param name="rows">
        /// <paramref name="height"/> rows of <paramref name="width"/> tiles, top to bottom
        /// and left to right. Held by reference, not copied.
        /// </param>
        public TileGrid(MapTile[][] rows, int width, int height, int originX = 0, int originY = 0)
        {
            ArgumentNullException.ThrowIfNull(rows);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

            if (rows.Length != height)
                throw new ArgumentException($"A grid {height} tiles high needs {height} rows; got {rows.Length}.", nameof(rows));

            _rows = rows;
            Width = width;
            Height = height;
            OriginX = originX;
            OriginY = originY;
        }

        public int Width { get; }

        public int Height { get; }

        /// <summary>Map coordinate of the top-left tile.</summary>
        public int OriginX { get; }

        /// <inheritdoc cref="OriginX"/>
        public int OriginY { get; }

        public int Count => Width * Height;

        public int RowCount => Height;

        public MapTile this[int index] => _rows[index / Width][index % Width];

        /// <summary>Whether <paramref name="coordinate"/> falls inside the grid.</summary>
        public bool Contains(TileCoordinate coordinate) =>
            coordinate.X >= OriginX && coordinate.X < OriginX + Width &&
            coordinate.Y >= OriginY && coordinate.Y < OriginY + Height;

        /// <summary>The tile at a map coordinate, or null when it falls outside the grid.</summary>
        public MapTile? At(int x, int y) =>
            Contains(new TileCoordinate(x, y)) ? _rows[y - OriginY][x - OriginX] : null;

        /// <summary>Row <paramref name="index"/> counted from the top of the grid.</summary>
        public ReadOnlySpan<MapTile> Row(int index) => _rows[index];

        /// <summary>
        /// Part of a row, in map coordinates. The caller is expected to have clamped both
        /// <paramref name="x"/> and <paramref name="length"/> to the grid.
        /// </summary>
        internal ReadOnlySpan<MapTile> RowSlice(int y, int x, int length) =>
            _rows[y - OriginY].AsSpan(x - OriginX, length);

        /// <summary>
        /// The tiles inside a rectangle given in map coordinates, inclusive, as a view over
        /// this grid rather than a copy of it. A rectangle that misses the grid entirely
        /// gives an empty window.
        /// </summary>
        public TileWindow Window(int minX, int minY, int maxX, int maxY)
        {
            var left = Math.Max(minX, OriginX);
            var top = Math.Max(minY, OriginY);
            var right = Math.Min(maxX, OriginX + Width - 1);
            var bottom = Math.Min(maxY, OriginY + Height - 1);

            return right < left || bottom < top
                ? new TileWindow(this, OriginX, OriginY, 0, 0)
                : new TileWindow(this, left, top, right - left + 1, bottom - top + 1);
        }

        public IEnumerator<MapTile> GetEnumerator()
        {
            foreach (var row in _rows)
            {
                foreach (var tile in row)
                    yield return tile;
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>
    /// A rectangular part of a <see cref="TileGrid"/>, presented as a tile list without
    /// copying a tile.
    /// <para>
    /// What the control hands a renderer when only a screenful is in view. Cheap to make --
    /// four integers and a reference -- so one per frame costs nothing, and the tiles it
    /// exposes are the grid's own, which makes it safe to hand to the render thread on
    /// exactly the same terms as the grid.
    /// </para>
    /// </summary>
    public sealed class TileWindow(TileGrid grid, int minX, int minY, int columns, int rows)
        : IReadOnlyList<MapTile>, ITileRows
    {
        /// <summary>Map coordinate of the window's top-left tile.</summary>
        public int MinX { get; } = minX;

        /// <inheritdoc cref="MinX"/>
        public int MinY { get; } = minY;

        public int Columns { get; } = columns;

        public int Rows { get; } = rows;

        public int Count => Columns * Rows;

        public int RowCount => Rows;

        public MapTile this[int index] =>
            grid.RowSlice(MinY + (index / Columns), MinX + (index % Columns), 1)[0];

        public ReadOnlySpan<MapTile> Row(int index) =>
            Columns == 0 ? default : grid.RowSlice(MinY + index, MinX, Columns);

        public IEnumerator<MapTile> GetEnumerator()
        {
            for (var row = 0; row < Rows; row++)
            {
                for (var column = 0; column < Columns; column++)
                    yield return grid.RowSlice(MinY + row, MinX + column, 1)[0];
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
