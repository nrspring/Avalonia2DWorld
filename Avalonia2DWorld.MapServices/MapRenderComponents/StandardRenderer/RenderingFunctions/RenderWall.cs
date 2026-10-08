using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia2DWorld.Map.Models;
using SkiaSharp;
using static Avalonia2DWorld.MapServices.MapRenderComponents.StandardRenderer.RoadNetwork;

namespace Avalonia2DWorld.MapServices.MapRenderComponents.StandardRenderer.RenderingFunctions
{
    /// <summary>
    /// A stone wall: a run of curtain with a walk along the top, battlements down both sides of
    /// it, and a shadow thrown off to the south-east.
    /// <para>
    /// Laid a tile at a time and joined up the way a road is -- the same sixteen shapes, read off
    /// the neighbours at draw time by the same <see cref="StoneSpan"/> -- but on a network of its
    /// own. A wall joins only more wall (see <see cref="RoadNetwork.WallMask"/>): a road running
    /// up to one is a road meeting a wall, not a length of wall, and a wall that bent itself
    /// towards every road beside it would never run straight.
    /// </para>
    /// <para>
    /// The fort's stone, deliberately -- see <see cref="RenderFort"/>. A length of wall in the
    /// field is the same work as the curtain round a fort, and laid beside one it should read as
    /// having been built by the same hands.
    /// </para>
    /// <para>
    /// Drawn standing up rather than as a stripe. The walk along the top is the footprint pushed
    /// a little towards the north-west, so the face shows as a dark band down the south and east
    /// sides and the shadow falls past it, which is what lifts a wall off the ground it stands on
    /// at every zoom this is drawn at. Only the walk moves across an arm and never along it, or
    /// the top of one tile would stop a little short of the top of the next.
    /// </para>
    /// </summary>
    public static class RenderWall
    {
        /// <summary>
        /// How thick the wall is, as a fraction of the tile. Thicker than a fort's curtain,
        /// which has towers and a keep to say what it is: a wall out on its own has only its
        /// thickness, and a thin one reads as a fence.
        /// </summary>
        private const float Thickness = 0.22f;

        /// <summary>How much of the face shows below the walk, as a share of the thickness.</summary>
        private const float FaceShare = 0.32f;

        /// <summary>How far the shadow falls, as a fraction of the tile.</summary>
        private const float CastShare = 0.06f;

        /// <summary>
        /// How long a stone of the walk is, in wall thicknesses -- longer than it is wide, as
        /// dressed coping is.
        /// </summary>
        private const float BlockShare = 1.3f;

        /// <summary>How deep a merlon is across the walk, as a share of the thickness.</summary>
        private const float MerlonShare = 0.24f;

        /// <summary>Below this the walk is one face, without its joints.</summary>
        private const int MinCourseTileSize = 16;

        /// <summary>Below this the battlements are noise, and the walk is left plain.</summary>
        private const int MinMerlonTileSize = 22;

        private static readonly StoneSpan Span = new(0x3A11C0u, Draw, mask: WallMask);

        public static Task Render(TileRenderContext context) => Span.Render(context);

        /// <inheritdoc cref="StoneSpan.Prewarm"/>
        public static Task Prewarm(int tileSize) => Span.Prewarm(tileSize);

        /// <inheritdoc cref="StoneSpan.ClearCache"/>
        public static void ClearCache() => Span.ClearCache();

        /// <summary>Shadow, then the face, then the walk and its battlements on top.</summary>
        private static void Draw(SKCanvas canvas, int tileSize, int arms, uint seed)
        {
            var half = tileSize / 2f;
            var reach = Math.Max(1f, tileSize * Thickness / 2f);
            var face = Math.Max(1f, reach * 2 * FaceShare);
            var cast = Math.Max(1f, tileSize * CastShare);

            using var paint = new SKPaint { IsAntialias = false, Style = SKPaintStyle.Fill };

            // One path rather than a rect per piece, because the shadow is translucent and the
            // pieces overlap where the arms meet: drawn separately, every junction would come out
            // as a darker square.
            using (var shadow = new SKPath { FillType = SKPathFillType.Winding })
            {
                foreach (var piece in Footprint(arms, tileSize, half, reach, cast))
                    shadow.AddRect(piece.Rect);

                paint.Color = RenderFort.Shadow;
                canvas.DrawPath(shadow, paint);
            }

            paint.Color = RenderFort.WallFace;

            foreach (var piece in Footprint(arms, tileSize, half, reach, 0))
                canvas.DrawRect(piece.Rect, paint);

            var walk = Footprint(arms, tileSize, half, reach, -face);

            Walk(canvas, tileSize, walk, reach, seed);

            if (tileSize >= MinMerlonTileSize)
                Battlements(canvas, tileSize, arms, walk, reach, seed);
        }

