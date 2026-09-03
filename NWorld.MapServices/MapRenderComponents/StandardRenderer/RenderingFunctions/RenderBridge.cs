using System;
using System.Threading.Tasks;
using NWorld.Map.Models;
using SkiaSharp;
using static NWorld.MapServices.MapRenderComponents.StandardRenderer.RoadNetwork;

namespace NWorld.MapServices.MapRenderComponents.StandardRenderer.RenderingFunctions
{
    /// <summary>
    /// A stone bridge: the same road carried over water on a deck, with a parapet down either
    /// side of it.
    /// <para>
    /// Deliberately the same masonry as <see cref="RenderRoad"/>, laid by the same code from the
    /// same palette -- see <see cref="StoneWork"/>. A bridge is not a different thing that a
    /// road meets, it is the stretch of road that happens to be over water, and the join where
    /// one becomes the other should be invisible. What says bridge instead of road is the pair
    /// of walls, and nothing else needs to.
    /// </para>
    /// <para>
    /// The parapets are what a bridge has from directly above. Seen from a bird's height you
    /// cannot see an arch, a pier or the drop; what you can see is that the way is walled where
    /// it crosses the water and open where it does not. So the walls run along the sides of
    /// every arm and stop at the tile's edge, and a deck with no arms at all is walled on the
    /// two long sides of wherever it happens to lie.
    /// </para>
    /// <para>
    /// The deck is not robbed. A road loses a stone here and there to a thousand years of carts
    /// and nobody minds; a hole in a bridge is not wear, it is a way into the river, so
    /// <see cref="StoneWork.Paving"/> is told to leave this one whole.
    /// </para>
    /// </summary>
    public static class RenderBridge
    {
        /// <summary>
        /// How wide the whole structure is, as a fraction of the tile -- parapets included.
        /// <para>
        /// Wider than the road it carries, because the walls have to go somewhere and a deck
        /// that narrowed to fit them inside the roadway would read as a road pinching in at
        /// every crossing rather than as a bridge.
        /// </para>
        /// </summary>
        private const float Width = 0.72f;

        /// <summary>
        /// How much of that width each parapet takes. The rest is deck.
        /// <para>
        /// Thin: a parapet is a wall a person could lean on, and against a roadway two carts
        /// wide it is a line rather than a band. Enough to read, never enough to look like a
        /// second road running alongside the first.
        /// </para>
        /// </summary>
        private const float ParapetShare = 0.23f;

        /// <summary>
        /// The wall's own stone, well darker than the deck: what is seen of a parapet from
        /// overhead is mostly its shaded flank rather than its top, and a wall drawn in the
        /// deck's own grey reads as nothing but a wider road.
        /// </summary>
        private static readonly SKColor Wall = new(0x55, 0x50, 0x47);

        /// <summary>
        /// The coping along the top of the wall, which is the part actually facing the sky.
        /// Lighter than the deck rather than like it, so the eye reads a lit edge above a
        /// shaded face and puts the wall upright.
        /// </summary>
        private static readonly SKColor Coping = new(0xAE, 0xA7, 0x99);

        /// <summary>
        /// What the wall throws onto the water beside it. A structure standing over a surface
        /// casts something, and without it a bridge lies flat in the river rather than over it.
        /// </summary>
        private static readonly SKColor Shadow = new(0x14, 0x18, 0x1E, 0x8C);

        private static readonly StoneSpan Span = new(0xB81D6Eu, Draw);

        public static Task Render(TileRenderContext context) => Span.Render(context);

        /// <inheritdoc cref="StoneSpan.Prewarm"/>
        public static Task Prewarm(int tileSize) => Span.Prewarm(tileSize);

        /// <inheritdoc cref="StoneSpan.ClearCache"/>
        public static void ClearCache() => Span.ClearCache();

        /// <summary>Deck first, then the walls down either side of every arm.</summary>
        private static void Draw(SKCanvas canvas, int tileSize, int arms, uint seed)
        {
            var half = tileSize / 2f;
            var outer = tileSize * Width / 2f;
            var wall = Math.Max(1f, outer * ParapetShare);
            var deck = outer - wall;

            // No verge. A road is worn back to the grass at its edges; a bridge ends at its
            // parapet, and water does not encroach on stonework the way grass does.
            StoneWork.Bedding(canvas, tileSize, StoneWork.Spans(arms, half, deck), seed);

            if (tileSize >= StoneWork.MinStoneTileSize)
                StoneWork.Paving(canvas, arms, half, deck, seed, robbed: false);

            Parapets(canvas, tileSize, arms, half, deck, outer, seed);
        }

        /// <summary>
        /// Runs a wall down each side of every arm, from the tile's edge to the middle.
        /// <para>
        /// Per arm rather than round the outside, so a bridge that turns a corner or meets
        /// another gets walls along everything it actually carries and none across the mouths
        /// where the way continues. The walls of two arms overlap in the middle, which is what a
        /// parapet does at a junction anyway.
        /// </para>
        /// </summary>
        private static void Parapets(
            SKCanvas canvas, int tileSize, int arms, float half, float deck, float outer, uint seed)
        {
            using var paint = new SKPaint { IsAntialias = false, Style = SKPaintStyle.Fill };

            // A course of coping stones along the top of the wall, in cells about as long as the
            // wall is thick -- so the parapet reads as built of blocks rather than ruled on.
            var block = Math.Max(1f, (outer - deck) * 1.6f);

            foreach (var arm in StoneWork.Arms)
            {
                if ((arms & arm) == 0)
                    continue;

                var vertical = arm is North or South;
                var near = arm is North or West ? 0f : half;

                for (var side = 0; side < 2; side++)
                {
                    var edge = side == 0 ? half - outer : half + deck;

                    var band = vertical
                        ? SKRect.Create(edge, near, outer - deck, half)
                        : SKRect.Create(near, edge, half, outer - deck);

                    // Thrown outwards, onto whatever the bridge is standing over. A hair wide:
                    // enough to lift the structure off the water, not enough to read as a third
                    // wall running alongside the other two.
                    var cast = Math.Max(1f, (outer - deck) * 0.5f);

                    paint.Color = Shadow;
                    canvas.DrawRect(
                        vertical
                            ? SKRect.Create(side == 0 ? band.Left - cast : band.Right, near, cast, half)
                            : SKRect.Create(near, side == 0 ? band.Top - cast : band.Bottom, half, cast),
                        paint);

                    paint.Color = Wall;
                    canvas.DrawRect(band, paint);

                    // The blocks, walked along the wall. Each is a shade of its own and every
                    // so often one is missing, which is the whole difference between a wall
                    // somebody built and a line somebody drew.
                    for (var along = 0f; along < half; along += block)
                    {
                        var hash = RenderNoise.Hash((int)(along / block), (side * 4) + arm, seed);

                        if ((hash & 0xFF) < 30)
                            continue;

                        var cell = vertical
                            ? SKRect.Create(band.Left, near + along, band.Width, block)
                            : SKRect.Create(near + along, band.Top, block, band.Height);

                        cell.Intersect(band);
                        cell.Inflate(-Math.Max(0.5f, block * 0.08f), -Math.Max(0.5f, block * 0.08f));

                        if (cell.Width <= 0 || cell.Height <= 0)
                            continue;

                        paint.Color = RenderNoise.Shade(
                            Coping, ((((hash >> 8) & 0x3F) / 63f) - 0.5f) * 0.22f);

                        canvas.DrawRect(cell, paint);
                    }
                }
            }
        }
    }
}
