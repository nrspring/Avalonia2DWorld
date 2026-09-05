using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using NWorld.Map.Interfaces;
using NWorld.Map.Models;
using NWorld.MapServices.Renderers;
using SkiaSharp;

namespace NWorld.MapServices.MapRenderComponents.StandardRenderer
{
    /// <summary>
    /// Draws where the land meets the water the way it looks from the air: the sea going pale
    /// and green as the bottom comes up under it, a line of surf, and a strip of wet sand, round
    /// every island, lake and river on screen.
    /// <para>
    /// Two things at once, and the second is the point. The line is what makes the map read as
    /// a map rather than as a field of coloured squares -- but the squares are still square,
    /// and a line that traced them exactly would only draw attention to the staircase. So the
    /// path is drawn with its corners <em>rounded</em>, cutting across each corner of the tile
    /// grid, and the strip is made wide enough to cover the ground it cuts off. What is left is
    /// a coast that curves. The tiles underneath have not moved; they are simply no longer the
    /// outline anybody sees.
    /// </para>
    /// <para>
    /// That is the whole reason for the beach. <see cref="ShoreFraction"/> is not a taste about
    /// how broad a shore should be, it is a floor set by <see cref="CornerFraction"/> -- and the
    /// floor is worked out rather than written down, so that rounding the coast further widens
    /// the strip that has to hide what the rounding cut off, instead of leaving corners of grass
    /// poking out into the sea for somebody to notice much later.
    /// </para>
    /// <para>
    /// The solid middle of the sand is the one thing here that may not be softened, and that is
    /// what <see cref="Width"/> is for: a blur spreads paint out and thins it at the edges, so
    /// a corner sitting in the feather would show through it. The corner has to sit in the part
    /// that is opaque.
    /// </para>
    /// <para>
    /// Not a render component and not in <c>RenderHelperFunctions</c>' table with the ones that
    /// are. Everything in that table is dispatched by a type sitting on a tile; a coast is not
    /// on a tile, it is <em>between</em> two of them, and no tile carries it. It belongs to the
    /// renderer, which draws it in one pass after the ground and before anything that stands on
    /// the ground -- see <see cref="Renderers.StandardRenderer"/>.
    /// </para>
    /// <para>
    /// The shallows are what makes it read as a photograph rather than as a drawing, and the
    /// blur on them is the whole of it. Sea over a shelf pales towards green because there is
    /// less water over the sand to swallow the red out of the light, and that happens gradually
    /// -- so the one thing it must not have is an edge. A stroke along the coast, blurred by
    /// most of a tile and clipped to the water, gives the falloff for one blur rather than for
    /// the dozen overlapping strokes it would otherwise take.
    /// </para>
    /// <para>
    /// Nothing here is outlined, which is the other half of it. There is no dark line round a
    /// real coast; what separates the land from the sea in a photograph is that they are
    /// different colours, with sand and surf between them. Every edge that could be drawn here
    /// is softened instead -- even the sand, which carries a blurred skirt outside its solid
    /// middle so that it fades into the water rather than stopping at a stroke width.
    /// </para>
    /// <para>
    /// The whole screenful goes into one path and out in a handful of strokes, rather than a
    /// stroke per tile. A coast is a few hundred tiles of a screenful that may be tens of
    /// thousands, so this costs about what walking the tiles costs and almost nothing to draw.
    /// </para>
    /// <para>
    /// An instance per renderer, not a static, because it keeps its path between frames and
    /// reuses it -- so a steady view settles into allocating nothing here. That makes it
    /// single-threaded by construction, exactly as the renderer holding it is.
    /// </para>
    /// </summary>
    internal sealed class Coastline
    {
        /// <summary>
        /// Below this the coast is left off altogether.
        /// <para>
        /// A strip and two ink lines inside six pixels is three shades of mud, and a map with
        /// mud round every island is worse than one with plain edges. It also keeps the coast
        /// out of the mini-map, which draws the whole world at a few pixels a tile.
        /// </para>
        /// </summary>
        public const int MinTileSize = 6;

