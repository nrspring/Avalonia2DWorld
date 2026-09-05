using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using NWorld.Map.Interfaces;
using NWorld.Map.Models;
using NWorld.MapServices.MapRenderComponents.StandardRenderer.RenderingFunctions;
using NWorld.MapServices.Renderers;
using SkiaSharp;

namespace NWorld.MapServices.MapRenderComponents.StandardRenderer
{
    /// <summary>
    /// Softens the step where one depth of water meets another, so a drop-off reads as the
    /// bottom falling away rather than as the edge of a tile.
    /// <para>
    /// It only has to touch the colour, and that is why this is small. Every kind of water draws
    /// the same wave field from the same block at the same phase -- see <see cref="WaterStyle"/>
    /// -- so the crests already run unbroken across a shelf edge and the only thing that steps
    /// is the palette. There is no geometry to fix here, only a tone to ramp.
    /// </para>
    /// <para>
    /// Ramped by <b>multiplying</b> rather than by painting a blend colour over the top, which
    /// is what keeps the water looking like water. A flat colour laid over the shallows at the
    /// strength needed to meet the deep would take the waves with it and leave a band of smooth
    /// paint down the drop-off; multiplying scales every pixel instead, so the crests and the
    /// troughs and the flecks of foam all survive and simply get darker together -- which is
    /// what happens as water deepens.
    /// </para>
    /// <para>
    /// How far it is darkened is decided by <b>how far it is from deep water</b>, and by nothing
    /// else. That is the whole design, and it was arrived at the hard way. The obvious thing is
    /// to draw a ramp along each boundary between two tiles, which is right along a straight
    /// edge and wrong everywhere else: a stroke stops dead where the boundary it follows stops,
    /// so every corner of every patch of deep water grows a square notch of un-darkened water,
    /// and every place two of them abut leaves a seam. Distance has no such trouble, because it
    /// is a fact about a point rather than about an edge.
    /// </para>
    /// <para>
    /// It is measured the cheap way. The deep water is filled as one silhouette, spread outwards
    /// by <see cref="OutsetFraction"/> so that its own edge already covers the boundary, and
    /// blurred; what comes out is a field that is solid over the deep, falls away smoothly for
    /// about a tile around it, and turns every corner by itself. Multiplying by that field is
    /// the slope. One fill and one blur per pair of depths, however tangled the coastline of the
    /// deep happens to be.
    /// </para>
    /// <para>
    /// The blur goes on the <em>layer</em> rather than on the paint, and that matters. A blur
    /// asked of a paint arrives as coverage, and coverage and a blend mode are two different
    /// things asked of one draw: Skia resolves the mask and the multiply comes out of it with
    /// nothing to show. Filtered as a layer it is an image by the time the blend sees it.
    /// </para>
    /// <para>
    /// Not a render component, for the reason <see cref="Coastline"/> is not: this is about the
    /// boundary <em>between</em> two tiles and no tile carries it. It runs in the same pass and
    /// before the coast, because a shore laid over the sea should be laid over the finished sea.
    /// </para>
    /// <para>
    /// An instance per renderer, not a static: it keeps its paths between frames and reuses
    /// them, which makes it single-threaded by construction exactly as the renderer holding it
    /// is.
    /// </para>
    /// </summary>
    internal sealed class WaterDepth
    {
        /// <summary>
        /// Below this the drop-off is left alone.
        /// <para>
        /// A slope across a few pixels is a smudge rather than a slope, and at these sizes there
        /// is most of it to draw -- the same trade <see cref="Coastline"/> makes, for the same
        /// reason.
        /// </para>
        /// </summary>
        public const int MinTileSize = 12;

        /// <summary>
        /// How far the deep water is spread outwards before it is blurred, as a fraction of a
        /// tile.
        /// <para>
        /// Without it the blurred edge would sit at half strength exactly where the two waters
        /// meet, which is a step of half the difference dressed up as a slope. Pushed out by
        /// this much first, the field is all but solid at the boundary and the shallow water
        /// arrives at the deep tone right where it should.
        /// </para>
        /// </summary>
        private const float OutsetFraction = 0.36f;

