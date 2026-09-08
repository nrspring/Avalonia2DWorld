using System;
using System.Threading.Tasks;
using NWorld.Map.Models;
using SkiaSharp;
using static NWorld.MapServices.MapRenderComponents.StandardRenderer.RenderNoise;

namespace NWorld.MapServices.MapRenderComponents.StandardRenderer
{
    /// <summary>What a token carries in the middle of it.</summary>
    internal enum TokenMark
    {
        /// <summary>Nothing. The colour and the rim carry it alone.</summary>
        None,

        /// <summary>Two staves crossed: arms held anyhow, which is disorder drawn as a shape.</summary>
        Crossed,

        /// <summary>Three uprights side by side: a rank, which is order drawn as the same shape.</summary>
        Ranked,
    }

    /// <summary>
    /// How one kind of token is marked. Every field here exists to tell it from the token beside
    /// it, and they are deliberately not variations of one idea -- see <see cref="UnitToken"/>.
    /// </summary>
    /// <param name="Face">The disc itself.</param>
    /// <param name="Rim">The edge round it.</param>
    /// <param name="RimShare">How thick that edge is, as a fraction of the radius.</param>
    /// <param name="Ink">What the mark is drawn in.</param>
    /// <param name="Mark">The device in the middle.</param>
    /// <param name="Rough">
    /// Whether the disc is cut true or cut by hand. A wobbling edge and a struck circle are
    /// different silhouettes, which is the only difference on this list that still works after
    /// the colour has gone grey and the mark has closed up.
    /// </param>
    internal readonly record struct TokenStyle(
        SKColor Face,
        SKColor Rim,
        float RimShare,
        SKColor Ink,
        TokenMark Mark,
        bool Rough);

    /// <summary>
    /// A round marker standing on a tile: what a body of men comes to when it has to be
    /// recognised rather than looked at.
    /// <para>
    /// This replaced figures drawn from above, and the reason is worth keeping. Men drawn as men
    /// are the right picture and the wrong sign. At the zoom a map is actually played at, three
    /// militia and four soldiers are both a small dark clump: the things that separated them --
    /// the count, the formation, the colour of a tunic, a helmet against a cap -- are all details
    /// of the same size as the thing itself, so they go together. Two tokens have no such
    /// problem, because a token is not a picture of anything and can therefore be made as
    /// unalike as the eye needs.
    /// </para>
    /// <para>
    /// It is a real break from the rest of this map, which draws everything as the thing itself
    /// -- a fort is walls, a wood is trees, and the ships beside these are still ships. The
    /// break is on purpose and confined to the two that needed it: what is being told apart here
    /// is not what a thing looks like but whose it is and what it can do, and that was never
    /// visible from above at any size.
    /// </para>
    /// <para>
    /// <b>The distinctions are stacked so they fail one at a time.</b> Two tokens differ in hue,
    /// in value, in whether they carry a bright rim, in whether their edge is struck or ragged,
    /// and in the device they hold. That is five differences where one would do, and the point
    /// is what happens as the map zooms out: the device closes up first, then the ragged edge
    /// goes, then the rim thins away -- and value and hue are still there at four pixels across.
    /// Any one of them alone would have some size at which the two tokens become the same token.
    /// </para>
    /// </summary>
    internal sealed class UnitToken(TokenStyle style, uint seed)
    {
        /// <summary>
        /// The token's radius, as a fraction of the tile.
        /// <para>
        /// Short of half, and it has to be. A token is a discrete thing standing on a tile rather
        /// than a covering of it, so there is ground all round it -- run out to the edge, two of
        /// them side by side would touch and read as one long shape, which is the same trap
        /// <see cref="RenderingFunctions.RenderFort"/> keeps its margin against.
        /// </para>
        /// </summary>
        private const float Radius = 0.30f;

        /// <summary>
        /// Below this the device in the middle is a scribble and the token is left plain. The
        /// colour and the rim are still doing their work, and they are enough.
        /// </summary>
        private const int MinMarkTileSize = 16;

        /// <summary>How far the token is thrown onto the ground below it, as a fraction of the tile.</summary>
        private const float ShadowOffset = 0.028f;

        /// <summary>How much the ragged edge varies, as a fraction of the radius.</summary>
        private const float Wobble = 0.14f;

        /// <summary>How many sides a ragged disc is cut from.</summary>
        private const int Facets = 18;

        /// <summary>
        /// What a token throws on the ground. Firmer than the shadow under a man, because this is
        /// not a man: it is a marker laid on the map, and the shadow is most of what makes it sit
        /// on the ground rather than float over it.
        /// </summary>
        private static readonly SKColor Shadow = new(0x14, 0x10, 0x0C, 0x66);

        private readonly UnitMarker _marker = new((canvas, tileSize) => Draw(canvas, tileSize, style, seed));

