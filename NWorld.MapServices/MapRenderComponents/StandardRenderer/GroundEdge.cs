using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using NWorld.Map.Interfaces;
using NWorld.Map.Models;
using NWorld.MapServices.MapRenderComponents.StandardRenderer.RenderingFunctions;
using NWorld.MapServices.Renderers;
using SkiaSharp;
using static NWorld.MapServices.MapRenderComponents.StandardRenderer.RenderNoise;

namespace NWorld.MapServices.MapRenderComponents.StandardRenderer
{
    /// <summary>
    /// Where one ground meets another, drawn as a boundary the ground decided rather than one
    /// the grid did.
    /// <para>
    /// Every ground fills exact squares -- the grass blits a per-tile variant, the bog and the
    /// sand repeat a tiling texture -- so a marsh in a meadow came out as a staircase of tile
    /// corners, which is a picture of the data structure rather than of a marsh. The water has
    /// had an answer to this since there was a <see cref="Coastline"/>, whose whole job is to
    /// draw over the tiles either side of the shore and hide those corners. Land against land
    /// never had one.
    /// </para>
    /// <para>
    /// The shore's answer will not do here, though. A coast is a real band of a third thing --
    /// sand, surf, shallows -- so a soft ribbon drawn along it reads as beach. There is no band
    /// between grass and bog: one ground simply becomes the other. So what happens here is that
    /// the boundary is <b>moved and then mixed</b>: a noise field in map space pushes the line
    /// off the grid it was cut on, and the two grounds are cross-faded through each other across
    /// it.
    /// </para>
    /// <para>
    /// The mixing is why this pass draws <b>both</b> grounds rather than laying one over the
    /// other. It was tried the other way and cannot work: the ground pass has already put the
    /// winner down opaque to its own tile edge, so softening it onto its neighbour fades in from
    /// one side and steps on the other -- from all of a ground to half of it in the width of a
    /// tile edge, which is the square edge this exists to remove, drawn a second time. Fading
    /// back the other way in a second pass fails differently: after the wander and the blur, the
    /// mask never reaches full strength inside a patch a tile or two across, so small marshes
    /// drown in whatever is around them.
    /// </para>
    /// <para>
    /// Owning the band settles both. Over the tiles either side of a boundary the losing ground
    /// is laid down whole and the winning one is faded across it, so what was underneath does not
    /// matter and neither half of the fade has an opaque edge to argue with.
    /// </para>
    /// <para>
    /// The noise is a field in map space, not a per-tile pattern, which is the same reason
    /// <see cref="TiledGround"/> gives for its own arrangement: two neighbours choosing their
    /// own wobble would disagree along the edge they share, and the disagreement would be a
    /// straight line -- the very thing this exists to remove.
    /// </para>
    /// </summary>
    internal sealed class GroundEdge
    {
        /// <summary>
        /// Below this the whole thing is left off. A wobble of a third of a tile inside eight
        /// pixels is two or three pixels of noise on a boundary that is itself a few pixels
        /// long, which is not an organic edge but a ragged one -- and it is at these sizes that
        /// there is most of it to draw. The same trade <see cref="Coastline"/> makes.
        /// </summary>
        public const int MinTileSize = 10;

        /// <summary>
        /// The grounds that meet, in the order they give way. Later cuts into earlier, and that
        /// order is a fact about the world rather than about drawing: a marsh spreads into the
        /// meadow at its margin and sand blows onto grass, while nobody has ever watched a
        /// lawn advance into a bog.
        /// <para>
        /// One winner per pair, always the same one, is what keeps a boundary from being drawn
        /// twice from both sides -- which would put two different wobbles on one edge and leave
        /// the losing ground showing through between them.
        /// </para>
        /// </summary>
        private static readonly Guid[] Grounds =
        [
            MapRenderComponentConstants.Grass,
            MapRenderComponentConstants.Desert,
            MapRenderComponentConstants.Swamp,
        ];

        /// <summary>How to draw each of those, in the same order.</summary>
        private static readonly Func<TileRenderContext, Task>[] Painters =
        [
            RenderGrass.Render,
            RenderDesert.Render,
            RenderSwamp.Render,
        ];

        /// <summary>
        /// How deep into the winning ground its own shape is built, past the band being redrawn,
        /// in tiles. The shape has to be solid where the band ends or the losing ground it is
        /// faded over would show through inside the winner, which is a pale ghost of the grid in
        /// place of a hard line -- no better.
        /// </summary>
        private const int ShapeDepth = 2;