        /// <summary>
        /// Below this the sea is left plain and the coast is the sand alone.
        /// <para>
        /// Both halves of the same judgement. Shallows blurred across a few pixels are a smudge
        /// rather than a falloff, so there is nothing to see; and it is at exactly these sizes
        /// that there is most of it, because a screenful at eight pixels a tile is tens of
        /// thousands of tiles and a broken coast among them can put a blurred stroke over most
        /// of the sea. The arrangement that costs the most is the one that shows the least.
        /// </para>
        /// <para>
        /// It is also what a photograph does. From high enough up the shelf is a thin line and
        /// not a gradient, and there is nothing to render but the shore itself.
        /// </para>
        /// </summary>
        public const int MinShallowsTileSize = 12;

        /// <summary>
        /// How far back from each corner of a tile the coast starts to turn, as a fraction of
        /// the tile. The bigger it is the rounder the coast -- and the wider the strip, which
        /// follows it up rather than having to be raised to match.
        /// </summary>
        private const float CornerFraction = 0.30f;

        /// <summary>
        /// How much ground a rounded corner leaves outside the curve, as a fraction of the
        /// corner radius.
        /// <para>
        /// The corner is turned with a quadratic through the corner point, whose midpoint sits
        /// a quarter of the way along the diagonal from it -- <c>0.25 * sqrt(2)</c>, near
        /// enough 0.354. Half the strip has to reach at least that far or the corner shows.
        /// </para>
        /// </summary>
        private const float CornerOvershoot = 0.354f;

        /// <summary>
        /// Width of the solid middle of the sand, as a fraction of the tile -- or the width the
        /// corners need, whichever is the greater. See <see cref="Width"/>.
        /// </summary>
        private const float ShoreFraction = 0.215f;

        /// <summary>
        /// Width of the skirt of sand outside that, which is blurred so the beach fades into
        /// the water instead of ending at a stroke width. Wider than the solid part, and far
        /// fainter.
        /// </summary>
        private const float SandSkirtFraction = 0.62f;

        /// <summary>How far the skirt of sand is blurred, as a fraction of the tile.</summary>
        private const float SandBlurFraction = 0.12f;

        /// <summary>
        /// How wide the shallows are drawn before blurring, as a fraction of the tile. Reaching
        /// half of this either side of the coast, of which the landward half is thrown away by
        /// the clip -- a stroke has two sides and only one of them is the sea.
        /// </summary>
        private const float ShallowsFraction = 1.15f;

        /// <summary>
        /// How far the shallows are blurred. Most of a tile, because what is being drawn is the
        /// bottom coming up under the water rather than anything with an edge, and a falloff
        /// that lands inside one tile would read as a rim round the island.
        /// </summary>
        private const float ShallowsBlurFraction = 0.26f;

        /// <summary>Width of the line of surf along the waterline itself.</summary>
        private const float SurfFraction = 0.075f;

        /// <inheritdoc cref="SurfFraction"/>
        private const float SurfBlurFraction = 0.035f;

        /// <summary>
        /// Wet sand seen from above: darker and greyer than dry sand, which is what a beach at
        /// the waterline actually is. Nearly opaque, because this is the part that has to hide
        /// the corners of the tile grid and a corner half showing is a corner showing.
        /// </summary>
        private static readonly SKColor Sand = new(0xC4, 0xB4, 0x93, 0xD4);

        /// <inheritdoc cref="SandSkirtFraction"/>
        private static readonly SKColor SandSkirt = new(0xCA, 0xBC, 0x9E, 0x50);

