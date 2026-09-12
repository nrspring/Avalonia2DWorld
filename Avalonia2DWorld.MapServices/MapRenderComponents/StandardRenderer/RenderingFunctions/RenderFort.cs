using System;
using System.Threading.Tasks;
using Avalonia2DWorld.Map.Models;
using SkiaSharp;
using static Avalonia2DWorld.MapServices.MapRenderComponents.StandardRenderer.RoadNetwork;

namespace Avalonia2DWorld.MapServices.MapRenderComponents.StandardRenderer.RenderingFunctions
{
    /// <summary>
    /// A fort: a square of curtain wall with a tower on each corner, a keep standing in the
    /// bailey, and a gate wherever a road arrives.
    /// <para>
    /// The opposite kind of thing to <see cref="RenderCity"/>, and drawn as one on purpose. A
    /// town is a piece of somewhere larger, so it has no wall and grows by having more town laid
    /// beside it; a fort is one whole thing that fits on one tile, so it is walled all the way
    /// round and two of them side by side are two forts rather than a bigger one. That is why
    /// this reads the road network for its shape rather than <see cref="RoadNetwork.CityMask"/>:
    /// what a neighbour changes here is not how far the walls run but where the doors are.
    /// </para>
    /// <para>
    /// Which is the whole trick of it. Lay a fort on empty ground and it is shut -- four blank
    /// walls, which is what a strongpoint with nothing leading to it should look like. Run a road
    /// up to it and a gatehouse opens on that side on the very next frame, with the roadway
    /// funnelling from its full width down to the gap, because a gate is narrower than the road
    /// that arrives at it and always was. Nothing is stored to make that happen, and nothing has
    /// to go round afterwards and tell the fort a road was built.
    /// </para>
    /// <para>
    /// Grey masonry, deliberately the road's and the bridge's palette rather than the town's warm
    /// one -- see <see cref="StoneWork"/>. A fort is stonework in the same sense a bridge is: cut,
    /// dressed and military. A town is where people live, and is coloured to say so.
    /// </para>
    /// </summary>
    public static class RenderFort
    {
        /// <summary>
        /// How far the walls stop short of the tile edge, as a fraction of the tile. A fort is a
        /// discrete object rather than a surface, so there is ground all the way round it: built
        /// out to the edge it would fuse with the fort on the next tile, which is exactly the
        /// reading this one does not want.
        /// </summary>
        private const float Margin = 0.07f;

        /// <summary>How thick the curtain wall is, as a fraction of the tile.</summary>
        private const float WallShare = 0.105f;

        /// <summary>
        /// How big a corner tower is, as a fraction of the tile -- comfortably more than the wall
        /// is thick, since a tower that only matched the curtain would read as a corner rather
        /// than as something standing on one.
        /// </summary>
        private const float TowerShare = 0.20f;

        /// <summary>How far a tower stands proud of the curtain, in wall thicknesses.</summary>
        private const float TowerProud = 0.25f;

        /// <summary>
        /// The width of the passage through a gateway, as a fraction of the tile. Wide enough to
        /// still be a hole at the zoom levels this is mostly looked at: a gate that closes up into
        /// its own gatehouse is a lump on a wall, and the one thing a gate has to do here is read
        /// as a way in.
        /// </summary>
        private const float GateShare = 0.22f;

        /// <summary>
        /// How far the gatehouse reaches past the passage on either side, in wall thicknesses --
        /// enough that it bonds into the curtain rather than sitting on it.
        /// </summary>
        private const float GateHouseShare = 0.85f;

        /// <summary>How deep the gatehouse is through the wall, in wall thicknesses.</summary>
        private const float GateHouseDepth = 1.9f;

        /// <summary>The keep's side, as a fraction of the tile.</summary>
        private const float KeepShare = 0.30f;

        /// <summary>
        /// Below this there is no room for a bailey with anything in it, and the fort is drawn as
        /// a walled block and nothing else -- which at that size is all a fort looks like anyway.
        /// <para>
        /// As low as it will go rather than as low as it looks comfortable. Everything the fort is
        /// made of has a floor of a pixel or two under it, so the whole thing keeps drawing far
        /// past the point where it is drawing well, and every zoom level that keeps its towers is
        /// a zoom level where a fort still looks like one. Below this they have all bottomed out
        /// together and the shape is a smudge, so it is better to say less.
        /// </para>
        /// </summary>
        private const int MinDetailTileSize = 10;