        /// <summary>One rectangle of a wall's outline, and which arm it is -- 0 for the middle.</summary>
        private readonly record struct Piece(SKRect Rect, int Arm);

        /// <summary>
        /// The rectangles a set of arms covers: the middle square, and each arm from the tile
        /// edge in to it, moved <paramref name="shift"/> towards the south-east.
        /// <para>
        /// The middle moves both ways; an arm moves only across itself and still runs right to
        /// the tile edge. That is what keeps a shadow or a walk continuous from one tile into the
        /// next -- an arm moved along its own length would leave a gap at one end of every tile
        /// that its neighbour, clipped at its own edge, could never fill.
        /// </para>
        /// </summary>
        private static List<Piece> Footprint(int arms, int tileSize, float half, float reach, float shift)
        {
            var near = half - reach + shift;
            var far = half + reach + shift;
            var width = reach * 2;

            var pieces = new List<Piece>(5) { new(SKRect.Create(near, near, width, width), 0) };

            if ((arms & North) != 0) pieces.Add(new(new SKRect(near, 0, far, near), North));
            if ((arms & South) != 0) pieces.Add(new(new SKRect(near, far, far, tileSize), South));
            if ((arms & West) != 0) pieces.Add(new(new SKRect(0, near, near, far), West));
            if ((arms & East) != 0) pieces.Add(new(new SKRect(far, near, tileSize, far), East));

            return pieces;
        }

        /// <summary>
        /// The length of a stone along the walk: close to <see cref="BlockShare"/> thicknesses,
        /// but an exact division of the tile, so the joints on one tile carry on at the same
        /// spacing into the next.
        /// </summary>
        private static float Step(int tileSize, float reach)
        {
            var wanted = Math.Max(1f, reach * 2 * BlockShare);
            var count = Math.Max(1, (int)MathF.Round(tileSize / wanted));

            return tileSize / (float)count;
        }

        /// <summary>
        /// Lays the walk along the top in coping stones, each a shade of its own, with the joint
        /// between them drawn once there is room for it.
        /// <para>
        /// Measured from the tile edge rather than from the middle, at a spacing that divides
        /// the tile exactly, so the stones of a straight run on across the join at the same
        /// spacing and nothing marks where one tile ended.
        /// </para>
        /// </summary>
        private static void Walk(SKCanvas canvas, int tileSize, List<Piece> walk, float reach, uint seed)
        {
            using var paint = new SKPaint { IsAntialias = false, Style = SKPaintStyle.Fill };

            var step = Step(tileSize, reach);
            var joints = tileSize >= MinCourseTileSize;

            foreach (var (rect, arm) in walk)
            {
                if (arm == 0)
                {
                    paint.Color = RenderNoise.Shade(RenderFort.WallTop, (RenderNoise.Hash01(0, 0, seed) - 0.5f) * 0.12f);
                    canvas.DrawRect(rect, paint);
                    continue;
                }

                var upright = arm is North or South;
                var length = upright ? rect.Height : rect.Width;

                for (var k = 0; k * step < length; k++)
                {
                    var from = k * step;
                    var to = Math.Min(length, from + step);

                    // From the tile edge inwards, whichever edge this arm runs to.
                    var stone = arm switch
                    {
                        North => new SKRect(rect.Left, from, rect.Right, to),
                        South => new SKRect(rect.Left, tileSize - to, rect.Right, tileSize - from),
                        West => new SKRect(from, rect.Top, to, rect.Bottom),
                        _ => new SKRect(tileSize - to, rect.Top, tileSize - from, rect.Bottom),
                    };

                    paint.Color = RenderNoise.Shade(
                        RenderFort.WallTop, (RenderNoise.Hash01(k, arm, seed) - 0.5f) * 0.14f);

                    canvas.DrawRect(stone, paint);

                    if (!joints || k == 0)
                        continue;

                    // The joint on the tile-edge side of the stone, so the edge itself never
                    // carries one: two tiles meeting would otherwise draw it twice, a pixel apart.
                    paint.Color = RenderNoise.Shade(RenderFort.WallTop, -0.22f);

                    canvas.DrawRect(
                        arm switch
                        {
                            North => SKRect.Create(stone.Left, stone.Top, stone.Width, 1f),
                            South => SKRect.Create(stone.Left, stone.Bottom - 1f, stone.Width, 1f),
                            West => SKRect.Create(stone.Left, stone.Top, 1f, stone.Height),
                            _ => SKRect.Create(stone.Right - 1f, stone.Top, 1f, stone.Height),
                        },
                        paint);
                }
            }
        }

