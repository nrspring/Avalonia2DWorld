using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia2DWorld.Map.Models;
using SkiaSharp;
using static Avalonia2DWorld.MapServices.MapRenderComponents.StandardRenderer.RoadNetwork;

namespace Avalonia2DWorld.MapServices.MapRenderComponents.StandardRenderer.RenderingFunctions
{
    /// <summary>
    /// A dirt track: trodden earth with a pair of ruts worn down it, a few stones kicked up out
    /// of it, and edges that crumble back into whatever ground it crosses.
    /// <para>
    /// The road's poor relation and joined to it in every way that matters. It is part of the
    /// same network -- see <see cref="RoadNetwork.Carries"/> -- so a path runs into a road, a
    /// bridge, a town or a fort gate exactly as a road would, and it is shaped, cached and
    /// blitted by the same <see cref="StoneSpan"/>. What is different is only what it is made of.
    /// </para>
    /// <para>
    /// Narrower than a road on purpose. The same width in brown would read as a road somebody
    /// forgot to pave; a track a little under it reads as a way people made by walking, and where
    /// one meets the other the step in width says which is which.
    /// </para>
    /// </summary>
    public static class RenderPath
    {
        /// <summary>
        /// How wide the track is, as a fraction of the tile. Narrower than
        /// <see cref="StoneWork.Way"/>; see the class remarks for why.
        /// </summary>
        private const float Way = 0.48f;

        /// <summary>
        /// Below this the ruts and stones are noise, and trodden earth on its own says path.
        /// </summary>
        private const int MinDetailTileSize = 12;

        /// <summary>
        /// Fixes the broad light and dark patches of the earth. The same for every variant, and
        /// that is deliberate: the field wraps at the tile edge, so with one seed for all of them
        /// a damp patch running off one tile carries on into the next whichever variant it is.
        /// </summary>
        private const uint MottleSeed = 0xD127A11u;

        /// <summary>
        /// Packed earth. Warmer and lighter than a town's streets, which are mud worked over by
        /// everyone who lives there; this is dry ground worn bare by feet and wheels passing
        /// through.
        /// </summary>
        private static readonly SKColor Earth = new(0x9A, 0x7A, 0x52);

        /// <summary>
        /// What turns up in the dirt: a few pale stones and as many dark clods. Pale ones
        /// catch the eye; the dark ones are what stop the pale ones looking like litter.
        /// <para>
        /// The clods are kept close to the earth and never drawn large. Anything that stands out
        /// on a tile marks it, and with only four variants of each shape a marked tile is how the
        /// eye finds the repeat along a straight.
        /// </para>
        /// </summary>
        private static readonly SKColor[] Stones =
        [
            new(0xB9, 0xAF, 0x9D),
            new(0xA8, 0x9F, 0x8E),
            new(0x80, 0x67, 0x48),
            new(0x88, 0x6E, 0x4D),
        ];

        /// <summary>How many of <see cref="Stones"/> are stones; the rest are clods.</summary>
        private const int PaleStones = 2;

        private static readonly StoneSpan Span = new(0xD127A7u, Draw);

        public static Task Render(TileRenderContext context) => Span.Render(context);

        /// <inheritdoc cref="StoneSpan.Prewarm"/>
        public static Task Prewarm(int tileSize) => Span.Prewarm(tileSize);

        /// <inheritdoc cref="StoneSpan.ClearCache"/>
        public static void ClearCache() => Span.ClearCache();

        /// <summary>Earth, the ruts and stones on it, then the verge worn back.</summary>
        private static void Draw(SKCanvas canvas, int tileSize, int arms, uint seed)
        {
            var half = tileSize / 2f;
            var reach = tileSize * Way / 2f;
            var spans = StoneWork.Spans(arms, half, reach);

            Dirt(canvas, tileSize, arms, spans, half, reach, seed);

            if (tileSize >= MinDetailTileSize)
                Scatter(canvas, tileSize, spans, seed);

            // Last, so a notch takes whatever was drawn in it -- a stone half in the grass is
            // fine, but one floating where the verge used to be is not.
            StoneWork.Verge(canvas, tileSize, arms, half, reach, seed);
        }

        /// <summary>
        /// Lays the earth in cells a couple of pixels across, mottled in broad patches, worn
        /// into ruts and crumbled away at its edges.
        /// <para>
        /// The crumbling is what makes it dirt rather than a brown road. A paved way has an edge
        /// somebody set; a track only has the place where people stopped treading, and the grass
        /// grows back into it a pixel here and there.
        /// </para>
        /// <para>
        /// One grid over the whole tile, measured from its corner, rather than one per span: the
        /// spans overlap in the middle, and two grids laid over each other there would draw every
        /// cell twice and disagree about which ones had crumbled.
        /// </para>
        /// </summary>
        private static void Dirt(
            SKCanvas canvas, int tileSize, int arms, List<SKRect> spans, float half, float reach, uint seed)
        {
            using var paint = new SKPaint { IsAntialias = false, Style = SKPaintStyle.Fill };

            var grit = Math.Max(1f, tileSize / 24f);
            var ruts = tileSize >= MinDetailTileSize;
            var gauge = reach * 0.42f;
            var rut = Math.Max(grit, tileSize / 20f) / 2f;
            var step = Math.Max(1f, tileSize / 12f);
            var cellY = 0;

            for (var y = 0f; y < tileSize; y += grit, cellY++)
            {
                var cellX = 0;

                for (var x = 0f; x < tileSize; x += grit, cellX++)
                {
                    var cx = x + (grit / 2f);
                    var cy = y + (grit / 2f);

                    if (Outside(spans, tileSize, cx, cy))
                        continue;

                    if (Edge(spans, tileSize, cx, cy, grit)
                        && RenderNoise.Hash01(cellX, cellY, seed ^ 0xC2B2AE35u) < 0.4f)
                    {
                        continue;
                    }

                    var mottle = RenderNoise.PeriodicFbm(cx / tileSize, cy / tileSize, 2, 3, MottleSeed);
                    var fleck = RenderNoise.Hash01(cellX, cellY, seed);

                    var lift = ((mottle - 0.5f) * 0.36f) + ((fleck - 0.5f) * 0.16f);

                    if (ruts && Rutted(arms, cx, cy, half, gauge, rut, tileSize, step, seed) is { } depth)
                        lift -= depth;

                    paint.Color = RenderNoise.Shade(Earth, lift);
                    canvas.DrawRect(SKRect.Create(x, y, grit, grit), paint);
                }
            }
        }

