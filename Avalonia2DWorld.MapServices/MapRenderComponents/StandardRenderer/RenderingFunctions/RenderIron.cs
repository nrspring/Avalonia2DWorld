using System;
using System.Threading.Tasks;
using Avalonia2DWorld.Map.Models;
using SkiaSharp;
using static Avalonia2DWorld.MapServices.MapRenderComponents.StandardRenderer.RenderNoise;

namespace Avalonia2DWorld.MapServices.MapRenderComponents.StandardRenderer.RenderingFunctions
{
    /// <summary>
    /// An iron deposit: a scatter of angular ore chunks, cold grey rock with rust bleeding out
    /// of it.
    /// <para>
    /// Faceted rather than rounded, which is what tells it apart from <see cref="RenderStone"/>
    /// at a glance -- the two are both rock on the same ground and the palette alone would not
    /// do it, so ore is drawn as freshly broken and stone as long weathered. The rust is the
    /// other half of that: it is the only warm colour in either, and it is what says
    /// <em>metal</em> rather than merely dark stone.
    /// </para>
    /// </summary>
    public static class RenderIron
    {
        private const uint Seed = 0x1F6C4A2Du;

        /// <summary>Sides to a chunk. Few enough that each one is a face rather than a curve.</summary>
        private const int Facets = 5;

        /// <summary>Edge width, in radii. Narrow: broken rock has a hard edge.</summary>
        private const float Softness = 0.08f;

        private static readonly SKColor Shadowed = new(0x1B, 0x1E, 0x24);
        private static readonly SKColor Lit = new(0x8A, 0x92, 0x9E);
        private static readonly SKColor Rust = new(0x8E, 0x49, 0x1D);

        private static readonly ResourceMarker Marker = new(
            new ResourceStyle(
                Seed: Seed,
                Pieces: 4,
                Radius: 0.15f,
                Spread: 0.20f,
                SizeJitter: 0.28f,
                Shadow: new SKColor(0x10, 0x0E, 0x0A),
                ShadowAlpha: 132),
            Chunk);

        public static Task Render(TileRenderContext context) => Marker.Render(context);

        /// <inheritdoc cref="ResourceMarker.Prewarm"/>
        public static Task Prewarm(int tileSize) => Marker.Prewarm(tileSize);

        /// <inheritdoc cref="ResourceMarker.ClearCache"/>
        public static void ClearCache() => Marker.ClearCache();

        private static PieceSample Chunk(float dx, float dy, int index)
        {
            var distance = MathF.Sqrt(dx * dx + dy * dy);
            var angle = MathF.Atan2(dy, dx);

            // The facet radius is taken from the sector the angle falls in and not
            // interpolated between neighbours, so the outline steps at each boundary. That
            // step is the whole shape: interpolating here, as the softer materials do, gives
            // a pebble instead of something broken off a seam.
            var facet = Wrap((int)MathF.Floor(angle * (Facets / MathF.Tau)), Facets);
            var edge = 0.72f + 0.28f * Hash01(facet, index, Seed);

            var coverage = Smoothstep(edge + Softness, edge - Softness, distance);
            if (coverage <= 0f)
                return default;

            // Lit from the upper left, as the highlights are. Nothing else in the scene is,
            // but every piece here agrees with every other, and it is that agreement rather
            // than any particular direction that makes them read as solid.
            var tone = Clamp01(0.52f - dy * 0.42f - dx * 0.26f);
            var color = LerpColor(Shadowed, Lit, tone);

            // Rust in patches on a grid fixed to the chunk, so it scales with the piece and
            // stays put from one zoom level to the next rather than crawling over the rock.
            var patch = Hash01((int)MathF.Floor(dx * 6f), (int)MathF.Floor(dy * 6f), Seed ^ 0x77B1u);
            color = LerpColor(color, Rust, Smoothstep(0.62f, 0.95f, patch) * 0.7f);

            // A dark rim, so two chunks that overlap still read as two.
            color = Shade(color, -0.35f * Smoothstep(edge - 0.34f, edge, distance));

            return new PieceSample(coverage, color);
        }
    }
}