        /// <summary>
        /// How far the field is blurred, as a fraction of a tile.
        /// <para>
        /// With the outset above this puts the slope at about a tile: near enough solid at the
        /// boundary, a quarter of the way back at half a tile out, and nothing at all by a whole
        /// one -- which is what <see cref="Reach"/> then has to be wide enough to hold.
        /// </para>
        /// </summary>
        private const float BlurFraction = 0.22f;

        /// <summary>
        /// How far from the deep water the slope is allowed to be seen, in whole tiles.
        /// <para>
        /// The clip is built a tile out from the deep and no further, so the field has to have
        /// faded to nothing by then; a fade cut off by a straight line would be the very tile
        /// edge this exists to remove. At the numbers above it is down to about a hundredth of
        /// its strength there, which is a good deal less than a pixel of tone.
        /// </para>
        /// </summary>
        private const int Reach = 1;

        /// <summary>
        /// Below this difference between two waters there is nothing worth ramping, because
        /// there is nothing to see: two palettes this close are the same colour, and blurring
        /// one into the other would cost a pass and change no pixel anybody could point at.
        /// <para>
        /// Low, and it was raised once and put back. The shelf and the open water differ by
        /// almost nothing in red and blue and by a tenth in green -- sixteen all told, which
        /// sounds like nothing and is a plainly visible change of hue, because it is the green
        /// alone that carries it. A threshold on the total is a blunt instrument and this is
        /// where it shows: set it high enough to be sure of catching only real steps and it
        /// throws away one of the two that a map with a shelf on it actually has.
        /// </para>
        /// </summary>
        private const int MinContrast = 10;

        /// <summary>
        /// The kinds of water, deepest last. The order is the whole of what "deeper" means here,
        /// and it is a fact about this world rather than about drawing: a shelf is shallower than
        /// open water and open water is shallower than the deep.
        /// </summary>
        private static readonly Guid[] Depths =
        [
            MapRenderComponentConstants.ShallowWater,
            MapRenderComponentConstants.Water,
            MapRenderComponentConstants.DeepWater,
        ];

        /// <summary>What each of those is coloured at the mean surface, in the same order.</summary>
        private static readonly Func<SKColor>[] Bodies =
        [
            static () => RenderShallowWater.Body,
            static () => RenderWater.Body,
            static () => RenderDeepWater.Body,
        ];

        /// <summary>
        /// The deep water itself, spread outwards, one path for each pair of depths that can
        /// meet: shelf against open water, shelf against deep, open water against deep.
        /// <see cref="Pair"/> is the index.
        /// <para>
        /// Overlapping rectangles rather than a tidy union, which is what a fill rule is for:
        /// two tiles spread outwards overlap by most of their width and come out as one shape
        /// anyway, at no cost and with no seam between them.
        /// </para>
        /// </summary>
        private readonly SKPath[] _deep = [new(), new(), new()];

        /// <summary>
        /// Where the slope is allowed to show: the water of the shallower kind within
        /// <see cref="Reach"/> of that deep. Built from <see cref="_near"/> once the tiles have
        /// been walked, so a tile that several boundaries name is still one rectangle.
        /// <para>
        /// Needed because the field spreads in every direction and only some of them are the
        /// sea it belongs to. Without it the deep would darken itself, and the slope would run
        /// up the beach.
        /// </para>
        /// </summary>
        private readonly SKPath[] _shallower = [new(), new(), new()];

        /// <inheritdoc cref="_shallower"/>
        private readonly HashSet<long>[] _near = [[], [], []];

        /// <summary>Which deep tiles are already in <see cref="_deep"/>, so none goes in twice.</summary>
        private readonly HashSet<long>[] _spread = [[], [], []];

