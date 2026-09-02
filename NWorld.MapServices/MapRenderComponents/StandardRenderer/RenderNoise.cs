using System;
using SkiaSharp;

namespace NWorld.MapServices.MapRenderComponents
{
    /// <summary>
    /// The deterministic noise and colour arithmetic the render functions build their textures
    /// out of. Everything here is a pure function of its arguments and stable across processes
    /// and runtimes, which is what lets a tile be rebuilt on demand and come out identical.
    /// </summary>
    internal static class RenderNoise
    {
        private static readonly float[] OctavePhases = [0.37f, 0.71f, 0.13f, 0.59f];

        /// <summary>
        /// Stable 32-bit mix of two coordinates. Deliberately not GetHashCode: this has to
        /// produce the same value across processes and runtimes.
        /// </summary>
        public static uint Hash(int x, int y, uint seed)
        {
            var h = seed + (uint)x * 0x9E3779B1u + (uint)y * 0x85EBCA77u;
            h ^= h >> 15;
            h *= 0x2C1B3C6Du;
            h ^= h >> 12;
            h *= 0x297A2D39u;
            h ^= h >> 15;
            return h;
        }

        public static float Hash01(int x, int y, uint seed) => (Hash(x, y, seed) >> 8) * (1f / 16777216f);

        /// <summary>
        /// Value noise on a lattice that repeats every <paramref name="period"/> cells, so a
        /// field sampled over u,v in [0,1) has matching opposite edges.
        /// </summary>
        public static float PeriodicValueNoise(float x, float y, int period, uint seed)
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

        /// <summary>Stacked octaves of <see cref="PeriodicValueNoise"/>; u and v are 0..1 across the field.</summary>
        public static float PeriodicFbm(float u, float v, int period, int octaves, uint seed)
        {
            float sum = 0f, amplitude = 0.5f, total = 0f;
            for (var i = 0; i < octaves; i++)
            {
                // Shifting each octave off the lattice origin matters more than it looks.
                // Unshifted, every octave has a lattice node exactly on the field edge, where
                // the noise takes its raw value instead of an interpolated one -- so the edge
                // pixels have visibly different statistics from the interior and a map tiled
                // with the result ends up with a faint grid drawn on it. The shift is a
                // constant, so the field is still periodic and still wraps.
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

        /// <summary>
        /// Value noise round a circle, with <paramref name="lobes"/> of them in a full turn and
        /// matching values either side of the seam at pi. Added to a radius it gives an outline
        /// an irregular edge that still closes on itself -- a lump rather than a disc -- which
        /// is what the resource pieces are drawn out of.
        /// </summary>
        public static float PeriodicAngularNoise(float angle, int lobes, uint seed)
        {
            var t = angle * (lobes / MathF.Tau);
            var i = (int)MathF.Floor(t);
            var f = Smoothstep(0f, 1f, t - i);

            return Lerp(
                Hash01(Wrap(i, lobes), 0, seed),
                Hash01(Wrap(i + 1, lobes), 0, seed),
                f);
        }

        public static int Wrap(int value, int period)
        {
            var m = value % period;
            return m < 0 ? m + period : m;
        }

        public static float Lerp(float a, float b, float t) => a + (b - a) * t;

        public static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;

        public static float Smoothstep(float edge0, float edge1, float v)
        {
            var t = Clamp01((v - edge0) / (edge1 - edge0));
            return t * t * (3f - 2f * t);
        }

        public static SKColor LerpColor(SKColor a, SKColor b, float t)
        {
            t = Clamp01(t);
            return new SKColor(
                (byte)(a.Red + (b.Red - a.Red) * t),
                (byte)(a.Green + (b.Green - a.Green) * t),
                (byte)(a.Blue + (b.Blue - a.Blue) * t));
        }

        public static SKColor Shade(SKColor color, float amount)
        {
            var factor = 1f + amount;
            return new SKColor(
                (byte)Clamp255(color.Red * factor),
                (byte)Clamp255(color.Green * factor),
                (byte)Clamp255(color.Blue * factor));
        }

        public static float Clamp255(float v) => v < 0f ? 0f : v > 255f ? 255f : v;

        /// <summary>xorshift32: tiny, deterministic, and plenty for scattering things about.</summary>
        public struct Rng
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
