using System;
using System.Threading.Tasks;
using NWorld.Map.Models;
using SkiaSharp;
using static NWorld.MapServices.MapRenderComponents.StandardRenderer.RoadNetwork;

namespace NWorld.MapServices.MapRenderComponents.StandardRenderer.RenderingFunctions
{
    /// <summary>
    /// A factory: a long shed under a sawtooth roof, a chimney smudging the air beside it, and a
    /// yard with a loading apron wherever a road arrives.
    /// <para>
    /// The sawtooth is the whole of what makes it read as a factory rather than as a big barn,
    /// and it is worth knowing why it existed. Before electric light a workshop was lit through
    /// its roof, and the roof was folded into a run of ridges with one face sloped to the sky and
    /// glazed -- north-facing, so the light was even all day and never direct. From above that is
    /// a set of stripes, alternately bright metal and dark glass, and nothing else on a map looks
    /// remotely like it.
    /// </para>
    /// <para>
    /// A place rather than a surface, so it is built like <see cref="RenderFort"/> and not like
    /// <see cref="RenderCity"/>: whole on its one tile, with ground all round it, and two side by
    /// side are two factories. What it takes from the road network is the same thing a fort takes
    /// -- not how far to build but where the doors go. Run a road up to it and the yard opens an
    /// apron on that side on the next frame; leave it alone in a field and it is shut, which is
    /// what a works nobody can reach should look like.
    /// </para>
    /// <para>
    /// Soot and brick rather than the grey stone of the roads or the warm roofs of a town. It is
    /// the one thing on this map that burns coal, and looking as though it does is most of the
    /// job.
    /// </para>
    /// </summary>
    public static class RenderFactory
    {
        /// <summary>
        /// How far the yard stops short of the tile edge, as a fraction of the tile. A works is
        /// a discrete object: built to the edge it would fuse with whatever is on the next tile.
        /// </summary>
        private const float Margin = 0.06f;

        /// <summary>How much of the tile the shed itself covers, across and down.</summary>
        private const float ShedWidth = 0.72f;

        /// <inheritdoc cref="ShedWidth"/>
        private const float ShedHeight = 0.50f;

        /// <summary>
        /// Roughly how many ridges the sawtooth roof is folded into.
        /// <para>
        /// Five. Three reads as a shed with a stripe painted on it and eight is a texture by the
        /// time a tile is thirty pixels; five is the fewest that still reads as a repeating fold
        /// rather than as a pattern somebody chose.
        /// </para>
        /// </summary>
        private const int Ridges = 5;

        /// <summary>How much of each ridge is the glazed face, the rest being the metal one.</summary>
        private const float GlazedShare = 0.45f;

        /// <summary>The chimney's width, as a fraction of the tile.</summary>
        private const float ChimneyShare = 0.13f;

        /// <summary>
        /// Below this the ridges are a smear and the works is drawn as a shed, a yard and a dot
        /// of chimney -- which at that size is all anybody can see of one anyway.
        /// </summary>
        private const int MinDetailTileSize = 13;

        /// <summary>The yard: oiled earth, darker and greyer than a town's beaten streets.</summary>
        private static readonly SKColor Yard = new(0x5E, 0x59, 0x50);

        /// <summary>Brick, well sooted. The walls of the shed where they show past the roof.</summary>
        private static readonly SKColor Brick = new(0x6B, 0x4A, 0x3C);

        /// <summary>The lit face of a ridge: painted metal, dulled by the smoke it stands in.</summary>
        private static readonly SKColor Metal = new(0x93, 0x9A, 0x94);

        /// <summary>The glazed face. Dark because it is looking at the sky and not lit by it.</summary>
        private static readonly SKColor Glazing = new(0x3B, 0x4B, 0x57);

        /// <summary>The chimney's brickwork, and the cap of soot on top of it.</summary>
        private static readonly SKColor Stack = new(0x59, 0x3D, 0x33);

        /// <inheritdoc cref="Stack"/>
        private static readonly SKColor Soot = new(0x2B, 0x26, 0x24);

        /// <summary>What everything here casts onto the yard.</summary>
        private static readonly SKColor Shadow = new(0x00, 0x00, 0x00, 0x4E);

        private static readonly StoneSpan Span = new(0xFAC7_0217u, Draw, runsWhenAlone: false);