        /// <summary>
        /// Whether a cell sits on the outside of the track: a neighbour a cell away is off the
        /// track but still on the tile.
        /// <para>
        /// Still on the tile is the important half. An arm runs right up to the tile edge to meet
        /// the next one, and a cell there whose neighbour is over the edge is in the middle of
        /// the track, not at the side of it -- crumbling it would open a gap at every join.
        /// </para>
        /// </summary>
        private static bool Edge(List<SKRect> spans, int tileSize, float x, float y, float grit) =>
            Outside(spans, tileSize, x - grit, y)
            || Outside(spans, tileSize, x + grit, y)
            || Outside(spans, tileSize, x, y - grit)
            || Outside(spans, tileSize, x, y + grit);

        /// <inheritdoc cref="Edge"/>
        private static bool Outside(List<SKRect> spans, int tileSize, float x, float y)
        {
            if (x < 0 || y < 0 || x >= tileSize || y >= tileSize)
                return false;

            foreach (var rect in spans)
            {
                if (rect.Contains(x, y))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// How much darker a point is for lying in a rut, or null where it does not.
        /// <para>
        /// Two ruts down every arm, a wheel's width apart, from the tile edge in to just past
        /// the middle. Worn into the earth cell by cell rather than drawn over it as lines --
        /// drawn, they come out as a pair of rails, and what a rut actually is is the same dirt
        /// a shade deeper.
        /// </para>
        /// <para>
        /// Fixed across the arm rather than wandering, so the ruts on one tile meet the ruts on
        /// the next and a long track reads as one track. Their depth varies along it instead, in
        /// stretches: deep where the ground was soft, nearly gone where it was not. Where two
        /// arms meet the ruts cross, which is what a junction in a cart track looks like from
        /// above.
        /// </para>
        /// </summary>
        private static float? Rutted(
            int arms, float x, float y, float half, float gauge, float rut, int tileSize, float step, uint seed)
        {
            var reach = half + gauge + rut;

            foreach (var arm in StoneWork.Arms)
            {
                if ((arms & arm) == 0)
                    continue;

                // How far in from the tile edge this arm starts, and how far off its centre line.
                var (along, across) = arm switch
                {
                    North => (y, x),
                    South => (tileSize - y, x),
                    West => (x, y),
                    _ => (tileSize - x, y),
                };

                if (along > reach || MathF.Abs(MathF.Abs(across - half) - gauge) > rut)
                    continue;

                var stretch = (int)(along / step);
                var side = across < half ? 0 : 1;

                return 0.08f + (RenderNoise.Hash01(stretch + (arm * 31), side, seed ^ 0x27D4EB2Fu) * 0.1f);
            }

            return null;
        }

        /// <summary>
        /// Scatters a few stones and clods over the track, in proportion to how much of it there
        /// is on the tile. Each one lit from the north-west like everything else on the map, with
        /// a pixel of shadow on its south-east side once there is room for one.
        /// </summary>
        private static void Scatter(SKCanvas canvas, int tileSize, List<SKRect> spans, uint seed)
        {
            using var paint = new SKPaint { IsAntialias = false, Style = SKPaintStyle.Fill };

            var size = Math.Max(1f, tileSize / 36f);
            var area = (float)tileSize * tileSize;

            for (var index = 0; index < spans.Count; index++)
            {
                var rect = spans[index];
                var count = (int)MathF.Round(rect.Width * rect.Height / area * 12f);

                for (var i = 0; i < count; i++)
                {
                    var hash = RenderNoise.Hash(i, index, seed ^ 0x165667B1u);

                    var kind = (int)((hash >> 8) % (uint)Stones.Length);
                    var s = kind < PaleStones ? size * (1f + ((hash >> 28) & 1)) : size;
                    var x = rect.Left + (RenderNoise.Hash01(i, index + 16, seed) * (rect.Width - s));
                    var y = rect.Top + (RenderNoise.Hash01(i, index + 32, seed) * (rect.Height - s));

                    var stone = SKRect.Create(MathF.Floor(x), MathF.Floor(y), s, s);
                    var face = Stones[kind];

                    paint.Color = face;
                    canvas.DrawRect(stone, paint);

                    if (s < 2f)
                        continue;

                    var lip = Math.Max(1f, s / 3f);

                    paint.Color = RenderNoise.Shade(face, -0.3f);
                    canvas.DrawRect(SKRect.Create(stone.Left + lip, stone.Bottom, s, lip), paint);
                    canvas.DrawRect(SKRect.Create(stone.Right, stone.Top + lip, lip, s), paint);
                }
            }
        }
    }
}
