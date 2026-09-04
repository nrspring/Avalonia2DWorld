using System;
using System.Collections.Generic;
using SkiaSharp;
using static NWorld.MapServices.MapRenderComponents.StandardRenderer.RoadNetwork;

namespace NWorld.MapServices.MapRenderComponents.StandardRenderer
{
    /// <summary>
    /// The masonry a road and a bridge are both built out of: a gritty bed, stones set into it,
    /// a verge worn back to the ground, and a parapet wall.
    /// <para>
    /// Shared so that a bridge is visibly the same road carried over water rather than a
    /// different thing that meets it -- the deck of a bridge and the surface of the road running
    /// onto it are laid by the same code from the same palette, and the join between them is
    /// invisible, which is exactly what a join between them should be.
    /// </para>
    /// </summary>
    internal static class StoneWork
    {
        /// <summary>
        /// The packed rubble the stones are set into, and what shows through a joint or a hole.
        /// <para>
        /// Close in tone to the stones rather than far from it, which is the difference between
        /// a road and a handful of dice. Seen from directly above, a paved road is one grey
        /// surface with the joints faintly drawn on it; the bed reads as mortar, not as the
        /// ground between separate objects.
        /// </para>
        /// </summary>
        public static readonly SKColor Bed = new(0x83, 0x7C, 0x70);

        /// <summary>
        /// The stones, in a narrow band about the bed. Old work is not one colour, but the
        /// spread between its stones is a shade or two of weathering rather than a full range:
        /// they were cut from the same hill.
        /// </summary>
        public static readonly SKColor[] Slabs =
        [
            new(0x9E, 0x97, 0x8A),
            new(0x94, 0x8D, 0x81),
            new(0x8A, 0x84, 0x78),
            new(0xA6, 0x9F, 0x92),
            new(0x8F, 0x88, 0x7C),
        ];

        /// <summary>
        /// How wide the roadway is, as a fraction of the tile. Wide enough to read as a road at
        /// a glance and narrow enough that the ground it crosses still shows either side, which
        /// is what keeps a road looking laid on the land rather than cut out of it.
        /// <para>
        /// Here rather than in <c>RenderRoad</c> because it is not only the road's any more: a
        /// fort's gateway has to funnel down from exactly this width at the tile edge, or the
        /// road running into it steps in or out at the join.
        /// </para>
        /// </summary>
        public const float Way = 0.56f;

        /// <summary>
        /// How many stones lie side by side across the way. Two: one is a plank and three are
        /// cobbles, and a paved road at this scale is neither.
        /// </summary>
        public const int Lanes = 2;

        /// <summary>
        /// Below this the stones are noise and the surface reads better as the line the bed
        /// alone already makes.
        /// </summary>
        public const int MinStoneTileSize = 12;

        /// <summary>
        /// The rectangles a set of arms covers: the middle always, plus a half-tile out along
        /// each arm.
        /// <para>
        /// The middle is in the list whatever the arms are, so that arms meeting at the centre
        /// make one continuous surface rather than four rectangles with a seam down each join.
        /// </para>
        /// </summary>
        public static List<SKRect> Spans(int arms, float half, float reach)
        {
            var spans = new List<SKRect>(5)
            {
                SKRect.Create(half - reach, half - reach, reach * 2, reach * 2),
            };

            if ((arms & North) != 0) spans.Add(SKRect.Create(half - reach, 0, reach * 2, half));
            if ((arms & South) != 0) spans.Add(SKRect.Create(half - reach, half, reach * 2, half));
            if ((arms & West) != 0) spans.Add(SKRect.Create(0, half - reach, half, reach * 2));
            if ((arms & East) != 0) spans.Add(SKRect.Create(half, half - reach, half, reach * 2));

            return spans;
        }

