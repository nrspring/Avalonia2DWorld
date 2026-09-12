using System;
using System.Threading.Tasks;
using Avalonia2DWorld.Map.Models;
using SkiaSharp;
using static Avalonia2DWorld.MapServices.MapRenderComponents.StandardRenderer.RenderNoise;

namespace Avalonia2DWorld.MapServices.MapRenderComponents.StandardRenderer.RenderingFunctions
{
    /// <summary>
    /// A stone quarry: pale weathered boulders, rounded and squat.
    /// <para>
    /// Deliberately the plainest of the deposits. Stone is the one that will end up on the most
    /// tiles, so it is the one that must intrude least -- no accent colour, no strong edge, and
    /// nothing to notice on a map covered in it. It carries its own tone rather than the
    /// ground's for a related reason: pale grey is the one value that stays visible over grass,
    /// bog and sand alike.
    /// </para>
    /// <para>
    /// Rounded where <see cref="RenderIron"/> is faceted, which is what keeps the two apart on
    /// the same hillside: both are rock, and only the shape says which was broken open and
    /// which has been lying there.
    /// </para>
    /// </summary>
    public static class RenderStone
    {
        private const uint Seed = 0x58E2B419u;

        /// <summary>
        /// How square a boulder is. Three gives a rounded box -- corners without edges, which
        /// is what weathered stone comes to.
        /// </summary>
        private const float Squareness = 3f;

        /// <summary>Edge width, in radii. Wider than ore, because these corners are worn.</summary>
        private const float Softness = 0.10f;

        private static readonly SKColor Shadowed = new(0x45, 0x45, 0x43);
        private static readonly SKColor Lit = new(0xC4, 0xC4, 0xBA);

        private static readonly ResourceMarker Marker = new(
            new ResourceStyle(
                Seed: Seed,
                Pieces: 3,
                Radius: 0.17f,
                Spread: 0.19f,
                SizeJitter: 0.26f,
                Shadow: new SKColor(0x12, 0x12, 0x0E),
                ShadowAlpha: 130),
            Boulder);

        public static Task Render(TileRenderContext context) => Marker.Render(context);

        /// <inheritdoc cref="ResourceMarker.Prewarm"/>
        public static Task Prewarm(int tileSize) => Marker.Prewarm(tileSize);

        /// <inheritdoc cref="ResourceMarker.ClearCache"/>
        public static void ClearCache() => Marker.ClearCache();

        private static PieceSample Boulder(float dx, float dy, int index)
        {
            // Each boulder is turned a little way off the axis. Without it three rounded boxes
            // of the same proportion sit in a row like something manufactured; a few degrees
            // apart and they are three rocks.
            var turn = (Hash01(index, 0, Seed) - 0.5f) * 0.7f;
            var cos = MathF.Cos(turn);
            var sin = MathF.Sin(turn);
            var rx = dx * cos - dy * sin;

            // Squatter than it is wide, because a boulder seen from above and a little in
            // front is.
            var ry = (dx * sin + dy * cos) * 1.28f;

            var distance = MathF.Cbrt(
                MathF.Pow(MathF.Abs(rx), Squareness) + MathF.Pow(MathF.Abs(ry), Squareness));

            var edge = 0.80f + 0.14f * Hash01(index, 1, Seed);
            var coverage = Smoothstep(edge + Softness, edge - Softness, distance);
            if (coverage <= 0f)
                return default;

            // Lit from the upper left, as the ore is, so a hillside of both is lit one way.
            var tone = Clamp01(0.50f - dy * 0.52f - dx * 0.22f);
            var color = LerpColor(Shadowed, Lit, tone);

            // Grain, on a grid fixed to the boulder so it does not crawl between zoom levels.
            // Slight -- enough to stop the face reading as painted, not enough to be texture.
            var grain = Hash01((int)MathF.Floor(rx * 9f), (int)MathF.Floor(ry * 9f), Seed ^ 0x2C7Fu);
            color = Shade(color, (grain - 0.5f) * 0.14f);

            return new PieceSample(coverage, color);
        }
    }
}
