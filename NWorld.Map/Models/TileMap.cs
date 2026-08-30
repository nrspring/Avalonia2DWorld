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
    /// and drops it into a fresh list -- copy-on-write. That is not tidiness: the renderer
    /// walks these tiles on Avalonia's render thread, and editing a
    /// <see cref="MapTile.MapRenderComponents"/> dictionary while it is being enumerated
    /// throws out of the compositor, intermittently and nowhere near the cause.
    /// </para>
    /// </summary>
    public sealed class TileMap
    {
        private readonly MapTile[] _tiles;

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

            _tiles = new MapTile[width * height];

            // Reading order, which is also the order the renderer prefers: components are
            // free to coalesce runs of adjacent tiles, and none of them require it.
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var coordinate = new TileCoordinate(originX + x, originY + y);
                    _tiles[(y * width) + x] = fill?.Invoke(coordinate)
                        ?? new MapTile { X = coordinate.X, Y = coordinate.Y };
                }
            }

            Tiles = Publish();
        }

        public int Width { get; }

        public int Height { get; }

        public int OriginX { get; }

        public int OriginY { get; }

        /// <summary>
        /// The current tiles, for handing to <see cref="Controls.MapView.Tiles"/>. A new list
        /// after every <see cref="Edit"/>, so binding to it repaints; the one previously
        /// handed out is left untouched and stays safe for a frame still being drawn.
        /// </summary>
        public IReadOnlyList<MapTile> Tiles { get; private set; }

        /// <summary>Whether <paramref name="coordinate"/> falls inside the map.</summary>
        public bool Contains(TileCoordinate coordinate) =>
            coordinate.X >= OriginX && coordinate.X < OriginX + Width &&
            coordinate.Y >= OriginY && coordinate.Y < OriginY + Height;

        /// <summary>
        /// The tile at <paramref name="coordinate"/>, or null if it is off the map. Read-only:
        /// change a tile through <see cref="Edit"/>, never by writing to what this returns.
        /// </summary>
        public MapTile? this[TileCoordinate coordinate] =>
            TryGetIndex(coordinate, out var index) ? _tiles[index] : null;

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

            edits(new Editor(this));
            Tiles = Publish();
        }

        private bool TryGetIndex(TileCoordinate coordinate, out int index)
        {
            if (!Contains(coordinate))
            {
                index = -1;
                return false;
            }

            index = ((coordinate.Y - OriginY) * Width) + (coordinate.X - OriginX);
            return true;
        }

        /// <summary>
        /// Snapshots the working array. The copy is what callers get, so an edit landing in
        /// <see cref="_tiles"/> cannot alter a list the render thread is already reading.
        /// </summary>
        /// <remarks>
        /// One array copy per edit -- 8 bytes a tile. Past about 10,600 tiles that clears the
        /// 85,000-byte large-object threshold and every publish lands on the LOH, which at
        /// drag speed is real gen2 pressure. The way out is to stop copying and swap the slot
        /// in place instead (<c>Volatile.Write(ref _tiles[index], replacement)</c>, publishing
        /// the same array every time), which works because an array enumerator has no version
        /// check to trip -- but it then needs a revision the control can watch, since the list
        /// reference no longer changes and nothing would invalidate the visual.
        /// </remarks>
        private MapTile[] Publish() => (MapTile[])_tiles.Clone();

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

                if (!map.TryGetIndex(coordinate, out var index))
                    return;

                var replacement = map._tiles[index].Clone();
                change(replacement);
                map._tiles[index] = replacement;
            }
        }
    }
}