        /// <summary>
        /// The sea where the bottom is close under it. Green rather than blue, and that is the
        /// physics rather than a preference: water takes the red out of light first and the
        /// blue last, so a metre of it over pale sand comes back green while ten metres of it
        /// comes back blue. It is laid over the water the renderer already drew, so it only has
        /// to carry the sea that far and not paint it from nothing.
        /// </summary>
        private static readonly SKColor Shallows = new(0x6E, 0xCF, 0xC0, 0x7A);

        /// <summary>
        /// The surf. Not white: broken water over sand is white with the sand in it, and a
        /// clean white line reads as something drawn on rather than something floating in the
        /// water.
        /// </summary>
        private static readonly SKColor Surf = new(0xE8, 0xF4, 0xEE, 0x5A);

        /// <summary>
        /// The coast for this frame. Kept and rewound rather than made afresh, since it is
        /// rebuilt on every frame and would otherwise be a few hundred segments of garbage
        /// sixty times a second.
        /// </summary>
        private readonly SKPath _path = new();

        /// <summary>
        /// The water near the shore, as one rectangle per tile: what the shallows are clipped
        /// to.
        /// <para>
        /// Needed because a stroke has two sides and only one of them is the sea. Without it the
        /// same pale green would be laid across the land, where it would read as fog.
        /// </para>
        /// <para>
        /// Only the tiles the shallows can reach are collected, which is the water touching land
        /// -- corners included, since the tint curving round a headland leaves by one side of a
        /// tile and arrives at another. The whole sea would be tens of thousands of rectangles
        /// for a clip that is never consulted more than a tile from the shore.
        /// </para>
        /// </summary>
        private readonly SKPath _sea = new();

        /// <summary>
        /// The blurs, one per zoom level they are asked for at.
        /// <para>
        /// Kept rather than made per frame because a mask filter is an immutable native object
        /// and building one is not free, and thrown away never, because the zoom ladder is nine
        /// rungs long -- so this holds at most a couple of dozen bytes each for the life of the
        /// renderer, and a zoom back to a level already seen costs nothing.
        /// </para>
        /// </summary>
        private readonly Dictionary<int, SKMaskFilter> _sandBlur = [];

        /// <inheritdoc cref="_sandBlur"/>
        private readonly Dictionary<int, SKMaskFilter> _shallowsBlur = [];

        /// <inheritdoc cref="_sandBlur"/>
        private readonly Dictionary<int, SKMaskFilter> _surfBlur = [];

        /// <summary>
        /// Draws every coast the canvas can show.
        /// <para>
        /// Nothing here moves with <see cref="RenderFrame.TimeSeconds"/>: the shore stays where
        /// the land is however the water under it is moving.
        /// </para>
        /// </summary>
        /// <param name="tiles">
        /// The screenful, in whatever order the caller has it. Only the tiles on it are walked;
        /// what is <em>next</em> to them comes from <see cref="RenderFrame.World"/>, which is
        /// why a coast at the edge of the window is drawn the same as one in the middle of it.
        /// </param>
        public void Render(SKCanvas canvas, RenderFrame frame, IReadOnlyList<MapTile> tiles)
        {
            var tileSize = frame.TileSize;

            // No world, no neighbours, no coast. A still render or a test has none to give, and
            // guessing at one would draw a coastline round every tile on screen.
            if (canvas is null || tiles is null || frame.World is not { } world || tileSize < MinTileSize)
                return;

            _path.Rewind();
            _sea.Rewind();

            var (minX, minY, maxX, maxY) = VisibleTiles.For(canvas, tileSize);

            // Walked as a span where the list allows it, for the reason every other pass over
            // the tiles does the same: on a large map most of them are off screen, and fetching
            // each one through the interface only to drop it is milliseconds a frame.
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

            if (_path.IsEmpty)
                return;

            // Deepest first, and each one softer at its edges than the one before is hard at
            // its middle: the sea shoaling, then the sand fading out of it, then the sand
            // itself, then the surf along the line where the two meet. Nothing is outlined and
            // nothing ends at a stroke width except the one stroke that has to.
            Shoal(canvas, tileSize);

            if (tileSize >= MinShallowsTileSize)
                Stroke(canvas, SandSkirt, SandSkirtFraction * tileSize, Blur(_sandBlur, SandBlurFraction, tileSize));

            Stroke(canvas, Sand, Math.Max(Width * tileSize, 1f));

            if (tileSize >= MinShallowsTileSize)
                Stroke(canvas, Surf, Math.Max(SurfFraction * tileSize, 1f), Blur(_surfBlur, SurfBlurFraction, tileSize));
        }