        /// <summary>
        /// Puts the battlements along both sides of the walk: merlons and crenels in turn, half a
        /// stone each.
        /// <para>
        /// Round the outline rather than down every arm. Each arm gets them along its two long
        /// sides from the tile edge in to the middle, and the middle square gets them only on the
        /// sides no arm leaves from -- so a corner is battlemented round its outside and a
        /// junction is open where the walks meet, rather than a wall of merlons standing across
        /// the way.
        /// </para>
        /// </summary>
        private static void Battlements(
            SKCanvas canvas, int tileSize, int arms, List<Piece> walk, float reach, uint seed)
        {
            using var paint = new SKPaint { IsAntialias = false, Style = SKPaintStyle.Fill };

            var merlon = Math.Max(1f, reach * 2 * MerlonShare);
            var tooth = Step(tileSize, reach) / 2f;
            var lit = RenderNoise.Shade(RenderFort.WallTop, 0.12f);
            var under = RenderNoise.Shade(RenderFort.WallTop, -0.3f);
            var gap = RenderNoise.Shade(RenderFort.WallFace, 0.2f);

            foreach (var (rect, arm) in walk)
            {
                if (arm == 0)
                {
                    // The middle's own sides, wherever no arm carries the walk on through them.
                    if ((arms & North) == 0) Teeth(SKRect.Create(rect.Left, rect.Top, rect.Width, merlon), false, false, 0);
                    if ((arms & South) == 0) Teeth(SKRect.Create(rect.Left, rect.Bottom - merlon, rect.Width, merlon), false, false, 1);
                    if ((arms & West) == 0) Teeth(SKRect.Create(rect.Left, rect.Top, merlon, rect.Height), true, false, 2);
                    if ((arms & East) == 0) Teeth(SKRect.Create(rect.Right - merlon, rect.Top, merlon, rect.Height), true, false, 3);
                    continue;
                }

                var upright = arm is North or South;
                var backwards = arm is South or East;

                if (upright)
                {
                    Teeth(SKRect.Create(rect.Left, rect.Top, merlon, rect.Height), true, backwards, arm);
                    Teeth(SKRect.Create(rect.Right - merlon, rect.Top, merlon, rect.Height), true, backwards, arm + 16);
                }
                else
                {
                    Teeth(SKRect.Create(rect.Left, rect.Top, rect.Width, merlon), false, backwards, arm);
                    Teeth(SKRect.Create(rect.Left, rect.Bottom - merlon, rect.Width, merlon), false, backwards, arm + 16);
                }
            }

            // One strip of merlons, counted from whichever end meets the tile edge so the rhythm
            // runs on into the next tile. A merlon is lit on top with a dark line on its
            // south-east side, the shadow of a block standing up off the walk; the crenel
            // between two is cut dark, because a gap lit the same as the walk behind it reads
            // as a pale stone rather than as a hole in the parapet.
            void Teeth(SKRect strip, bool upright, bool backwards, int salt)
            {
                var length = upright ? strip.Height : strip.Width;

                for (var k = 0; k * tooth < length; k++)
                {
                    var from = k * tooth;
                    var to = Math.Min(length, from + tooth);

                    var block = (upright, backwards) switch
                    {
                        (true, false) => new SKRect(strip.Left, strip.Top + from, strip.Right, strip.Top + to),
                        (true, true) => new SKRect(strip.Left, strip.Bottom - to, strip.Right, strip.Bottom - from),
                        (false, false) => new SKRect(strip.Left + from, strip.Top, strip.Left + to, strip.Bottom),
                        _ => new SKRect(strip.Right - to, strip.Top, strip.Right - from, strip.Bottom),
                    };

                    if (k % 2 == 1)
                    {
                        paint.Color = gap;
                        canvas.DrawRect(block, paint);
                        continue;
                    }

                    paint.Color = RenderNoise.Shade(lit, (RenderNoise.Hash01(k, salt, seed) - 0.5f) * 0.1f);
                    canvas.DrawRect(block, paint);

                    if (block.Width < 2f || block.Height < 2f)
                        continue;

                    paint.Color = under;
                    canvas.DrawRect(SKRect.Create(block.Left, block.Bottom - 1f, block.Width, 1f), paint);
                    canvas.DrawRect(SKRect.Create(block.Right - 1f, block.Top, 1f, block.Height), paint);
                }
            }
        }
    }
}
