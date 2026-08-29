using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NWorld.Map.Models;
using SkiaSharp;
using static NWorld.MapServices.MapRenderComponents.RenderNoise;

namespace NWorld.MapServices.MapRenderComponents
{
    /// <summary>
    /// How one kind of water is coloured and lit. Everything about the wave *shape* is shared
    /// between kinds and lives on <see cref="AnimatedWater"/>; a style only changes how that
    /// shape is painted.
    /// <para>
    /// That split is the point. Water is water: a shoreline is a change of depth, not a change
    /// of sea, so the crests have to run unbroken from the shallows out into the deep. Since
    /// every kind draws the same wave field from the same block, at the same phase, the crests
    /// line up across the boundary on their own and only the colour steps.
    /// </para>
    /// </summary>
    /// <param name="Seed">Seeds the depth field, so different kinds get different broad shapes.</param>
    /// <param name="Trough">Colour at the bottom of a wave.</param>
    /// <param name="Body">Colour at the mean surface.</param>
    /// <param name="Crest">Colour at the top of a wave.</param>
    /// <param name="Highlight">Colour of glints and foam.</param>
    /// <param name="Steepness">How far the wave gradient tips the surface normal.</param>
    /// <param name="Shine">Specular exponent: higher is a tighter, sparklier highlight.</param>
    internal readonly record struct WaterStyle(
        uint Seed,
        SKColor Trough,
        SKColor Body,
        SKColor Crest,
        SKColor Highlight,
        float Steepness,
        float Shine,
        float GlintStrength,
        float FoamStrength,
        float TurbidityStrength,
        int DepthLattice,
        float DepthStrength);

    /// <summary>
    /// Draws moving water across a batch of tiles, in one <see cref="WaterStyle"/>.
    /// <para>
    /// The surface is one seamlessly tiling texture repeated over the whole batch, not a pool
    /// of per-tile variants, and that is the central decision here. Waves have to run across
    /// tile boundaries; a pool cannot do that, because two neighbouring tiles drawing
    /// different variants disagree along their shared edge and the wave visibly breaks. One
    /// texture that wraps against itself, repeated on the grid, is by construction a
    /// continuous surface -- every tile lines up with its neighbour because every tile *is*
    /// its neighbour.
    /// </para>
    /// <para>
    /// That also makes it cheap: the whole batch is a repeat shader, so a screenful of water
    /// is a couple of rects per run of adjacent tiles rather than anything per tile.
    /// </para>
    /// <para>
    /// Movement comes from pre-rendered phases. The surface is a sum of travelling sine waves
    /// whose wavevectors are whole numbers of cycles across the texture block -- which is what
    /// makes it tile in space -- and whose temporal frequencies are whole numbers of cycles
    /// across the loop, which is what makes the last phase run back into the first with no
    /// jump. Consecutive phases are cross-faded, so the motion is continuous rather than
    /// stepping at the phase rate.
    /// </para>
    /// </summary>
    internal sealed class AnimatedWater
    {
        /// <summary>Seconds for the animation to return to where it started.</summary>
        private const float LoopSeconds = 6f;

        private const int MaxPhases = 32;
        private const int MinPhases = 8;

        // A phase texture spans WaveTiles tiles square. More than one tile so the long waves
        // can be longer than a tile: a texture one tile across could hold nothing bigger than
        // a tile, and the surface would read as ripples in a tray rather than as open water.
        private const int TargetTextureEdge = 384;
        private const int MinWaveTiles = 2;
        private const int MaxWaveTiles = 12;

        private const long MaxSurfaceBytes = 48L * 1024 * 1024;

        /// <summary>Budget per style, so one kind of water cannot starve another.</summary>
        private const long MaxCacheBytes = 128L * 1024 * 1024;

        // The map-space depth field: one texel per tile, repeating every DepthPeriod tiles.
        // Its job is to break up the texture's own repeat, which without it shows as the same
        // patch of water every WaveTiles tiles.
        private const int DepthPeriod = 128;

