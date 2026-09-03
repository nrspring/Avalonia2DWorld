using System;
using System.Globalization;
using System.Threading.Tasks;
using NWorld.Map.Models;
using NWorld.MapServices.Constants;
using SkiaSharp;

namespace NWorld.MapServices.MapRenderComponents.StandardRenderer.RenderingFunctions
{
    /// <summary>
    /// An old stone road: a bed of packed rubble with worn slabs laid along it, joined up to
    /// whatever roads are next door.
    /// <para>
    /// The shape of a tile is not stored on the tile. It is read off the map at draw time from
    /// the four neighbours -- see <see cref="Mask"/> -- so laying a road next to another makes
    /// both of them fit together on the very next frame, and lifting one leaves its neighbours
    /// correct without anything having to go round and tell them. That is what
    /// <see cref="RenderFrame.World"/> is for, and it is why placing a road is a one-tile edit
    /// rather than an edit to five tiles.
    /// </para>
    /// <para>
    /// Sixteen shapes fall out of four neighbours, and they are the usual set: a crossing, four
    /// tees, four corners, two straights, four stubs, and the one with no neighbours at all.
    /// That last one is the only tile with nothing to tell it which way to lie, so it is the
    /// only one that reads the rotation in its parameters -- see <see cref="Orientation"/>.
    /// </para>
    /// <para>
    /// Drawn from a sprite per shape per zoom level rather than stroked per tile. There are
    /// eighteen of them and they are tile-sized, so the whole set for a zoom level is smaller
    /// than one screenful of road drawn the other way, and a tile costs one blit.
    /// </para>
    /// <para>
    /// Ancient rather than made: the slabs are set slightly askew, their tone varies stone to
    /// stone, and the bed shows through at the edges. All of it is deterministic in the tile's
    /// own coordinates, so a road looks the same on every repaint and does not crawl as the map
    /// is panned.
    /// </para>
    /// </summary>
    public static class RenderRoad
    {
        /// <summary>Which side a bit of <see cref="Mask"/> stands for.</summary>
        private const int North = 1;
        private const int East = 2;
        private const int South = 4;
        private const int West = 8;

        /// <summary>
        /// How many shapes there are: sixteen from the neighbours, and the lone tile counted
        /// twice because it can lie either way.
        /// </summary>
        private const int Shapes = 17;

        /// <summary>Where the second orientation of the lone tile sits in the sprite set.</summary>
        private const int LoneUpright = 16;

        /// <summary>
        /// How wide the roadway is, as a fraction of the tile. Wide enough to read as a road at
        /// a glance and narrow enough that the ground it crosses still shows either side, which
        /// is what keeps a road looking laid on the land rather than cut out of it.
        /// </summary>
        private const float Width = 0.56f;

        /// <summary>
        /// Nothing is drawn below this. At a few pixels a tile the slabs are noise and the
        /// road reads better as the line the bed alone already makes.
        /// </summary>
        private const int MinSlabTileSize = 12;

        /// <summary>Below this a road is not drawn at all -- there is nothing legible left.</summary>
        public const int MinTileSize = 4;

        /// <summary>The packed rubble the slabs are set into, and what shows at a worn edge.</summary>
        private static readonly SKColor Bed = new(0x6B, 0x63, 0x58);

        /// <summary>The slabs themselves, lightest to darkest. An old road is not one colour.</summary>
        private static readonly SKColor[] Slabs =
        [
            new(0xA8, 0xA2, 0x96),
            new(0x9B, 0x94, 0x88),
            new(0x8D, 0x87, 0x7B),
            new(0xB2, 0xAC, 0xA0),
            new(0x82, 0x7C, 0x71),
        ];

        /// <summary>
        /// How many stones lie side by side across the roadway. Two: one is a plank and three
        /// are cobbles, and a paved road at this scale is neither.
        /// </summary>
        private const int Lanes = 2;

        /// <summary>
        /// One sprite per shape at one zoom level. Sixteen tile-sized images plus the second
        /// lone orientation; a few hundred kilobytes at the largest zoom.
        /// </summary>
        private sealed class Sprites : IZoomLevelResource
        {
            public Sprites(SKImage[] shapes, long bytes)
            {
                Shape = shapes;
                Bytes = bytes;
            }

            public SKImage[] Shape { get; }

            public long Bytes { get; }

            public void Dispose()
            {
                foreach (var image in Shape)
                    image.Dispose();
            }
        }

        private static readonly ZoomLevelCache<Sprites> Cache = new(24L * 1024 * 1024, Build);

