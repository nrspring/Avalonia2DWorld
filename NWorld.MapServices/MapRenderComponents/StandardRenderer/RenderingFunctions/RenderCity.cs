using System;
using System.Threading.Tasks;
using NWorld.Map.Models;
using SkiaSharp;
using static NWorld.MapServices.MapRenderComponents.StandardRenderer.RoadNetwork;

namespace NWorld.MapServices.MapRenderComponents.StandardRenderer.RenderingFunctions
{
    /// <summary>
    /// A section of a city: blocks of roofs divided by streets, seen from overhead.
    /// <para>
    /// A tile is a <b>piece</b> of a town rather than a town. Cities grow by adding tiles beside
    /// the ones already there, so what a piece has to know is where the rest of the town is --
    /// see <see cref="RoadNetwork.CityMask"/> -- and on those edges it builds right out to the
    /// tile boundary so the streets and the blocks run on into the next piece and the two read as
    /// one place. On the edges with open country beyond, it stops short and leaves a margin, which
    /// is the outskirts.
    /// </para>
    /// <para>
    /// So a single tile is a hamlet, a row of three is a town along a street, and a block of nine
    /// is a city with a middle -- all from the same seventeen sprites, and none of it stored on
    /// the tiles. Adding a tile to the edge of a town redraws its neighbour's margin into street
    /// on the next frame, without anything going round to tell it.
    /// </para>
    /// <para>
    /// No wall and no gate. A wall is a thing a whole city has and this is a piece of one; drawing
    /// it per tile would put a rampart down the middle of every town that ever grew, and drawing
    /// it only on the outside edge would be a wall that shuffles outward every time somebody
    /// builds a house. Roads simply arrive: the network counts a city as something to reach, so a
    /// road runs up to the edge of the built ground and ends there.
    /// </para>
    /// </summary>
    public static class RenderCity
    {
        /// <summary>
        /// How far the built ground stops short of a tile edge with no more city beyond it, as a
        /// fraction of the tile. The outskirts: enough that a town has an edge rather than
        /// looking sliced off, little enough that two tiles apart still read as one settlement.
        /// </summary>
        private const float Margin = 0.10f;

        /// <summary>
        /// How wide a street is, as a fraction of a block. Narrow -- a medieval street is a gap
        /// between buildings rather than a boulevard, and at these sizes a wide one turns the
        /// town into a chessboard.
        /// </summary>
        private const float Street = 0.26f;

        /// <summary>
        /// Roughly how many blocks fit across a tile. Three: two reads as four big sheds and four
        /// is a grain rather than a plan by the time a tile is 32 pixels.
        /// </summary>
        private const int Blocks = 3;

        /// <summary>Below this the blocks are noise and the tile is drawn as built ground alone.</summary>
        private const int MinBlockTileSize = 14;

        /// <summary>
        /// The ground between the buildings: beaten earth, not the dressed stone of a road. A
        /// town's streets were mud everywhere but the very grandest of them.
        /// </summary>
        private static readonly SKColor Ground = new(0x6F, 0x63, 0x52);

        /// <summary>
        /// The roofs, which is nearly all of what a town is from above. Warm on purpose: the
        /// roads and the bridges are grey stone, and a settlement that shared their palette would
        /// read as a wide place in the road rather than as somewhere people live.
        /// </summary>
        private static readonly SKColor[] Roofs =
        [
            new(0xA5, 0x5F, 0x3E),
            new(0x8E, 0x50, 0x35),
            new(0x9C, 0x84, 0x53),
            new(0xB4, 0x6C, 0x46),
            new(0x86, 0x6F, 0x48),
            new(0x77, 0x6B, 0x63),
        ];

        private static readonly StoneSpan Span =
            new(0xC17A9Bu, Draw, runsWhenAlone: false, mask: CityMask);

        public static Task Render(TileRenderContext context) => Span.Render(context);

        /// <inheritdoc cref="StoneSpan.Prewarm"/>
        public static Task Prewarm(int tileSize) => Span.Prewarm(tileSize);

        /// <inheritdoc cref="StoneSpan.ClearCache"/>
        public static void ClearCache() => Span.ClearCache();

        /// <summary>Built ground out to wherever the town reaches, then the blocks on it.</summary>
        private static void Draw(SKCanvas canvas, int tileSize, int joins, uint seed)
        {
            // The tile edges the town runs out to. An edge with more city beyond it is built
            // right up to; an edge with fields beyond it stops short.
            var margin = tileSize * Margin;

            var built = new SKRect(
                (joins & West) != 0 ? 0 : margin,
                (joins & North) != 0 ? 0 : margin,
                tileSize - ((joins & East) != 0 ? 0 : margin),
                tileSize - ((joins & South) != 0 ? 0 : margin));

            using var paint = new SKPaint { IsAntialias = false, Style = SKPaintStyle.Fill };

            paint.Color = Ground;
            canvas.DrawRect(built, paint);

            // Trodden, rutted and puddled. The same trick the road bed uses, and for the same
            // reason: a flat fill under the roofs is what says "drawn" at a glance.
            var grit = Math.Max(1f, tileSize / 22f);

            for (var y = built.Top; y < built.Bottom; y += grit)
            {
                for (var x = built.Left; x < built.Right; x += grit)
                {
                    var lift = (RenderNoise.Hash01((int)(x / grit), (int)(y / grit), seed) - 0.5f) * 0.2f;

                    paint.Color = RenderNoise.Shade(Ground, lift);
                    canvas.DrawRect(SKRect.Create(x, y, grit, grit), paint);
                }
            }

            if (tileSize >= MinBlockTileSize)
                Buildings(canvas, tileSize, built, seed);
        }