        /// <summary>Heading of the main swell, in radians, and how far the chop fans off it.</summary>
        private const float Heading = 0.5f;
        private const float Spread = 1.25f;

        /// <summary>
        /// Shortest wavelength the surface carries, in pixels. Below about this the detail is
        /// finer than the filtering can hold on to and turns into a crawling shimmer.
        /// </summary>
        private const int FinestWavelength = 7;

        /// <summary>
        /// Longest wave the texture carries, in cycles across the block. Nothing spans the
        /// block, and that is deliberate: the block repeats every few tiles, and a feature as
        /// large as the block is precisely what makes the repeat findable -- the eye locks on
        /// to the big shape and then sees it again, and again. The broad variation is left to
        /// the map-space depth field, which has no such period. It is the same bargain the
        /// grass makes with its tone field, for the same reason.
        /// </summary>
        private const float LongestWavenumber = 2.2f;

        private const uint WaveSeed = 0x2B7F91C5u ^ 0x5EA0u;
        private const uint TurbiditySeed = 0x2B7F91C5u ^ 0x7A1Du;

        private static readonly (float X, float Y, float Z) Light = Normalize(-0.55f, -0.62f, 0.56f);
        private static readonly (float X, float Y, float Z) Half = Normalize(Light.X, Light.Y, Light.Z + 1f);

        // Shared by every style, because the wave shape is: two kinds of water drawing the
        // same block at the same phase agree about where the crests are, which is what lets a
        // shoreline be a change of colour rather than a change of sea.
        private static readonly ConcurrentDictionary<(int Edge, int Phases), Wave[]> WaveSets = new();
        private static readonly ConcurrentDictionary<int, float[]> Turbidities = new();

        // Its shader and alpha change every frame, so unlike the cached paints it cannot be
        // shared between threads. Shared between styles is fine -- it is reset on every draw.
        [ThreadStatic] private static SKPaint? Scratch;

        private readonly WaterStyle _style;
        private readonly ConcurrentDictionary<int, Lazy<WaveSurface>> _surfaces = new();
        private readonly ConcurrentDictionary<int, SKPaint> _depthPaints = new();
        private readonly Lazy<SKImage> _depthField;
        private long _cacheBytes;

        public AnimatedWater(WaterStyle style)
        {
            _style = style;
            _depthField = new Lazy<SKImage>(BuildDepthField, isThreadSafe: true);
        }

        /// <summary>
        /// One travelling wave. <see cref="Kx"/> and <see cref="Ky"/> are whole cycles across
        /// the texture block and <see cref="Frequency"/> whole cycles across the loop, so every
        /// component is periodic in both space and time and the surface tiles and loops.
        /// </summary>
        private readonly record struct Wave(int Kx, int Ky, int Frequency, float Amplitude);

        /// <summary>
        /// Draws every tile in the batch: the current phase, the next one faded in over it,
        /// and the map-space depth field on top.
        /// </summary>
        public Task Render(TileRenderContext context)
        {
            var canvas = context.Canvas;
            var tileSize = context.TileSize;
            var tiles = context.Tiles;

            if (canvas is null || tileSize <= 0 || tiles.Length == 0)
                return Task.CompletedTask;

            var surface = GetSurface(tileSize);
            var phases = surface.Shaders.Length;

            // Reduced to one loop *before* scaling up to the phase count. Taking the modulus
            // last would lose resolution as TimeSeconds grows: a float has about seven digits,
            // and an hour in there are too few of them left below the second to place a phase
            // steadily.
            var loop = context.TimeSeconds / LoopSeconds;
            loop -= MathF.Floor(loop);

            var position = loop * phases;
            var index = Math.Min((int)position, phases - 1);
            var blend = position - index;

            var paint = Scratch ??= new SKPaint
            {
                IsAntialias = false,
                FilterQuality = SKFilterQuality.Low,
            };

            paint.BlendMode = SKBlendMode.SrcOver;
            paint.Shader = surface.Shaders[index];
            paint.Color = SKColors.White;
            TileRuns.Fill(canvas, tiles, tileSize, paint);

            // The cross-fade is what makes the motion continuous. Without it the surface steps
            // once per phase, which at a handful of phases a second reads as a stutter rather
            // than as water.
            var alpha = (byte)(blend * 255f);
            if (alpha > 0)
            {
                paint.Shader = surface.Shaders[(index + 1) % phases];
                paint.Color = SKColors.White.WithAlpha(alpha);
                TileRuns.Fill(canvas, tiles, tileSize, paint);
            }

            // Not left holding a shader the cache may want to dispose.
            paint.Shader = null;

            TileRuns.Fill(canvas, tiles, tileSize, GetDepthPaint(tileSize));

            return Task.CompletedTask;
        }

