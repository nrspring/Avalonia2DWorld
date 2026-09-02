using System;
using System.Collections.Concurrent;
using SkiaSharp;

namespace NWorld.MapServices.MapRenderComponents
{
    /// <summary>
    /// A coarse field sampled in <b>map</b> space, one texel per tile, laid over a ground layer
    /// to give it broad variation that runs across tile boundaries instead of stopping at them.
    /// <para>
    /// Every ground type needs this and needs it for the same reason. A ground drawn from a
    /// repeating source -- a pool of variants, or a texture that tiles every few tiles -- has a
    /// period, and a period is what the eye finds. Anything broad enough to notice therefore
    /// has to come from somewhere with no such period, which is what this is: the grass gets
    /// its light and dark stretches from one, the water its shallows and deeps.
    /// </para>
    /// <para>
    /// Applied with <see cref="SKBlendMode.Overlay"/>, under which neutral grey is a no-op, so
    /// texels either side of 128 darken or lighten what is already there. Build texel values
    /// with <see cref="Level"/> to stay on that convention.
    /// </para>
    /// </summary>
    internal sealed class MapOverlayField(Func<float, float, SKColor> texel)
    {
        /// <summary>
        /// Field size in texels, which is also how many tiles it spans before repeating. Long
        /// enough that its own period is not the one anybody notices.
        /// </summary>
        public const int Period = 128;

        private readonly Lazy<SKImage> _image = new(() => Build(texel), isThreadSafe: true);
        private readonly ConcurrentDictionary<int, SKPaint> _paints = new();

        /// <summary>
        /// A texel value around the neutral grey that <see cref="SKBlendMode.Overlay"/> leaves
        /// alone: <paramref name="noise"/> of 0.5 is no change, 0 and 1 are full strength
        /// either way.
        /// </summary>
        public static float Level(float noise, float strength) => 128f + (noise - 0.5f) * 2f * strength;

        /// <summary>
        /// The paint that lays the field over a tile of <paramref name="tileSize"/> pixels.
        /// Cached per tile size and only ever read after construction, so it is safe to share
        /// across draws.
        /// <para>
        /// The shader is scaled and never translated, which anchors it to the canvas rather
        /// than to whatever rect is being filled. That is not incidental: it is the
        /// precondition <see cref="TileRuns.Fill"/> relies on to cover a whole run of tiles
        /// with one rect and get the pixels it would have got from one rect each. Adding a
        /// translation here would quietly break every caller's run coalescing.
        /// </para>
        /// </summary>
        public SKPaint PaintFor(int tileSize) =>
            _paints.GetOrAdd(tileSize, size => new SKPaint
            {
                // One texel per tile, so a tile samples its own texel centre and blends
                // smoothly towards its neighbours' rather than stopping at the edge.
                Shader = SKShader.CreateImage(
                    _image.Value,
                    SKShaderTileMode.Repeat,
                    SKShaderTileMode.Repeat,
                    SKMatrix.CreateScale(size, size)),
                BlendMode = SKBlendMode.Overlay,
                FilterQuality = SKFilterQuality.Low,
                IsAntialias = false,
            });

        /// <summary>
        /// Drops the cached paints; they are rebuilt on the next draw. The field image itself
        /// is kept -- it is one small bitmap and it never changes.
        /// </summary>
        public void Clear()
        {
            foreach (var key in _paints.Keys)
            {
                if (_paints.TryRemove(key, out var paint))
                {
                    paint.Shader?.Dispose();
                    paint.Dispose();
                }
            }
        }

        private static SKImage Build(Func<float, float, SKColor> texel)
        {
            var pixels = new byte[Period * Period * 4];
            var scale = 1f / Period;
            var i = 0;

            for (var ty = 0; ty < Period; ty++)
            {
                for (var tx = 0; tx < Period; tx++)
                {
                    var color = texel(tx * scale, ty * scale);

                    pixels[i++] = color.Red;
                    pixels[i++] = color.Green;
                    pixels[i++] = color.Blue;
                    pixels[i++] = 0xFF;
                }
            }

            var info = new SKImageInfo(Period, Period, SKColorType.Rgba8888, SKAlphaType.Opaque);
            return SKImage.FromPixelCopy(info, pixels);
        }
    }
}