        /// <summary>Softens every drop-off the canvas can show.</summary>
        public void Render(SKCanvas canvas, RenderFrame frame, IReadOnlyList<MapTile> tiles)
        {
            var tileSize = frame.TileSize;

            if (canvas is null || tiles is null || frame.World is not { } world || tileSize < MinTileSize)
                return;

            for (var i = 0; i < 3; i++)
            {
                _deep[i].Rewind();
                _shallower[i].Rewind();
                _near[i].Clear();
                _spread[i].Clear();
            }

            var (minX, minY, maxX, maxY) = VisibleTiles.For(canvas, tileSize);

            // Walked as a span where the list allows it, for the reason every other pass over the
            // tiles does the same: most of a large map is off screen, and fetching each tile
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

            for (var shallower = 0; shallower < Depths.Length; shallower++)
            {
                for (var deeper = shallower + 1; deeper < Depths.Length; deeper++)
                {
                    var pair = Pair(shallower, deeper);

                    if (_deep[pair].IsEmpty)
                        continue;

                    foreach (var packed in _near[pair])
                    {
                        _shallower[pair].AddRect(SKRect.Create(
                            (int)(packed >> 32) * (float)tileSize,
                            (int)packed * (float)tileSize,
                            tileSize,
                            tileSize));
                    }

                    Slope(
                        canvas,
                        tileSize,
                        _shallower[pair],
                        _deep[pair],
                        Towards(Bodies[shallower](), Bodies[deeper]()));
                }
            }
        }