        /// <summary>
        /// The sea going pale and green where the bottom comes up under it: one blurred stroke
        /// along the coast, clipped to the water.
        /// <para>
        /// Blurred rather than stepped, because that is the difference between a photograph and
        /// a chart -- and one blur rather than the dozen overlapping strokes it would take to
        /// fake the same falloff, which is also why this is affordable at all.
        /// </para>
        /// </summary>
        private void Shoal(SKCanvas canvas, int tileSize)
        {
            if (_sea.IsEmpty || tileSize < MinShallowsTileSize)
                return;

            var checkpoint = canvas.Save();

            try
            {
                // Not antialiased: the clip runs down the middle of the sand, which is drawn
                // over it afterwards, so no part of this edge is ever seen -- and an antialiased
                // clip over a few hundred rectangles is not free.
                canvas.ClipPath(_sea, SKClipOperation.Intersect, antialias: false);

                Stroke(
                    canvas,
                    Shallows,
                    ShallowsFraction * tileSize,
                    Blur(_shallowsBlur, ShallowsBlurFraction, tileSize));
            }
            finally
            {
                canvas.RestoreToCount(checkpoint);
            }
        }

        /// <summary>The blur for one zoom level, built the first time it is asked for.</summary>
        private static SKMaskFilter Blur(Dictionary<int, SKMaskFilter> cache, float fraction, int tileSize)
        {
            if (!cache.TryGetValue(tileSize, out var blur))
            {
                blur = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, Math.Max(0.5f, fraction * tileSize));
                cache[tileSize] = blur;
            }

            return blur;
        }

        /// <summary>
        /// How wide the solid sand actually is, as a fraction of the tile: what was asked for,
        /// or what it takes to cover the corners the rounding cuts off, whichever is the more.
        /// <para>
        /// Worked out here rather than checked by hand, so that the one number anybody is ever
        /// likely to reach for -- how round the coast should be -- cannot be changed into a
        /// coast with the corners of the tile grid showing through it.
        /// </para>
        /// </summary>
        private static float Width =>
            Math.Max(ShoreFraction, CornerFraction * CornerOvershoot * 2f * CoverMargin);

        /// <summary>
        /// How much wider than the bare arithmetic the solid sand is drawn.
        /// <para>
        /// The corner is covered exactly at 1.0, which is not the same as covered. A stroke edge
        /// is antialiased, so the last fraction of a pixel of it is half transparent, and a
        /// corner that reaches exactly to it shows through that half. A twelfth over is the
        /// difference between a guarantee and a coincidence.
        /// </para>
        /// </summary>
        private const float CoverMargin = 1.08f;

        private void Stroke(SKCanvas canvas, SKColor colour, float width, SKMaskFilter? blur = null)
        {
            // Round throughout. The joins are what carry the curve round a corner inside one
            // tile; the caps are what make two tiles' worth of coast meet as one line instead
            // of as two lines that stop beside each other.
            using var pen = new SKPaint
            {
                Color = colour,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = width,
                StrokeCap = SKStrokeCap.Round,
                StrokeJoin = SKStrokeJoin.Round,
                IsAntialias = true,
                MaskFilter = blur,
            };

            canvas.DrawPath(_path, pen);
        }

