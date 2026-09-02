using System;
using System.Threading.Tasks;
using NWorld.Map.Models;
using SkiaSharp;
using static NWorld.MapServices.MapRenderComponents.StandardRenderer.RenderNoise;

namespace NWorld.MapServices.MapRenderComponents.StandardRenderer
{
    /// <summary>
    /// How one kind of tile highlight is coloured. The shape is shared between kinds and lives
    /// on <see cref="TileHighlight"/>; a style only changes the colours, so two highlights are
    /// told apart by hue alone and read as members of one family.
    /// </summary>
    /// <param name="Outline">The line just inside the tile edge.</param>
    /// <param name="Shadow">What falls inward from the edges behind it.</param>
    /// <param name="OutlineFraction">Outline thickness, as a fraction of the tile.</param>
    /// <param name="ShadowFraction">How far the shadow reaches in, as a fraction of the tile.</param>
    internal readonly record struct HighlightStyle(
        SKColor Outline,
        SKColor Shadow,
        byte OutlineAlpha,
        byte ShadowAlpha,
        float OutlineFraction,
        float ShadowFraction);

    /// <summary>
    /// Marks a tile: an outline just inside its edge, with a shadow falling inward from all
    /// four sides.
    /// <para>
    /// This is an overlay, not a ground: it runs on a layer above whatever base tile has
    /// already been drawn and has to let it through. So it is built with an alpha channel and
    /// composited, the centre is left completely clear, and the shadow is what keeps the
    /// outline legible over ground of any brightness -- a bare outline over ground of a
    /// similar tone would half disappear. Leaving the middle clear is the other half of that
    /// bargain: whatever is drawn on the tile afterwards, a unit above all, stays as readable
    /// as it was before the tile was marked.
    /// </para>
    /// <para>
    /// Every marked tile looks the same, so rather than one sprite blitted per tile this is a
    /// repeat shader anchored to the canvas, exactly as the water surface is. The sprite is one
    /// tile square and the grid is on tile boundaries, so repeating it lands one copy on each
    /// tile, and a whole run of them costs a single rect.
    /// </para>
    /// </summary>
    internal sealed class TileHighlight
    {
        /// <summary>Tiny -- one tile-sized image per zoom level -- but bounded on principle.</summary>
        private const long MaxCacheBytes = 16L * 1024 * 1024;

        private readonly HighlightStyle _style;
        private readonly ZoomLevelCache<Sprite> _sprites;

        public TileHighlight(HighlightStyle style)
        {
            _style = style;
            _sprites = new ZoomLevelCache<Sprite>(MaxCacheBytes, Build);
        }

        public Task Render(TileRenderContext context)
        {
            var canvas = context.Canvas;
            var tileSize = context.TileSize;
            var tiles = context.Tiles;

            if (canvas is null || tileSize <= 0 || tiles.Length == 0)
                return Task.CompletedTask;

            TileRuns.Fill(canvas, tiles, tileSize, _sprites.Get(tileSize).Paint);

            return Task.CompletedTask;
        }

        /// <inheritdoc cref="ZoomLevelCache{T}.Prewarm"/>
        public Task Prewarm(int tileSize) => _sprites.Prewarm(tileSize);

        /// <inheritdoc cref="ZoomLevelCache{T}.Clear"/>
        public void ClearCache() => _sprites.Clear();

        private sealed class Sprite : IZoomLevelResource
        {
            public required SKImage Image { get; init; }
            public required SKPaint Paint { get; init; }
            public required long Bytes { get; init; }

            public void Dispose()
            {
                Paint.Shader?.Dispose();
                Paint.Dispose();
                Image.Dispose();
            }
        }

        private Sprite Build(int tileSize)
        {
            var image = BuildImage(tileSize);

            return new Sprite
            {
                Image = image,
                Paint = new SKPaint
                {
                    // No local matrix, so the sprite is anchored to the canvas and one copy
                    // lands on each tile. That is also the precondition TileRuns.Fill needs to
                    // cover a run with a single rect -- see MapOverlayField.PaintFor.
                    Shader = SKShader.CreateImage(image, SKShaderTileMode.Repeat, SKShaderTileMode.Repeat),

                    // Nearest, to keep the outline crisp. Filtering it would both soften the
                    // line and, at the sprite edge, bleed the neighbouring repeat's outline
                    // into this tile's clear centre.
                    FilterQuality = SKFilterQuality.None,
                    IsAntialias = false,
                    BlendMode = SKBlendMode.SrcOver,
                },
                Bytes = 4L * tileSize * tileSize,
            };
        }

        /// <summary>
        /// Paints the sprite a pixel at a time rather than stroking a rect and blurring it.
        /// The shape is axis-aligned and lands on exact pixel boundaries, so there is nothing
        /// for antialiasing to do, and computing the shadow directly avoids the awkwardness of
        /// a stroke that must sit wholly inside the sprite -- half of a centred stroke would
        /// fall outside, and with a repeating shader it would reappear against the far edge.
        /// </summary>
        private SKImage BuildImage(int tileSize)
        {
            var pixels = new byte[tileSize * tileSize * 4];

            // Both are clamped so that even a handful of pixels across, the outline stays
            // visible and the shadow cannot close over the middle of the tile.
            var outline = Math.Clamp(
                (int)MathF.Round(tileSize * _style.OutlineFraction),
                1,
                Math.Max(1, tileSize / 4));
            var depth = Math.Min(tileSize * _style.ShadowFraction, tileSize / 2f - outline);

            var i = 0;
            for (var py = 0; py < tileSize; py++)
            {
                for (var px = 0; px < tileSize; px++)
                {
                    var left = px;
                    var right = tileSize - 1 - px;
                    var top = py;
                    var bottom = tileSize - 1 - py;

                    SKColor color;
                    byte alpha;

                    if (Math.Min(Math.Min(left, right), Math.Min(top, bottom)) < outline)
                    {
                        color = _style.Outline;
                        alpha = _style.OutlineAlpha;
                    }
                    else
                    {
                        // Each side contributes independently and they are combined the way
                        // overlapping shadows actually behave -- what one leaves through, the
                        // next attenuates again. Summing instead would clip to flat black in
                        // the corners, where two sides always overlap.
                        var reach =
                            (1f - Reach(left - outline, depth)) *
                            (1f - Reach(right - outline, depth)) *
                            (1f - Reach(top - outline, depth)) *
                            (1f - Reach(bottom - outline, depth));

                        color = _style.Shadow;
                        alpha = (byte)(_style.ShadowAlpha * (1f - reach));
                    }

                    // Premultiplied, to match the image's alpha type.
                    pixels[i++] = (byte)(color.Red * alpha / 255);
                    pixels[i++] = (byte)(color.Green * alpha / 255);
                    pixels[i++] = (byte)(color.Blue * alpha / 255);
                    pixels[i++] = alpha;
                }
            }

            var info = new SKImageInfo(tileSize, tileSize, SKColorType.Rgba8888, SKAlphaType.Premul);
            return SKImage.FromPixelCopy(info, pixels);
        }

        /// <summary>How strongly one side reaches a pixel <paramref name="distance"/> in from it.</summary>
        private static float Reach(float distance, float depth) =>
            depth <= 0f ? 0f : Smoothstep(depth, 0f, distance);
    }
}
