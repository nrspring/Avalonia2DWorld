using System;
using System.Threading.Tasks;
using NWorld.Map.Models;
using SkiaSharp;
using static NWorld.MapServices.MapRenderComponents.StandardRenderer.RenderNoise;

namespace NWorld.MapServices.MapRenderComponents.StandardRenderer.RenderingFunctions
{
    /// <summary>
    /// A stand of timber: a few conifers, tiered canopies over short trunks.
    /// <para>
    /// The only one of the deposits that stands up rather than lying on the ground, and it is
    /// drawn to make the most of that -- a silhouette with a top and a bottom is recognisable
    /// at sizes where colour alone has stopped working, and no other resource can be mistaken
    /// for it however small the tiles get.
    /// </para>
    /// <para>
    /// Conifers rather than round crowns because the tiers survive the shrinking: a stepped
    /// triangle keeps its outline down to a handful of pixels, where a broadleaf canopy becomes
    /// the same green blob as everything else standing in a field.
    /// </para>
    /// </summary>
    public static class RenderWood
    {
        private const uint Seed = 0x3D91C7A5u;

        /// <summary>How much of the tree, top to bottom, is canopy rather than trunk.</summary>
        private const float CanopyBase = 0.82f;

        /// <summary>Tiers in a canopy. What gives the silhouette its steps.</summary>
        private const float Tiers = 3f;

        /// <summary>Edge width, in radii.</summary>
        private const float Softness = 0.07f;

        private static readonly SKColor NeedleShade = new(0x1C, 0x33, 0x1B);
        private static readonly SKColor NeedleLit = new(0x4E, 0x78, 0x38);
        private static readonly SKColor Bark = new(0x3A, 0x2A, 0x1B);

        private static readonly ResourceMarker Marker = new(
            new ResourceStyle(
                Seed: Seed,
                Pieces: 3,
                Radius: 0.24f,
                Spread: 0.18f,
                SizeJitter: 0.20f,
                Shadow: new SKColor(0x0C, 0x10, 0x08),
                ShadowAlpha: 120),
            Tree);

        public static Task Render(TileRenderContext context) => Marker.Render(context);

        /// <inheritdoc cref="ResourceMarker.Prewarm"/>
        public static Task Prewarm(int tileSize) => Marker.Prewarm(tileSize);

        /// <inheritdoc cref="ResourceMarker.ClearCache"/>
        public static void ClearCache() => Marker.ClearCache();

        /// <summary>
        /// One tree, standing in the box from dy -1 at its tip to dy 1 at its foot. Height and
        /// breadth vary with the tree's index, so a stand is three trees rather than one drawn
        /// three times.
        /// </summary>
        private static PieceSample Tree(float dx, float dy, int index)
        {
            var height = 0.82f + 0.36f * Hash01(index, 0, Seed);
            var breadth = 0.86f + 0.28f * Hash01(index, 1, Seed);

            // Foot fixed and the tip moved instead, so a short tree is a short tree and not one
            // hovering above the ground its neighbours are standing on.
            var top = 1f - 2f * height;
            if (dy < top || dy > 1f)
                return default;

            var down = (dy - top) / (1f - top);

            if (down <= CanopyBase)
            {
                var along = down / CanopyBase;

                // Widening towards the foot, with a sawtooth over it: each tier reaches its
                // widest just before the next begins, which is the step a conifer's branches
                // make. The tip is left with a little width of its own so it does not come to
                // a point too fine for the pixels to hold.
                var tier = along * Tiers;
                var half = breadth * (0.10f + 0.46f * along) * (0.74f + 0.26f * (tier - MathF.Floor(tier)));

                var coverage = Smoothstep(half + Softness, half - Softness, MathF.Abs(dx));
                if (coverage <= 0f)
                    return default;

                // Lit from the left and from above, the same corner the other deposits take
                // their light from, with the underside of each tier left dark.
                var tone = Clamp01(0.46f - dx * 0.85f + (1f - along) * 0.22f);
                return new PieceSample(coverage, LerpColor(NeedleShade, NeedleLit, tone));
            }

            var trunk = 0.07f * breadth;
            var trunkCoverage = Smoothstep(trunk + Softness * 0.5f, trunk - Softness * 0.5f, MathF.Abs(dx));

            return trunkCoverage <= 0f
                ? default
                : new PieceSample(trunkCoverage, Shade(Bark, -dx * 0.5f));
        }
    }
}