        /// <summary>
        /// Lays the bed: the rubble the stones are set into, gritty rather than flat.
        /// <para>
        /// The bed is what makes stonework look old. A clean rectangle reads as something built
        /// last week; grit where the paving has gone reads as something found.
        /// </para>
        /// </summary>
        public static void Bedding(SKCanvas canvas, int tileSize, IEnumerable<SKRect> spans, uint seed)
        {
            using var paint = new SKPaint { IsAntialias = false, Style = SKPaintStyle.Fill };

            paint.Color = Bed;

            foreach (var rect in spans)
                canvas.DrawRect(rect, paint);

            // Grit, in cells about two pixels across: the bed is crushed stone, and a flat fill
            // under the paving is the one thing that gives away drawn work at a glance.
            var grit = Math.Max(1f, tileSize / 24f);

            foreach (var rect in spans)
            {
                for (var y = rect.Top; y < rect.Bottom; y += grit)
                {
                    for (var x = rect.Left; x < rect.Right; x += grit)
                    {
                        var lift = (RenderNoise.Hash01((int)(x / grit), (int)(y / grit), seed) - 0.5f) * 0.18f;

                        paint.Color = RenderNoise.Shade(Bed, lift);
                        canvas.DrawRect(SKRect.Create(x, y, grit, grit), paint);
                    }
                }
            }
        }

        /// <summary>
        /// Bites notches out of the long edges of the way, so the verge is broken rather than
        /// ruled.
        /// <para>
        /// Cleared rather than painted over: the sprite carries alpha and is composited on top
        /// of whatever ground the tile has, so taking the bed away here is what lets the grass,
        /// the sand or the marsh come back through at the edge. Painting a colour would only
        /// ever be right over one ground.
        /// </para>
        /// <para>
        /// Only the outer edges of an arm, and never across the end of one: an arm ends where
        /// the next tile begins, and a notch there would open a gap in the middle of a road.
        /// </para>
        /// </summary>
        public static void Verge(SKCanvas canvas, int tileSize, int arms, float half, float reach, uint seed)
        {
            var bite = Math.Max(1f, tileSize / 16f);

            using var clear = new SKPaint { BlendMode = SKBlendMode.Clear, IsAntialias = false };

            var step = 0;

            foreach (var arm in Arms)
            {
                if ((arms & arm) == 0)
                    continue;

                var vertical = arm is North or South;

                for (var along = 0f; along < half; along += bite)
                {
                    // Each side of the arm asked separately, so the way narrows on one side and
                    // widens on the other rather than pinching symmetrically like a bowtie.
                    for (var side = 0; side < 2; side++)
                    {
                        if (RenderNoise.Hash01(step, side, seed) > 0.45f)
                        {
                            step++;
                            continue;
                        }

                        var depth = bite * (0.35f + (RenderNoise.Hash01(step, side + 8, seed) * 0.95f));

                        var near = arm is North or West ? half - along - bite : half + along;
                        var edge = side == 0 ? half - reach : half + reach - depth;

                        canvas.DrawRect(
                            vertical
                                ? SKRect.Create(edge, near, depth, bite)
                                : SKRect.Create(near, edge, bite, depth),
                            clear);

                        step++;
                    }
                }
            }
        }

