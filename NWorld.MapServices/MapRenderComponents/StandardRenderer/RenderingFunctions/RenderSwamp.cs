using System;
using System.Threading.Tasks;
using NWorld.Map.Models;
using SkiaSharp;
using static NWorld.MapServices.MapRenderComponents.StandardRenderer.RenderNoise;

namespace NWorld.MapServices.MapRenderComponents.StandardRenderer.RenderingFunctions
{
    /// <summary>
    /// Boggy ground: standing water in the hollows, matted vegetation over the rest, scum on
    /// the surface and dead reeds through it.
    /// <para>
    /// Tiling rather than a pool of variants -- see <see cref="TiledGround"/> -- because the
    /// pools are the character of it, and a pool that stopped at a tile edge would look like a
    /// puddle in a box. Everything here is kept small and numerous for the reason that
    /// arrangement demands: a feature approaching the size of the block is what lets the eye
    /// find where the texture repeats, so all the broad variation is left to the overlay field.
    /// </para>
    /// </summary>
    public static class RenderSwamp
    {
        private const uint Seed = 0x47B3D2A1u;

        private static readonly SKColor Peat = new(0x2E, 0x30, 0x1E);
        private static readonly SKColor Moss = new(0x46, 0x55, 0x2C);
        private static readonly SKColor DryGrass = new(0x6B, 0x6A, 0x3B);
        private static readonly SKColor Water = new(0x1E, 0x2E, 0x2A);
        private static readonly SKColor WaterSheen = new(0x4C, 0x6B, 0x5E);
        private static readonly SKColor Scum = new(0x62, 0x7A, 0x33);
        private static readonly SKColor Reed = new(0x36, 0x33, 0x22);

        private static readonly TiledGround Ground = new(
            Paint,
            new MapOverlayField(static (u, v) =>
            {
                // Whether a stretch of bog is wetter or drier, over distances far larger than
                // the texture block.
                var n = PeriodicFbm(u, v, 8, 4, Seed ^ 0x63C2u);
                var level = MapOverlayField.Level(n, 38f);

                return new SKColor(
                    (byte)Clamp255(level - 8f),
                    (byte)Clamp255(level + 2f),
                    (byte)Clamp255(level - 4f));
            }));

        public static Task Render(TileRenderContext context) => Ground.Render(context);

        /// <inheritdoc cref="TiledGround.Prewarm"/>
        public static Task Prewarm(int tileSize) => Ground.Prewarm(tileSize);

        /// <inheritdoc cref="TiledGround.ClearCache"/>
        public static void ClearCache() => Ground.ClearCache();

        private static void Paint(byte[] pixels, int edge, int tilesPerBlock)
        {
            var scale = 1f / edge;

            // Lattices are in cells across the block, so a block spanning more tiles gets
            // proportionally more of everything and the features stay the same size on screen
            // whatever the zoom. Pools are deliberately several tiles across: at one cell per
            // tile they came out the same size as the vegetation mottle, and a scene with
            // everything at one scale reads as camouflage rather than as ground with things
            // on it.
            var poolLattice = Math.Max(2, tilesPerBlock / 3);
            var mottleLattice = Math.Max(3, tilesPerBlock * 2);

            Parallel.For(0, edge, py =>
            {
                var v = py * scale;
                var i = py * edge * 4;

                for (var px = 0; px < edge; px++)
                {
                    var u = px * scale;

                    var wet = PeriodicFbm(u, v, poolLattice, 4, Seed ^ 0x1A5Fu);
                    var mottle = PeriodicFbm(u, v, mottleLattice, 3, Seed ^ 0x9E11u);

                    // The bank: peat under matted moss and dead grass.
                    var bank = LerpColor(Peat, Moss, Smoothstep(0.30f, 0.75f, mottle));
                    bank = LerpColor(bank, DryGrass, Smoothstep(0.72f, 0.98f, mottle) * 0.7f);

                    // The water, with a little sheen where the sky catches it.
                    var pool = LerpColor(Water, WaterSheen, Smoothstep(0.55f, 0.95f, mottle) * 0.5f);

                    // A soft edge between them, because a bog has no shoreline -- it just gets
                    // wetter until it is water. Narrow enough that a pool still reads as a
                    // pool, wide enough that it never looks cut out.
                    var flooded = Smoothstep(0.44f, 0.57f, wet);
                    var color = LerpColor(bank, pool, flooded);

                    // Scum gathers on standing water and at its margins, not on dry ground.
                    var scum = PeriodicFbm(u, v, mottleLattice * 2, 2, Seed ^ 0x5D0Cu);
                    color = LerpColor(color, Scum, Smoothstep(0.62f, 0.92f, scum) * flooded * 0.55f);

                    // Dead reeds: short dark strokes, only where it is wet enough to grow them.
                    // Hashing on a stretched grid is what makes them strokes rather than dots.
                    if (flooded > 0.25f && Hash01(px / 2, py / 7, Seed ^ 0x3F17u) > 0.978f)
                        color = LerpColor(color, Reed, 0.9f);

                    color = Shade(color, (Hash01(px, py, Seed ^ 0x29u) - 0.5f) * 0.14f);

                    pixels[i++] = color.Red;
                    pixels[i++] = color.Green;
                    pixels[i++] = color.Blue;
                    pixels[i++] = 0xFF;
                }
            });
        }
    }
}