        public static Task Render(TileRenderContext context) => Span.Render(context);

        /// <inheritdoc cref="StoneSpan.Prewarm"/>
        public static Task Prewarm(int tileSize) => Span.Prewarm(tileSize);

        /// <inheritdoc cref="StoneSpan.ClearCache"/>
        public static void ClearCache() => Span.ClearCache();

        /// <param name="roads">Which sides a road arrives from, and so where the aprons go.</param>
        private static void Draw(SKCanvas canvas, int tileSize, int roads, uint seed)
        {
            var margin = tileSize * Margin;
            var yard = new SKRect(margin, margin, tileSize - margin, tileSize - margin);

            using var paint = new SKPaint { IsAntialias = false, Style = SKPaintStyle.Fill };

            paint.Color = Yard;
            canvas.DrawRect(yard, paint);

            // Rutted and oil-stained. The same trick the town's streets use: a flat fill under
            // the buildings is what says "drawn" at a glance, whatever is standing on it.
            Grit(canvas, yard, Yard, tileSize / 20f, 0.22f, seed);

            var shed = new SKRect(
                (tileSize - (tileSize * ShedWidth)) / 2f,
                (tileSize - (tileSize * ShedHeight)) / 2f,
                (tileSize + (tileSize * ShedWidth)) / 2f,
                (tileSize + (tileSize * ShedHeight)) / 2f);

            // Over the yard rather than under it, which took seeing it drawn to notice: laid
            // first, the apron is covered by the yard everywhere except the margin, and the one
            // thing it exists to show -- that a road arrives here and goes somewhere -- is the
            // part that gets covered.
            Aprons(canvas, tileSize, roads, shed, seed);

            var cast = Math.Max(1f, tileSize * 0.045f);

            paint.Color = Shadow;
            canvas.DrawRect(
                SKRect.Create(shed.Left + cast, shed.Top + cast, shed.Width, shed.Height), paint);

            if (tileSize < MinDetailTileSize)
            {
                // One block of roof and a dot of chimney. The stripes have bottomed out by here
                // -- five ridges in ten pixels is two pixels each and they average to a smear --
                // so what is left is the shape and the two tones it is made of.
                paint.Color = Metal;
                canvas.DrawRect(shed, paint);

                paint.Color = Glazing;
                canvas.DrawRect(SKRect.Create(shed.Left, shed.MidY, shed.Width, shed.Height / 3f), paint);

                paint.Color = Soot;
                canvas.DrawRect(
                    SKRect.Create(yard.Right - (tileSize * 0.2f), yard.Top + (tileSize * 0.06f),
                        Math.Max(1f, tileSize * 0.11f), Math.Max(1f, tileSize * 0.11f)),
                    paint);

                return;
            }

            Sawtooth(canvas, shed, tileSize, seed);
            Chimney(canvas, tileSize, yard, seed);
        }

        /// <summary>
        /// The roof: a run of ridges across the shed, each a face of metal and a face of glass.
        /// <para>
        /// Folded across the long way rather than down it, so the stripes run with the shed and
        /// the whole thing reads as one building rather than as a row of separate ones.
        /// </para>
        /// </summary>
        private static void Sawtooth(SKCanvas canvas, SKRect shed, int tileSize, uint seed)
        {
            using var paint = new SKPaint { IsAntialias = false, Style = SKPaintStyle.Fill };

            // The brick showing as a sill at the foot of the roof, which is what stops the
            // stripes from floating: a roof with no wall under it is a flag lying on the ground.
            var sill = Math.Max(1f, tileSize * 0.035f);

            paint.Color = Brick;
            canvas.DrawRect(shed, paint);

            var roof = shed;
            roof.Inflate(-sill, -sill);

            if (roof.Width <= 0 || roof.Height <= 0)
                return;

            var pitch = roof.Height / Ridges;
            var glazed = Math.Max(1f, pitch * GlazedShare);

            for (var i = 0; i < Ridges; i++)
            {
                var top = roof.Top + (i * pitch);

                // The metal first across the whole ridge, then the glass along the far edge of
                // it. Drawn as two rectangles rather than as a slope, because at these sizes a
                // shaded ramp is three grey pixels and a hard pair of tones is a roof.
                paint.Color = RenderNoise.Shade(Metal, (RenderNoise.Hash01(i, 0, seed) - 0.5f) * 0.10f);
                canvas.DrawRect(SKRect.Create(roof.Left, top, roof.Width, pitch), paint);

                paint.Color = RenderNoise.Shade(Glazing, (RenderNoise.Hash01(i, 1, seed) - 0.5f) * 0.14f);
                canvas.DrawRect(
                    SKRect.Create(roof.Left, top + pitch - glazed, roof.Width, glazed), paint);
            }
        }