        /// <summary>Below this the wall's courses are noise, and it is left as one face.</summary>
        private const int MinCourseTileSize = 22;

        /// <summary>The bailey: trodden earth, the same as a town's streets and for the same reason.</summary>
        private static readonly SKColor Yard = new(0x6A, 0x60, 0x51);

        /// <summary>
        /// The curtain's flank, well darker than the stone it is built of. What is seen of a wall
        /// from overhead is mostly the shaded side of it, the same as a bridge's parapet.
        /// </summary>
        private static readonly SKColor WallFace = new(0x53, 0x4E, 0x46);

        /// <summary>The walkway along the top of the curtain, which is the part facing the sky.</summary>
        private static readonly SKColor WallTop = new(0x9C, 0x96, 0x89);

        /// <summary>A tower top, a shade above the wall walk: it stands higher, so it catches more.</summary>
        private static readonly SKColor TowerTop = new(0xA8, 0xA1, 0x93);

        /// <summary>The keep, which is roofed rather than open, so it is not as pale as the tops.</summary>
        private static readonly SKColor KeepFace = new(0x87, 0x81, 0x75);

        /// <summary>
        /// The passage under a gatehouse. Dark, because it is: a gateway is a tunnel through the
        /// thickest part of the wall, and from directly overhead almost none of the road in it is
        /// lit.
        /// <para>
        /// Drawn dark rather than in the road's own bed, which is what this was first and did not
        /// work. A passage the colour of the road outside it is not a hole, it is a change of
        /// paving -- and the gate stops being a way in and becomes a pattern on a wall. The one
        /// thing that has to survive being sixteen pixels wide is that there is a hole here.
        /// </para>
        /// </summary>
        private static readonly SKColor Gateway = new(0x37, 0x32, 0x2B);

        /// <summary>What the work throws onto the ground beside and below it.</summary>
        private static readonly SKColor Shadow = new(0x16, 0x12, 0x0E, 0x77);

        private static readonly StoneSpan Span = new(0xF0B71Fu, Draw, runsWhenAlone: false);

        public static Task Render(TileRenderContext context) => Span.Render(context);

        /// <inheritdoc cref="StoneSpan.Prewarm"/>
        public static Task Prewarm(int tileSize) => Span.Prewarm(tileSize);

        /// <inheritdoc cref="StoneSpan.ClearCache"/>
        public static void ClearCache() => Span.ClearCache();

        /// <summary>
        /// The roads in, then the fort standing on top of them: shadow, bailey, curtain,
        /// gatehouses, towers, keep. The towers and the keep last because they are the tall
        /// things, and what is tallest is what is least covered from above.
        /// </summary>
        /// <param name="gates">Which sides something of the road network arrives on.</param>
        private static void Draw(SKCanvas canvas, int tileSize, int gates, uint seed)
        {
            var margin = tileSize * Margin;
            var outer = new SKRect(margin, margin, tileSize - margin, tileSize - margin);
            var wall = Math.Max(1f, tileSize * WallShare);

            using var paint = new SKPaint { IsAntialias = false, Style = SKPaintStyle.Fill };

            if (tileSize < MinDetailTileSize)
            {
                // A pale rim round a dark middle, which is what all of the above comes to once
                // there are nine pixels to say it in: something walled, and stone rather than
                // ground. Gritted like everything else at this size, since a flat block among
                // textured roads is the one thing that reads as missing rather than as small.
                paint.Color = WallTop;
                canvas.DrawRect(outer, paint);

                var slot = outer;

                slot.Inflate(-Math.Max(1f, wall), -Math.Max(1f, wall));

                if (slot.Width > 0 && slot.Height > 0)
                {
                    paint.Color = WallFace;
                    canvas.DrawRect(slot, paint);
                }

                // A pixel a cell, and clipped: at this size the grit is the pixels, and one
                // straying past the edge is the whole fort a tile-width wider on that side.
                canvas.Save();
                canvas.ClipRect(outer);

                for (var y = outer.Top; y < outer.Bottom; y += 1f)
                {
                    for (var x = outer.Left; x < outer.Right; x += 1f)
                    {
                        var lift = (RenderNoise.Hash01((int)x, (int)y, seed) - 0.5f) * 0.16f;

                        paint.Color = RenderNoise.Shade(slot.Contains(x, y) ? WallFace : WallTop, lift);
                        canvas.DrawRect(SKRect.Create(x, y, 1f, 1f), paint);
                    }
                }

                canvas.Restore();

                return;
            }

            // Laid before the shadow rather than after, so the walls fall across the road going
            // in. A road runs over the ground, and a shadow lies on top of both.
            Approaches(canvas, tileSize, gates, margin + wall, seed);

            paint.Color = Shadow;

            var cast = Math.Max(1f, tileSize * 0.05f);

            canvas.DrawRect(
                SKRect.Create(outer.Left + cast, outer.Top + cast, outer.Width, outer.Height), paint);

            Bailey(canvas, tileSize, outer, seed);
            Curtain(canvas, tileSize, gates, outer, wall, seed);
            Gatehouses(canvas, tileSize, gates, outer, wall, seed);
            Towers(canvas, tileSize, outer, wall, seed);
            Keep(canvas, tileSize, outer, wall, seed);
        }

