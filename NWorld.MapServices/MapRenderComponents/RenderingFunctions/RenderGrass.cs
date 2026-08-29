using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using NWorld.Map.Models;
using SkiaSharp;

namespace NWorld.MapServices.MapRenderComponents.RenderingFunctions
{
    /// <summary>
    /// Draws a grass tile at map coordinate (x, y), i.e. pixel (x * tileSize, y * tileSize).
    /// <para>
    /// Everything is deterministic in (x, y): a tile looks the same on every repaint and
    /// across runs. A tile is drawn in two parts -- a pre-rendered texture chosen from a pool
    /// of variants and cached per (tileSize, variant), then a broad tone taken from a coarse
    /// map-space noise field. Drawing a tile that has been seen before is a blit plus a rect.
    /// </para>
    /// <para>
    /// Keeping the grid invisible is the whole difficulty here, and it drives most of the
    /// decisions below: variant textures wrap at their own edges, they carry no feature large
    /// enough to be noticed where two different variants meet, the noise octaves are shifted
    /// off the tile edge, and all the broad variation is left to the tone field, which is
    /// continuous across tiles by construction.
    /// </para>
    /// <para>
    /// How much of that a tile actually gets scales with its size -- see <see cref="DetailFor"/>.
    /// A zoomed-out view draws far more tiles per frame with far fewer pixels each, so the
    /// small sizes drop the passes that would not survive being shrunk anyway.
    /// </para>
    /// </summary>
    public static class RenderGrass
    {
        private const uint Seed = 0x6A5F1D3Bu;

        // Cached tiles are cheap (tileSize^2 * 4 bytes) but the tile size changes when the
        // view zooms, so drop everything once we are holding an unreasonable number.
        private const int MaxCachedTiles = 512;

        // Noise lattice sizes, in cells per tile. Each field wraps at the tile edge, so a
        // tile is seamless against a copy of itself -- but two *different* variants still
        // disagree along a shared edge, and at low frequencies that disagreement reads as a
        // visible grid. So the ground carries only fine detail, and everything broad enough
        // to notice is left to the map-space tone field, which is continuous across tiles.
        private const int GroundPeriod = 10;
        private const int PatchPeriod = 8;

        // The map-space tone field: one texel per tile, repeating every TonePeriod tiles.
        private const int TonePeriod = 128;
        private const float ToneStrength = 42f;

        // Lattice sizes for the tone field, in cells across TonePeriod tiles: the broad light
        // and dark stretches come out roughly 10 tiles across, the dry ones roughly 20.
        private const int ToneLattice = 12;
        private const int DryLattice = 6;

        private static readonly float[] OctavePhases = [0.37f, 0.71f, 0.13f, 0.59f];

        private static readonly ConcurrentDictionary<(int TileSize, int Variant), SKImage> Cache = new();
        private static readonly ConcurrentDictionary<int, SKPaint> TonePaints = new();
        private static readonly Lazy<SKBitmap> ToneField = new(BuildToneField, isThreadSafe: true);

        private static readonly SKColor GrassDark = new(0x39, 0x63, 0x2C);
        private static readonly SKColor GrassMid = new(0x5C, 0x8B, 0x3B);
        private static readonly SKColor GrassLight = new(0x7C, 0xAA, 0x50);
        private static readonly SKColor GrassDry = new(0x8E, 0x9C, 0x4C);
        private static readonly SKColor BladeShadow = new(0x2C, 0x4A, 0x22);
        private static readonly SKColor BladeLight = new(0x9E, 0xC6, 0x66);
        private static readonly SKColor FlowerPale = new(0xE8, 0xE4, 0xB8);

        /// <summary>
        /// How much work one tile is worth at a given size. Zooming out puts far more tiles on
        /// screen with far fewer pixels each, so rather than draw detail nobody can resolve,
        /// small tiles drop the expensive passes and draw from a smaller pool of variants.
        /// </summary>
        /// <param name="Variants">
        /// Size of the variant pool. Each variant is also mirrored, so the map repeats after
        /// twice this many tiles -- and every variant is built on first use, which makes this
        /// the main lever on how long a first paint takes.
        /// </param>
        /// <param name="Tufted">
        /// Whether blades are gathered into clumps with shadows under them and the odd flower,
        /// or the tile is just a mottled mat with a few strays over it.
        /// </param>
        private readonly record struct DetailLevel(
            int Variants,
            int GroundOctaves,
            int PatchOctaves,
            bool Tufted);