        /// <summary>
        /// The chimney, standing in a corner of the yard with its own soot around it.
        /// <para>
        /// In the corner rather than on the shed, which is where they actually were: a stack
        /// serves the boiler house, the boiler house is a fire hazard, and it was built at arm's
        /// length from the shed full of timber and oil.
        /// </para>
        /// </summary>
        private static void Chimney(SKCanvas canvas, int tileSize, SKRect yard, uint seed)
        {
            using var paint = new SKPaint { IsAntialias = false, Style = SKPaintStyle.Fill };

            var side = Math.Max(2f, tileSize * ChimneyShare);

            // One of the four corners, fixed by the seed: the same works always has its stack in
            // the same place, and two works side by side do not.
            var right = (RenderNoise.Hash(0, 0, seed) & 1) == 0;
            var bottom = (RenderNoise.Hash(1, 0, seed) & 1) == 0;

            var x = right ? yard.Right - side - (tileSize * 0.03f) : yard.Left + (tileSize * 0.03f);
            var y = bottom ? yard.Bottom - side - (tileSize * 0.03f) : yard.Top + (tileSize * 0.03f);

            var stack = SKRect.Create(x, y, side, side);

            // The smudge first and the stack on top of it, so the soot lies on the yard and the
            // brickwork stands clear of it rather than being dirtied by its own smoke.
            var smudge = stack;
            smudge.Inflate(side * 0.55f, side * 0.55f);

            paint.Color = new SKColor(Soot.Red, Soot.Green, Soot.Blue, 0x3C);
            canvas.DrawRect(smudge, paint);

            paint.Color = Stack;
            canvas.DrawRect(stack, paint);

            // The mouth. A stack seen from directly overhead is a ring of brick round a hole,
            // and the hole is the only part of a factory that is truly black.
            var mouth = stack;
            mouth.Inflate(-side * 0.28f, -side * 0.28f);

            if (mouth.Width >= 1f && mouth.Height >= 1f)
            {
                paint.Color = Soot;
                canvas.DrawRect(mouth, paint);
            }
        }

        /// <summary>
        /// The loading apron on each side a road arrives from: a strip of hard standing running
        /// from the tile edge in to the doors, in the road's own bed so the join does not show.
        /// <para>
        /// Stopped at the shed rather than run under it, which is what makes it read as a way in
        /// rather than as a stripe painted across the works. A yard is mostly a place for carts
        /// to stand, and where they stand is between the gate and the door.
        /// </para>
        /// </summary>
        private static void Aprons(SKCanvas canvas, int tileSize, int roads, SKRect shed, uint seed)
        {
            if (roads == 0)
                return;

            using var paint = new SKPaint { IsAntialias = false, Style = SKPaintStyle.Fill };

            var half = tileSize / 2f;
            var width = tileSize * StoneWork.Way / 2f;

            foreach (var arm in StoneWork.Arms)
            {
                if ((roads & arm) == 0)
                    continue;

                var strip = arm switch
                {
                    North => new SKRect(half - width, 0f, half + width, shed.Top),
                    South => new SKRect(half - width, shed.Bottom, half + width, tileSize),
                    East => new SKRect(shed.Right, half - width, tileSize, half + width),
                    _ => new SKRect(0f, half - width, shed.Left, half + width),
                };

                if (strip.Width <= 0 || strip.Height <= 0)
                    continue;

                paint.Color = StoneWork.Bed;
                canvas.DrawRect(strip, paint);

                Grit(canvas, strip, StoneWork.Bed, Math.Max(1f, tileSize / 26f), 0.18f, seed);
            }
        }

        /// <summary>
        /// Roughens a flat fill, a cell at a time. The one thing every made surface on this map
        /// has in common: an unbroken block of colour reads as a hole in the picture.
        /// </summary>
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
