using System;
using System.Threading.Tasks;
using NWorld.Map.Models;
using SkiaSharp;
using static NWorld.MapServices.MapRenderComponents.StandardRenderer.RenderNoise;

namespace NWorld.MapServices.MapRenderComponents.StandardRenderer.RenderingFunctions
{
    /// <summary>
    /// Wind-rippled sand.
    /// <para>
    /// Built as one tiling texture rather than a pool of per-tile variants, because the ripples
    /// are the point and ripples run in long parallel lines that have to cross tile boundaries
    /// -- see <see cref="TiledGround"/>. Sand is also about as uniform as ground gets, so there
    /// is little for the eye to latch on to and find the block's period with.
    /// </para>
    /// </summary>
    public static class RenderDesert
    {
        private const uint Seed = 0x9C41E70Du;

        private static readonly SKColor SandDark = new(0xA6, 0x86, 0x52);
        private static readonly SKColor SandMid = new(0xD3, 0xB4, 0x78);
        private static readonly SKColor SandLight = new(0xEC, 0xD8, 0xA4);
        private static readonly SKColor Pebble = new(0x8C, 0x76, 0x55);

        /// <summary>
        /// Ripple wavelength in pixels, at the block size the texture is built at. Rounded to a
        /// whole number of cycles across the block, because a fractional one would not meet
        /// itself where the texture repeats.
        /// </summary>
        private const int RippleWavelength = 17;

        /// <summary>
        /// How far the ripple lines wander, in cycles, at two scales.
        /// <para>
        /// Both are needed and neither alone will do. The slow warp bends whole stretches of
        /// ripple, which curves the bands but leaves them just as parallel; the quick one
        /// breaks them and makes them fork. Without enough of the second the field reads as
        /// corduroy -- straight, evenly spaced, unmistakably woven rather than blown.
        /// </para>
        /// </summary>
        private const float SlowWander = 1.7f;
        private const float QuickWander = 0.45f;

        private static readonly TiledGround Ground = new(
            Paint,
            new MapOverlayField(static (u, v) =>
            {
                // Broad dunes: where the sand piles up it catches more light, and the hollows
                // between are cooler and darker.
                var n = PeriodicFbm(u, v, 7, 4, Seed ^ 0x51A9u);
                var level = MapOverlayField.Level(n, 40f);

                return new SKColor(
                    (byte)Clamp255(level + 7f),
                    (byte)Clamp255(level + 1f),
                    (byte)Clamp255(level - 9f));
            }));

        public static Task Render(TileRenderContext context) => Ground.Render(context);

        /// <inheritdoc cref="TiledGround.Prewarm"/>
        public static Task Prewarm(int tileSize) => Ground.Prewarm(tileSize);

        /// <inheritdoc cref="TiledGround.ClearCache"/>
        public static void ClearCache() => Ground.ClearCache();

        private static void Paint(byte[] pixels, int edge, int tilesPerBlock)
        {
            // Whole cycles across the block, so the ripples meet where the texture repeats.
            var acrossX = Math.Max(1, (int)MathF.Round((float)edge / RippleWavelength * 0.32f));
            var acrossY = Math.Max(1, (int)MathF.Round((float)edge / RippleWavelength * 0.94f));

            var scale = 1f / edge;

            Parallel.For(0, edge, py =>
            {
                var v = py * scale;
                var i = py * edge * 4;

                for (var px = 0; px < edge; px++)
                {
                    var u = px * scale;

                    // The ripple phase is pushed about at two scales, which is what turns
                    // straight bands into the wandering, forking lines sand actually makes.
                    // Both warps are periodic, so the whole thing still tiles.
                    var slow = PeriodicFbm(u, v, 3, 2, Seed ^ 0x2C7Bu) - 0.5f;
                    var quick = PeriodicFbm(u, v, 11, 3, Seed ^ 0x8A03u) - 0.5f;
                    var phase = MathF.Tau *
                        (acrossX * u + acrossY * v + slow * SlowWander + quick * QuickWander);

                    // Ripples do not cover a desert evenly. They build where the wind has
                    // worked and fade out to smooth sand elsewhere, and letting them come and
                    // go does more to break up the regularity than any amount of warping.
                    var strength = Smoothstep(0.30f, 0.78f, PeriodicFbm(u, v, 5, 2, Seed ^ 0x4E62u));
                    var lit = Clamp01(0.5f + 0.5f * MathF.Sin(phase) * strength);

                    var color = lit < 0.5f
                        ? LerpColor(SandDark, SandMid, lit * 2f)
                        : LerpColor(SandMid, SandLight, (lit - 0.5f) * 2f);

                    // Grain. Sand is grains, and without this it reads as painted plaster.
                    color = Shade(color, (Hash01(px, py, Seed ^ 0x11u) - 0.5f) * 0.15f);

                    // The odd darker pebble, sparse enough to be a detail rather than a texture.
                    if (Hash01(px, py, Seed ^ 0xBEEFu) > 0.9985f)
                        color = Pebble;

                    pixels[i++] = color.Red;
                    pixels[i++] = color.Green;
                    pixels[i++] = color.Blue;
                    pixels[i++] = 0xFF;
                }
            });
        }
    }
}
