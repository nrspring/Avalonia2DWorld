using System;
using System.Threading.Tasks;
using NWorld.Map.Models;
using SkiaSharp;
using static NWorld.MapServices.MapRenderComponents.RenderNoise;

namespace NWorld.MapServices.MapRenderComponents.RenderingFunctions
{
    /// <summary>
    /// An oil seep: flat pools of it lying on the ground, near black, with the sheen that only
    /// oil has across their surface.
    /// <para>
    /// Flat and low where the other deposits stand up, because that is what a seep is and
    /// because the map needs the contrast: a black lump would be read as burnt ground or as
    /// shadow, and a black <em>pool</em> is unmistakable.
    /// </para>
    /// <para>
    /// The sheen is doing the work here. Near-black over dark ground -- bog, deep water,
    /// anything shaded -- is very nearly invisible, and the one thing that survives is the film
    /// of colour on top of it, so it is drawn strongest at the rim where the pool needs an edge
    /// most.
    /// </para>
    /// </summary>
    public static class RenderOil
    {
        private const uint Seed = 0x2A73F58Cu;

        /// <summary>Lobes round a pool's outline. Few: this has spread, not shattered.</summary>
        private const int Lobes = 4;

        /// <summary>Edge width, in radii. Soft -- a pool thins out rather than stopping.</summary>
        private const float Softness = 0.14f;

        private static readonly SKColor Crude = new(0x11, 0x0F, 0x16);
        private static readonly SKColor SheenViolet = new(0x53, 0x36, 0x7A);
        private static readonly SKColor SheenGreen = new(0x22, 0x5E, 0x55);
        private static readonly SKColor Glint = new(0xB9, 0xB2, 0xC8);

        private static readonly ResourceMarker Marker = new(
            new ResourceStyle(
                Seed: Seed,
                Pieces: 3,
                Radius: 0.19f,
                Spread: 0.15f,
                SizeJitter: 0.32f,

                // Barely there. A pool lying in the ground casts almost nothing, and what it
                // has is here to lift the black off dark ground rather than to model a solid.
                Shadow: new SKColor(0x08, 0x07, 0x0B),
                ShadowAlpha: 84),
            Pool);

        public static Task Render(TileRenderContext context) => Marker.Render(context);

        /// <inheritdoc cref="ResourceMarker.Prewarm"/>
        public static Task Prewarm(int tileSize) => Marker.Prewarm(tileSize);

        /// <inheritdoc cref="ResourceMarker.ClearCache"/>
        public static void ClearCache() => Marker.ClearCache();

        private static PieceSample Pool(float dx, float dy, int index)
        {
            // Flattened hard, which is what makes it lie in the ground rather than sit on it.
            var fy = dy * 2.1f;

            var distance = MathF.Sqrt(dx * dx + fy * fy);
            var angle = MathF.Atan2(fy, dx);

            var edge = 0.74f + 0.26f * PeriodicAngularNoise(angle, Lobes, Seed ^ (uint)index);
            var coverage = Smoothstep(edge + Softness, edge - Softness, distance);
            if (coverage <= 0f)
                return default;

            // The film: violet through green round the pool, and strongest towards the rim,
            // where the oil is thin. The middle stays as good as black.
            var band = PeriodicAngularNoise(angle, 3, Seed ^ 0x8811u);
            var sheen = LerpColor(SheenViolet, SheenGreen, band);
            var color = LerpColor(Crude, sheen, Smoothstep(0.30f, 0.95f, distance / edge) * 0.55f);

            // One highlight, up and left with the light. Small and hard: a specular on a liquid
            // is the difference between a wet surface and a hole cut in the ground.
            var toGlint = MathF.Sqrt((dx + 0.34f) * (dx + 0.34f) + (fy + 0.30f) * (fy + 0.30f));
            color = LerpColor(color, Glint, Smoothstep(0.30f, 0.05f, toGlint) * 0.5f);

            return new PieceSample(coverage, color);
        }
    }
}
