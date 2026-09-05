using System;
using System.Threading.Tasks;
using NWorld.Map.Models;
using NWorld.MapServices.Constants;
using SkiaSharp;
using static NWorld.MapServices.MapRenderComponents.StandardRenderer.RoadNetwork;

namespace NWorld.MapServices.MapRenderComponents.StandardRenderer.RenderingFunctions
{
    /// <summary>
    /// A works: whatever it takes to get a resource out of the ground and make something of it.
    /// <para>
    /// One kind of thing that looks like five, and that is the whole idea. Every works has the
    /// same two halves -- a yard with a shed on it, which is the processing, and a piece of
    /// head-gear, which is the extraction -- and only the second half changes. So a stranger to
    /// the map learns "shed on a scraped yard means a works" once, and then reads the derrick or
    /// the headframe or the cut benches as <em>which</em> works without being told again.
    /// </para>
    /// <para>
    /// Which head-gear it grows is read off the <see cref="RenderComponentLayers.Resource"/>
    /// layer of the tile it stands on, at draw time, in the same way a coast is read off the
    /// neighbours. Nothing about the kind is stored on the enhancement, so a works never
    /// disagrees with the ground under it -- and there is nothing to keep in step, because there
    /// is only ever one copy of the fact.
    /// </para>
    /// <para>
    /// Nothing says where one may be built, which is why the plain case matters. A works can be
    /// put on bare ground or on water, and on either it comes out as a yard with a shed and no
    /// head-gear -- a works that has not found anything. Drop a deposit under it afterwards and
    /// it grows the right gear on the next frame, for the same reason it never disagrees with
    /// the ground: it was never told, it looks.
    /// </para>
    /// <para>
    /// Drawn straight onto the canvas rather than blitted from a sprite set, which is the one
    /// place this departs from everything else built on the enhancement layer. A road or a town
    /// is drawn thousands of times a frame and has to come off an atlas; a works is placed by
    /// hand on a deposit, so a whole map holds a few dozen and a screenful holds fewer. Caching
    /// it would mean five kinds times sixteen road shapes times the variants at every zoom, for
    /// a thing that costs a few dozen rectangles to draw outright.
    /// </para>
    /// </summary>
    public static class RenderWorks
    {
        /// <summary>Fixes the wear, the spoil and the lie of every works on the map.</summary>
        private const uint Seed = 0x0F7B_2C51u;

        /// <summary>Below this there is nothing legible left and nothing is drawn.</summary>
        public const int MinTileSize = 6;

        /// <summary>
        /// Below this the head-gear is a smudge, and a works is drawn as a yard with a shed and
        /// a heap on it -- which says "something is being got out of here" and stops there.
        /// </summary>
        private const int MinDetailTileSize = 13;

        /// <summary>How far the yard stops short of the tile edge, as a fraction of the tile.</summary>
        private const float Margin = 0.05f;

        /// <summary>The scraped ground a works stands on: spoil, mud and trodden stone.</summary>
        private static readonly SKColor Yard = new(0x5F, 0x57, 0x4A);

        /// <summary>The shed the stuff is processed in. Slate, so it is not another factory.</summary>
        private static readonly SKColor Roof = new(0x4E, 0x53, 0x58);

        /// <summary>Framing: pit timber and iron, tarred against the weather.</summary>
        private static readonly SKColor Frame = new(0x33, 0x2A, 0x22);

        /// <summary>What everything standing here casts onto the yard.</summary>
        private static readonly SKColor Shadow = new(0x00, 0x00, 0x00, 0x4A);