        private static DetailLevel DetailFor(int tileSize) => tileSize switch
        {
            // A blade is about a pixel here, so clumping it and casting a shadow under it only
            // muddies the result -- a mottled mat with a little speckle reads better and skips
            // most of the build. The tufts are also what make one tile tellable from another,
            // so with them gone there is little left to repeat and a small pool does.
            < 16 => new DetailLevel(Variants: 16, GroundOctaves: 2, PatchOctaves: 1, Tufted: false),

            // Full treatment from here up; only the pool keeps growing with the tile size.
            < 32 => new DetailLevel(Variants: 32, GroundOctaves: 3, PatchOctaves: 2, Tufted: true),

            _ => new DetailLevel(Variants: 64, GroundOctaves: 3, PatchOctaves: 2, Tufted: true),
        };

        /// <summary>
        /// Grass does not animate, so <see cref="TileRenderContext.TimeSeconds"/> is ignored
        /// and a tile stays cached across frames.
        /// </summary>
        public static Task Render(TileRenderContext context)
        {
            var canvas = context.Canvas;
            var tileSize = context.TileSize;
            int x = context.X, y = context.Y;

            if (canvas is null || tileSize <= 0)
                return Task.CompletedTask;

            var hash = Hash(x, y, Seed);
            var variant = (int)(hash % (uint)DetailFor(tileSize).Variants);
            var mirrored = ((hash >> 20) & 1u) == 1u;

            var tile = GetTile(tileSize, variant);

            canvas.Save();
            if (mirrored)
            {
                canvas.Translate((x + 1) * tileSize, y * tileSize);
                canvas.Scale(-1f, 1f);
            }
            else
            {
                canvas.Translate(x * tileSize, y * tileSize);
            }
            canvas.DrawImage(tile, 0, 0);
            canvas.Restore();

            PaintMeadowTone(canvas, tileSize, x, y);

            return Task.CompletedTask;
        }

        /// <summary>
        /// Shades the tile against a coarse noise field sampled in map space, one texel per
        /// tile and filtered smoothly, so the map gets lighter and darker stretches that run
        /// across tile boundaries instead of stopping at them.
        /// </summary>
        private static void PaintMeadowTone(SKCanvas canvas, int tileSize, int x, int y) =>
            canvas.DrawRect(
                SKRect.Create(x * tileSize, y * tileSize, tileSize, tileSize),
                GetTonePaint(tileSize));

        /// <summary>
        /// Drops every cached tile and tone paint; they are rebuilt on the next draw. Worth
        /// calling if the palette changes, or to release memory after a long zoom session.
        /// </summary>
        public static void ClearCache()
        {
            foreach (var key in Cache.Keys)
            {
                if (Cache.TryRemove(key, out var image))
                    image.Dispose();
            }

            foreach (var key in TonePaints.Keys)
            {
                if (TonePaints.TryRemove(key, out var paint))
                {
                    paint.Shader?.Dispose();
                    paint.Dispose();
                }
            }
        }

        /// <summary>
        /// The coarse map-space tone field, one texel per tile. Neutral grey is a no-op under
        /// SKBlendMode.Overlay, so texels either side of it darken or lighten the ground.
        /// </summary>
        private static SKBitmap BuildToneField()
        {
            var bitmap = new SKBitmap(new SKImageInfo(TonePeriod, TonePeriod, SKColorType.Rgba8888, SKAlphaType.Opaque));
            var pixels = new SKColor[TonePeriod * TonePeriod];
            var scale = 1f / TonePeriod;

            for (var ty = 0; ty < TonePeriod; ty++)
            {
                for (var tx = 0; tx < TonePeriod; tx++)
                {
                    var n = PeriodicFbm(tx * scale, ty * scale, ToneLattice, 4, Seed ^ 0x70A5u);
                    var dry = PeriodicFbm(tx * scale, ty * scale, DryLattice, 2, Seed ^ 0xD47Au);

                    var level = 128f + (n - 0.5f) * 2f * ToneStrength;
                    pixels[ty * TonePeriod + tx] = new SKColor(
                        (byte)Clamp255(level + dry * 10f),
                        (byte)Clamp255(level + 2f),
                        (byte)Clamp255(level - dry * 12f));
                }
            }

            bitmap.Pixels = pixels;
            return bitmap;
        }