        /// <summary>
        /// Draws every road tile in the batch, each in the shape its neighbours call for.
        /// </summary>
        public static Task Render(TileRenderContext context)
        {
            var canvas = context.Canvas;
            var tileSize = context.TileSize;
            var tiles = context.Tiles;

            if (canvas is null || tileSize < MinTileSize || tiles.Length == 0)
                return Task.CompletedTask;

            var sprites = Cache.Get(tileSize);
            var world = context.World;

            foreach (var tile in tiles)
            {
                var shape = world is null
                    ? Lone(tile.Params)
                    : Shape(world, tile.X, tile.Y, tile.Params);

                canvas.DrawImage(
                    sprites.Shape[shape],
                    new SKPoint(tile.X * tileSize, tile.Y * tileSize));
            }

            return Task.CompletedTask;
        }

        /// <inheritdoc cref="ZoomLevelCache{T}.Prewarm"/>
        public static Task Prewarm(int tileSize) =>
            tileSize < MinTileSize ? Task.CompletedTask : Cache.Prewarm(tileSize);

        /// <inheritdoc cref="ZoomLevelCache{T}.Clear"/>
        public static void ClearCache() => Cache.Clear();

        /// <summary>
        /// Which of the shapes a tile takes: its neighbour mask, or one of the two lone
        /// orientations when it has no neighbours at all.
        /// </summary>
        private static int Shape(TileGrid world, int x, int y, string[] parameters)
        {
            var mask = Mask(world, x, y);

            return mask == 0 ? Lone(parameters) : mask;
        }

        /// <summary>
        /// Which sides of a tile have a road on them, as four bits.
        /// <para>
        /// Four-way and not eight, like everything else that decides what joins to what on this
        /// map. A road meeting another only across a corner is two roads, the same way a coast
        /// touching only at a corner is two islands.
        /// </para>
        /// </summary>
        private static int Mask(TileGrid world, int x, int y) =>
            (HasRoad(world, x, y - 1) ? North : 0)
            | (HasRoad(world, x + 1, y) ? East : 0)
            | (HasRoad(world, x, y + 1) ? South : 0)
            | (HasRoad(world, x - 1, y) ? West : 0);

        /// <summary>
        /// Whether the tile at a map coordinate carries a road. False off the edge of the map,
        /// which is what makes a road run up to the border and stop rather than reach past it.
        /// </summary>
        private static bool HasRoad(TileGrid world, int x, int y) =>
            world.At(x, y) is { } tile
            && tile.MapRenderComponents.TryGetValue(RenderComponentLayers.Enhancement, out var built)
            && built.ComponentType == MapRenderComponentConstants.Road;

        /// <summary>
        /// Which way a road with no neighbours lies, from the rotation in its parameters: an
        /// odd number of quarter turns stands it upright, anything else lays it flat.
        /// <para>
        /// The one thing about a road that is stored rather than worked out, because it is the
        /// one thing the map cannot answer. A tile with any neighbour at all takes its shape
        /// from them and this is not consulted.
        /// </para>
        /// </summary>
        private static int Lone(string[] parameters) => Orientation(parameters) ? LoneUpright : 0;