        /// <summary>
        /// Draws every works in the batch, each in the kind of works the ground under it calls
        /// for.
        /// </summary>
        public static Task Render(TileRenderContext context)
        {
            var canvas = context.Canvas;
            var tileSize = context.TileSize;
            var tiles = context.Tiles;

            if (canvas is null || tileSize < MinTileSize || tiles.Length == 0)
                return Task.CompletedTask;

            var world = context.World;

            foreach (var tile in tiles)
            {
                // No world means no way to know what is under it or what reaches it -- a still
                // render, or a test. A works with neither is still a works, so it is drawn as
                // the half that never changes.
                var kind = world is null ? Kind.Plain : Deposit(world, tile.X, tile.Y);
                var roads = world is null ? 0 : Mask(world, tile.X, tile.Y);

                var checkpoint = canvas.Save();

                try
                {
                    canvas.Translate(tile.X * tileSize, tile.Y * tileSize);
                    Draw(canvas, tileSize, kind, roads, RenderNoise.Hash(tile.X, tile.Y, Seed));
                }
                finally
                {
                    canvas.RestoreToCount(checkpoint);
                }
            }

            return Task.CompletedTask;
        }

        /// <summary>What a works is getting out of the ground.</summary>
        private enum Kind
        {
            /// <summary>
            /// Nothing named: a yard and a shed, and no head-gear. Not an edge case -- a works
            /// may be built anywhere, so this is what most of the ground on the map would give.
            /// </summary>
            Plain,

            Iron,
            Stone,
            Oil,
            Wood,
            Sulphur,
        }

        /// <summary>
        /// Which deposit the tile carries, as the kind of works that would sit on it.
        /// <para>
        /// Read from the tile rather than from the enhancement's own parameters, and that is
        /// deliberate: a works built on iron and later found to be standing on stone would be
        /// two facts disagreeing, and the way to have no disagreement is to keep one fact.
        /// </para>
        /// </summary>
        private static Kind Deposit(TileGrid world, int x, int y)
        {
            if (world.At(x, y) is not { } tile
                || !tile.MapRenderComponents.TryGetValue(RenderComponentLayers.Resource, out var found))
            {
                return Kind.Plain;
            }

            var type = found.ComponentType;

            return type == MapRenderComponentConstants.Iron ? Kind.Iron
                : type == MapRenderComponentConstants.Stone ? Kind.Stone
                : type == MapRenderComponentConstants.Oil ? Kind.Oil
                : type == MapRenderComponentConstants.Wood ? Kind.Wood
                : type == MapRenderComponentConstants.Sulphur ? Kind.Sulphur
                : Kind.Plain;
        }

        private static void Draw(SKCanvas canvas, int tileSize, Kind kind, int roads, uint seed)
        {
            var margin = tileSize * Margin;
            var yard = new SKRect(margin, margin, tileSize - margin, tileSize - margin);

            using var paint = new SKPaint { IsAntialias = false, Style = SKPaintStyle.Fill };

            paint.Color = Yard;
            canvas.DrawRect(yard, paint);

            Grit(canvas, yard, Yard, tileSize / 18f, 0.26f, seed);

            // The shed sits along the bottom of the yard and the head-gear stands above it, so
            // every works reads the same way round however different its head is.
            var shed = SKRect.Create(
                yard.Left + (yard.Width * 0.08f),
                yard.Bottom - (yard.Height * 0.34f),
                yard.Width * 0.84f,
                yard.Height * 0.30f);

            Aprons(canvas, tileSize, roads, yard, seed);

            if (tileSize < MinDetailTileSize)
            {
                // A heap and a roof. Everything below has bottomed out to a pixel or two by
                // here, and five different two-pixel smudges are five ways of drawing the same
                // smudge -- so it says works, and does not try to say which.
                paint.Color = Product(kind);
                canvas.DrawRect(
                    SKRect.Create(yard.Left, yard.Top, yard.Width * 0.5f, yard.Height * 0.4f), paint);

                paint.Color = Roof;
                canvas.DrawRect(shed, paint);

                return;
            }

            var head = SKRect.Create(
                yard.Left + (yard.Width * 0.12f),
                yard.Top + (yard.Height * 0.06f),
                yard.Width * 0.76f,
                yard.Height * 0.52f);

            switch (kind)
            {
                case Kind.Iron: Headframe(canvas, tileSize, head, seed); break;
                case Kind.Stone: Benches(canvas, tileSize, head, seed); break;
                case Kind.Oil: Derrick(canvas, tileSize, head, seed); break;
                case Kind.Wood: Logs(canvas, tileSize, head, seed); break;
                case Kind.Sulphur: Kilns(canvas, tileSize, head, seed); break;
                default: Spoil(canvas, tileSize, head, Product(kind), seed); break;
            }

            var cast = Math.Max(1f, tileSize * 0.04f);

            paint.Color = Shadow;
            canvas.DrawRect(SKRect.Create(shed.Left + cast, shed.Top + cast, shed.Width, shed.Height), paint);

            paint.Color = Roof;
            canvas.DrawRect(shed, paint);

            // The ridge along the shed, which is what tells a roof from a floor -- and a stub of
            // a flue at one end, because processing anything means burning something.
            paint.Color = RenderNoise.Shade(Roof, -0.28f);
            canvas.DrawRect(
                SKRect.Create(shed.Left, shed.MidY - Math.Max(0.5f, tileSize * 0.012f),
                    shed.Width, Math.Max(1f, tileSize * 0.024f)),
                paint);

            var flue = Math.Max(1f, tileSize * 0.075f);

            paint.Color = Frame;
            canvas.DrawRect(SKRect.Create(shed.Right - (flue * 1.6f), shed.Top - (flue * 0.5f), flue, flue), paint);
        }

