using NWorld.Map.Interfaces;
using NWorld.Map.Models;
using NWorld.MapServices.MapRenderComponents.StandardRenderer;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace NWorld.MapServices.Renderers
{
    /// <summary>
    /// Sorts a screenful of tiles into one batch per (layer, component type) and hands each
    /// batch to its render function in a single call.
    /// <para>
    /// Tiles the canvas cannot show are dropped before they reach a batch. Skia would clip
    /// them anyway, but only after they had cost a dictionary walk, a placement, and a rect,
    /// and a map is mostly off screen the moment it is bigger than the window: the work of a
    /// frame follows the size of the map rather than the size of the window, and a large map
    /// pays it on every frame it draws.
    /// </para>
    /// <para>
    /// The instance owns its batch buffers and reuses them frame to frame, so a steady view
    /// settles into allocating nothing per frame. That also makes an instance single-threaded
    /// by construction: give each rendering thread its own.
    /// </para>
    /// </summary>
    public class StandardRenderer : IMapRenderer
    {
        private readonly Dictionary<BatchKey, int> _index = [];
        private readonly List<BatchKey> _keys = [];
        private readonly List<Batch> _batches = [];
        private int _drawing;

        public async Task RenderTiles(SKCanvas canvas, RenderFrame frame, IReadOnlyList<MapTile> tiles)
        {
            ArgumentNullException.ThrowIfNull(canvas);
            ArgumentNullException.ThrowIfNull(tiles);

            // Sharing an instance across threads corrupts the batch buffers, and does it far
            // from here: what surfaces is an enumeration failure inside a draw, which says
            // nothing about the cause. Cheaper to refuse.
            if (Interlocked.Exchange(ref _drawing, 1) == 1)
            {
                throw new InvalidOperationException(
                    $"{nameof(StandardRenderer)} reuses its batch buffers between frames and cannot " +
                    "draw two frames at once. Give each rendering thread its own instance.");
            }

            try
            {
                await Draw(canvas, frame, tiles);
            }
            finally
            {
                Volatile.Write(ref _drawing, 0);
            }
        }

        private async Task Draw(SKCanvas canvas, RenderFrame frame, IReadOnlyList<MapTile> tiles)
        {
            Collect(canvas, frame.TileSize, tiles);

            // Ascending layer, and within a layer the order the component types were first
            // seen. List.Sort is unstable, so the layer is not the whole comparison -- the
            // insertion sequence breaks the tie and keeps a frame reproducible.
            _keys.Sort(static (a, b) =>
                a.Layer != b.Layer ? a.Layer.CompareTo(b.Layer) : a.Sequence.CompareTo(b.Sequence));

            foreach (var key in _keys)
            {
                var batch = _batches[_index[key]];
                if (batch.Count == 0)
                    continue;

                await RenderHelperFunctions.RenderComponent(
                    new TileRenderContext(canvas, frame, batch.Placements, batch.Count),
                    key.ComponentType);
            }
        }

        private void Collect(SKCanvas canvas, int tileSize, IReadOnlyList<MapTile> tiles)
        {
            foreach (var batch in _batches)
                batch.Count = 0;
            _keys.Clear();

            var (minX, minY, maxX, maxY) = VisibleTiles(canvas, tileSize);

            // Walked as a span where the list allows it. Every tile costs an indirect call
            // through the interface otherwise, and on a large map most of those calls exist
            // only to fetch a tile that the next four comparisons throw away: at a million
            // tiles that is several milliseconds of every frame.
            switch (tiles)
            {
                // A window over a grid: whole rows, contiguous, no indexing through the
                // list at all. This is the shape the control hands over for the map pass.
                case ITileRows grid:
                    for (var row = 0; row < grid.RowCount; row++)
                        Collect(grid.Row(row), minX, minY, maxX, maxY);
                    break;

                case MapTile[] array:
                    Collect(array.AsSpan(), minX, minY, maxX, maxY);
                    break;

                case List<MapTile> list:
                    Collect(CollectionsMarshal.AsSpan(list), minX, minY, maxX, maxY);
                    break;

                default:
                    foreach (var tile in tiles)
                        Collect(tile, minX, minY, maxX, maxY);
                    break;
            }
        }

        private void Collect(ReadOnlySpan<MapTile> tiles, int minX, int minY, int maxX, int maxY)
        {
            foreach (var tile in tiles)
                Collect(tile, minX, minY, maxX, maxY);
        }

        /// <summary>
        /// Adds one tile's components to their batches, unless the tile is off screen.
        /// </summary>
        private void Collect(MapTile? tile, int minX, int minY, int maxX, int maxY)
        {
            if (tile is null)
                return;

            // Four comparisons, which is what the rest of this method is being spared.
            if (tile.X < minX || tile.X > maxX || tile.Y < minY || tile.Y > maxY)
                return;

            foreach (var (layer, component) in tile.MapRenderComponents)
            {
                if (component is null)
                    continue;

                // Empty is the default type and means the layer is unoccupied. Dropped
                // here rather than dispatched to a renderer that draws nothing, so a
                // sparsely populated map does not build batches it will never draw.
                if (component.ComponentType == MapRenderComponentConstants.Empty)
                    continue;

                Add(layer, component.ComponentType, new TilePlacement(tile.X, tile.Y, component.Params ?? []));
            }
        }

        private void Add(int layer, Guid componentType, TilePlacement placement)
        {
            // Sequence is deliberately not part of equality -- the lookup has to find the
            // batch whatever order this frame happened to meet the component types in.
            var key = new BatchKey(layer, componentType, _keys.Count);

            if (_index.TryGetValue(key, out var slot))
            {
                // Already seen this frame if its batch is non-empty; otherwise it is a batch
                // left over from an earlier frame and needs putting back into the draw order.
                if (_batches[slot].Count == 0)
                    _keys.Add(key);
            }
            else
            {
                slot = _batches.Count;
                _index[key] = slot;
                _batches.Add(new Batch());
                _keys.Add(key);
            }

            _batches[slot].Add(placement);
        }

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
        private static (int MinX, int MinY, int MaxX, int MaxY) VisibleTiles(SKCanvas canvas, int tileSize)
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

        /// <summary>
        /// A layer and a component type. <see cref="Sequence"/> rides along to order the
        /// component types within a layer and is excluded from equality and hashing.
        /// </summary>
        private readonly struct BatchKey(int layer, Guid componentType, int sequence)
            : IEquatable<BatchKey>
        {
            public int Layer { get; } = layer;
            public Guid ComponentType { get; } = componentType;
            public int Sequence { get; } = sequence;

            public bool Equals(BatchKey other) => Layer == other.Layer && ComponentType == other.ComponentType;
            public override bool Equals(object? obj) => obj is BatchKey other && Equals(other);
            public override int GetHashCode() => HashCode.Combine(Layer, ComponentType);
        }

        private sealed class Batch
        {
            public TilePlacement[] Placements = new TilePlacement[64];
            public int Count;

            public void Add(TilePlacement placement)
            {
                if (Count == Placements.Length)
                    Array.Resize(ref Placements, Placements.Length * 2);

                Placements[Count++] = placement;
            }
        }
    }
}