        public Task Render(TileRenderContext context) => _marker.Render(context);

        /// <inheritdoc cref="UnitMarker.Prewarm"/>
        public Task Prewarm(int tileSize) => _marker.Prewarm(tileSize);

        /// <inheritdoc cref="UnitMarker.ClearCache"/>
        public void ClearCache() => _marker.ClearCache();

        private static void Draw(SKCanvas canvas, int tileSize, TokenStyle style, uint seed)
        {
            if (canvas is null || tileSize <= 0)
                return;

            var centre = tileSize / 2f;
            var radius = Radius * tileSize;

            using var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };

            var offset = ShadowOffset * tileSize;

            using (var shadow = Disc(centre + offset, centre + offset, radius, style.Rough, seed))
            {
                paint.Color = Shadow;
                canvas.DrawPath(shadow, paint);
            }

            using (var face = Disc(centre, centre, radius, style.Rough, seed))
            {
                paint.Color = style.Face;
                canvas.DrawPath(face, paint);

                // The rim is stroked onto the same outline rather than drawn as a second disc, so
                // that a ragged edge stays ragged: a true circle laid over a hand-cut one would
                // put the wobble back under a perfect ring and undo the whole difference.
                paint.Style = SKPaintStyle.Stroke;
                paint.StrokeWidth = Math.Max(1f, style.RimShare * radius * 2f);
                paint.Color = style.Rim;

                canvas.DrawPath(face, paint);

                paint.Style = SKPaintStyle.Fill;
            }

            if (tileSize >= MinMarkTileSize)
                Mark(canvas, paint, centre, radius * (1f - style.RimShare), style);
        }

        /// <summary>
        /// The device: crossed staves or a rank of uprights, which are the same idea the men were
        /// drawn with when they were men -- disorder against order -- said in a shape that does
        /// not need thirty pixels to be seen.
        /// </summary>
        private static void Mark(SKCanvas canvas, SKPaint paint, float centre, float inner, TokenStyle style)
        {
            if (style.Mark == TokenMark.None)
                return;

            paint.Color = style.Ink;
            paint.Style = SKPaintStyle.Stroke;
            paint.StrokeCap = SKStrokeCap.Round;

            if (style.Mark == TokenMark.Crossed)
            {
                paint.StrokeWidth = Math.Max(1f, inner * 0.30f);

                // Neither of them upright, and not at a right angle to each other either. Two
                // staves at forty degrees and fifty read as thrown down anyhow; at forty-five and
                // forty-five they read as a saltire, which is a device somebody designed.
                Stave(canvas, paint, centre, inner, -40f);
                Stave(canvas, paint, centre, inner, 52f);
            }
            else
            {
                paint.StrokeWidth = Math.Max(1f, inner * 0.15f);

                // Three uprights, the outer two cut down to sit inside the circle. Dead level
                // they would poke out of the disc; cut to fit, the rank reads as being held
                // within something, which is what a formation is.
                for (var i = -1; i <= 1; i++)
                {
                    var x = centre + (i * inner * 0.50f);
                    var reach = inner * (i == 0 ? 0.74f : 0.50f);

                    canvas.DrawLine(x, centre - reach, x, centre + reach, paint);
                }
            }

            paint.Style = SKPaintStyle.Fill;
        }

        /// <summary>One stave of the cross, laid at an angle through the middle of the token.</summary>
        private static void Stave(SKCanvas canvas, SKPaint paint, float centre, float inner, float degrees)
        {
            var radians = degrees * MathF.PI / 180f;

            var dx = MathF.Sin(radians) * inner * 0.78f;
            var dy = MathF.Cos(radians) * inner * 0.78f;

            canvas.DrawLine(centre - dx, centre - dy, centre + dx, centre + dy, paint);
        }

        /// <summary>
        /// The token's outline: struck true, or cut by hand.
        /// <para>
        /// Cut from straight facets rather than smoothed, and that is the point of it. A wobbling
        /// curve reads as a circle drawn badly; a ring of short straight cuts reads as something
        /// made by somebody, which is what the ragged one is meant to say.
        /// </para>
        /// </summary>
        private static SKPath Disc(float cx, float cy, float radius, bool rough, uint seed)
        {
            var path = new SKPath();

            if (!rough)
            {
                path.AddCircle(cx, cy, radius);
                return path;
            }

            for (var i = 0; i < Facets; i++)
            {
                var angle = i / (float)Facets * MathF.Tau;
                var reach = radius * (1f - (Hash01(i, 0, seed) * Wobble));

                var x = cx + (MathF.Cos(angle) * reach);
                var y = cy + (MathF.Sin(angle) * reach);

                if (i == 0)
                    path.MoveTo(x, y);
                else
                    path.LineTo(x, y);
            }

            path.Close();

            return path;
        }
    }
}