        /// <summary>
        /// A pit head: the winding frame over the shaft, with its own spoil beside it. The one
        /// silhouette that says a hole goes straight down here.
        /// </summary>
        private static void Headframe(SKCanvas canvas, int tileSize, SKRect head, uint seed)
        {
            using var paint = new SKPaint { IsAntialias = false, Style = SKPaintStyle.Fill };

            Spoil(canvas, tileSize, SKRect.Create(head.Left, head.Top, head.Width * 0.4f, head.Height), Product(Kind.Iron), seed);

            var beam = Math.Max(1f, tileSize * 0.045f);
            var tower = SKRect.Create(
                head.MidX + (head.Width * 0.06f), head.Top, head.Width * 0.34f, head.Height * 0.9f);

            paint.Color = Frame;

            // Two legs, a head and a brace: the whole of a winding frame seen from above is a
            // rectangle with a diagonal in it, and the diagonal is what stops it reading as a
            // building.
            canvas.DrawRect(SKRect.Create(tower.Left, tower.Top, beam, tower.Height), paint);
            canvas.DrawRect(SKRect.Create(tower.Right - beam, tower.Top, beam, tower.Height), paint);
            canvas.DrawRect(SKRect.Create(tower.Left, tower.Top, tower.Width, beam), paint);
            canvas.DrawRect(SKRect.Create(tower.Left, tower.MidY, tower.Width, beam), paint);

            // The shaft itself: the one truly black thing in a works.
            var shaft = SKRect.Create(
                tower.MidX - (beam * 0.9f), tower.Bottom - (beam * 2.6f), beam * 1.8f, beam * 1.8f);

            if (shaft.Width >= 1f)
            {
                paint.Color = new SKColor(0x0C, 0x0A, 0x09);
                canvas.DrawRect(shaft, paint);
            }
        }

        /// <summary>
        /// A quarry: benches cut back in steps, each one paler than the last as it catches more
        /// sky, with dressed blocks stacked at the foot of them.
        /// </summary>
        private static void Benches(SKCanvas canvas, int tileSize, SKRect head, uint seed)
        {
            using var paint = new SKPaint { IsAntialias = false, Style = SKPaintStyle.Fill };

            const int steps = 3;
            var rise = head.Height / (steps + 1);

            for (var i = 0; i < steps; i++)
            {
                var bench = SKRect.Create(
                    head.Left + (head.Width * 0.06f * i),
                    head.Top + (i * rise),
                    head.Width - (head.Width * 0.12f * i),
                    rise);

                paint.Color = RenderNoise.Shade(Product(Kind.Stone), -0.34f + (i * 0.17f));
                canvas.DrawRect(bench, paint);

                // The lip of each bench, which is the shadow the step above throws on it.
                paint.Color = RenderNoise.Shade(Product(Kind.Stone), -0.52f);
                canvas.DrawRect(
                    SKRect.Create(bench.Left, bench.Top, bench.Width, Math.Max(1f, tileSize * 0.02f)), paint);
            }

            // Dressed blocks, squared off and stacked to go out. Square on purpose: everything
            // else in the quarry is a rough face, and the point of the works is that some of it
            // stops being rough.
            var block = Math.Max(1f, tileSize * 0.07f);

            for (var i = 0; i < 3; i++)
            {
                var x = head.Left + (head.Width * (0.12f + (i * 0.22f)));

                paint.Color = RenderNoise.Shade(
                    Product(Kind.Stone), 0.10f + ((RenderNoise.Hash01(i, 3, seed) - 0.5f) * 0.2f));

                canvas.DrawRect(SKRect.Create(x, head.Bottom - block, block, block), paint);
            }
        }