        /// <summary>
        /// The paint that lays the tone field over a tile. Cached per tile size and only ever
        /// read after construction, so it is safe to share across draws.
        /// </summary>
        private static SKPaint GetTonePaint(int tileSize) =>
            TonePaints.GetOrAdd(tileSize, static size => new SKPaint
            {
                // One texel per tile, so a tile samples its own texel centre and blends
                // smoothly towards its neighbours' rather than stopping at the edge.
                Shader = SKShader.CreateBitmap(
                    ToneField.Value,
                    SKShaderTileMode.Repeat,
                    SKShaderTileMode.Repeat,
                    SKMatrix.CreateScale(size, size)),
                BlendMode = SKBlendMode.Overlay,
                FilterQuality = SKFilterQuality.Low,
                IsAntialias = false,
            });

        private static SKImage GetTile(int tileSize, int variant)
        {
            var key = (tileSize, variant);
            if (Cache.TryGetValue(key, out var cached))
                return cached;

            // Only reached on a miss. ConcurrentDictionary.Count takes every one of the
            // dictionary's locks, so it must not sit on the path a zoomed-out frame walks
            // thousands of times.
            if (Cache.Count > MaxCachedTiles)
                ClearCache();

            return Cache.GetOrAdd(key, static k => BuildTile(k.TileSize, k.Variant));
        }