        /// <summary>
        /// Runs the roadway in through each gate: full width where it meets the tile edge,
        /// narrowing to the width of the gap by the time it reaches the wall.
        /// <para>
        /// The funnel is the point. A gate is much narrower than the road that arrives at it, and
        /// a strip of the road's own width driven straight through the curtain would be a hole in
        /// the wall rather than a gate in it. Drawn in the road's own bed and grit -- see
        /// <see cref="StoneWork"/> -- so the join at the tile edge does not show.
        /// </para>
        /// </summary>
        /// <param name="depth">How far in from the tile edge the inside of the curtain lies.</param>
        private static void Approaches(SKCanvas canvas, int tileSize, int gates, float depth, uint seed)
        {
            if (gates == 0)
                return;

            var half = tileSize / 2f;
            var mouth = tileSize * StoneWork.Way / 2f;
            var throat = Math.Max(1f, tileSize * GateShare) / 2f;
            var far = tileSize - depth;

            foreach (var arm in StoneWork.Arms)
            {
                if ((gates & arm) == 0)
                    continue;

                using var funnel = new SKPath();

                switch (arm)
                {
                    case North:
                        funnel.MoveTo(half - mouth, 0);
                        funnel.LineTo(half + mouth, 0);
                        funnel.LineTo(half + throat, depth);
                        funnel.LineTo(half - throat, depth);
                        break;

                    case South:
                        funnel.MoveTo(half - mouth, tileSize);
                        funnel.LineTo(half + mouth, tileSize);
                        funnel.LineTo(half + throat, far);
                        funnel.LineTo(half - throat, far);
                        break;

                    case West:
                        funnel.MoveTo(0, half - mouth);
                        funnel.LineTo(0, half + mouth);
                        funnel.LineTo(depth, half + throat);
                        funnel.LineTo(depth, half - throat);
                        break;

                    default:
                        funnel.MoveTo(tileSize, half - mouth);
                        funnel.LineTo(tileSize, half + mouth);
                        funnel.LineTo(far, half + throat);
                        funnel.LineTo(far, half - throat);
                        break;
                }

                funnel.Close();

                // Clipped rather than laid out shape by shape, so the bed and its grit come from
                // the same place the road's do and cannot drift apart from them.
                canvas.Save();
                canvas.ClipPath(funnel);
                StoneWork.Bedding(canvas, tileSize, [funnel.Bounds], seed);
                canvas.Restore();
            }
        }

        /// <summary>The ground inside the walls: beaten earth, rutted by everything kept on it.</summary>
        private static void Bailey(SKCanvas canvas, int tileSize, SKRect outer, uint seed)
        {
            using var paint = new SKPaint { IsAntialias = false, Style = SKPaintStyle.Fill };

            paint.Color = Yard;
            canvas.DrawRect(outer, paint);

            var grit = Math.Max(1f, tileSize / 22f);

            for (var y = outer.Top; y < outer.Bottom; y += grit)
            {
                for (var x = outer.Left; x < outer.Right; x += grit)
                {
                    var lift = (RenderNoise.Hash01((int)(x / grit), (int)(y / grit), seed) - 0.5f) * 0.2f;

                    paint.Color = RenderNoise.Shade(Yard, lift);
                    canvas.DrawRect(SKRect.Create(x, y, grit, grit), paint);
                }
            }
        }