        /// <summary>
        /// Builds the phase set for <paramref name="tileSize"/> off the calling thread. Water
        /// is much more expensive to prepare than static ground -- it is a whole animation
        /// rather than one texture -- so a zoom control that does not prewarm will show the
        /// build as a stall on the frame that first asks for it.
        /// </summary>
        public Task Prewarm(int tileSize) =>
            tileSize <= 0 ? Task.CompletedTask : Task.Run(() => GetSurface(tileSize));

        /// <summary>
        /// Drops every cached phase set and depth paint; they are rebuilt on the next draw.
        /// Not safe to call while a frame is in flight -- it disposes images that frame may
        /// still be drawing from.
        /// </summary>
        public void ClearCache()
        {
            foreach (var key in _surfaces.Keys)
            {
                if (_surfaces.TryRemove(key, out var surface) && surface.IsValueCreated)
                {
                    Interlocked.Add(ref _cacheBytes, -surface.Value.Bytes);
                    surface.Value.Dispose();
                }
            }

            foreach (var key in _depthPaints.Keys)
            {
                if (_depthPaints.TryRemove(key, out var paint))
                {
                    paint.Shader?.Dispose();
                    paint.Dispose();
                }
            }
        }

        // ---- the phase set -------------------------------------------------------

        private sealed class WaveSurface : IDisposable
        {
            public required SKImage[] Images { get; init; }

            /// <summary>
            /// One repeat shader per phase, built once. A shader is immutable, so these are
            /// safe to hand to any thread; only the paint they are hung on is per-thread.
            /// </summary>
            public required SKShader[] Shaders { get; init; }

            public required long Bytes { get; init; }

            public long LastUsed;

            public void Dispose()
            {
                foreach (var shader in Shaders)
                    shader.Dispose();
                foreach (var image in Images)
                    image.Dispose();
            }
        }

        private WaveSurface GetSurface(int tileSize)
        {
            if (_surfaces.TryGetValue(tileSize, out var cached) && cached.IsValueCreated)
            {
                cached.Value.LastUsed = Environment.TickCount64;
                return cached.Value;
            }

            var lazy = _surfaces.GetOrAdd(tileSize, size => new Lazy<WaveSurface>(() =>
            {
                var built = BuildSurface(size);
                Interlocked.Add(ref _cacheBytes, built.Bytes);
                return built;
            }, LazyThreadSafetyMode.ExecutionAndPublication));

            var surface = lazy.Value;
            surface.LastUsed = Environment.TickCount64;
            Trim(keep: tileSize);
            return surface;
        }

        private void Trim(int keep)
        {
            if (Interlocked.Read(ref _cacheBytes) <= MaxCacheBytes)
                return;

            var candidates = _surfaces
                .Where(entry => entry.Key != keep && entry.Value.IsValueCreated)
                .OrderBy(entry => entry.Value.Value.LastUsed)
                .Select(entry => entry.Key)
                .ToList();

            foreach (var size in candidates)
            {
                if (Interlocked.Read(ref _cacheBytes) <= MaxCacheBytes)
                    return;

                if (_surfaces.TryRemove(size, out var victim) && victim.IsValueCreated)
                {
                    Interlocked.Add(ref _cacheBytes, -victim.Value.Bytes);
                    victim.Value.Dispose();
                }
            }
        }