        private static SKImage BuildTile(int tileSize, int variant)
        {
            var seed = Hash(variant, variant * 31 + 7, Seed);
            var detail = DetailFor(tileSize);

            var info = new SKImageInfo(tileSize, tileSize, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var surface = SKSurface.Create(info);
            var canvas = surface.Canvas;

            PaintGround(canvas, tileSize, seed, detail);
            PaintBlades(canvas, tileSize, seed, detail);

            if (detail.Tufted)
                PaintDetails(canvas, tileSize, seed);

            return surface.Snapshot();
        }

        /// <summary>Lays down the mottled ground out of a few octaves of value noise.</summary>
        private static void PaintGround(SKCanvas canvas, int tileSize, uint seed, DetailLevel detail)
        {
            // Filled as raw RGBA rather than through SKBitmap.Pixels, whose setter converts
            // pixel by pixel and costs more than the noise it would be carrying.
            var pixels = new byte[tileSize * tileSize * 4];
            var scale = 1f / tileSize;
            var i = 0;

            for (var py = 0; py < tileSize; py++)
            {
                var v = py * scale;
                for (var px = 0; px < tileSize; px++)
                {
                    var u = px * scale;

                    // Both fields wrap at the tile edge. Keeping the largest feature well under
                    // a tile is what stops neighbouring tiles from showing a seam.
                    var mottle = PeriodicFbm(u, v, GroundPeriod, detail.GroundOctaves, seed);
                    var patch = PeriodicFbm(u, v, PatchPeriod, detail.PatchOctaves, seed ^ 0x51A3u);

                    var color = LerpColor(GrassDark, GrassLight, 0.30f + mottle * 0.55f + patch * 0.20f);

                    // Sun-bleached patches where the broad noise peaks.
                    color = LerpColor(color, GrassDry, Smoothstep(0.60f, 0.92f, patch) * 0.45f);

                    // Per-pixel grain to break up the smooth gradients.
                    color = Shade(color, (Hash01(px, py, seed ^ 0x77u) - 0.5f) * 0.10f);

                    pixels[i++] = color.Red;
                    pixels[i++] = color.Green;
                    pixels[i++] = color.Blue;
                    pixels[i++] = 0xFF;
                }
            }

            var info = new SKImageInfo(tileSize, tileSize, SKColorType.Rgba8888, SKAlphaType.Opaque);
            using var ground = SKImage.FromPixelCopy(info, pixels);
            canvas.DrawImage(ground, 0, 0);
        }

        /// <summary>
        /// Strokes the blades. Blades are grouped into clumps that share a lean and a rough
        /// height, with a few loose strays on top -- scattering them evenly instead reads as
        /// woven fabric rather than as grass.
        /// </summary>
        private static void PaintBlades(SKCanvas canvas, int tileSize, uint seed, DetailLevel detail)
        {
            var rng = new Rng(seed ^ 0xB1ADE5u);
            var unit = tileSize / 32f;

            // Zero means the blades cast nothing, which is how the small sizes skip a whole
            // draw per blade.
            var shadowOffset = detail.Tufted ? Math.Max(0.5f, unit * 0.7f) : 0f;

            // Clumps are kept small relative to the tile. Features that approach tile size
            // make each tile read as one distinct tuft, and a grid of distinct tufts is
            // exactly the pattern this is trying to avoid.
            var clumpCount = detail.Tufted ? Math.Max(3, (int)(tileSize * tileSize / 170f)) : 0;

            using var paint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeCap = SKStrokeCap.Round,
                StrokeWidth = Math.Max(0.8f, unit),
            };
            using var path = new SKPath();

            for (var c = 0; c < clumpCount; c++)
            {
                var clumpX = rng.Range(0f, tileSize);
                var clumpY = rng.Range(0f, tileSize);
                var spread = rng.Range(0.05f, 0.11f) * tileSize;
                var clumpHeight = rng.Range(0.10f, 0.24f) * tileSize;
                var clumpLean = rng.Range(-0.45f, 0.45f);
                var blades = (int)rng.Range(7f, 16f);

                if (shadowOffset > 0f)
                    DrawClumpShadow(canvas, clumpX, clumpY, spread, tileSize, shadowOffset);

                for (var b = 0; b < blades; b++)
                {
                    var baseX = clumpX + rng.Range(-spread, spread);
                    var baseY = clumpY + rng.Range(-spread * 0.6f, spread * 0.6f);
                    var height = clumpHeight * rng.Range(0.6f, 1.3f);
                    var lean = (clumpLean + rng.Range(-0.16f, 0.16f)) * height;

                    DrawBlade(canvas, paint, path, baseX, baseY, height, lean, rng.NextFloat(), tileSize, shadowOffset);
                }
            }

            // Strays, so the clumps do not read as discrete tufts. With no clumps to fill the
            // tile they have to carry it alone, so there are more of them.
            var strays = Math.Max(6, (int)(tileSize * tileSize / (detail.Tufted ? 70f : 34f)));
            for (var i = 0; i < strays; i++)
            {
                var baseX = rng.Range(0f, tileSize);
                var baseY = rng.Range(0f, tileSize);
                var height = rng.Range(0.07f, 0.19f) * tileSize;
                var lean = rng.Range(-0.5f, 0.5f) * height;

                DrawBlade(canvas, paint, path, baseX, baseY, height, lean, rng.NextFloat(), tileSize, shadowOffset);
            }
        }

        /// <summary>A soft dark pool under a clump, so the tuft sits in the ground instead of on it.</summary>
        private static void DrawClumpShadow(SKCanvas canvas, float cx, float cy, float spread, int tileSize, float shadowOffset)
        {
            using var paint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Fill,
                Color = BladeShadow.WithAlpha(38),
                MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, spread * 0.45f),
            };

            var rect = SKRect.Create(cx - spread, cy - spread * 0.5f + shadowOffset, spread * 2f, spread);

            // The blur reaches about three sigma past the oval, and a copy that gets skipped
            // here is shadow missing from an edge, so the bounds are deliberately generous.
            var bounds = rect;
            bounds.Inflate(spread * 1.5f, spread * 1.5f);