        /// <summary>
        /// How far each of the winning ground's tiles is grown before they are gathered into one
        /// shape, as a fraction of a tile.
        /// <para>
        /// Not an advance and not a bias: it is what makes the tiles into a shape at all. They go
        /// into one path as separate rectangles, and the corner rounding works on each contour it
        /// finds -- so tiles that merely touch are rounded apart into a string of circles, and a
        /// marsh comes out as a handful of blobs with sand showing between them. Grown enough to
        /// overlap, they round as one outline.
        /// </para>
        /// <para>
        /// It does bias the fade outwards by this much, which is not a problem and is arguably
        /// the point: the ground pass has already laid the winner down opaque on its own tiles,
        /// so the mask wants to be solid by the time it reaches them.
        /// </para>
        /// </summary>
        private const float GrowFraction = 0.30f;

        /// <summary>
        /// How far the noise is allowed to push the boundary, as a fraction of a tile. Enough
        /// that the wander is plainly bigger than a tile corner and so cannot read as one; short
        /// of the point where a tile of one ground could be swallowed whole by its neighbour.
        /// </summary>
        private const float WanderFraction = 0.35f;

        /// <summary>
        /// How far the two grounds are mixed through each other, as a fraction of a tile: the
        /// blur on the shape they are cross-faded by.
        /// <para>
        /// It does a different job from the wander. The wander decides <em>where</em> the
        /// boundary runs and takes the grid out of its shape; this decides how hard it lands
        /// once it is there. Kept well under the wander, so the boundary stays a line that
        /// wanders rather than becoming a haze that happens to be somewhere, and small enough
        /// that everything the shape can reach still falls inside <see cref="Reach"/>.
        /// </para>
        /// <para>
        /// The hard limit on it, though, is the smallest thing on the map worth keeping. A fade
        /// this wide either side eats a feature two of them across: taken up to a third of a
        /// tile, marshes a tile or two wide stopped being marshes and became a stain on the sand.
        /// Whatever else it is tuned for, it has to stay well inside half the width of the
        /// narrowest ground anyone draws.
        /// </para>
        /// </summary>
        private const float BlendFraction = 0.10f;

        /// <summary>
        /// How far either side of a boundary tile this pass takes the ground over, in tiles. It
        /// redraws everything it takes, so it wants to be as small as it can be -- and it is
        /// bounded from below by how far the shape can carry: <see cref="WanderFraction"/> of a
        /// push and three times <see cref="BlendFraction"/> of softening, a little over a tile.
        /// One ring gives a tile and a half either side of the boundary, which clears that.
        /// </summary>
        private const int Reach = 1;

        /// <summary>
        /// How hard the corners of the advance are rounded off, as a fraction of a tile. About
        /// half of one: enough to turn a tile corner into a curve, and not so much that a single
        /// tile of ground rounds away to a disc.
        /// </summary>
        private const float RoundingFraction = 0.55f;


        /// <summary>
        /// Cells of noise across the block. Coarse: the edge should wander in bays and
        /// headlands a few tiles across, not fray at the pixel.
        /// </summary>
        private const int NoiseLattice = 6;

        /// <summary>Tiles across one block of the noise field, and so how far it goes before it repeats.</summary>
        private const int NoiseTiles = 16;

        private const long MaxCacheBytes = 32L * 1024 * 1024;

        /// <summary>
        /// The winner's silhouette and the loser's tiles, one of each per ordered pair of
        /// grounds. <see cref="Pair"/> is the index.
        /// </summary>
        private readonly SKPath[] _advance;

        private readonly SKPath[] _losing;
        private readonly HashSet<long>[] _spread;
        private readonly HashSet<long>[] _near;

        private readonly ZoomLevelCache<Wander> _wander;

        private TilePlacement[] _placements = new TilePlacement[256];

        public GroundEdge()
        {
            var pairs = Grounds.Length * Grounds.Length;

            _advance = new SKPath[pairs];
            _losing = new SKPath[pairs];
            _spread = new HashSet<long>[pairs];
            _near = new HashSet<long>[pairs];

            for (var i = 0; i < pairs; i++)
            {
                _advance[i] = new SKPath();
                _losing[i] = new SKPath();
                _spread[i] = [];
                _near[i] = [];
            }

            _wander = new ZoomLevelCache<Wander>(MaxCacheBytes, Build);
        }