        private WaveSurface BuildSurface(int tileSize)
        {
            // Hold the texture near a fixed pixel size rather than a fixed number of tiles, so
            // the cost of a phase set barely moves with the zoom level.
            var waveTiles = Math.Clamp(TargetTextureEdge / tileSize, MinWaveTiles, MaxWaveTiles);
            var edge = waveTiles * tileSize;

            var bytesPerPhase = 4L * edge * edge;
            var phases = (int)Math.Clamp(MaxSurfaceBytes / bytesPerPhase, MinPhases, MaxPhases);

            // Both are shared by every phase, and both would otherwise be built concurrently
            // by all of them at once.
            WavesFor(edge, phases);
            TurbidityFor(edge);

            var images = new SKImage[phases];
            Parallel.For(0, phases, p => images[p] = BuildPhase(edge, p, phases));

            var shaders = new SKShader[phases];
            for (var p = 0; p < phases; p++)
            {
                // No local matrix: the texture is built at exactly the size it repeats at, and
                // anchoring it to the canvas origin is what puts tile (0,0) on texture (0,0)
                // and keeps every tile agreeing about where the wave is.
                shaders[p] = SKShader.CreateImage(images[p], SKShaderTileMode.Repeat, SKShaderTileMode.Repeat);
            }

            return new WaveSurface
            {
                Images = images,
                Shaders = shaders,
                Bytes = bytesPerPhase * phases,
            };
        }

        /// <summary>
        /// The wave spectrum for a texture block of <paramref name="edge"/> pixels: an octave
        /// ladder of wavenumbers, a few differently angled components at each step, amplitude
        /// falling away as the waves shorten.
        /// <para>
        /// Built per block size rather than fixed, because a wavenumber means cycles across
        /// the block and the block is a different number of pixels at every zoom. A fixed
        /// table would be soft blobs at one zoom and aliased shimmer at another.
        /// </para>
        /// <para>
        /// Travel speed follows the deep-water relation, frequency going with the square root
        /// of the wavenumber, so the long swell rolls through while the chop skitters.
        /// Rounding that to a whole number of cycles per loop is what lets the animation
        /// close; the rounding only detunes a component slightly from true dispersion, which
        /// is not something anyone can see.
        /// </para>
        /// </summary>
        private static Wave[] WavesFor(int edge, int phases) => WaveSets.GetOrAdd((edge, phases), static key =>
        {
            var (blockEdge, phaseCount) = key;

            var maxWavenumber = Math.Clamp(blockEdge / FinestWavelength, 3, 44);

            // A component advancing more than about an eighth of a cycle between phases is
            // sampled too coarsely to cross-fade: blending two positions that far apart dips
            // the amplitude in between, so instead of travelling the wave fades out and back
            // in. Capping the frequency slows the shortest chop below true dispersion, which
            // is invisible; letting it through is not.
            var maxFrequency = Math.Max(1, phaseCount / 8);

            var rng = new Rng(WaveSeed);
            var waves = new List<Wave>();
            var placed = new HashSet<(int, int)>();

            for (var length = LongestWavenumber; length <= maxWavenumber; length *= 1.42f)
            {
                // More components as the waves shorten: at the long end a second one is
                // already a cross swell, whereas the chop wants to come from everywhere. Too
                // few, or too tightly aimed, and they line up into diagonal corduroy.
                var perOctave = length < 5f ? 3 : 4;

                for (var i = 0; i < perOctave; i++)
                {
                    var angle = Heading + rng.Range(-Spread, Spread);
                    var kx = (int)MathF.Round(length * MathF.Cos(angle));
                    var ky = (int)MathF.Round(length * MathF.Sin(angle));

                    if ((kx == 0 && ky == 0) || !placed.Add((kx, ky)))
                        continue;

                    var wavenumber = MathF.Sqrt(kx * kx + ky * ky);
                    waves.Add(new Wave(
                        kx,
                        ky,
                        Math.Clamp((int)MathF.Round(1.15f * MathF.Sqrt(wavenumber)), 1, maxFrequency),
                        1f / MathF.Pow(wavenumber, 1.12f)));
                }
            }

            return waves.ToArray();
        });