        /// <inheritdoc cref="Lone"/>
        private static bool Orientation(string[] parameters) =>
            parameters.Length > 0
            && int.TryParse(parameters[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var quarters)
            && (((quarters % 2) + 2) % 2) == 1;

        /// <summary>Builds every shape at one tile size.</summary>
        private static Sprites Build(int tileSize)
        {
            var images = new SKImage[Shapes];
            var info = new SKImageInfo(tileSize, tileSize, SKColorType.Rgba8888, SKAlphaType.Premul);

            for (var shape = 0; shape < Shapes; shape++)
            {
                using var surface = SKSurface.Create(info);

                if (surface is null)
                {
                    // Nothing to draw with. An empty sprite is a road that does not show, which
                    // is a great deal better than a frame that throws.
                    images[shape] = SKImage.FromBitmap(new SKBitmap(info));
                    continue;
                }

                surface.Canvas.Clear(SKColors.Transparent);
                Draw(surface.Canvas, tileSize, shape);

                images[shape] = surface.Snapshot();
            }

            return new Sprites(images, (long)tileSize * tileSize * 4 * Shapes);
        }

        /// <summary>
        /// Draws one shape onto a tile-sized canvas: the bed first, then the slabs over it.
        /// </summary>
        private static void Draw(SKCanvas canvas, int tileSize, int shape)
        {
            // The lone upright is the lone flat turned a quarter, which is the whole of what
            // the rotation does -- so it is drawn by turning the canvas rather than by a second
            // set of arms.
            if (shape == LoneUpright)
            {
                canvas.Save();
                canvas.RotateDegrees(90, tileSize / 2f, tileSize / 2f);
                Draw(canvas, tileSize, 0);
                canvas.Restore();
                return;
            }

            var half = tileSize / 2f;
            var reach = tileSize * Width / 2f;

            // A tile with no neighbours still gets a length of road rather than a blob: it is a
            // road somebody has started, and a square of stone reads as a floor.
            var arms = shape == 0 ? East | West : shape;

            using var bed = new SKPaint { Color = Bed, IsAntialias = false, Style = SKPaintStyle.Fill };

            // The middle, always, so that arms meeting at the centre make one continuous
            // surface rather than four rectangles with a seam down each join.
            canvas.DrawRect(SKRect.Create(half - reach, half - reach, reach * 2, reach * 2), bed);

            if ((arms & North) != 0) canvas.DrawRect(SKRect.Create(half - reach, 0, reach * 2, half), bed);
            if ((arms & South) != 0) canvas.DrawRect(SKRect.Create(half - reach, half, reach * 2, half), bed);
            if ((arms & West) != 0) canvas.DrawRect(SKRect.Create(0, half - reach, half, reach * 2), bed);
            if ((arms & East) != 0) canvas.DrawRect(SKRect.Create(half, half - reach, half, reach * 2), bed);

            if (tileSize >= MinSlabTileSize)
                Slab(canvas, tileSize, arms, half, reach);
        }

        /// <summary>
        /// Lays the slabs along whichever arms the shape has.
        /// <para>
        /// Along the arm rather than across the tile, so the courses turn with the road and a
        /// corner reads as a corner rather than as two straights crossing. Each slab is inset
        /// from its neighbour by a joint, nudged a fraction off square, and given a tone of its
        /// own -- which is the difference between a paved road and a painted stripe.
        /// </para>
        /// </summary>
        private static void Slab(SKCanvas canvas, int tileSize, int arms, float half, float reach)
        {
            // Two stones across the roadway, and stones about as long as they are wide. A road
            // paved in one full-width piece per course reads as a ladder, and one paved in many
            // narrow ones reads as planking; two courses of roughly square stone is what a laid
            // road actually looks like from above.
            var lane = reach * 2 / Lanes;
            var course = Math.Max(1, (int)Math.Round(half / lane));
            var step = half / course;

            // The gap between stones is the bed showing through rather than a line drawn over
            // it, so there is nothing to paint here: the slab is simply laid short of its own
            // cell. Half a pixel at the least, or the joints close up at the small zooms and
            // the paving goes back to being one flat band.
            var joint = Math.Max(0.5f, step * 0.13f);

            using var slab = new SKPaint { IsAntialias = false, Style = SKPaintStyle.Fill };

            // The four arms, each walked out from the middle. The centre is covered by whichever
            // arms exist, so a crossing gets its stones from all four and a stub from one -- and
            // no arm has to know what the others are doing.
            foreach (var arm in new[] { North, East, South, West })
            {
                if ((arms & arm) == 0)
                    continue;

                for (var along = 0; along < course; along++)
                {
                    for (var across = 0; across < Lanes; across++)
                    {
                        var near = along * step;
                        var side = half - reach + (across * lane);

                        var stone = arm switch
                        {
                            North => SKRect.Create(side, half - near - step, lane, step),
                            South => SKRect.Create(side, half + near, lane, step),
                            West => SKRect.Create(half - near - step, side, step, lane),
                            _ => SKRect.Create(half + near, side, step, lane),
                        };

                        stone.Inflate(-joint, -joint);

                        if (stone.Width <= 0 || stone.Height <= 0)
                            continue;

                        // Set askew, and not by the same amount twice running: an old road has
                        // shifted under a thousand years of carts, and stones laid true read as
                        // new work. Hashed on where the stone is rather than counted, so the
                        // pattern does not march along the arm.
                        var hash = (along * 73) + (across * 31) + (arm * 17);

                        var skew = joint * 0.7f * (((hash % 5) - 2) / 2f);
                        stone.Offset(skew, -skew);

                        slab.Color = Slabs[hash % Slabs.Length];
                        canvas.DrawRect(stone, slab);
                    }
                }
            }
        }
    }
}