        /// <summary>
        /// Lays the blocks over the built ground: a rough grid of roofs with streets between.
        /// <para>
        /// The grid is measured from the tile's own corner rather than from the built rectangle,
        /// so two neighbouring pieces lay their blocks on the same lines and the streets of one
        /// meet the streets of the other instead of running into a roof. That is the whole of
        /// what makes a row of these read as one town.
        /// </para>
        /// <para>
        /// Not every cell is a building. A town has yards, markets and burnt plots, and a
        /// perfectly full grid reads as a barracks.
        /// </para>
        /// </summary>
        private static void Buildings(SKCanvas canvas, int tileSize, SKRect built, uint seed)
        {
            var block = (float)tileSize / Blocks;
            var street = Math.Max(1f, block * Street);

            using var paint = new SKPaint { IsAntialias = false, Style = SKPaintStyle.Fill };

            // Plots already swallowed by a hall on the row above or the plot to the left.
            var taken = new bool[Blocks, Blocks];

            for (var row = 0; row < Blocks; row++)
            {
                for (var column = 0; column < Blocks; column++)
                {
                    if (taken[column, row])
                        continue;

                    // Hashed on the cell's place in the tile. Two neighbouring tiles hash their
                    // own cells, so the pattern differs across the join without the streets
                    // failing to line up -- the lines are shared, the buildings on them are not.
                    var hash = RenderNoise.Hash(column, row, seed);

                    // A yard, a market, or a plot nobody has built on again.
                    if ((hash & 0xFF) < 46)
                        continue;

                    // Now and then a hall takes two plots. Buildings all of one size is what
                    // makes a town read as brickwork; a long roof among the short ones is a
                    // market hall, a barn, or somebody who did well.
                    var wide = ((hash >> 20) & 0xFF) < 54 && column + 1 < Blocks && !taken[column + 1, row];
                    var tall = !wide && ((hash >> 28) & 0xF) < 3 && row + 1 < Blocks;

                    var across = wide ? 2 : 1;
                    var down = tall ? 2 : 1;

                    for (var dx = 0; dx < across; dx++)
                        for (var dy = 0; dy < down; dy++)
                            taken[column + dx, row + dy] = true;

                    var roof = SKRect.Create(column * block, row * block, block * across, block * down);

                    roof.Inflate(-street / 2f, -street / 2f);

                    // Buildings do not run into the fields. Clipped to the built ground rather
                    // than skipped, so a block on the margin comes out as a half-block at the
                    // edge of town instead of a hole in it.
                    roof.Intersect(built);

                    if (roof.Width <= 1 || roof.Height <= 1)
                        continue;

                    // Never square, and never twice the same: a hall is long, a cottage is
                    // small, and a town of identical boxes is a warehouse yard.
                    var squeeze = block * 0.16f * ((((hash >> 8) & 0xFF) / 255f) - 0.5f);

                    if (((hash >> 16) & 1) == 0)
                        roof.Inflate(0, -Math.Abs(squeeze));
                    else
                        roof.Inflate(-Math.Abs(squeeze), 0);

                    roof.Intersect(built);

                    if (roof.Width <= 1 || roof.Height <= 1)
                        continue;

                    var face = Roofs[(int)((hash >> 24) % (uint)Roofs.Length)];

                    // What the building throws onto the street, to the south-east, which is
                    // where the light on this map comes from. Without it the roofs lie flat on
                    // the ground like paint.
                    var drop = Math.Max(1f, block * 0.09f);

                    paint.Color = new SKColor(0x1A, 0x14, 0x10, 0x66);
                    canvas.DrawRect(
                        SKRect.Intersect(
                            SKRect.Create(roof.Left + drop, roof.Top + drop, roof.Width, roof.Height),
                            built),
                        paint);

                    paint.Color = face;
                    canvas.DrawRect(roof, paint);

                    if (roof.Height <= drop * 2 || roof.Width <= drop * 2)
                        continue;

                    // A ridge along the longer way of the roof, lit on one side of it: the one
                    // mark that turns a coloured rectangle into a pitched roof from above.
                    paint.Color = RenderNoise.Shade(face, 0.18f);

                    canvas.DrawRect(
                        roof.Width >= roof.Height
                            ? SKRect.Create(roof.Left, roof.MidY - (drop / 2f), roof.Width, drop)
                            : SKRect.Create(roof.MidX - (drop / 2f), roof.Top, drop, roof.Height),
                        paint);
                }
            }
        }
    }
}