        /// <summary>
        /// The curtain: a band down each side of the fort, broken in the middle on any side a
        /// gate opens on.
        /// </summary>
        private static void Curtain(
            SKCanvas canvas, int tileSize, int gates, SKRect outer, float wall, uint seed)
        {
            var half = tileSize / 2f;
            var throat = Math.Max(1f, tileSize * GateShare) / 2f;

            foreach (var arm in StoneWork.Arms)
            {
                var upright = arm is East or West;

                var band = arm switch
                {
                    North => SKRect.Create(outer.Left, outer.Top, outer.Width, wall),
                    South => SKRect.Create(outer.Left, outer.Bottom - wall, outer.Width, wall),
                    West => SKRect.Create(outer.Left, outer.Top, wall, outer.Height),
                    _ => SKRect.Create(outer.Right - wall, outer.Top, wall, outer.Height),
                };

                if ((gates & arm) == 0)
                {
                    Masonry(canvas, tileSize, band, upright, wall, arm, seed);
                    continue;
                }

                // Two stretches with the gateway between them. The gap is left rather than
                // painted over, because what shows through it is the road already laid under it.
                var near = band;
                var away = band;

                if (upright)
                {
                    near.Bottom = half - throat;
                    away.Top = half + throat;
                }
                else
                {
                    near.Right = half - throat;
                    away.Left = half + throat;
                }

                Masonry(canvas, tileSize, near, upright, wall, arm, seed);
                Masonry(canvas, tileSize, away, upright, wall, arm, seed + 0x9E37u);
            }
        }

        /// <summary>
        /// One stretch of wall: a shaded face with a course of walk stones along the top of it.
        /// <para>
        /// The stones are what make it a wall rather than a rule. Each is a shade of its own and
        /// every so often one is gone, which is the same trick and the same reasoning as the
        /// bridge's parapet.
        /// </para>
        /// </summary>
        private static void Masonry(
            SKCanvas canvas, int tileSize, SKRect band, bool upright, float wall, int arm, uint seed)
        {
            if (band.Width <= 0 || band.Height <= 0)
                return;

            using var paint = new SKPaint { IsAntialias = false, Style = SKPaintStyle.Fill };

            paint.Color = WallFace;
            canvas.DrawRect(band, paint);

            if (tileSize < MinCourseTileSize)
            {
                // No room for courses, but a wall drawn in its flank alone reads as a ditch. One
                // lit line along it is the least that still stands the wall up.
                var edge = Math.Max(1f, wall * 0.45f);

                paint.Color = WallTop;
                canvas.DrawRect(
                    upright
                        ? SKRect.Create(band.Left, band.Top, edge, band.Height)
                        : SKRect.Create(band.Left, band.Top, band.Width, edge),
                    paint);

                return;
            }

            var block = Math.Max(1f, wall * 1.4f);
            var run = upright ? band.Height : band.Width;

            for (var along = 0f; along < run; along += block)
            {
                var hash = RenderNoise.Hash((int)(along / block), arm, seed);

                if ((hash & 0xFF) < 26)
                    continue;

                var stone = upright
                    ? SKRect.Create(band.Left, band.Top + along, band.Width, block)
                    : SKRect.Create(band.Left + along, band.Top, block, band.Height);

                stone.Intersect(band);
                stone.Inflate(-Math.Max(0.5f, block * 0.09f), -Math.Max(0.5f, block * 0.09f));

                if (stone.Width <= 0 || stone.Height <= 0)
                    continue;

                paint.Color = RenderNoise.Shade(WallTop, ((((hash >> 8) & 0x3F) / 63f) - 0.5f) * 0.22f);
                canvas.DrawRect(stone, paint);
            }
        }