        /// <summary>Redraws every ground boundary the canvas can show, wandering.</summary>
        public void Render(SKCanvas canvas, RenderFrame frame, IReadOnlyList<MapTile> tiles)
        {
            var tileSize = frame.TileSize;

            if (canvas is null || tiles is null || frame.World is not { } world || tileSize < MinTileSize)
                return;

            for (var i = 0; i < _advance.Length; i++)
            {
                _advance[i].Rewind();
                _losing[i].Rewind();
                _spread[i].Clear();
                _near[i].Clear();
            }

            var (minX, minY, maxX, maxY) = VisibleTiles.For(canvas, tileSize);

            // Walked as a span where the list allows it, for the reason every other pass over
            // the tiles does the same: most of a large map is off screen, and fetching each tile
            // through the interface only to drop it is milliseconds a frame.
            switch (tiles)
            {
                case ITileRows grid:
                    for (var row = 0; row < grid.RowCount; row++)
                        Collect(grid.Row(row), world, minX, minY, maxX, maxY, tileSize);
                    break;

                case MapTile[] array:
                    Collect(array.AsSpan(), world, minX, minY, maxX, maxY, tileSize);
                    break;

                case List<MapTile> list:
                    Collect(CollectionsMarshal.AsSpan(list), world, minX, minY, maxX, maxY, tileSize);
                    break;

                default:
                    foreach (var tile in tiles)
                        Collect(tile, world, minX, minY, maxX, maxY, tileSize);
                    break;
            }

            for (var winner = 0; winner < Grounds.Length; winner++)
            {
                for (var loser = 0; loser < Grounds.Length; loser++)
                {
                    if (winner == loser)
                        continue;

                    var pair = Pair(winner, loser);

                    if (_advance[pair].IsEmpty || _near[pair].Count == 0)
                        continue;

                    if (_placements.Length < _near[pair].Count)
                        _placements = new TilePlacement[_near[pair].Count * 2];

                    var count = 0;

                    foreach (var packed in _near[pair])
                    {
                        var x = (int)(packed >> 32);
                        var y = (int)packed;

                        _losing[pair].AddRect(SKRect.Create(
                            x * (float)tileSize, y * (float)tileSize, tileSize, tileSize));

                        _placements[count++] = new TilePlacement(x, y, MapRenderComponent.None);
                    }

                    Mix(canvas, frame, _losing[pair], _advance[pair], Painters[winner], count);
                }
            }
        }

        /// <inheritdoc cref="ZoomLevelCache{T}.Prewarm"/>
        public Task Prewarm(int tileSize) => _wander.Prewarm(tileSize);

        /// <inheritdoc cref="ZoomLevelCache{T}.Clear"/>
        public void ClearCache() => _wander.Clear();

        /// <summary>
        /// Fades the winning ground onto its neighbour's tiles, along a boundary that has been
        /// rounded, pushed about by the noise and then softened.
        /// <para>
        /// It only ever <b>adds</b>. Laying the losing ground down first was tried, so that the
        /// fade would have a known backdrop rather than whatever the ground pass left -- and it
        /// works, for one boundary. It cannot work for a map: the bands of different pairs
        /// overlap, so a marsh running near a beach had its pass repaint grass across tiles the
        /// sand had already been faded onto, and the sand-to-grass edge came back hard while
        /// every edge involving the marsh looked right. Adding only, the passes cannot undo one
        /// another and their order stops mattering.
        /// </para>
        /// <para>
        /// What makes that safe is <see cref="GrowFraction"/>: the shape is solid by the time it
        /// reaches the winner's own tiles, where the ground pass has already put that ground down
        /// opaque, so the two meet at full strength and there is no step between them.
        /// </para>
        /// <para>
        /// The winner goes on as a shape filled through <see cref="SKBlendMode.SrcIn"/> rather
        /// than as a ground masked through <c>DstIn</c>. The obvious way round does not survive
        /// an image filter: a filtered draw is composited only across the bounds the filter
        /// reports, and ground lying outside those bounds is never masked at all -- it comes back
        /// as squares of the wrong ground sitting in the middle of its neighbour, cut to the
        /// shape of the batch. Laid down as a shape and filled into, the compositing is layer
        /// against layer and covers everything either of them touches.
        /// </para>
        /// </summary>
        private void Mix(
            SKCanvas canvas,
            RenderFrame frame,
            SKPath band,
            SKPath shape,
            Func<TileRenderContext, Task> winner,
            int count)
        {
            if (band.IsEmpty || count == 0)
                return;

            var checkpoint = canvas.Save();

            try
            {
                // Nothing outside the band is this pass's business, and the band is whole tiles,
                // so the clip is exact and needs no antialiasing.
                canvas.ClipPath(band, SKClipOperation.Intersect, antialias: false);

                var context = new TileRenderContext(canvas, frame, _placements, count);

                canvas.SaveLayer(null);

                try
                {
                    var wander = _wander.Get(frame.TileSize);

                    using (var edge = new SKPaint
                    {
                        // Rounded, then pushed about, then softened -- in that order, because
                        // each works on what the one before it produced. Displacing a staircase
                        // moves the steps without unmaking them and the edge reads as torn paper;
                        // rounding alone gives the same tidy scallop at every tile, which is a
                        // different way of drawing the grid; and softening last is what turns the
                        // line into a mixing of one ground with the other.
                        PathEffect = wander.Corners,
                        ImageFilter = wander.Filter,
                        Color = SKColors.Black,
                        IsAntialias = true,
                    })
                    {
                        canvas.DrawPath(shape, edge);
                    }

                    using var into = new SKPaint { BlendMode = SKBlendMode.SrcIn };

                    canvas.SaveLayer(into);

                    try
                    {
                        winner(context).GetAwaiter().GetResult();
                    }
                    finally
                    {
                        canvas.Restore();
                    }
                }
                finally
                {
                    canvas.Restore();
                }
            }
            finally
            {
                canvas.RestoreToCount(checkpoint);
            }
        }

