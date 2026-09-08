using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using NWorld.Map.Interfaces;
using NWorld.Map.Models;
using NWorld.MapServices.Renderers;
using SkiaSharp;
using static NWorld.MapServices.MapRenderComponents.StandardRenderer.RenderNoise;

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
        private const float CornerFraction = 0.20f;

        /// <summary>
        /// How far the coast is pushed off the grid it was cut on, as a fraction of a tile:
        /// each corner of the tile grid is shifted a little way in some direction of its own.
        /// <para>
        /// Rounding alone gives every tile the same tidy scallop, which is a different way of
        /// drawing the grid rather than a way of hiding it -- a coast made only of quarter
        /// circles at a fixed radius reads as machined, and at a glance the eye finds the
        /// period it repeats on. Moving the corners first means the curves are struck between
        /// points that are no longer evenly spaced, so no two turns come out alike.
        /// </para>
        /// <para>
        /// The ceiling on it is <see cref="Width"/>: everything the sand has to hide is measured
        /// from where the tile edges actually are, so a push the sand cannot cover is a push
        /// that shows those edges through it. Which is why this is folded into that sum rather
        /// than tuned against it by eye.
        /// </para>
        /// <para>
        /// Small, and deliberately so, because it is the dearest thing on this class by some
        /// way: it is paid for twice over in solid sand, and solid sand is the one measurement
        /// anybody looking at the map actually reads as the width of the beach. A push worth
        /// seeing here costs a beach nobody wanted. Nearly all of the irregularity is bought
        /// instead through <see cref="FringeWanderFraction"/>, which has no such bill to pay --
        /// this is left with just enough to take the ruled look off a straight run.
        /// </para>
        /// </summary>
        private const float WanderFraction = 0.02f;

        /// <summary>
        /// How far the <em>fringe</em> is pushed about, as a fraction of a tile, and it is a
        /// good deal further than the coast itself.
        /// <para>
        /// It can afford to be, because it is not load-bearing. The coast proper has to have the
        /// sand covering the corners of the tile grid wherever it goes, so every pixel it
        /// wanders is paid for twice over in width. The fringe covers nothing and promises
        /// nothing: whatever it adds is added outside a beach that is already whole.
        /// </para>
        /// <para>
        /// Which is what makes the beach an irregular one. A single stroke is a constant width
        /// by construction -- wander it as much as you like and it is still a ribbon, merely a
        /// wobbly one -- so the sand is laid down <b>twice</b>, once along each line, and what
        /// is seen is the union of the two. Where they diverge the beach is broad; where they
        /// nearly coincide it pinches to the width of the one that must always be there. That
        /// is a shore that is wide in the bays and thin on the headlands, and it costs the
        /// guarantee nothing.
        /// </para>
        /// <para>
        /// Hashed on its own seed rather than on a multiple of the coast's, or the two would
        /// bulge in the same places and stay parallel, which is the ribbon again.
        /// </para>
        /// </summary>
        private const float FringeWanderFraction = 0.10f;

        /// <summary>The coast's own wander, and the skirt's, which must not agree.</summary>
        private const uint WanderSeed = 0x6D2B79F5u;

        /// <inheritdoc cref="WanderSeed"/>
        private const uint FringeSeed = 0x1B873593u;

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
        /// corners and the wander need, whichever is the greater. See <see cref="Width"/>.
        /// </summary>
        private const float ShoreFraction = 0.20f;

        /// <summary>
        /// Width of the second pass of sand, the one laid along the fringe, as a fraction of a
        /// tile. Well under the first: it is there to widen the beach unevenly, not to be a
        /// beach of its own, and a fringe near the width of the coast would simply double the
        /// ribbon.
        /// <para>
        /// This and <see cref="FringeWanderFraction"/> together are what the beach can swell to
        /// -- half of this plus the whole of that, outside the coast's own half width. They want
        /// reading as one number and not two, because it is their sum that is either a shore
        /// that varies or a shore that is fat.
        /// </para>
        /// </summary>
        private const float FringeShoreFraction = 0.12f;

        /// <summary>
        /// Width of the skirt of sand outside that, which is blurred so the beach fades into
        /// the water instead of ending at a stroke width.
        /// <para>
        /// Kept close to the solid part rather than several times it. A skirt much wider than
        /// the sand it belongs to stops reading as the far end of a beach and starts reading as
        /// a halo around a line -- and a halo is most of what makes a bright band look lit from
        /// within rather than lain on the ground. It wants to be the last of the sand running
        /// out, which is a short distance, and not a glow.
        /// </para>
        /// </summary>
        private const float SandSkirtFraction = 0.20f;

        /// <summary>
        /// How far the skirt of sand is blurred, as a fraction of the tile. Short, for the same
        /// reason the skirt is narrow: this is meant to be the sand thinning out over the last
        /// of its width, and a blur run much past that turns the strip into a lit edge with a
        /// soft surround, which is the look being got rid of.
        /// </summary>
        private const float SandBlurFraction = 0.09f;

        /// <summary>
        /// How wide the shallows are drawn before blurring, as a fraction of the tile. Reaching
        /// half of this either side of the coast, of which the landward half is thrown away by
        /// the clip -- a stroke has two sides and only one of them is the sea.
        /// </summary>
        private const float ShallowsFraction = 0.95f;

        /// <summary>
        /// How far the shallows are blurred. Most of a tile, because what is being drawn is the
        /// bottom coming up under the water rather than anything with an edge, and a falloff
        /// that lands inside one tile would read as a rim round the island.
        /// </summary>
        private const float ShallowsBlurFraction = 0.26f;

        /// <summary>
        /// Width of the line of surf along the waterline itself. Thin, and drawn faint -- see
        /// <see cref="Surf"/> -- because a bright line blurred along a coast is a glow, whatever
        /// it is called.
        /// </summary>
        private const float SurfFraction = 0.06f;

        /// <inheritdoc cref="SurfFraction"/>
        private const float SurfBlurFraction = 0.035f;

        /// <summary>
        /// Wet sand seen from above: warm and fairly dark, which is what a beach at the
        /// waterline actually is -- sand with water still in it is a shade or two down from the
        /// dry sand behind it, not a shade or two up.
        /// <para>
        /// Opaque, and that does two jobs. It is the part that has to hide the corners of the
        /// tile grid, and a corner half showing is a corner showing. It is also the difference
        /// between a beach and a band of light: paint let through at four fifths picks up the
        /// water and the grass either side of it and comes back as a pale average of both, which
        /// has no colour of its own to read as sand -- so what is seen is a bright strip rather
        /// than a material.
        /// </para>
        /// </summary>
        private static readonly SKColor Sand = new(0xC2, 0xA9, 0x7E, 0xFF);

        /// <summary>
        /// The sand running out, at the seaward and landward ends of the strip alike. The same
        /// sand a little darker rather than a little paler, so that the beach fades by thinning
        /// out into what is around it instead of brightening away from it.
        /// </summary>
        /// <inheritdoc cref="SandSkirtFraction"/>
        private static readonly SKColor SandSkirt = new(0xB3, 0x9A, 0x72, 0x6E);

        /// <summary>
        /// Sand that has dried out, up at the back of the beach, and the odd shell or dark
        /// pebble lying in it. What <see cref="Grains"/> mottles <see cref="Sand"/> towards.
        /// </summary>
        private static readonly SKColor SandDry = new(0xD5, 0xC0, 0x94);

        /// <inheritdoc cref="SandDry"/>
        private static readonly SKColor Shell = new(0xE9, 0xDF, 0xC6);

        /// <inheritdoc cref="SandDry"/>
        private static readonly SKColor Pebble = new(0x86, 0x71, 0x54);

        /// <summary>The speckle's own noise, unrelated to the wander's.</summary>
        private const uint GrainSeed = 0x9C41E70Du;

        /// <summary>
        /// The speckle texture's size in pixels, and how many tiles of map it is stretched
        /// across. Between them they decide the one thing that matters here, which is how near
        /// life size the texture lands at the zoom being drawn.
        /// <para>
        /// Ten tiles across five hundred and twelve pixels puts it at life size at about fifty
        /// pixels a tile, which is the near end of the zoom ladder -- and the near end is the
        /// right place to put it, because grain is only worth drawing where the beach is wide
        /// enough to see grain in. Zoomed further out the texture minifies and the speckle
        /// averages down towards an even sand, which is what sand looks like from that far up
        /// anyway.
        /// </para>
        /// <para>
        /// Landing near life size is also what lets the grain be per pixel, as
        /// <see cref="RenderingFunctions.RenderDesert"/> draws it. Stretched hard the other way
        /// it could not be: magnified, single pixels of noise come back as a mosaic of visible
        /// squares rather than as grains, which reads as a bad texture and not as a beach.
        /// </para>
        /// <para>
        /// Ten tiles is also long enough that the repeat does not announce itself. It would on a
        /// field; on a ribbon a third of a tile wide the eye never sees two copies at once.
        /// </para>
        /// </summary>
        private const int GrainEdge = 512;

        /// <inheritdoc cref="GrainEdge"/>
        private const int GrainTiles = 10;

        /// <summary>
        /// The sea where the bottom is close under it. Green rather than blue, and that is the
        /// physics rather than a preference: water takes the red out of light first and the
        /// blue last, so a metre of it over pale sand comes back green while ten metres of it
        /// comes back blue. It is laid over the water the renderer already drew, so it only has
        /// to carry the sea that far and not paint it from nothing.
        /// <para>
        /// Muted green rather than a clean one, which is the rest of that same physics and was
        /// the single biggest thing making this coast glow. What is being seen through the water
        /// is <em>sand</em>, so the colour coming back is a green with the sand still in it. A
        /// saturated turquoise is the colour of nothing on a shelf: it has no bottom in it, and
        /// laid down a tile wide and heavily blurred it reads as light coming out of the coast
        /// rather than as ground showing through the sea.
        /// </para>
        /// </summary>
        private static readonly SKColor Shallows = new(0x8E, 0xB4, 0xA0, 0x66);

        /// <summary>
        /// The surf. Not white: broken water over sand is white with the sand in it, and a
        /// clean white line reads as something drawn on rather than something floating in the
        /// water.
        /// </summary>
        private static readonly SKColor Surf = new(0xDE, 0xD8, 0xC4, 0x36);

        /// <summary>
        /// The coast for this frame. Kept and rewound rather than made afresh, since it is
        /// rebuilt on every frame and would otherwise be a few hundred segments of garbage
        /// sixty times a second.
        /// </summary>
        private readonly SKPath _path = new();

        /// <summary>
        /// The same coast wandered further, which the skirt of sand is drawn along instead of
        /// the coast proper. See <see cref="FringeWanderFraction"/>.
        /// </summary>
        private readonly SKPath _fringe = new();

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
        /// The speckled sand, one shader per zoom level -- all of them views of the one texture
        /// below at different scales, so this is a handful of bytes each and not a copy.
        /// </summary>
        private readonly Dictionary<int, SKShader> _sandShader = [];

        /// <summary>
        /// The speckle itself, built once and kept: a megabyte for the life of the process,
        /// against building it per zoom level or per frame.
        /// </summary>
        private static SKImage? _grains;

        /// <summary>
        /// Which path this pass is laying down, how hard it is wandering, on what hash, and
        /// whether it is the pass that gathers the sea.
        /// <para>
        /// Fields rather than arguments because the walk threads them through four methods that
        /// carry nine parameters between them already, and because they are only ever set and
        /// read inside a single call to <see cref="Render"/> -- which is safe for exactly the
        /// reason this is an instance and not a static: one renderer, one thread.
        /// </para>
        /// </summary>
        private SKPath _target;

        /// <inheritdoc cref="_target"/>
        private float _wander;

        /// <inheritdoc cref="_target"/>
        private uint _seed;

        /// <inheritdoc cref="_target"/>
        private bool _gatherSea;

        public Coastline() => _target = _path;

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
            _fringe.Rewind();
            _sea.Rewind();

            var (minX, minY, maxX, maxY) = VisibleTiles.For(canvas, tileSize);

            var soft = tileSize >= MinShallowsTileSize;

            _target = _path;
            _wander = WanderFraction;
            _seed = WanderSeed;
            _gatherSea = soft;

            Walk(tiles, world, minX, minY, maxX, maxY, tileSize);

            if (_path.IsEmpty)
                return;

            // The skirt's own line, walked a second time on its own hash. A second walk rather
            // than a second set of points threaded through the first, because the walk is a few
            // hundred tiles of a screenful that may be tens of thousands -- cheaper to do twice
            // than to complicate, and only done at all where there is a skirt to draw.
            if (soft)
            {
                _target = _fringe;
                _wander = FringeWanderFraction;
                _seed = FringeSeed;
                _gatherSea = false;

                Walk(tiles, world, minX, minY, maxX, maxY, tileSize);
            }

            // Deepest first, and each one softer at its edges than the one before is hard at
            // its middle: the sea shoaling, then the sand fading out of it, then the sand
            // itself, then the surf along the line where the two meet. Nothing is outlined and
            // nothing ends at a stroke width except the one stroke that has to.
            Shoal(canvas, tileSize);

            // Not below the size the softer half of the coast is drawn at. The speckle is
            // grains of sand, and a beach two pixels across has no room for any -- so this is
            // also what keeps a map only ever seen at that size from building the texture at
            // all.
            var speckle = soft ? Speckle(tileSize) : null;

            if (soft)
            {
                Stroke(
                    canvas, _fringe, SandSkirt, SandSkirtFraction * tileSize,
                    Blur(_sandBlur, SandBlurFraction, tileSize));

                // The uneven half of the beach. Laid before the guaranteed one and not after,
                // so that wherever the two disagree it is the stroke that has to cover the tile
                // corners which ends up on top.
                Stroke(
                    canvas, _fringe, Sand, Math.Max(FringeShoreFraction * tileSize, 1f),
                    speckle: speckle);
            }

            Stroke(canvas, _path, Sand, Math.Max(Width * tileSize, 1f), speckle: speckle);

            if (soft)
            {
                // Along the fringe rather than the coast, because after the pass above the
                // waterline is mostly out at the fringe -- and being the faintest thing here it
                // is no great matter where it is not.
                Stroke(
                    canvas, _fringe, Surf, Math.Max(SurfFraction * tileSize, 1f),
                    Blur(_surfBlur, SurfBlurFraction, tileSize));
            }
        }

        /// <summary>
        /// One pass over the screenful, laying the coast into <see cref="_target"/>.
        /// <para>
        /// Walked as a span where the list allows it, for the reason every other pass over the
        /// tiles does the same: on a large map most of them are off screen, and fetching each
        /// one through the interface only to drop it is milliseconds a frame.
        /// </para>
        /// </summary>
        private void Walk(
            IReadOnlyList<MapTile> tiles, TileGrid world,
            int minX, int minY, int maxX, int maxY, int tileSize)
        {
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
                    _path,
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
            Math.Max(
                ShoreFraction,
                ((CornerFraction * CornerOvershoot) + WanderFraction) * 2f * CoverMargin);

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

        private static void Stroke(
            SKCanvas canvas,
            SKPath path,
            SKColor colour,
            float width,
            SKMaskFilter? blur = null,
            SKShader? speckle = null)
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

                // Where there is a speckle it supplies the colour and the colour above is only
                // what the speckle was mixed from. The paint does not own it -- it is cached and
                // reused -- so disposing the pen must not take it with it, which is why it is
                // never created here.
                Shader = speckle,
                FilterQuality = speckle is null ? SKFilterQuality.None : SKFilterQuality.Low,
            };

            canvas.DrawPath(path, pen);
        }

        /// <summary>
        /// The speckled sand at one zoom level: the one texture below, scaled so that a copy of
        /// it spans <see cref="GrainTiles"/> tiles of map.
        /// <para>
        /// Anchored in map space rather than to the screen, which a shader is by default --
        /// it is laid down in the canvas's own coordinates -- so the grains stay on the stretch
        /// of beach they belong to instead of crawling along it as the view is panned. The same
        /// reason <see cref="GroundEdge"/> gives for never translating its noise.
        /// </para>
        /// </summary>
        private SKShader Speckle(int tileSize)
        {
            if (_sandShader.TryGetValue(tileSize, out var shader))
                return shader;

            var scale = GrainTiles * tileSize / (float)GrainEdge;

            shader = SKShader.CreateImage(
                Grains(),
                SKShaderTileMode.Repeat,
                SKShaderTileMode.Repeat,
                SKMatrix.CreateScale(scale, scale));

            _sandShader[tileSize] = shader;

            return shader;
        }

        /// <summary>
        /// Sand, as a tiling texture: damp and dry in broad patches, grain over the whole of it,
        /// and the odd shell or pebble.
        /// <para>
        /// The patches matter more than the grain does, which is not obvious. Grain alone is a
        /// uniform fizz, and a uniform fizz laid along a strip is still a strip of one colour --
        /// the eye averages it back out at any distance. What stops the beach reading as a line
        /// somebody drew is that it is a different shade of sand fifty yards further along.
        /// </para>
        /// <para>
        /// Built at most once. Two renderers racing here would each build one and one of them
        /// would be collected unused, which is a wasted megabyte and not a wrong picture -- the
        /// texture is a pure function of the constants above.
        /// </para>
        /// </summary>
        private static SKImage Grains()
        {
            if (_grains is not null)
                return _grains;

            var pixels = new byte[GrainEdge * GrainEdge * 4];
            var scale = 1f / GrainEdge;
            var i = 0;

            for (var y = 0; y < GrainEdge; y++)
            {
                for (var x = 0; x < GrainEdge; x++)
                {
                    var damp = PeriodicFbm(x * scale, y * scale, 4, 3, GrainSeed ^ 0x3B17u);
                    var colour = LerpColor(Sand, SandDry, Smoothstep(0.32f, 0.76f, damp));

                    colour = Shade(colour, (Hash01(x, y, GrainSeed) - 0.5f) * 0.30f);

                    // A shell here and there, and the odd dark pebble. Sparse enough to be
                    // something noticed rather than a texture in its own right.
                    var fleck = Hash01(x, y, GrainSeed ^ 0xBEEFu);

                    if (fleck > 0.9972f)
                        colour = Shell;
                    else if (fleck < 0.0028f)
                        colour = Pebble;

                    pixels[i++] = colour.Red;
                    pixels[i++] = colour.Green;
                    pixels[i++] = colour.Blue;
                    pixels[i++] = 0xFF;
                }
            }

            var info = new SKImageInfo(GrainEdge, GrainEdge, SKColorType.Rgba8888, SKAlphaType.Opaque);

            return _grains = SKImage.FromPixelCopy(info, pixels);
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
            if (_gatherSea
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
            if (mask == Shoreline.All)
            {
                // An island one tile across, so the coast is a closed loop with no ends. Drawn
                // as one, rather than as four runs that would each have to stop somewhere: an
                // end in the middle of a curve is a round cap where there should be no cap.
                Loop(x, y, size, corner);
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

                Run(world, x, y, mask, side, size, corner);
            }
        }

        /// <summary>
        /// One unbroken stretch of coast around a tile, from the corner it starts at to the
        /// corner it runs out at.
        /// </summary>
        private void Run(
            TileGrid world, int x, int y, int mask, int side, float size, float corner)
        {
            var placed = false;

            while (true)
            {
                var next = (side + 1) & 3;
                var previous = (side + 3) & 3;

                var (startX, startY) = Start(side, x, y, size);
                var (endX, endY) = End(side, x, y, size);
                var (stepX, stepY) = Step(side);

                if (!placed)
                {
                    // A run starts at the corner itself where the coast arrives along the same
                    // straight line out of the tile before, so a straight shore has no notch at
                    // every tile boundary. It starts short of the corner only where the coast
                    // turns into this tile: the tile it turned out of has drawn the curve
                    // through that corner already, and stopped exactly here.
                    var into = Turns(world, x, y, side, previous) ? corner : 0f;

                    _target.MoveTo(startX + (stepX * into), startY + (stepY * into));
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

                _target.LineTo(endX - (stepX * trim), endY - (stepY * trim));

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

                _target.QuadTo(endX, endY, endX + (nextX * corner), endY + (nextY * corner));

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

        /// <summary>
        /// A tile with water on every side: a rounded square, closed -- and, since its four
        /// corners are wandered like everyone else's, not much of a square.
        /// </summary>
        private void Loop(int x, int y, float size, float corner)
        {
            var a = Start(0, x, y, size);
            var b = Start(1, x, y, size);
            var c = Start(2, x, y, size);
            var d = Start(3, x, y, size);

            // Trimmed back along the axes rather than along the wandered sides, exactly as Run
            // does it: the push is a fraction of a tile and the sides are a whole one, so the
            // difference between the two is well under a pixel at any zoom that draws a coast.
            _target.MoveTo(a.X + corner, a.Y);
            _target.LineTo(b.X - corner, b.Y);
            _target.QuadTo(b.X, b.Y, b.X, b.Y + corner);
            _target.LineTo(c.X, c.Y - corner);
            _target.QuadTo(c.X, c.Y, c.X - corner, c.Y);
            _target.LineTo(d.X + corner, d.Y);
            _target.QuadTo(d.X, d.Y, d.X, d.Y - corner);
            _target.LineTo(a.X, a.Y + corner);
            _target.QuadTo(a.X, a.Y, a.X + corner, a.Y);
            _target.Close();
        }

        /// <summary>
        /// The sides in the order the walk goes round them: north, east, south, west, which is
        /// clockwise on screen. Every corner is then the end of one side and the start of the
        /// next, and the turn between them needs no special case for which corner it is.
        /// </summary>
        private static int Bit(int side) => 1 << side;

        /// <summary>
        /// Where a side of a tile begins, wandered.
        /// <para>
        /// Keyed on the corner's own place in the grid rather than on the tile asking for it,
        /// and that is the whole of what makes this work. Every corner is shared by up to four
        /// tiles, each of which may run a stretch of coast through it; asked from any of them it
        /// hashes the same and comes back in the same place, so the coast stays joined. Keyed on
        /// the tile instead, the same corner would land somewhere different depending on who was
        /// drawing it, and the shore would come apart at every tile boundary.
        /// </para>
        /// </summary>
        private (float X, float Y) Start(int side, int x, int y, float size)
        {
            var (cx, cy) = CornerAt(side);

            var gx = x + cx;
            var gy = y + cy;

            var (jx, jy) = Jitter(gx, gy, size);

            return ((gx * size) + jx, (gy * size) + jy);
        }

        /// <inheritdoc cref="Start"/>
        private (float X, float Y) End(int side, int x, int y, float size) =>
            Start((side + 1) & 3, x, y, size);

        /// <summary>Which corner of a tile a side starts at, going round clockwise.</summary>
        private static (int X, int Y) CornerAt(int side) => side switch
        {
            0 => (0, 0),
            1 => (1, 0),
            2 => (1, 1),
            _ => (0, 1),
        };

        /// <summary>
        /// How far one corner of the grid is pushed, and which way.
        /// <para>
        /// Polar rather than a pair of offsets, so that the push is never longer than it says it
        /// is. Hashing x and y separately and using them as they come would reach a further
        /// <c>sqrt(2)</c> on the diagonal, and since <see cref="Width"/> is a promise made
        /// against this number, a corner that quietly went half again as far would be a corner
        /// of the tile grid showing through the sand -- rarely, diagonally, and very hard to
        /// account for later.
        /// </para>
        /// </summary>
        private (float X, float Y) Jitter(int cornerX, int cornerY, float size)
        {
            var reach = _wander * size;

            if (reach <= 0f)
                return (0f, 0f);

            var angle = Hash01(cornerX, cornerY, _seed) * MathF.Tau;
            var radius = Hash01(cornerX, cornerY, _seed ^ 0x9E3779B9u) * reach;

            return (MathF.Cos(angle) * radius, MathF.Sin(angle) * radius);
        }

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