        /// <summary>
        /// The static colour wander, which does not depend on the phase and so is built once
        /// for the block rather than once per phase.
        /// </summary>
        private static float[] TurbidityFor(int edge) => Turbidities.GetOrAdd(edge, static blockEdge =>
        {
            var field = new float[blockEdge * blockEdge];
            var scale = 1f / blockEdge;

            for (var py = 0; py < blockEdge; py++)
            {
                for (var px = 0; px < blockEdge; px++)
                    field[py * blockEdge + px] = PeriodicFbm(px * scale, py * scale, 3, 3, TurbiditySeed);
            }

            return field;
        });

        /// <summary>
        /// Shades one phase of the surface.
        /// <para>
        /// The sum is walked one wave at a time along a row, carrying that wave's sine and
        /// cosine forward by a fixed rotation per pixel, rather than calling sin and cos per
        /// wave per pixel. With a spectrum this wide that is the difference between a phase
        /// set costing a fraction of a second and costing minutes: two trig calls become four
        /// multiplies. The rotation is re-seeded every row, so its error never accumulates
        /// past a few parts in a million.
        /// </para>
        /// <para>
        /// Height and both derivatives fall out of the same walk, so the normal is the wave's
        /// true gradient rather than a difference of neighbouring pixels -- which matters at
        /// the small tile sizes, where a pixel is a large step and a sampled gradient turns
        /// the highlights into stairs.
        /// </para>
        /// </summary>
        private SKImage BuildPhase(int edge, int phase, int phases)
        {
            var waves = WavesFor(edge, phases);
            var turbidity = TurbidityFor(edge);

            var pixels = new byte[edge * edge * 4];
            var tau = (float)phase / phases;
            var scale = 1f / edge;

            var amplitude = 0f;
            foreach (var wave in waves)
                amplitude += wave.Amplitude;

            var height = new float[edge];
            var slopeU = new float[edge];
            var slopeV = new float[edge];

            var i = 0;
            for (var py = 0; py < edge; py++)
            {
                Array.Clear(height);
                Array.Clear(slopeU);
                Array.Clear(slopeV);

                var v = py * scale;

                foreach (var wave in waves)
                {
                    // Phase at the start of the row, and the fixed step along it.
                    var theta = MathF.Tau * (wave.Ky * v - wave.Frequency * tau);
                    var step = MathF.Tau * wave.Kx * scale;

                    float sin = MathF.Sin(theta), cos = MathF.Cos(theta);
                    float stepSin = MathF.Sin(step), stepCos = MathF.Cos(step);

                    var weightU = wave.Amplitude * wave.Kx;
                    var weightV = wave.Amplitude * wave.Ky;

                    for (var px = 0; px < edge; px++)
                    {
                        height[px] += wave.Amplitude * sin;
                        slopeU[px] += weightU * cos;
                        slopeV[px] += weightV * cos;

                        var rotated = cos * stepCos - sin * stepSin;
                        sin = sin * stepCos + cos * stepSin;
                        cos = rotated;
                    }
                }

                for (var px = 0; px < edge; px++)
                {
                    var h = height[px] / amplitude;
                    var du = slopeU[px] * MathF.Tau / amplitude;
                    var dv = slopeV[px] * MathF.Tau / amplitude;

                    var normal = Normalize(-du * _style.Steepness, -dv * _style.Steepness, 1f);
                    var diffuse = Math.Max(0f, normal.X * Light.X + normal.Y * Light.Y + normal.Z * Light.Z);
                    var glint = MathF.Pow(
                        Math.Max(0f, normal.X * Half.X + normal.Y * Half.Y + normal.Z * Half.Z),
                        _style.Shine) * _style.GlintStrength;

                    // Crests are the lighter, shallower-looking water; troughs the darker.
                    var crest = Clamp01(0.5f + 0.5f * h);
                    var body = crest < 0.5f
                        ? LerpColor(_style.Trough, _style.Body, crest * 2f)
                        : LerpColor(_style.Body, _style.Crest, (crest - 0.5f) * 2f);

                    body = Shade(body, (turbidity[py * edge + px] - 0.5f) * _style.TurbidityStrength);
                    body = Shade(body, (diffuse - 0.66f) * 0.42f);

                    // Foam only where a crest is high and the surface there is actually steep.
                    var foam = Smoothstep(0.78f, 0.98f, crest)
                        * Smoothstep(0.35f, 1.1f, MathF.Abs(du) + MathF.Abs(dv));

                    var color = LerpColor(body, _style.Highlight, Clamp01(glint + foam * _style.FoamStrength));

                    pixels[i++] = color.Red;
                    pixels[i++] = color.Green;
                    pixels[i++] = color.Blue;
                    pixels[i++] = 0xFF;
                }
            }

            var info = new SKImageInfo(edge, edge, SKColorType.Rgba8888, SKAlphaType.Opaque);
            return SKImage.FromPixelCopy(info, pixels);
        }

