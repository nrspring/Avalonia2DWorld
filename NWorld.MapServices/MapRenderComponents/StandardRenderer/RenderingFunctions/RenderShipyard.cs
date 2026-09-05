using System;
using System.Threading.Tasks;
using NWorld.Map.Models;
using SkiaSharp;
using static NWorld.MapServices.MapRenderComponents.StandardRenderer.RoadNetwork;

namespace NWorld.MapServices.MapRenderComponents.StandardRenderer.RenderingFunctions
{
    /// <summary>
    /// A shipyard: slipways running down into the water, a gantry across them, and a shed and a
    /// timber stack on the dry ground behind.
    /// <para>
    /// The one thing built on this map whose shape is decided by the <b>sea</b> rather than by
    /// the roads, and it is the only one for which that is the right question. A yard exists to
    /// put a hull in the water; everything about it points that way, and which way that is
    /// depends on where the water is. So this reads <see cref="Shoreline.Mask"/> where a fort or
    /// a factory reads <see cref="RoadNetwork.Mask"/> -- the same four bits, meaning something
    /// else -- and the slipways go down whichever sides face water, with the shed and the stack
    /// filling whatever dry ground is left.
    /// </para>
    /// <para>
    /// It costs nothing to keep that up to date, which is the nice part. The mask is read off the
    /// map when the tile is drawn, so flooding the ground beside a yard turns it to face the new
    /// water on the very next frame, and nothing has to go round afterwards and tell it.
    /// </para>
    /// <para>
    /// Timber and tar rather than stone or brick. A yard is the one industrial place on this map
    /// built almost entirely out of wood -- the slips, the shores, the staging, the stacks -- and
    /// it should not be mistaken at a glance for the factory it otherwise sits near.
    /// </para>
    /// </summary>
    public static class RenderShipyard
    {
        /// <summary>How far the yard stops short of a tile edge with dry land beyond it.</summary>
        private const float Margin = 0.06f;

        /// <summary>
        /// How far a slipway reaches from the tile edge it runs out of, as a fraction of the
        /// tile. Past halfway, so that a yard with water on two sides has its slips meet in the
        /// middle rather than leaving an island of yard between them.
        /// </summary>
        private const float SlipReach = 0.46f;

        /// <summary>How wide the whole run of slipways is, as a fraction of the tile.</summary>
        private const float SlipSpread = 0.66f;

        /// <summary>How many slips there are in that run.</summary>
        private const int Slips = 3;

        /// <summary>How much of the spread is timber rather than the gaps between the slips.</summary>
        private const float SlipShare = 0.62f;

        /// <summary>How thick the gantry beam across the slips is, as a fraction of the tile.</summary>
        private const float GantryShare = 0.055f;

        /// <summary>
        /// Below this the slips are stripes a pixel wide and the yard is drawn as a ramp, a shed
        /// and nothing else -- which at that size is as much as reads.
        /// </summary>
        private const int MinDetailTileSize = 14;

        /// <summary>The yard: trodden earth and wood chips, paler and warmer than a works.</summary>
        private static readonly SKColor Ground = new(0x6A, 0x5C, 0x47);

        /// <summary>The slipways themselves: greased baulks of timber, wet at the water end.</summary>
        private static readonly SKColor Timber = new(0xC2, 0xA3, 0x71);

        /// <inheritdoc cref="Timber"/>
        private static readonly SKColor Wet = new(0x7B, 0x66, 0x47);

        /// <summary>The gantry, and the staging it carries. Tarred, so nearly black.</summary>
        private static readonly SKColor Tar = new(0x4A, 0x3A, 0x2C);

        /// <summary>The mould loft and the sawmill behind: one long shed with a tiled roof.</summary>
        private static readonly SKColor Roof = new(0x86, 0x5A, 0x41);

        /// <summary>Sawn planks stacked to season, which is half of what a yard is at any time.</summary>
        private static readonly SKColor Stacked = new(0xD2, 0xB8, 0x8A);

        /// <summary>What the shed and the gantry cast onto the yard.</summary>
        private static readonly SKColor Shadow = new(0x00, 0x00, 0x00, 0x46);

        /// <summary>
        /// Shaped by the water rather than the roads. The mask is the only difference between
        /// this and any other place built on one tile -- see <see cref="Shoreline"/>.
        /// </summary>
        private static readonly StoneSpan Span =
            new(0x5417_9A2Du, Draw, runsWhenAlone: false, mask: Shoreline.Mask);

        public static Task Render(TileRenderContext context) => Span.Render(context);

        /// <inheritdoc cref="StoneSpan.Prewarm"/>
        public static Task Prewarm(int tileSize) => Span.Prewarm(tileSize);

        /// <inheritdoc cref="StoneSpan.ClearCache"/>
        public static void ClearCache() => Span.ClearCache();