        private void Collect(
            ReadOnlySpan<MapTile> tiles, TileGrid world, int minX, int minY, int maxX, int maxY, int tileSize)
        {
            foreach (var tile in tiles)
                Collect(tile, world, minX, minY, maxX, maxY, tileSize);
        }

        /// <summary>Adds one tile's water-facing sides to the path, unless it is off screen.</summary>
        private void Collect(
            MapTile? tile, TileGrid world, int minX, int minY, int maxX, int maxY, int tileSize)
        {
            if (tile is null)
                return;

            if (tile.X < minX || tile.X > maxX || tile.Y < minY || tile.Y > maxY)
                return;

            var mask = Shoreline.Mask(world, tile.X, tile.Y);

            if (mask != 0)
            {
                Add(world, tile.X, tile.Y, mask, tileSize, CornerFraction * tileSize);
                return;
            }

            // Not land, so it may be the sea the shallows are allowed onto -- and worth
            // gathering only when there are going to be shallows to clip.
            if (tileSize >= MinShallowsTileSize
                && Shoreline.IsWater(world, tile.X, tile.Y)
                && Shoreline.TouchesLand(world, tile.X, tile.Y))
            {
                _sea.AddRect(SKRect.Create(tile.X * tileSize, tile.Y * tileSize, tileSize, tileSize));
            }
        }

        /// <summary>
        /// Adds the water-facing sides of one tile, walking its edge clockwise and turning each
        /// corner where two of them meet.
        /// </summary>
        /// <param name="mask">Which sides face water, from <see cref="Shoreline.Mask"/>.</param>
        /// <param name="size">Tile size in pixels.</param>
        /// <param name="corner">How far back from a corner the turn begins.</param>
        private void Add(TileGrid world, int x, int y, int mask, float size, float corner)
        {
            var left = x * size;
            var top = y * size;

            if (mask == Shoreline.All)
            {
                // An island one tile across, so the coast is a closed loop with no ends. Drawn
                // as one, rather than as four runs that would each have to stop somewhere: an
                // end in the middle of a curve is a round cap where there should be no cap.
                Loop(left, top, size, corner);
                return;
            }

            for (var side = 0; side < 4; side++)
            {
                // Only where a run begins -- a water-facing side whose predecessor going round
                // is land. Everything after it is picked up by the walk itself, so a tile with
                // two separate stretches of coast on it gets two runs and a tile with one gets
                // one.
                if ((mask & Bit(side)) == 0 || (mask & Bit((side + 3) & 3)) != 0)
                    continue;

                Run(world, x, y, mask, side, left, top, size, corner);
            }
        }

        /// <summary>
        /// One unbroken stretch of coast around a tile, from the corner it starts at to the
        /// corner it runs out at.
        /// </summary>
        private void Run(
            TileGrid world, int x, int y, int mask, int side,
            float left, float top, float size, float corner)
        {
            var placed = false;

            while (true)
            {
                var next = (side + 1) & 3;
                var previous = (side + 3) & 3;

                var (startX, startY) = Start(side, left, top, size);
                var (endX, endY) = End(side, left, top, size);
                var (stepX, stepY) = Step(side);

                if (!placed)
                {
                    // A run starts at the corner itself where the coast arrives along the same
                    // straight line out of the tile before, so a straight shore has no notch at
                    // every tile boundary. It starts short of the corner only where the coast
                    // turns into this tile: the tile it turned out of has drawn the curve
                    // through that corner already, and stopped exactly here.
                    var into = Turns(world, x, y, side, previous) ? corner : 0f;

                    _path.MoveTo(startX + (stepX * into), startY + (stepY * into));
                    placed = true;
                }

                // Two ways the coast can turn at the far corner, and they round the same way.
                // Inside the tile, where the next side is water as well: land jutting out into
                // the water. Out of the tile, where the next side is land but the ground
                // diagonally past both is land too: water cutting into the land, and the coast
                // leaving this tile at a right angle to carry on round it.
                var inside = (mask & Bit(next)) != 0;
                var outside = !inside && Turns(world, x, y, side, next);

                var trim = inside || outside ? corner : 0f;

                _path.LineTo(endX - (stepX * trim), endY - (stepY * trim));

                if (!inside && !outside)
                    return;

                // Round the corner with a quadratic through it. The corner point as the control
                // is what makes the curve leave and arrive along the two sides, so the turn is
                // smooth in both directions without having to say so.
                //
                // Which side it arrives along is the one difference between the two turns. A
                // turn inside the tile carries on round this tile; a turn out of it carries on
                // up the neighbour edge, which is the same line the neighbour own run begins
                // along -- hence the matching trim where a run starts.
                var arriving = inside ? next : previous;
                var (nextX, nextY) = Step(arriving);

                _path.QuadTo(endX, endY, endX + (nextX * corner), endY + (nextY * corner));

                if (!inside)
                    return;

                side = next;
            }
        }