        private void Collect(
            ReadOnlySpan<MapTile> tiles, TileGrid world, int minX, int minY, int maxX, int maxY, int tileSize)
        {
            foreach (var tile in tiles)
                Collect(tile, world, minX, minY, maxX, maxY, tileSize);
        }

        /// <summary>
        /// Takes one tile that gives way to a neighbour, and notes two things: the neighbour it
        /// gives way to, which is what advances onto it, and the stretch of its own ground around
        /// it, which is where that advance is allowed to show.
        /// </summary>
        private void Collect(
            MapTile? tile, TileGrid world, int minX, int minY, int maxX, int maxY, int tileSize)
        {
            if (tile is null)
                return;

            if (tile.X < minX || tile.X > maxX || tile.Y < minY || tile.Y > maxY)
                return;

            var here = Index(world, tile.X, tile.Y);

            if (here < 0)
                return;

            for (var side = 0; side < 4; side++)
            {
                var (dx, dy) = Outward(side);
                var other = Index(world, tile.X + dx, tile.Y + dy);

                // Only the ground that wins advances, and only onto the one that loses.
                if (other <= here)
                    continue;

                var pair = Pair(other, here);

                Spread(pair, tile.X + dx, tile.Y + dy, tileSize);

                // The band this pass takes over: the tiles of these two grounds within reach of
                // the boundary, on both sides of it. Both sides, because the fade needs somewhere
                // to come from as well as somewhere to go.
                //
                // These two and no others, which matters. The losing ground is laid across the
                // whole band before the winner is faded over it, so a third ground caught in the
                // band would be painted out and only partly put back -- sand over grass, in
                // rectangles, wherever a marsh happened to run near a beach.
                for (var ny = -Reach; ny <= Reach; ny++)
                {
                    for (var nx = -Reach; nx <= Reach; nx++)
                    {
                        var ground = Index(world, tile.X + nx, tile.Y + ny);

                        if (ground == here || ground == other)
                            _near[pair].Add(Pack(tile.X + nx, tile.Y + ny));
                    }
                }

                // And the winner's own ground, further out than the band goes, so the shape is
                // still solid where the band ends.
                const int depth = Reach + ShapeDepth;

                for (var ny = -depth; ny <= depth; ny++)
                {
                    for (var nx = -depth; nx <= depth; nx++)
                    {
                        if (Index(world, tile.X + nx, tile.Y + ny) == other)
                            Spread(pair, tile.X + nx, tile.Y + ny, tileSize);
                    }
                }
            }
        }

        /// <summary>
        /// Adds one of the winning ground's tiles to a pair's shape, unless it is already in it.
        /// <para>
        /// Grown by <see cref="GrowFraction"/> first, so that neighbouring tiles overlap and the
        /// rounding takes the corners off one outline rather than off each of them.
        /// </para>
        /// </summary>
        private void Spread(int pair, int x, int y, int tileSize)
        {
            if (!_spread[pair].Add(Pack(x, y)))
                return;

            var grow = GrowFraction * tileSize;

            _advance[pair].AddRect(SKRect.Create(
                (x * tileSize) - grow,
                (y * tileSize) - grow,
                tileSize + (grow * 2f),
                tileSize + (grow * 2f)));
        }

        private static (int Dx, int Dy) Outward(int side) => side switch
        {
            0 => (1, 0),
            1 => (-1, 0),
            2 => (0, 1),
            _ => (0, -1),
        };

        /// <summary>A map coordinate as one number, for the sets above.</summary>
        private static long Pack(int x, int y) => ((long)x << 32) | (uint)y;