        /// <summary>
        /// A derrick over the well, with tanks beside it: a tapering tower seen from overhead is
        /// a set of nested squares, and the tanks are the only circles in a works.
        /// </summary>
        private static void Derrick(SKCanvas canvas, int tileSize, SKRect head, uint seed)
        {
            using var paint = new SKPaint { IsAntialias = false, Style = SKPaintStyle.Fill };

            var side = Math.Min(head.Width * 0.44f, head.Height * 0.86f);
            var tower = SKRect.Create(head.Left + (head.Width * 0.04f), head.Top, side, side);

            // Nested squares, alternating frame and sky: a derrick narrows as it rises, so from
            // directly above its legs and its girts read as a target.
            var rings = Math.Max(2, (int)(side / Math.Max(2f, tileSize * 0.06f)) / 2);

            for (var i = 0; i < rings; i++)
            {
                var ring = tower;
                ring.Inflate(-side * 0.5f * i / rings, -side * 0.5f * i / rings);

                if (ring.Width < 1f)
                    break;

                paint.Color = (i & 1) == 0 ? Frame : RenderNoise.Shade(Yard, -0.18f);
                canvas.DrawRect(ring, paint);
            }

            // The tanks. Crude is the darkest thing on the map and a tank full of it reads as a
            // hole, so each gets a lit rim to sit it back on the ground.
            var radius = Math.Max(1.5f, tileSize * 0.075f);

            for (var i = 0; i < 2; i++)
            {
                var cx = head.Right - radius - (i * radius * 2.4f);
                var cy = head.Top + radius + (head.Height * 0.28f * i);

                paint.Color = new SKColor(0x6B, 0x6F, 0x73);
                canvas.DrawCircle(cx, cy, radius, paint);

                paint.Color = Product(Kind.Oil);
                canvas.DrawCircle(cx, cy, Math.Max(1f, radius * 0.62f), paint);
            }

            _ = seed;
        }

        /// <summary>
        /// A sawmill's yard: cut logs stacked in rows, seen end on. The one works whose product
        /// is stacked round rather than square.
        /// </summary>
        private static void Logs(SKCanvas canvas, int tileSize, SKRect head, uint seed)
        {
            using var paint = new SKPaint { IsAntialias = false, Style = SKPaintStyle.Fill };

            var radius = Math.Max(1f, tileSize * 0.045f);
            var across = Math.Max(2, (int)(head.Width / (radius * 2.3f)));
            var rows = Math.Max(2, (int)(head.Height / (radius * 2.6f)));

            for (var row = 0; row < rows; row++)
            {
                // Every other row set back half a log, which is how a stack of round timber
                // actually sits: they roll into the gaps.
                var inset = (row & 1) == 0 ? 0f : radius;

                for (var i = 0; i < across; i++)
                {
                    var cx = head.Left + radius + inset + (i * radius * 2.2f);
                    var cy = head.Top + radius + (row * radius * 2.4f);

                    if (cx + radius > head.Right || cy + radius > head.Bottom)
                        continue;

                    paint.Color = RenderNoise.Shade(
                        Product(Kind.Wood), (RenderNoise.Hash01(i, row, seed) - 0.5f) * 0.26f);

                    canvas.DrawCircle(cx, cy, radius, paint);

                    // The heart of the log, which is what makes a circle read as a cut end
                    // rather than as a boulder.
                    if (radius >= 2f)
                    {
                        paint.Color = RenderNoise.Shade(Product(Kind.Wood), -0.34f);
                        canvas.DrawCircle(cx, cy, radius * 0.34f, paint);
                    }
                }
            }
        }