        /// <summary>
        /// Whether the coast turns out of this tile at the corner between two of its sides: one
        /// of them water, the other land, and the ground diagonally past both of them land as
        /// well.
        /// <para>
        /// Water cutting into the land rather than land jutting out into it, and it is the
        /// corner the grid cannot round on its own: it happens <em>between</em> two tiles, and
        /// neither of them owns both of the edges that meet there. So the tile the coast is
        /// leaving draws the whole curve, a corner deep into its neighbour, and the neighbour
        /// begins a corner along to meet it.
        /// </para>
        /// <para>
        /// Where that diagonal is water instead there is no turn at all: the coast runs straight
        /// on along the neighbour own edge, and both tiles want the full corner.
        /// </para>
        /// </summary>
        private static bool Turns(TileGrid world, int x, int y, int water, int land)
        {
            var (wx, wy) = Outward(water);
            var (lx, ly) = Outward(land);

            return Shoreline.IsLand(world, x + wx + lx, y + wy + ly);
        }

        /// <summary>Which way a side faces, away from the middle of the tile.</summary>
        private static (int X, int Y) Outward(int side) => side switch
        {
            0 => (0, -1),
            1 => (1, 0),
            2 => (0, 1),
            _ => (-1, 0),
        };

        /// <summary>A tile with water on every side: a rounded square, closed.</summary>
        private void Loop(float left, float top, float size, float corner)
        {
            var right = left + size;
            var bottom = top + size;

            _path.MoveTo(left + corner, top);
            _path.LineTo(right - corner, top);
            _path.QuadTo(right, top, right, top + corner);
            _path.LineTo(right, bottom - corner);
            _path.QuadTo(right, bottom, right - corner, bottom);
            _path.LineTo(left + corner, bottom);
            _path.QuadTo(left, bottom, left, bottom - corner);
            _path.LineTo(left, top + corner);
            _path.QuadTo(left, top, left + corner, top);
            _path.Close();
        }

        /// <summary>
        /// The sides in the order the walk goes round them: north, east, south, west, which is
        /// clockwise on screen. Every corner is then the end of one side and the start of the
        /// next, and the turn between them needs no special case for which corner it is.
        /// </summary>
        private static int Bit(int side) => 1 << side;

        /// <inheritdoc cref="Bit"/>
        private static (float X, float Y) Start(int side, float left, float top, float size) => side switch
        {
            0 => (left, top),
            1 => (left + size, top),
            2 => (left + size, top + size),
            _ => (left, top + size),
        };

        /// <inheritdoc cref="Bit"/>
        private static (float X, float Y) End(int side, float left, float top, float size) =>
            Start((side + 1) & 3, left, top, size);

        /// <summary>Which way a side is walked, as a unit step.</summary>
        private static (float X, float Y) Step(int side) => side switch
        {
            0 => (1f, 0f),
            1 => (0f, 1f),
            2 => (-1f, 0f),
            _ => (0f, -1f),
        };
    }
}