        /// <summary>
        /// Darkens the shallower water towards the deeper one, by how near it is to it: the
        /// spread silhouette of the deep, blurred into a field, multiplied into the water that
        /// is already there.
        /// </summary>
        private static void Slope(
            SKCanvas canvas, int tileSize, SKPath clip, SKPath deep, SKColor towards)
        {
            if (clip.IsEmpty)
                return;

            var checkpoint = canvas.Save();

            try
            {
                // Not antialiased: the clip only cuts where the field has already faded to
                // nothing -- see Reach -- so none of this edge is ever seen, and an antialiased
                // clip over a few hundred rectangles is not free.
                canvas.ClipPath(clip, SKClipOperation.Intersect, antialias: false);

                var blur = BlurFraction * tileSize;

                using var filter = SKImageFilter.CreateBlur(blur, blur);
                using var composite = new SKPaint { ImageFilter = filter, BlendMode = SKBlendMode.Multiply };

                canvas.SaveLayer(composite);

                try
                {
                    using var fill = new SKPaint
                    {
                        Color = towards,
                        Style = SKPaintStyle.Fill,
                        IsAntialias = true,
                    };

                    canvas.DrawPath(deep, fill);
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

        /// <summary>
        /// What to multiply the shallower water by to land on the deeper one: the ratio of the
        /// two, channel by channel.
        /// <para>
        /// Clamped at white, since multiplying can darken and cannot brighten. That only bites
        /// where one water is brighter than the other in a single channel while being darker
        /// overall, which is a difference of a per cent or two between two palettes chosen to sit
        /// beside each other -- and <see cref="MinContrast"/> has already thrown out the pairs
        /// where that is all there is.
        /// </para>
        /// </summary>
        private static SKColor Towards(SKColor shallower, SKColor deeper) => new(
            Ratio(shallower.Red, deeper.Red),
            Ratio(shallower.Green, deeper.Green),
            Ratio(shallower.Blue, deeper.Blue));

        /// <inheritdoc cref="Towards"/>
        private static byte Ratio(byte from, byte to) =>
            (byte)Math.Clamp(255f * to / Math.Max((byte)1, from), 0f, 255f);

        private void Collect(
            ReadOnlySpan<MapTile> tiles, TileGrid world, int minX, int minY, int maxX, int maxY, int tileSize)
        {
            foreach (var tile in tiles)
                Collect(tile, world, minX, minY, maxX, maxY, tileSize);
        }

        /// <summary>
        /// Takes one tile of shallower water that touches deeper water, and notes two things: the
        /// deep tile it touches, which is what the slope runs down from, and the stretch of its
        /// own kind of water around it, which is where the slope is allowed to show.
        /// <para>
        /// Gathered from the shallower side because that is the side being drawn on. A deep tile
        /// is reached through its neighbour and may well be off screen, which is right -- water
        /// just past the edge of the window shades the water inside it exactly as it would if the
        /// window were wider.
        /// </para>
        /// </summary>
        private void Collect(
            MapTile? tile, TileGrid world, int minX, int minY, int maxX, int maxY, int tileSize)
        {
            if (tile is null)
                return;

            if (tile.X < minX || tile.X > maxX || tile.Y < minY || tile.Y > maxY)
                return;

            var here = Depth(world, tile.X, tile.Y);

            if (here < 0)
                return;

            for (var side = 0; side < 4; side++)
            {
                var (dx, dy) = Outward(side);
                var deeper = Depth(world, tile.X + dx, tile.Y + dy);

                if (deeper <= here || !Worth(here, deeper))
                    continue;

                var pair = Pair(here, deeper);

                Spread(pair, tile.X + dx, tile.Y + dy, tileSize);

                // Everything of this depth within reach of the boundary, corners included: the
                // field spreads in every direction, so the room it is given has to as well.
                for (var ny = -Reach; ny <= Reach; ny++)
                {
                    for (var nx = -Reach; nx <= Reach; nx++)
                    {
                        if (Depth(world, tile.X + nx, tile.Y + ny) == here)
                            _near[pair].Add(Pack(tile.X + nx, tile.Y + ny));
                    }
                }
            }
        }

        /// <summary>
        /// Adds one deep tile to a pair's silhouette, spread outwards, unless it is already in
        /// it -- which it will be whenever two of its sides face the same shallower water.
        /// </summary>
        private void Spread(int pair, int x, int y, int tileSize)
        {
            if (!_spread[pair].Add(Pack(x, y)))
                return;

            var outset = OutsetFraction * tileSize;

            _deep[pair].AddRect(SKRect.Create(
                (x * tileSize) - outset,
                (y * tileSize) - outset,
                tileSize + (outset * 2f),
                tileSize + (outset * 2f)));
        }

        /// <summary>A map coordinate as one number, for the sets above.</summary>
        private static long Pack(int x, int y) => ((long)x << 32) | (uint)y;

        /// <summary>
        /// How deep the water at a map coordinate is, as an index into <see cref="Depths"/>, or
        /// -1 for anything that is not water.
        /// </summary>
        private static int Depth(TileGrid world, int x, int y)
        {
            if (Shoreline.GroundType(world, x, y) is not { } ground)
                return -1;

            for (var i = 0; i < Depths.Length; i++)
            {
                if (Depths[i] == ground)
                    return i;
            }

            return -1;
        }

        /// <summary>Whether two depths differ by enough to be worth ramping between.</summary>
        private static bool Worth(int shallower, int deeper)
        {
            var from = Bodies[shallower]();
            var to = Bodies[deeper]();

            return Math.Abs(from.Red - to.Red)
                + Math.Abs(from.Green - to.Green)
                + Math.Abs(from.Blue - to.Blue) >= MinContrast;
        }

        /// <summary>
        /// Which of the three buckets a pair of depths belongs in. Ordered, shallower first, so a
        /// boundary lands in the same bucket whichever side of it is being walked.
        /// </summary>
        private static int Pair(int shallower, int deeper) => shallower + deeper - 1;

        /// <summary>Which way a side faces, away from the middle of the tile.</summary>
        private static (int X, int Y) Outward(int side) => side switch
        {
            0 => (0, -1),
            1 => (1, 0),
            2 => (0, 1),
            _ => (-1, 0),
        };
    }
}