        /// <summary>
        /// Sulphur kilns: a row of round retorts with the bloom of their own product round the
        /// foot of them. Yellow is doing most of the work here and is allowed to.
        /// </summary>
        private static void Kilns(SKCanvas canvas, int tileSize, SKRect head, uint seed)
        {
            using var paint = new SKPaint { IsAntialias = false, Style = SKPaintStyle.Fill };

            var stain = head;
            stain.Inflate(-head.Width * 0.04f, -head.Height * 0.06f);

            paint.Color = new SKColor(Product(Kind.Sulphur).Red, Product(Kind.Sulphur).Green, Product(Kind.Sulphur).Blue, 0x66);
            canvas.DrawRect(stain, paint);

            var radius = Math.Max(1.5f, tileSize * 0.075f);
            var kilns = Math.Max(2, (int)(head.Width / (radius * 2.6f)));

            for (var i = 0; i < kilns; i++)
            {
                var cx = head.Left + radius + (i * radius * 2.5f);
                var cy = head.MidY;

                if (cx + radius > head.Right)
                    break;

                paint.Color = Frame;
                canvas.DrawCircle(cx, cy, radius, paint);

                // The mouth, glowing with what is being burnt off in it.
                paint.Color = RenderNoise.Shade(
                    Product(Kind.Sulphur), (RenderNoise.Hash01(i, 5, seed) - 0.5f) * 0.2f);

                canvas.DrawCircle(cx, cy, Math.Max(1f, radius * 0.5f), paint);
            }
        }

        /// <summary>
        /// A heap of whatever is being got out, for the works that has no head-gear of its own.
        /// </summary>
        private static void Spoil(SKCanvas canvas, int tileSize, SKRect head, SKColor product, uint seed)
        {
            using var paint = new SKPaint { IsAntialias = false, Style = SKPaintStyle.Fill };

            var cell = Math.Max(1f, tileSize / 16f);

            for (var y = head.Top; y < head.Bottom; y += cell)
            {
                for (var x = head.Left; x < head.Right; x += cell)
                {
                    // Piled rather than spread: thinner towards the edges, so it reads as a heap
                    // with a top rather than as a rectangle of gravel.
                    var across = Math.Abs((x - head.MidX) / (head.Width / 2f));
                    var down = Math.Abs((y - head.MidY) / (head.Height / 2f));

                    if (across + down > 1.05f)
                        continue;

                    var lift = (RenderNoise.Hash01((int)(x / cell), (int)(y / cell), seed) - 0.5f) * 0.34f;

                    paint.Color = RenderNoise.Shade(product, lift - (across * 0.2f));
                    canvas.DrawRect(SKRect.Create(x, y, cell, cell), paint);
                }
            }
        }

        /// <summary>What a works of each kind piles up, taken from the deposit it stands on.</summary>
        private static SKColor Product(Kind kind) => kind switch
        {
            Kind.Iron => new SKColor(0x6E, 0x54, 0x40),
            Kind.Stone => new SKColor(0xC4, 0xC4, 0xBA),
            Kind.Oil => new SKColor(0x14, 0x11, 0x1A),
            Kind.Wood => new SKColor(0xA8, 0x82, 0x52),
            Kind.Sulphur => new SKColor(0xE2, 0xC4, 0x3A),
            _ => new SKColor(0x6A, 0x62, 0x56),
        };

        /// <summary>
        /// The apron on each side a road arrives from: hard standing from the tile edge in to the
        /// yard, in the road's own bed so the join does not show.
        /// </summary>
        private static void Aprons(SKCanvas canvas, int tileSize, int roads, SKRect yard, uint seed)
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
                    North => new SKRect(half - width, 0f, half + width, yard.Top),
                    South => new SKRect(half - width, yard.Bottom, half + width, tileSize),
                    East => new SKRect(yard.Right, half - width, tileSize, half + width),
                    _ => new SKRect(0f, half - width, yard.Left, half + width),
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