        /// <param name="water">Which sides of the tile have water against them.</param>
        private static void Draw(SKCanvas canvas, int tileSize, int water, uint seed)
        {
            var margin = tileSize * Margin;

            // Built right out to the edge on the sides that face water and stopped short on the
            // rest, which is what a yard does: the whole point of the place is that the ground
            // runs into the sea without a step, and a margin there would be a wall.
            var yard = new SKRect(
                (water & West) != 0 ? 0f : margin,
                (water & North) != 0 ? 0f : margin,
                tileSize - ((water & East) != 0 ? 0f : margin),
                tileSize - ((water & South) != 0 ? 0f : margin));

            using var paint = new SKPaint { IsAntialias = false, Style = SKPaintStyle.Fill };

            paint.Color = Ground;
            canvas.DrawRect(yard, paint);

            Grit(canvas, yard, Ground, tileSize / 18f, 0.24f, seed);

            if (tileSize < MinDetailTileSize)
            {
                // A pale ramp on each water side and a dark shed behind. Everything below has
                // bottomed out to a pixel by here, and three slips a pixel wide average to one
                // flat band -- so it is better to draw the one band on purpose.
                foreach (var side in StoneWork.Arms)
                {
                    if ((water & side) != 0)
                    {
                        paint.Color = Timber;
                        canvas.DrawRect(Run(tileSize, side, SlipSpread, SlipReach), paint);
                    }
                }

                paint.Color = Roof;
                canvas.DrawRect(Shed(tileSize, water, margin), paint);

                return;
            }

            Slipways(canvas, tileSize, water, seed);

            var shed = Shed(tileSize, water, margin);
            var cast = Math.Max(1f, tileSize * 0.04f);

            paint.Color = Shadow;
            canvas.DrawRect(SKRect.Create(shed.Left + cast, shed.Top + cast, shed.Width, shed.Height), paint);

            paint.Color = Roof;
            canvas.DrawRect(shed, paint);

            // The ridge down the middle of the shed, which is what tells a roof from a floor.
            var ridge = shed.Width > shed.Height
                ? SKRect.Create(shed.Left, shed.MidY - Math.Max(0.5f, tileSize * 0.012f), shed.Width, Math.Max(1f, tileSize * 0.024f))
                : SKRect.Create(shed.MidX - Math.Max(0.5f, tileSize * 0.012f), shed.Top, Math.Max(1f, tileSize * 0.024f), shed.Height);

            paint.Color = RenderNoise.Shade(Roof, -0.22f);
            canvas.DrawRect(ridge, paint);

            Timbers(canvas, tileSize, shed, yard, seed);
        }

        /// <summary>
        /// The slips: a run of greased baulks down each side that faces water, darkening towards
        /// the sea, with a tarred gantry standing across them.
        /// <para>
        /// Darkening is the detail that sells it. A slipway runs from dry ground down below the
        /// waterline, so its far end is permanently wet and weeded and its near end is not, and
        /// that gradient is the only thing in the picture saying which way a hull would travel.
        /// </para>
        /// </summary>
        private static void Slipways(SKCanvas canvas, int tileSize, int water, uint seed)
        {
            if (water == 0)
                return;

            using var paint = new SKPaint { IsAntialias = false, Style = SKPaintStyle.Fill };

            var steps = Math.Max(3, tileSize / 6);

            foreach (var side in StoneWork.Arms)
            {
                if ((water & side) == 0)
                    continue;

                var run = Run(tileSize, side, SlipSpread, SlipReach);
                var down = side is North or South;

                var across = down ? run.Width : run.Height;
                var pitch = across / Slips;
                var baulk = Math.Max(1f, pitch * SlipShare);

                for (var i = 0; i < Slips; i++)
                {
                    var offset = (i * pitch) + ((pitch - baulk) / 2f);

                    // Walked from the dry end to the wet one, a step at a time, so the timber
                    // can darken along its length. Rectangles rather than a gradient: everything
                    // else here is drawn in flat cells, and one smooth ramp among them shows.
                    for (var s = 0; s < steps; s++)
                    {
                        var t = (s + 0.5f) / steps;

                        // t runs from the land end to the water end whichever way the slip
                        // points, which is what keeps all four orientations consistent.
                        var wetness = side is North or West ? 1f - t : t;
                        var tone = RenderNoise.LerpColor(Timber, Wet, wetness);

                        paint.Color = RenderNoise.Shade(
                            tone, (RenderNoise.Hash01(i, s, seed) - 0.5f) * 0.16f);

                        canvas.DrawRect(down
                            ? SKRect.Create(
                                run.Left + offset,
                                run.Top + (run.Height * s / steps),
                                baulk,
                                (run.Height / steps) + 1f)
                            : SKRect.Create(
                                run.Left + (run.Width * s / steps),
                                run.Top + offset,
                                (run.Width / steps) + 1f,
                                baulk),
                            paint);
                    }
                }

                // The gantry, standing across the slips at the dry end of the run: a beam on
                // legs, which is what a hull is built under.
                var beam = Math.Max(1f, tileSize * GantryShare);

                paint.Color = Tar;

                // Just past the run on either side, so it reads as a beam standing over the
                // slips rather than as a bar laid across the yard.
                var over = beam * 0.6f;

                canvas.DrawRect(down
                    ? SKRect.Create(
                        run.Left - over,
                        side == North ? run.Bottom - beam : run.Top,
                        run.Width + (over * 2f),
                        beam)
                    : SKRect.Create(
                        side == West ? run.Right - beam : run.Left,
                        run.Top - over,
                        beam,
                        run.Height + (over * 2f)),
                    paint);
            }
        }

