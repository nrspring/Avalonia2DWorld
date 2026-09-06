using NWorld.Map.Interfaces;
using NWorld.Map.Models;
using NWorld.MapServices.Constants;
using NWorld.MapServices.MapRenderComponents.StandardRenderer;
using NWorld.MapServices.MapRenderComponents.StandardRenderer.RenderingFunctions;
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

        /// <summary>
        /// The line where the land meets the water, and the picture of it kept between frames.
        /// Held per renderer rather than statically, because it holds both a path and a surface
        /// that only one thread may be drawing at a time -- see <see cref="CoastlineCache"/>.
        /// </summary>
        private readonly CoastlineCache _coast = new();

        /// <summary>
        /// The slope where one depth of water meets another. Held per renderer for the reason
        /// the coast is -- see <see cref="WaterDepth"/>.
        /// <para>
        /// Not kept as a picture the way the coast is, and cannot be: it darkens the sea by
        /// multiplying into it, so what it draws depends on the water underneath and it has to
        /// be drawn against that water every frame. See <see cref="CoastlineCache"/>.
        /// </para>
        /// </summary>
        private readonly WaterDepth _depth = new();

        /// <summary>
        /// Below this many pixels a tile, the whole tile pass is drawn once and then held --
        /// see <see cref="_still"/>.
        /// <para>
        /// Eight because that is about where the moving part of the map stops being motion and
        /// becomes noise. A wave is a feature a few pixels across; drawn on a tile eight pixels
        /// wide there is nothing left of it to follow, and the frames spent redrawing it are
        /// spent on a shimmer nobody is watching. The two thresholds already on the map were set
        /// on the same judgement and land either side of this one: the drop-off gives up at
        /// twelve and the shore at six, both because a few pixels of a blurred thing is a smudge
        /// rather than the thing.
        /// </para>
        /// <para>
        /// It is the one number here worth arguing with. Raise it and more of the map is held
        /// still, which is faster and, past some point, visibly dead; lower it and the water runs
        /// further out at the cost of the frames it takes to run.
        /// </para>
        /// </summary>
        public const int StillTileSize = 8;

        /// <summary>
        /// The whole tile pass, held as a picture while the tiles are too small to be worth
        /// redrawing -- see <see cref="StillTileSize"/> and <see cref="HeldPicture"/>.
        /// <para>
        /// This is the one thing put through <see cref="HeldPicture"/> that <em>does</em> read
        /// the clock, and holding it deliberately stops that clock: below this zoom the water is
        /// frozen. That is the whole of the trade and it is meant. What it buys is the rest of
        /// the pass -- and zoomed out that is nearly the entire frame, because the work follows
        /// the number of tiles on screen and there are tens of thousands of them. Measured on a
        /// 600x400 world at six pixels a tile, the tile pass was 63 of a 71 millisecond frame.
        /// </para>
        /// <para>
        /// It is safe to hold as one picture for the reason the drop-off is not safe to hold on
        /// its own: everything that reads a backdrop is in here <em>with</em> its backdrop. The
        /// ground is laid down first inside the same picture, so the water has its sea to
        /// multiply into and the shade its ground to lift, exactly as on the canvas. Nothing may
        /// be lifted out of this and held separately without answering that question again.
        /// </para>
        /// </summary>
        private readonly HeldPicture _still = new();

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

        /// <summary>
        /// Draws the labels placed on the map, on top of everything above.
        /// <para>
        /// Shared with every other renderer rather than given a look of its own. A label's
        /// colours are the label's, chosen by whoever wrote it; a picture that restyled them
        /// would be a picture that changed what somebody said.
        /// </para>
        /// </summary>
        public Task RenderLabels(SKCanvas canvas, RenderFrame frame, IReadOnlyList<MapLabel> labels) =>
            RenderMapLabel.Render(canvas, frame, labels);

        private async Task Draw(SKCanvas canvas, RenderFrame frame, IReadOnlyList<MapTile> tiles)
        {
            // Small enough that the map is worth holding still. The pass below runs once into a
            // picture and the frames after this one blit it -- and it is run here, synchronously,
            // on the same thread and inside the same guard as an ordinary frame. Every render
            // function completes synchronously; the Task on IMapRenderer is there for the ones
            // that may not always, and MapView unwraps them on the same terms.
            if (frame.TileSize < StillTileSize && frame.World is not null && frame.Gpu is { } gpu &&
                _still.Draw(canvas, frame, gpu, held => DrawAll(held, frame, tiles).GetAwaiter().GetResult()))
            {
                return;
            }

            await DrawAll(canvas, frame, tiles);
        }

        private async Task DrawAll(SKCanvas canvas, RenderFrame frame, IReadOnlyList<MapTile> tiles)
        {
            Collect(canvas, frame.TileSize, tiles);

            // Ascending layer, and within a layer the order the component types were first
            // seen. List.Sort is unstable, so the layer is not the whole comparison -- the
            // insertion sequence breaks the tie and keeps a frame reproducible.
            _keys.Sort(static (a, b) =>
                a.Layer != b.Layer ? a.Layer.CompareTo(b.Layer) : a.Sequence.CompareTo(b.Sequence));

            var coastDrawn = false;

            foreach (var key in _keys)
            {
                // As the ground finishes and before anything standing on it. The coast is
                // drawn over the tiles either side of it -- that is how it hides the corners of
                // the grid, see Coastline -- so it has to come after the last of them; and it
                // is part of the ground rather than something built on it, so a hover, a road
                // or a town belongs on top of it and not under it.
                if (!coastDrawn && key.Layer > RenderComponentLayers.BaseGround)
                {
                    Edges(canvas, frame, tiles);
                    coastDrawn = true;
                }

                var batch = _batches[_index[key]];
                if (batch.Count == 0)
                    continue;

                await RenderHelperFunctions.RenderComponent(
                    new TileRenderContext(canvas, frame, batch.Placements, batch.Count),
                    key.ComponentType);
            }

            // A map with nothing on it but ground never crossed out of that layer above.
            if (!coastDrawn)
                Edges(canvas, frame, tiles);
        }

        /// <summary>
        /// Everything that happens at a boundary between two tiles rather than on one: the
        /// slope out into deeper water, and then the shore.
        /// <para>
        /// The sea is finished before the coast is laid over it, which is the only order these
        /// two can go in -- the shore is drawn onto the water, so the water underneath it had
        /// better be the water it is going to end up being.
        /// </para>
        /// </summary>
        private void Edges(SKCanvas canvas, RenderFrame frame, IReadOnlyList<MapTile> tiles)
        {
            _depth.Render(canvas, frame, tiles);
            _coast.Render(canvas, frame, tiles);
        }

        private void Collect(SKCanvas canvas, int tileSize, IReadOnlyList<MapTile> tiles)
        {
            foreach (var batch in _batches)
                batch.Count = 0;
            _keys.Clear();

            var (minX, minY, maxX, maxY) = VisibleTiles.For(canvas, tileSize);

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

                Add(
                    layer,
                    component.ComponentType,
                    new TilePlacement(tile.X, tile.Y, component.Params ?? MapRenderComponent.None));
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