        // ---- depth field ---------------------------------------------------------

        /// <summary>
        /// The coarse map-space depth field, one texel per tile. Neutral grey is a no-op under
        /// <see cref="SKBlendMode.Overlay"/>, so texels either side of it read as deeper or
        /// shallower water.
        /// </summary>
        private SKImage BuildDepthField()
        {
            var pixels = new byte[DepthPeriod * DepthPeriod * 4];
            var scale = 1f / DepthPeriod;
            var i = 0;

            for (var ty = 0; ty < DepthPeriod; ty++)
            {
                for (var tx = 0; tx < DepthPeriod; tx++)
                {
                    var n = PeriodicFbm(tx * scale, ty * scale, _style.DepthLattice, 4, _style.Seed ^ 0x3C90u);
                    var level = 128f + (n - 0.5f) * 2f * _style.DepthStrength;

                    // Deeper water loses the warm end first, so the darker texels come out
                    // bluer rather than simply dimmer.
                    pixels[i++] = (byte)Clamp255(level - 6f);
                    pixels[i++] = (byte)Clamp255(level);
                    pixels[i++] = (byte)Clamp255(level + 8f);
                    pixels[i++] = 0xFF;
                }
            }

            var info = new SKImageInfo(DepthPeriod, DepthPeriod, SKColorType.Rgba8888, SKAlphaType.Opaque);
            return SKImage.FromPixelCopy(info, pixels);
        }

        private SKPaint GetDepthPaint(int tileSize) =>
            _depthPaints.GetOrAdd(tileSize, size => new SKPaint
            {
                // One texel per tile, so a tile samples its own texel centre and blends
                // smoothly towards its neighbours' rather than stopping at the edge.
                Shader = SKShader.CreateImage(
                    _depthField.Value,
                    SKShaderTileMode.Repeat,
                    SKShaderTileMode.Repeat,
                    SKMatrix.CreateScale(size, size)),
                BlendMode = SKBlendMode.Overlay,
                FilterQuality = SKFilterQuality.Low,
                IsAntialias = false,
            });

        private static (float X, float Y, float Z) Normalize(float x, float y, float z)
        {
            var length = MathF.Sqrt(x * x + y * y + z * z);
            return length <= 0f ? (0f, 0f, 1f) : (x / length, y / length, z / length);
        }
    }
}
