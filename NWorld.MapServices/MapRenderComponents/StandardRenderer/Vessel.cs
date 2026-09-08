using System;
using System.Threading.Tasks;
using NWorld.Map.Models;
using SkiaSharp;
using static NWorld.MapServices.MapRenderComponents.StandardRenderer.RenderNoise;

namespace NWorld.MapServices.MapRenderComponents.StandardRenderer
{
    /// <summary>
    /// How big a ship is and how much of one she is. Everything that separates a boat from a
    /// carrack, and nothing else: the three of them are one hull drawn at three sizes with a
    /// different number of masts in it.
    /// </summary>
    /// <param name="Length">Stem to stern, as a fraction of the tile.</param>
    /// <param name="Beam">Across the widest part, as a fraction of the tile.</param>
    /// <param name="Masts">
    /// How many. This is the count anybody actually reads -- see <see cref="Vessel"/> -- so it
    /// is the one thing that must differ between the three by more than a little.
    /// </param>
    internal readonly record struct VesselStyle(float Length, float Beam, int Masts);

    /// <summary>
    /// A ship lying on a tile of water, seen from directly above.
    /// <para>
    /// From overhead a ship is a pointed hull with a pale deck inside it and her yards lying
    /// across her — and the yards are the whole of how her size is read. A hull twice as long as
    /// another is only twice as long, which the eye is poor at judging with nothing beside it to
    /// compare against; but one yard, two yards and three yards is a count, and a count needs no
    /// comparison. So the sizes are told apart by something countable first and by scale second.
    /// </para>
    /// <para>
    /// The sails are furled on the yards rather than set. Set, they are the right thing and
    /// ruinous: a ship under full sail seen from above is almost entirely sail, so all three
    /// would come out as pale blobs of three sizes with no hull and nothing to count. Furled,
    /// the yards read as bright bars across a dark hull, which is both what a ship in harbour
    /// actually looks like and the version that survives being thirty pixels long.
    /// </para>
    /// <para>
    /// Every ship lies bow-down the map, the way <see cref="Footmen"/>'s men face down it. That
    /// is a real limitation and worth naming: a fleet drawn here all points the same way, and
    /// ships more than anything else on this map look wrong doing that. Turning them is not a
    /// colour change but a structural one -- <see cref="UnitMarker"/> holds one sprite per zoom
    /// level, and a heading would mean one per zoom per heading -- so it is left until something
    /// asks for it.
    /// </para>
    /// </summary>
    internal sealed class Vessel(VesselStyle style)
    {
        /// <summary>
        /// Below this a ship is a hull and nothing else -- no deck, no yards.
        /// <para>
        /// A deck inset inside a hull that is itself six pixels across is a pale line where a
        /// dark shape should be, and it eats the hull rather than sitting in it. What has to
        /// survive at this size is that something is floating here and roughly how big it is,
        /// and the silhouette alone carries both.
        /// </para>
        /// </summary>
        private const int MinDeckTileSize = 14;

        /// <inheritdoc cref="MinDeckTileSize"/>
        private const int MinYardTileSize = 18;

        /// <summary>
        /// How far the deck is held in from the hull on every side, as a fraction of the beam.
        /// <para>
        /// A real distance inset all round, not a scaling of the hull, and the difference
        /// matters: scaled, the inset at the bow is a fraction of the <em>length</em> and comes
        /// out enormous, so the pale deck runs almost to the stem and the ship reads as a pale
        /// dart with a dark cup at the back of it. Held in by a fixed distance, there is a dark
        /// wale of the same width all the way round, which is what a hull looks like from above
        /// and what the pale yards need to be pale against.
        /// </para>
        /// </summary>
        private const float DeckInset = 0.28f;

        /// <summary>
        /// How far a yard overhangs the beam at its widest, as a fraction of the beam. Well past
        /// the hull, because a yard that stopped at the rail would be a mark on the deck rather
        /// than a spar across the ship -- and it is the yards, sticking out on both sides, that
        /// anybody actually counts.
        /// </summary>
        private const float YardOverhang = 1.34f;

        /// <summary>How thick a yard is drawn, as a fraction of the tile.</summary>
        private const float YardThickness = 0.046f;

        /// <summary>How much of the hull's length the masts are spread over.</summary>
        private const float MastSpread = 0.58f;

        /// <summary>How far the shadow is thrown, as a fraction of the tile.</summary>
        private const float ShadowOffset = 0.026f;

        /// <summary>
        /// Wet timber and tar. Dark, so that the pale yards across it have something to be pale
        /// against, and so the whole ship holds its shape on water of any colour.
        /// </summary>
        private static readonly SKColor Hull = new(0x3E, 0x2C, 0x1D);

        /// <summary>
        /// Planking, and only a little lighter than the timber round it.
        /// <para>
        /// It was scrubbed pine to begin with, and that was the mistake. A pale deck is the
        /// largest bright area on the ship, so it becomes what the eye reads, and all three
        /// vessels came out as pale darts with the yards lost inside them. The yards are the only
        /// things here worth being bright, because they are the things that get counted -- so
        /// everything else is dark, and the deck's job is to give the hull an inside rather than
        /// to be seen in its own right.
        /// </para>
        /// </summary>
        private static readonly SKColor Deck = new(0x5C, 0x47, 0x30);