        /// <summary>
        /// A gatehouse in every side that opens: a block of building bonded into the curtain,
        /// standing a little proud of it both ways, with the passage cut through the middle of it.
        /// <para>
        /// One block with a hole in it rather than a tower either side of a gap, which is what
        /// this was first drawn as and did not work. Two towers close enough together to flank a
        /// gateway are, at sixteen pixels to a tile, one solid lump -- and a gate that reads as
        /// solid is a wall. Cutting the passage out of a single mass leaves the hole as the
        /// darkest and straightest thing on that side of the fort, which is the one feature here
        /// that has to survive being small.
        /// </para>
        /// </summary>
        private static void Gatehouses(
            SKCanvas canvas, int tileSize, int gates, SKRect outer, float wall, uint seed)
        {
            if (gates == 0)
                return;

            var half = tileSize / 2f;
            var throat = Math.Max(1f, tileSize * GateShare) / 2f;
            var cheek = Math.Max(1f, wall * GateHouseShare);
            var deep = Math.Max(2f, wall * GateHouseDepth);

            foreach (var arm in StoneWork.Arms)
            {
                if ((gates & arm) == 0)
                    continue;

                // The middle of this side's wall. The gatehouse is centred on it, so it stands out
                // into the field and back into the bailey by the same amount.
                var line = arm switch
                {
                    North => outer.Top + (wall / 2f),
                    South => outer.Bottom - (wall / 2f),
                    West => outer.Left + (wall / 2f),
                    _ => outer.Right - (wall / 2f),
                };

                var upright = arm is East or West;

                var block = upright
                    ? SKRect.Create(line - (deep / 2f), half - throat - cheek, deep, (throat + cheek) * 2f)
                    : SKRect.Create(half - throat - cheek, line - (deep / 2f), (throat + cheek) * 2f, deep);

                Tower(canvas, block, tileSize, open: false, seed + (uint)(arm * 8));

                // The way through. Cut the full depth of the block, which is a shade more than
                // the funnel outside reaches, so the road runs into the dark rather than stopping
                // short of it and leaving a seam at the mouth.
                var passage = upright
                    ? SKRect.Create(block.Left, half - throat, block.Width, throat * 2f)
                    : SKRect.Create(half - throat, block.Top, throat * 2f, block.Height);

                Tunnel(canvas, tileSize, passage, seed + (uint)arm);
            }
        }

        /// <summary>
        /// The dark under a gatehouse, with a lit lip along the arch's near edge so the passage
        /// reads as sunk into the block rather than painted onto it.
        /// </summary>
        private static void Tunnel(SKCanvas canvas, int tileSize, SKRect passage, uint seed)
        {
            if (passage.Width <= 0 || passage.Height <= 0)
                return;

            using var paint = new SKPaint { IsAntialias = false, Style = SKPaintStyle.Fill };

            paint.Color = Gateway;
            canvas.DrawRect(passage, paint);

            // Not flat black: there is a road down there, and a little of it catches.
            var grit = Math.Max(1f, tileSize / 22f);

            for (var y = passage.Top; y < passage.Bottom; y += grit)
            {
                for (var x = passage.Left; x < passage.Right; x += grit)
                {
                    var lift = RenderNoise.Hash01((int)(x / grit), (int)(y / grit), seed) * 0.22f;

                    paint.Color = RenderNoise.Shade(Gateway, lift);
                    canvas.DrawRect(SKRect.Create(x, y, grit, grit), paint);
                }
            }
        }

        /// <summary>
        /// A tower on each corner, standing proud of the curtain on both its outer sides. Towers
        /// were built projecting so the wall between them could be shot along, and that projection
        /// is what tells the eye these are towers and not thicker corners.
        /// </summary>
        private static void Towers(SKCanvas canvas, int tileSize, SKRect outer, float wall, uint seed)
        {
            var tower = Math.Max(2f, tileSize * TowerShare);
            var proud = wall * TowerProud;

            var left = outer.Left - proud;
            var top = outer.Top - proud;
            var right = outer.Right + proud - tower;
            var bottom = outer.Bottom + proud - tower;

            Tower(canvas, SKRect.Create(left, top, tower, tower), tileSize, open: true, seed);
            Tower(canvas, SKRect.Create(right, top, tower, tower), tileSize, open: true, seed + 1);
            Tower(canvas, SKRect.Create(left, bottom, tower, tower), tileSize, open: true, seed + 2);
            Tower(canvas, SKRect.Create(right, bottom, tower, tower), tileSize, open: true, seed + 3);
        }