        /// <summary>An ordered pair of grounds as one index.</summary>
        private static int Pair(int winner, int loser) => (winner * Grounds.Length) + loser;

        /// <summary>
        /// Where a map coordinate's ground sits in <see cref="Grounds"/>, or -1 for water, for
        /// nothing at all, and for anything else this does not arrange.
        /// </summary>
        private static int Index(TileGrid world, int x, int y)
        {
            if (Shoreline.GroundType(world, x, y) is not { } ground)
                return -1;

            for (var i = 0; i < Grounds.Length; i++)
            {
                if (Grounds[i] == ground)
                    return i;
            }

            return -1;
        }

        /// <summary>
        /// One channel of the noise, as the displacement wants it: mid-grey is no push and the
        /// ends are a full one either way.
        /// <para>
        /// Stretched about the middle first, and that is not a nicety. Summed octaves pile up
        /// around their mean -- most of a field of them sits within a fifth of centre -- so
        /// handed over raw the push comes out at a fraction of what it was asked for, and the
        /// boundary keeps the shape of the tiles it was cut from. The stretch spends the whole
        /// range, and what it costs is the far tails flattening off, which is a wander that
        /// occasionally runs out at full lock rather than one that never leaves the middle.
        /// </para>
        /// </summary>
        private static byte Push(float noise) =>
            (byte)Math.Clamp(128f + ((noise - 0.5f) * 255f * Gain), 0f, 255f);

        /// <inheritdoc cref="Push"/>
        private const float Gain = 2.6f;

        /// <summary>The noise that pushes an edge about, as a filter ready to displace with.</summary>
        private sealed class Wander : IZoomLevelResource
        {
            public required SKImage Image { get; init; }
            public required SKPaint Paint { get; init; }
            public required SKImageFilter Filter { get; init; }

            /// <summary>The wander before it was softened. Held only so it can be let go of.</summary>
            public required SKImageFilter Wandered { get; init; }

            /// <summary>Takes the corners off the staircase before the noise gets to it.</summary>
            public required SKPathEffect Corners { get; init; }

            public required long Bytes { get; init; }

            public void Dispose()
            {
                Corners.Dispose();
                Filter.Dispose();
                Wandered.Dispose();
                Paint.Shader?.Dispose();
                Paint.Dispose();
                Image.Dispose();
            }
        }

        private static Wander Build(int tileSize)
        {
            var edge = NoiseTiles * tileSize;
            var pixels = new byte[edge * edge * 4];
            var scale = 1f / edge;

            // Red carries the sideways push and green the up-and-down one, which is what the
            // displacement wants: two fields that do not agree, or the edge would only ever
            // slide along one diagonal.
            for (var y = 0; y < edge; y++)
            {
                for (var x = 0; x < edge; x++)
                {
                    var u = x * scale;
                    var v = y * scale;

                    var acrossward = PeriodicFbm(u, v, NoiseLattice, 3, 0x51D3A97Bu);
                    var downward = PeriodicFbm(u, v, NoiseLattice, 3, 0x2E77C401u);

                    var i = ((y * edge) + x) * 4;

                    pixels[i] = Push(acrossward);
                    pixels[i + 1] = Push(downward);
                    pixels[i + 2] = 0;
                    pixels[i + 3] = 255;
                }
            }

            var info = new SKImageInfo(edge, edge, SKColorType.Rgba8888, SKAlphaType.Opaque);

            var image = SKImage.FromPixelCopy(info, pixels);

            // Anchored to the canvas and repeated, never translated, so that the same point of
            // the map is pushed the same way whatever is on screen -- otherwise the boundary
            // would crawl as the view was panned.
            var paint = new SKPaint
            {
                Shader = SKShader.CreateImage(image, SKShaderTileMode.Repeat, SKShaderTileMode.Repeat),
                FilterQuality = SKFilterQuality.Low,
                IsAntialias = false,
            };

            var wandered = SKImageFilter.CreateDisplacementMapEffect(
                SKColorChannel.R,
                SKColorChannel.G,
                WanderFraction * tileSize,
                SKImageFilter.CreatePaint(paint));

            // Softened after it is pushed about, not before. Softening first would only be
            // displaced afterwards, and would come back as a hard edge with blurry sides.
            var blend = BlendFraction * tileSize;
            var filter = SKImageFilter.CreateBlur(blend, blend, wandered);

            return new Wander
            {
                Image = image,
                Paint = paint,
                Filter = filter,
                Wandered = wandered,
                Corners = SKPathEffect.CreateCorner(RoundingFraction * tileSize),
                Bytes = 4L * edge * edge,
            };
        }
    }
}