            foreach (var (dx, dy) in WrapOffsets(bounds, tileSize))
                canvas.DrawOval(SKRect.Create(rect.Left + dx, rect.Top + dy, rect.Width, rect.Height), paint);
        }

        private static void DrawBlade(
            SKCanvas canvas,
            SKPaint paint,
            SKPath path,
            float baseX,
            float baseY,
            float height,
            float lean,
            float tint,
            int tileSize,
            float shadowOffset)
        {
            path.Reset();
            path.MoveTo(baseX, baseY);
            path.QuadTo(baseX + lean * 0.25f, baseY - height * 0.6f, baseX + lean, baseY - height);

            // Roughly a third of the blades sit darker than the ground, which is what gives
            // the mat any sense of depth.
            var color = tint < 0.35f
                ? LerpColor(GrassDark, GrassMid, tint / 0.35f)
                : LerpColor(GrassMid, BladeLight, (tint - 0.35f) / 0.65f);
            var bladeColor = color.WithAlpha((byte)(140f + tint * 90f));
            var shadowColor = BladeShadow.WithAlpha(70);
            var withShadow = shadowOffset > 0f;   // zero offset means this level draws no shadows

            // A blade that runs off one edge is drawn again coming in the opposite edge,
            // so the finished tile abuts itself (and any other variant) without a seam.
            var bounds = path.Bounds;
            bounds.Inflate(paint.StrokeWidth, paint.StrokeWidth);
            bounds.Bottom += shadowOffset;

            foreach (var (dx, dy) in WrapOffsets(bounds, tileSize))
            {
                canvas.Save();
                canvas.Translate(dx, dy);

                if (withShadow)
                {
                    paint.Color = shadowColor;
                    canvas.Translate(0, shadowOffset);
                    canvas.DrawPath(path, paint);
                    canvas.Translate(0, -shadowOffset);
                }

                paint.Color = bladeColor;
                canvas.DrawPath(path, paint);
                canvas.Restore();
            }
        }

        /// <summary>
        /// Yields the tile-sized translations needed to draw <paramref name="bounds"/> wrapped
        /// around the tile edges: always (0, 0), plus a copy for each edge the shape crosses.
        /// </summary>
        private static IEnumerable<(float Dx, float Dy)> WrapOffsets(SKRect bounds, int tileSize)
        {
            for (var sx = -1; sx <= 1; sx++)
            {
                for (var sy = -1; sy <= 1; sy++)
                {
                    var shifted = SKRect.Create(
                        bounds.Left + sx * tileSize,
                        bounds.Top + sy * tileSize,
                        bounds.Width,
                        bounds.Height);

                    if (shifted.IntersectsWith(SKRect.Create(0, 0, tileSize, tileSize)))
                        yield return (sx * tileSize, sy * tileSize);
                }
            }
        }

        /// <summary>Sprinkles a handful of seed heads and tiny flowers.</summary>
        private static void PaintDetails(SKCanvas canvas, int tileSize, uint seed)
        {
            var rng = new Rng(seed ^ 0xF10E7Du);
            var unit = tileSize / 32f;
            var count = (int)(tileSize / 10f);
            if (count <= 0)
                return;

            using var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };

            for (var i = 0; i < count; i++)
            {
                // Only about half the candidates are taken, so the count varies tile to tile.
                if (rng.NextFloat() > 0.55f)
                    continue;

                var cx = rng.Range(0f, tileSize);
                var cy = rng.Range(0f, tileSize);
                var radius = Math.Max(0.6f, unit * rng.Range(0.7f, 1.3f));

                paint.Color = rng.NextFloat() < 0.35f
                    ? FlowerPale.WithAlpha(210)
                    : GrassDry.WithAlpha(190);

                var bounds = SKRect.Create(cx - radius, cy - radius, radius * 2f, radius * 2f);
                foreach (var (dx, dy) in WrapOffsets(bounds, tileSize))
                    canvas.DrawCircle(cx + dx, cy + dy, radius, paint);
            }
        }

        // ---- deterministic noise -------------------------------------------------

        /// <summary>
        /// Stable 32-bit mix of two coordinates. Deliberately not GetHashCode: this has to
        /// produce the same value across processes and runtimes.
        /// </summary>
        private static uint Hash(int x, int y, uint seed)
        {
            var h = seed + (uint)x * 0x9E3779B1u + (uint)y * 0x85EBCA77u;
            h ^= h >> 15;
            h *= 0x2C1B3C6Du;
            h ^= h >> 12;
            h *= 0x297A2D39u;
            h ^= h >> 15;
            return h;
        }

        private static float Hash01(int x, int y, uint seed) => (Hash(x, y, seed) >> 8) * (1f / 16777216f);

        /// <summary>
        /// Value noise on a lattice that repeats every <paramref name="period"/> cells, so a
        /// field sampled over u,v in [0,1) has matching opposite edges.
        /// </summary>
        private static float PeriodicValueNoise(float x, float y, int period, uint seed)
        {
            var xi = (int)MathF.Floor(x);
            var yi = (int)MathF.Floor(y);
            var tx = Smoothstep(0f, 1f, x - xi);
            var ty = Smoothstep(0f, 1f, y - yi);

            int x0 = Wrap(xi, period), x1 = Wrap(xi + 1, period);
            int y0 = Wrap(yi, period), y1 = Wrap(yi + 1, period);

            var top = Lerp(Hash01(x0, y0, seed), Hash01(x1, y0, seed), tx);
            var bottom = Lerp(Hash01(x0, y1, seed), Hash01(x1, y1, seed), tx);
            return Lerp(top, bottom, ty);
        }

        /// <summary>Stacked octaves of <see cref="PeriodicValueNoise"/>; u and v are tile-relative (0..1).</summary>
        private static float PeriodicFbm(float u, float v, int period, int octaves, uint seed)
        {
            float sum = 0f, amplitude = 0.5f, total = 0f;
            for (var i = 0; i < octaves; i++)
            {
                // Shifting each octave off the lattice origin matters more than it looks.
                // Unshifted, every octave has a lattice node exactly on the tile edge, where
                // the noise takes its raw value instead of an interpolated one -- so the edge
                // pixels have visibly different statistics from the interior and the map ends
                // up with a faint grid drawn on it. The shift is a constant, so the field is
                // still periodic and the tile still wraps.
                var phase = OctavePhases[i % OctavePhases.Length] * period;
                sum += PeriodicValueNoise(
                    u * period + phase,
                    v * period + phase * 0.63f,
                    period,
                    seed + (uint)i * 0x9E37u) * amplitude;

                total += amplitude;
                period *= 2;
                amplitude *= 0.5f;
            }
            return sum / total;
        }

        private static int Wrap(int value, int period)
        {
            var m = value % period;
            return m < 0 ? m + period : m;
        }

        // ---- small helpers -------------------------------------------------------

        private static float Lerp(float a, float b, float t) => a + (b - a) * t;

        private static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;

        private static float Smoothstep(float edge0, float edge1, float v)
        {
            var t = Clamp01((v - edge0) / (edge1 - edge0));
            return t * t * (3f - 2f * t);
        }

        private static SKColor LerpColor(SKColor a, SKColor b, float t)
        {
            t = Clamp01(t);
            return new SKColor(
                (byte)(a.Red + (b.Red - a.Red) * t),
                (byte)(a.Green + (b.Green - a.Green) * t),
                (byte)(a.Blue + (b.Blue - a.Blue) * t));
        }

        private static SKColor Shade(SKColor color, float amount)
        {
            var factor = 1f + amount;
            return new SKColor(
                (byte)Clamp255(color.Red * factor),
                (byte)Clamp255(color.Green * factor),
                (byte)Clamp255(color.Blue * factor));
        }

        private static float Clamp255(float v) => v < 0f ? 0f : v > 255f ? 255f : v;

        /// <summary>xorshift32: tiny, deterministic, and plenty for scattering blades.</summary>
        private struct Rng
        {
            private uint _state;

            public Rng(uint seed) => _state = seed == 0u ? 0x9E3779B9u : seed;

            public uint NextUInt()
            {
                _state ^= _state << 13;
                _state ^= _state >> 17;
                _state ^= _state << 5;
                return _state;
            }

            public float NextFloat() => (NextUInt() >> 8) * (1f / 16777216f);

            public float Range(float min, float max) => min + (max - min) * NextFloat();
        }
    }
}