        /// <summary>
        /// Stacks of sawn planks, seasoning in whatever yard the shed and the slips have left.
        /// </summary>
        private static void Timbers(SKCanvas canvas, int tileSize, SKRect shed, SKRect yard, uint seed)
        {
            using var paint = new SKPaint { IsAntialias = false, Style = SKPaintStyle.Fill };

            var plank = Math.Max(1f, tileSize * 0.05f);
            var length = Math.Max(2f, tileSize * 0.22f);

            for (var i = 0; i < 3; i++)
            {
                var x = yard.Left + (RenderNoise.Hash01(i, 7, seed) * (yard.Width - length));
                var y = yard.Top + (RenderNoise.Hash01(i, 9, seed) * (yard.Height - plank));

                var stack = SKRect.Create(x, y, length, plank);

                // Not under the shed, which would be a stack on a roof. Anywhere else in the
                // yard is somewhere a yard would actually put one.
                if (stack.IntersectsWith(shed))
                    continue;

                paint.Color = RenderNoise.Shade(
                    Stacked, (RenderNoise.Hash01(i, 11, seed) - 0.5f) * 0.18f);

                canvas.DrawRect(stack, paint);
            }
        }

        /// <summary>
        /// Where the shed stands: on the driest side of the tile, which is the one furthest from
        /// any water.
        /// </summary>
        private static SKRect Shed(int tileSize, int water, float margin)
        {
            var deep = tileSize * (1f - SlipReach);
            var thickness = Math.Max(2f, tileSize * 0.26f);

            // Pushed away from every side that faces water. With water on one side the shed sits
            // against the far edge; with water on two it tucks into the corner between the two
            // that are dry; with water all round there is nowhere dry and it sits in the middle,
            // which is the only honest answer for a yard on a one-tile island.
            var left = (water & West) != 0 ? deep : margin;
            var top = (water & North) != 0 ? deep : margin;
            var right = tileSize - ((water & East) != 0 ? deep : margin);
            var bottom = tileSize - ((water & South) != 0 ? deep : margin);

            if (right - left <= 0 || bottom - top <= 0)
                return SKRect.Create(tileSize * 0.3f, tileSize * 0.3f, tileSize * 0.4f, tileSize * 0.4f);

            // Laid the long way along whichever of the two the yard has more room in, so the
            // shed reads as a building beside the slips rather than as a block dropped on them.
            return right - left >= bottom - top
                ? new SKRect(left, top, right, Math.Min(bottom, top + thickness))
                : new SKRect(left, top, Math.Min(right, left + thickness), bottom);
        }

        /// <summary>
        /// The band a run of slips occupies on one side: centred across that side, reaching in
        /// from the tile edge.
        /// </summary>
        private static SKRect Run(int tileSize, int side, float spread, float reach)
        {
            var half = tileSize * spread / 2f;
            var deep = tileSize * reach;
            var middle = tileSize / 2f;

            return side switch
            {
                North => new SKRect(middle - half, 0f, middle + half, deep),
                South => new SKRect(middle - half, tileSize - deep, middle + half, tileSize),
                East => new SKRect(tileSize - deep, middle - half, tileSize, middle + half),
                _ => new SKRect(0f, middle - half, deep, middle + half),
            };
        }

        /// <inheritdoc cref="RenderFactory" path="/summary"/>
        private static void Grit(
            SKCanvas canvas, SKRect area, SKColor colour, float cell, float depth, uint seed)
        {
            if (cell < 1f)
                cell = 1f;

            using var paint = new SKPaint { IsAntialias = false, Style = SKPaintStyle.Fill };

            canvas.Save();
            canvas.ClipRect(area);

            for (var y = area.Top; y < area.Bottom; y += cell)
            {
                for (var x = area.Left; x < area.Right; x += cell)
                {
                    var lift = (RenderNoise.Hash01((int)(x / cell), (int)(y / cell), seed) - 0.5f) * depth;

                    paint.Color = RenderNoise.Shade(colour, lift);
                    canvas.DrawRect(SKRect.Create(x, y, cell, cell), paint);
                }
            }

            canvas.Restore();
        }
    }
}