        /// <summary>
        /// One tower: what it throws to the south-east, its top, and a lit edge on the side the
        /// light comes from.
        /// </summary>
        /// <param name="open">
        /// Whether to sink a darker square into the top. A corner tower is open to the sky with a
        /// walk round its rim; a gatehouse is roofed, and has a hole cut through it afterwards
        /// besides.
        /// </param>
        private static void Tower(SKCanvas canvas, SKRect at, int tileSize, bool open, uint seed)
        {
            if (at.Width <= 0 || at.Height <= 0)
                return;

            using var paint = new SKPaint { IsAntialias = false, Style = SKPaintStyle.Fill };

            var drop = Math.Max(1f, tileSize * 0.035f);

            paint.Color = Shadow;
            canvas.DrawRect(SKRect.Create(at.Left + drop, at.Top + drop, at.Width, at.Height), paint);

            // A tone of its own per tower, so four corners are four towers rather than one
            // stamped four times.
            var face = RenderNoise.Shade(TowerTop, (RenderNoise.Hash01(0, 0, seed) - 0.5f) * 0.12f);

            paint.Color = face;
            canvas.DrawRect(at, paint);

            var lip = Math.Max(1f, Math.Min(at.Width, at.Height) * 0.16f);

            if (at.Width <= lip * 2 || at.Height <= lip * 2)
                return;

            // Lit from the north-west, which is where the light comes from everywhere else on
            // this map.
            paint.Color = RenderNoise.Shade(face, 0.13f);
            canvas.DrawRect(SKRect.Create(at.Left, at.Top, at.Width, lip), paint);
            canvas.DrawRect(SKRect.Create(at.Left, at.Top, lip, at.Height), paint);

            paint.Color = RenderNoise.Shade(face, -0.18f);
            canvas.DrawRect(SKRect.Create(at.Left, at.Bottom - lip, at.Width, lip), paint);
            canvas.DrawRect(SKRect.Create(at.Right - lip, at.Top, lip, at.Height), paint);

            if (!open)
                return;

            var well = at;

            well.Inflate(-lip * 1.7f, -lip * 1.7f);

            if (well.Width <= 0 || well.Height <= 0)
                return;

            paint.Color = RenderNoise.Shade(face, -0.3f);
            canvas.DrawRect(well, paint);
        }

        /// <summary>
        /// The keep, standing in the bailey. Set off centre rather than in the middle: a keep went
        /// where the ground was best or against the back wall, and one squared up in the exact
        /// centre of every fort on the map is a stamp rather than a building.
        /// </summary>
        private static void Keep(SKCanvas canvas, int tileSize, SKRect outer, float wall, uint seed)
        {
            var bailey = outer;

            bailey.Inflate(-wall, -wall);

            var side = Math.Max(3f, tileSize * KeepShare);

            if (bailey.Width <= side || bailey.Height <= side)
                return;

            var hash = RenderNoise.Hash(0x4B, 0x33, seed);

            // Nudged within whatever room the bailey has left over, and never all the way into a
            // wall: a keep touching the curtain reads as a thicker piece of it.
            var slack = (bailey.Width - side) / 2f * 0.7f;

            var at = SKRect.Create(
                bailey.MidX - (side / 2f) + (slack * (((hash & 0xFF) / 127.5f) - 1f)),
                bailey.MidY - (side / 2f) + (slack * ((((hash >> 8) & 0xFF) / 127.5f) - 1f)),
                side,
                side);

            using var paint = new SKPaint { IsAntialias = false, Style = SKPaintStyle.Fill };

            var drop = Math.Max(1f, tileSize * 0.05f);

            paint.Color = Shadow;
            canvas.DrawRect(
                SKRect.Intersect(
                    SKRect.Create(at.Left + drop, at.Top + drop, at.Width, at.Height), bailey),
                paint);

            paint.Color = KeepFace;
            canvas.DrawRect(at, paint);

            var lip = Math.Max(1f, side * 0.15f);

            if (at.Width <= lip * 2 || at.Height <= lip * 2)
                return;

            // The parapet round the roof, lit on the side the light is on. What is actually seen
            // of a keep from above is its battlement and the roof down inside it.
            paint.Color = RenderNoise.Shade(KeepFace, 0.16f);
            canvas.DrawRect(SKRect.Create(at.Left, at.Top, at.Width, lip), paint);
            canvas.DrawRect(SKRect.Create(at.Left, at.Top, lip, at.Height), paint);

            paint.Color = RenderNoise.Shade(KeepFace, -0.2f);
            canvas.DrawRect(SKRect.Create(at.Left, at.Bottom - lip, at.Width, lip), paint);
            canvas.DrawRect(SKRect.Create(at.Right - lip, at.Top, lip, at.Height), paint);

            var roof = at;

            roof.Inflate(-lip * 1.6f, -lip * 1.6f);

            if (roof.Width <= 0 || roof.Height <= 0)
                return;

            paint.Color = RenderNoise.Shade(KeepFace, -0.34f);
            canvas.DrawRect(roof, paint);
        }
    }
}
