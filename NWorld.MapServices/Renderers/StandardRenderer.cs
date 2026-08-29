using NWorld.Map.Interfaces;
using NWorld.Map.Models;
using NWorld.MapServices.MapRenderComponents;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace NWorld.MapServices.Renderers
{
    /// <summary>
    /// Sorts a screenful of tiles into one batch per (layer, component type) and hands each
    /// batch to its render function in a single call.
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
            Collect(tiles);

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

        private void Collect(IReadOnlyList<MapTile> tiles)
        {
            foreach (var batch in _batches)
                batch.Count = 0;
            _keys.Clear();

            foreach (var tile in tiles)
            {
                if (tile is null)
                    continue;

                foreach (var (layer, component) in tile.MapRenderComponents)
                {
                    if (component is null)
                        continue;

                    // Empty is the default type and means the layer is unoccupied. Dropped
                    // here rather than dispatched to a renderer that draws nothing, so a
                    // sparsely populated map does not build batches it will never draw.
                    if (component.ComponentType == MapRenderComponentConstants.Empty)
                        continue;

                    Add(layer, component.ComponentType, new TilePlacement(tile.X, tile.Y, component.Params));
                }
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