        /// <summary>Furled canvas, weathered rather than white.</summary>
        private static readonly SKColor Canvas = new(0xD8, 0xCE, 0xB4);

        /// <summary>The mast itself, where it comes through the deck.</summary>
        private static readonly SKColor Mast = new(0x2E, 0x22, 0x16);

        /// <summary>
        /// What she throws on the water. Weaker than a shadow on land, because it is not one: a
        /// hull on water darkens what is under it by displacing the light rather than by blocking
        /// it, and the hard-edged shadow that suits a fort would read as a second hull.
        /// </summary>
        private static readonly SKColor Shadow = new(0x0E, 0x1A, 0x1E, 0x48);

        private readonly UnitMarker _marker = new((canvas, tileSize) => Draw(canvas, tileSize, style));

        public Task Render(TileRenderContext context) => _marker.Render(context);

        /// <inheritdoc cref="UnitMarker.Prewarm"/>
        public Task Prewarm(int tileSize) => _marker.Prewarm(tileSize);

        /// <inheritdoc cref="UnitMarker.ClearCache"/>
        public void ClearCache() => _marker.ClearCache();

        private static void Draw(SKCanvas canvas, int tileSize, VesselStyle style)
        {
            if (canvas is null || tileSize <= 0)
                return;

            var half = tileSize / 2f;
            var length = style.Length * tileSize;
            var beam = style.Beam * tileSize;

            using var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };

            var offset = ShadowOffset * tileSize;

            using (var shadow = Outline(half + offset, half + offset, length, beam))
            {
                paint.Color = Shadow;
                canvas.DrawPath(shadow, paint);
            }

            using (var hull = Outline(half, half, length, beam))
            {
                paint.Color = Hull;
                canvas.DrawPath(hull, paint);
            }

            if (tileSize >= MinDeckTileSize)
            {
                var inset = DeckInset * beam;

                using var deck = Outline(half, half, length - (inset * 2f), beam - (inset * 2f));

                paint.Color = Deck;
                canvas.DrawPath(deck, paint);
            }

            if (tileSize >= MinYardTileSize)
                Yards(canvas, paint, tileSize, style, half, length, beam);
        }

        /// <summary>
        /// The yards lying across her, and a mast where each crosses the centreline.
        /// <para>
        /// Spread over the middle of the hull rather than the whole of it, because the ends of a
        /// ship are where nobody steps a mast: a yard out at the stem would read as a spar that
        /// had come adrift.
        /// </para>
        /// </summary>
        private static void Yards(
            SKCanvas canvas, SKPaint paint, int tileSize, VesselStyle style,
            float centre, float length, float beam)
        {
            var thickness = Math.Max(1f, YardThickness * tileSize);
            var span = length * MastSpread;

            paint.Style = SKPaintStyle.Stroke;
            paint.StrokeCap = SKStrokeCap.Round;

            for (var i = 0; i < style.Masts; i++)
            {
                // Evenly along the spread, and centred in it whatever the count -- one mast lands
                // amidships, two straddle it, three put one there and one either side.
                var along = style.Masts == 1
                    ? 0f
                    : ((i / (float)(style.Masts - 1)) - 0.5f) * span;

                var y = centre + along;

                // Shorter towards the ends, the way a real rig is: the main yard amidships is the
                // longest and the fore and mizzen are cut down from it. Without this, three equal
                // bars read as a ladder rather than as a ship.
                var taper = 1f - (0.28f * MathF.Abs(along) / MathF.Max(1f, span / 2f));
                var reach = beam * YardOverhang * taper / 2f;

                paint.Color = Canvas;
                paint.StrokeWidth = thickness;

                canvas.DrawLine(centre - reach, y, centre + reach, y, paint);

                paint.Color = Shade(Canvas, -0.22f);
                paint.StrokeWidth = Math.Max(1f, thickness * 0.34f);

                canvas.DrawLine(centre - reach, y + (thickness * 0.30f), centre + reach, y + (thickness * 0.30f), paint);

                paint.Style = SKPaintStyle.Fill;
                paint.Color = Mast;

                canvas.DrawCircle(centre, y, Math.Max(0.8f, thickness * 0.52f), paint);

                paint.Style = SKPaintStyle.Stroke;
            }

            paint.Style = SKPaintStyle.Fill;
        }

        /// <summary>
        /// A hull: pointed at the bow, carried out to the beam amidships, and cut off square
        /// across the stern.
        /// <para>
        /// The transom is what makes it a ship rather than a leaf. Pointed at both ends it reads
        /// as a canoe at every size and as an eye at the small ones; square at one end and sharp
        /// at the other, it reads as something with a front -- which is also the only thing
        /// telling anyone which way she is heading.
        /// </para>
        /// </summary>
        private static SKPath Outline(float cx, float cy, float length, float beam)
        {
            var half = length / 2f;
            var side = beam / 2f;

            // Bow down the map, stern up it.
            var bow = cy + half;
            var stern = cy - half;

            var path = new SKPath();

            path.MoveTo(cx, bow);
            path.CubicTo(
                cx - side, bow - (length * 0.30f),
                cx - side, stern + (length * 0.24f),
                cx - (side * 0.74f), stern);
            path.LineTo(cx + (side * 0.74f), stern);
            path.CubicTo(
                cx + side, stern + (length * 0.24f),
                cx + side, bow - (length * 0.30f),
                cx, bow);
            path.Close();

            return path;
        }
    }
}