        /// <summary>
        /// Sets the stones into the bed along whichever arms the shape has.
        /// <para>
        /// Along the arm rather than across the tile, so the courses turn with the way and a
        /// corner reads as a corner rather than as two straights crossing.
        /// </para>
        /// <para>
        /// What makes them read as stone rather than as tiles: each one is a little short of its
        /// cell and a little off square, each carries a tone of its own, each is lit from the
        /// north-west with a pale top edge and a dark bottom one -- and every so often a stone
        /// is missing altogether and the bed shows through the hole it left.
        /// </para>
        /// </summary>
        /// <param name="robbed">
        /// Whether stones may be missing. True for a road, which nobody repairs; false for a
        /// bridge deck, where a hole is not wear but a way to fall in the river.
        /// </param>
        public static void Paving(
            SKCanvas canvas, int arms, float half, float reach, uint seed, bool robbed = true)
        {
            // Stones about as long as they are wide. A way paved in one full-width piece per
            // course reads as a ladder, and one paved in many narrow ones reads as planking; two
            // courses of roughly square stone is what a laid road actually looks like from above.
            var lane = reach * 2 / Lanes;
            var course = Math.Max(1, (int)Math.Round(half / lane));
            var step = half / course;

            // The gap between stones is bed showing through rather than a line drawn over it, so
            // there is nothing to paint for it: the stone is simply laid short of its own cell.
            // Narrow, because a joint is a line between stones and not a gap between them --
            // widen it and the paving falls apart into scattered pieces.
            var joint = Math.Max(0.5f, step * 0.07f);

            // The lit and shaded edges. A whole pixel at least, or the relief disappears at the
            // zoom levels where it is doing the most work -- but no more than a whisker, since
            // what is wanted is a stone worn round at its edges rather than a button.
            var lip = Math.Max(1f, step * 0.10f);

            using var paint = new SKPaint { IsAntialias = false, Style = SKPaintStyle.Fill };

            foreach (var arm in Arms)
            {
                if ((arms & arm) == 0)
                    continue;

                for (var along = 0; along < course; along++)
                {
                    // Every so often a course is one slab right across instead of two side by
                    // side. Stones all of a size is the last thing that gives drawn work away: a
                    // real road was paved with whatever came off the cart, and the big pieces
                    // went down whole.
                    var whole = (RenderNoise.Hash(along + (arm * 31), 0x5B, seed) & 0xFF) < 74;

                    var pieces = whole ? 1 : Lanes;
                    var span = whole ? Lanes : 1;

                    for (var piece = 0; piece < pieces; piece++)
                    {
                        var across = piece * span;

                        // Hashed on where the stone is rather than counted along the arm, so the
                        // pattern does not march and two arms of a crossing do not rhyme.
                        var hash = RenderNoise.Hash(along + (arm * 31), across, seed);

                        // Prised out, robbed for a wall, or never replaced. A road with every
                        // stone in place is a road nobody has used. A whole slab is not lifted:
                        // taking one out leaves a hole the width of the way, which reads as one
                        // that stops rather than as one worn through.
                        if (robbed && !whole && (hash & 0xFF) < 26)
                            continue;

                        var near = along * step;
                        var side = half - reach + (across * lane);
                        var width = lane * span;

                        var stone = arm switch
                        {
                            North => SKRect.Create(side, half - near - step, width, step),
                            South => SKRect.Create(side, half + near, width, step),
                            West => SKRect.Create(half - near - step, side, step, width),
                            _ => SKRect.Create(half + near, side, step, width),
                        };

                        stone.Inflate(-joint, -joint);

                        // Set askew, and not by the same amount twice running: old work has
                        // shifted under a thousand years of carts, and stones laid true read as
                        // new work.
                        var skew = joint * 0.6f;

                        stone.Offset(
                            skew * ((((hash >> 8) & 0xFF) / 127.5f) - 1f),
                            skew * ((((hash >> 16) & 0xFF) / 127.5f) - 1f));

                        if (stone.Width <= 0 || stone.Height <= 0)
                            continue;

                        var face = RenderNoise.Shade(
                            Slabs[(int)((hash >> 24) % (uint)Slabs.Length)],
                            ((((hash >> 4) & 0x3F) / 63f) - 0.5f) * 0.13f);

                        paint.Color = face;
                        canvas.DrawRect(stone, paint);

                        if (stone.Height <= lip * 2 || stone.Width <= lip * 2)
                            continue;

                        // Lit from the north-west, which is where the light comes from
                        // everywhere else on this map.
                        paint.Color = RenderNoise.Shade(face, 0.11f);
                        canvas.DrawRect(SKRect.Create(stone.Left, stone.Top, stone.Width, lip), paint);
                        canvas.DrawRect(SKRect.Create(stone.Left, stone.Top, lip, stone.Height), paint);

                        paint.Color = RenderNoise.Shade(face, -0.15f);
                        canvas.DrawRect(SKRect.Create(stone.Left, stone.Bottom - lip, stone.Width, lip), paint);
                        canvas.DrawRect(SKRect.Create(stone.Right - lip, stone.Top, lip, stone.Height), paint);
                    }
                }
            }
        }

        /// <summary>The four arms, in a fixed order so a sprite is reproducible.</summary>
        public static readonly int[] Arms = [North, East, South, West];
    }
}
