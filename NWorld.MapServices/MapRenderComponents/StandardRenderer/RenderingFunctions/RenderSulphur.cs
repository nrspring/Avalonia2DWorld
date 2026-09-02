using System;
using System.Threading.Tasks;
using NWorld.Map.Models;
using SkiaSharp;
using static NWorld.MapServices.MapRenderComponents.StandardRenderer.RenderNoise;

namespace NWorld.MapServices.MapRenderComponents.StandardRenderer.RenderingFunctions
{
    /// <summary>
    /// A sulphur deposit: crusted yellow patches, cracked and pale at their edges, the way it
    /// dries around a vent.
    /// <para>
    /// Crust rather than lumps, and the only deposit whose pieces are ragged all round: the
    /// ore and the boulders are objects lying on the ground and this is the ground itself gone
    /// wrong, so it is drawn low, wide and irregular, with its cracks showing.
    /// </para>
    /// <para>
    /// The yellow is the loudest colour any of these use, which is deliberate and is also why
    /// it is the only one broken up by cracks. Flat, it would read as a highlight -- the
    /// selected-tile mark is a yellow of much the same value -- and the broken surface is what
    /// keeps it a material instead.
    /// </para>
    /// </summary>
    public static class RenderSulphur
    {
        private const uint Seed = 0x6B2D9E31u;

        /// <summary>Lobes round the outline. Many: crust breaks up at its edge.</summary>
        private const int Lobes = 9;

        /// <summary>Edge width, in radii.</summary>
        private const float Softness = 0.10f;

        private static readonly SKColor Crack = new(0x5E, 0x46, 0x0E);
        private static readonly SKColor Body = new(0xC2, 0x99, 0x1C);
        private static readonly SKColor Bloom = new(0xF2, 0xE0, 0x6B);
        private static readonly SKColor Rim = new(0xF7, 0xF0, 0xB4);

        private static readonly ResourceMarker Marker = new(
            new ResourceStyle(
                Seed: Seed,
                Pieces: 3,
                Radius: 0.18f,
                Spread: 0.17f,
                SizeJitter: 0.30f,
                Shadow: new SKColor(0x2A, 0x1C, 0x06),
                ShadowAlpha: 104),
            Crust);

        public static Task Render(TileRenderContext context) => Marker.Render(context);

        /// <inheritdoc cref="ResourceMarker.Prewarm"/>
        public static Task Prewarm(int tileSize) => Marker.Prewarm(tileSize);

        /// <inheritdoc cref="ResourceMarker.ClearCache"/>
        public static void ClearCache() => Marker.ClearCache();

        private static PieceSample Crust(float dx, float dy, int index)
        {
            var fy = dy * 1.55f;

            var distance = MathF.Sqrt(dx * dx + fy * fy);
            var angle = MathF.Atan2(fy, dx);

            var edge = 0.66f + 0.34f * PeriodicAngularNoise(angle, Lobes, Seed ^ (uint)index);
            var coverage = Smoothstep(edge + Softness, edge - Softness, distance);
            if (coverage <= 0f)
                return default;

            // Blotches on a grid fixed to the patch, so the crust is uneven rather than a
            // painted shape, and stays put between zoom levels.
            var bloom = Hash01((int)MathF.Floor(dx * 8f), (int)MathF.Floor(fy * 8f), Seed ^ 0x4D19u);
            var color = LerpColor(Body, Bloom, Smoothstep(0.35f, 0.9f, bloom));

            // Cracks: a second field, thresholded low so only the darkest of it shows, which
            // gives lines through the patch rather than a second set of blotches.
            var crack = Hash01((int)MathF.Floor(dx * 5f), (int)MathF.Floor(fy * 11f), Seed ^ 0x91C4u);
            color = LerpColor(color, Crack, Smoothstep(0.22f, 0.02f, crack) * 0.8f);

            // A pale rim, where the crust has dried out furthest. It is also what gives the
            // patch an edge over ground of a similar brightness -- sand, above all.
            color = LerpColor(color, Rim, Smoothstep(0.72f, 1f, distance / edge) * 0.6f);

            return new PieceSample(coverage, color);
        }
    }
}
