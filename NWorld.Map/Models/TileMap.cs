using System;
using System.Collections.Generic;

namespace NWorld.Map.Models
{
    /// <summary>
    /// A rectangular block of tiles that can be edited while it is being drawn.
    /// <para>
    /// Owned by the view model, never by the control. <see cref="Tiles"/> is what goes to
    /// <see cref="Controls.MapView"/>, and every edit goes through <see cref="Edit"/>.
    /// </para>
    /// <para>
    /// A tile is never written to once published. An edit clones the tile, changes the clone,
    /// and puts it in a fresh row -- copy-on-write. That is not tidiness: the renderer walks
    /// these tiles on Avalonia's render thread, and editing a
    /// <see cref="MapTile.MapRenderComponents"/> dictionary while it is being enumerated
    /// throws out of the compositor, intermittently and nowhere near the cause.
    /// </para>
    /// <para>
    /// The unit of copying is the row. An edit replaces the rows it touches and shares every
    /// other row with the generation before it, so publishing costs the rows that changed
    /// plus one array of row references -- kilobytes on a map where copying the whole thing
    /// would be megabytes. A hover, which is two tiles and usually two rows, is what this is
    /// for: it happens on every mouse move.
    /// </para>
    /// </summary>
    public sealed class TileMap
    {
        private readonly MapTile[][] _rows;

        /// <summary>
        /// Rows already replaced during the edit in progress, so that a second change to the
        /// same row edits the copy this edit made rather than copying it again.
        /// </summary>
        private readonly HashSet<int> _replaced = [];

        /// <summary>
        /// Creates a map covering <paramref name="width"/> x <paramref name="height"/> tiles
        /// with its top-left at (<paramref name="originX"/>, <paramref name="originY"/>).
        /// </summary>
        /// <param name="fill">
        /// Builds the tile at a coordinate. Called once per tile, in reading order.
        /// </param>
        public TileMap(int width, int height, int originX = 0, int originY = 0, Func<TileCoordinate, MapTile>? fill = null)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

            Width = width;
            Height = height;
            OriginX = originX;
            OriginY = originY;

            _rows = new MapTile[height][];

            // Reading order, which is also the order the renderer prefers: components are
            // free to coalesce runs of adjacent tiles, and none of them require it.
            for (var y = 0; y < height; y++)
            {
                var row = new MapTile[width];

                for (var x = 0; x < width; x++)
                {
                    var coordinate = new TileCoordinate(originX + x, originY + y);
                    row[x] = fill?.Invoke(coordinate)
                        ?? new MapTile { X = coordinate.X, Y = coordinate.Y };
                }

                _rows[y] = row;
            }

            Tiles = Publish();
        }

        public int Width { get; }

        public int Height { get; }

        public int OriginX { get; }

        public int OriginY { get; }

        /// <summary>
        /// The current tiles, for handing to <see cref="Controls.MapView.Tiles"/>. A new grid
        /// after every <see cref="Edit"/>, so binding to it repaints; the one previously
        /// handed out keeps the rows it was published with and stays safe for a frame that is
        /// still being drawn.
        /// </summary>
        public TileGrid Tiles { get; private set; }

        /// <summary>Whether <paramref name="coordinate"/> falls inside the map.</summary>
        public bool Contains(TileCoordinate coordinate) =>
            coordinate.X >= OriginX && coordinate.X < OriginX + Width &&
            coordinate.Y >= OriginY && coordinate.Y < OriginY + Height;

        /// <summary>
        /// The tile at <paramref name="coordinate"/>, or null if it is off the map. Read-only:
        /// change a tile through <see cref="Edit"/>, never by writing to what this returns.
        /// </summary>
        public MapTile? this[TileCoordinate coordinate] =>
            Contains(coordinate)
                ? _rows[coordinate.Y - OriginY][coordinate.X - OriginX]
                : null;

        /// <summary>
        /// A second map over the same tiles, which goes on standing still while this one is
        /// edited.
        /// <para>
        /// For the caller that has to file a map away and then change it in place -- undo,
        /// which keeps whole maps. A pass that builds a new <see cref="TileMap"/> can file the
        /// old one as it is, because nothing will touch it again; an edit to a handful of
        /// tiles has no new map to file, and filing this one would file the very map about to
        /// change.
        /// </para>
        /// <para>
        /// The tiles are shared, not copied, and that is the whole point: a tile is never
        /// written to once published -- an edit clones it -- so a snapshot that shares them
        /// keeps every tile exactly as it was however much the live map moves on. What it
        /// costs is one reference per tile: about a megabyte on a quarter-million-tile map,
        /// against the hundreds a real copy would take.
        /// </para>
        /// </summary>
        public TileMap Snapshot() =>
            new(Width, Height, OriginX, OriginY, coordinate => this[coordinate]!);

        /// <summary>
        /// Applies every change in <paramref name="edits"/> and then publishes once.
        /// <para>
        /// Batched rather than one call per tile because the interesting edits come in
        /// groups: moving a hover clears the old tile and sets the new one, and publishing
        /// between the two puts a frame on screen with the highlight on neither.
        /// </para>
        /// </summary>
        public void Edit(Action<Editor> edits)
        {
            ArgumentNullException.ThrowIfNull(edits);

            _replaced.Clear();
            edits(new Editor(this));
            Tiles = Publish();
        }

        /// <summary>
        /// Snapshots the rows. Only the array of row references is copied: the rows in it are
        /// shared with the grid published before this one, which is sound because a row is
        /// replaced wholesale when it changes and never written to in place.
        /// </summary>
        private TileGrid Publish() =>
            new((MapTile[][])_rows.Clone(), Width, Height, OriginX, OriginY);

        /// <summary>
        /// Replaces the row containing <paramref name="y"/> with a copy, unless the edit in
        /// progress has already done so, and returns it ready to be written to.
        /// </summary>
        private MapTile[] MutableRow(int y)
        {
            var index = y - OriginY;

            if (_replaced.Add(index))
                _rows[index] = (MapTile[])_rows[index].Clone();

            return _rows[index];
        }

        /// <summary>
        /// Edits tiles inside an <see cref="Edit"/> call. Only valid for the duration of that
        /// call -- holding one and using it later writes changes that are never published.
        /// </summary>
        public readonly struct Editor(TileMap map)
        {
            /// <summary>
            /// Replaces the tile at <paramref name="coordinate"/> with a clone that
            /// <paramref name="change"/> has been applied to. A coordinate off the map is
            /// ignored, which keeps the caller from having to bounds-check the pointer.
            /// </summary>
            public void Update(TileCoordinate coordinate, Action<MapTile> change)
            {
                ArgumentNullException.ThrowIfNull(change);

                if (!map.Contains(coordinate))
                    return;

                var row = map.MutableRow(coordinate.Y);
                var column = coordinate.X - map.OriginX;

                var replacement = row[column].Clone();
                change(replacement);
                row[column] = replacement;
            }
        }
    }
}
