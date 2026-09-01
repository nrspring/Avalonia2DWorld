using System;
using System.Threading.Tasks;
using NWorld.Map.Models;
using SkiaSharp;

namespace NWorld.MapServices.MapRenderComponents
{
    /// <summary>
    /// Fills the pixels of one block-sized texture. <paramref name="edge"/> is the block in
    /// pixels and <paramref name="tilesPerBlock"/> how many tiles that spans; the buffer is
    /// RGBA and must be left fully opaque.
    /// <para>
    /// Whatever is drawn has to be periodic across the block in both directions, or the
    /// repeated copies will not meet. In practice that means every feature comes from
    /// <see cref="RenderNoise.PeriodicFbm"/> or from a whole number of cycles across the block.
    /// </para>
    /// </summary>
    internal delegate void GroundPainter(byte[] pixels, int edge, int tilesPerBlock);

    /// <summary>
    /// A static ground drawn as one seamlessly tiling texture repeated over the batch, with a
    /// map-space field over it for the broad variation.
    /// <para>
    /// This is the water renderer's arrangement without the animation, and it suits a ground
    /// whose character is continuous rather than clumped -- ripples that run across tiles,
    /// pools that spread over several. A pool of per-tile variants, as the grass uses, cannot
    /// do that: two neighbours drawing different variants disagree along their shared edge, so
    /// nothing may cross it. One texture that wraps against itself is by construction
    /// continuous, because every tile is its neighbour.
    /// </para>
    /// <para>
    /// The cost of that is a period: the texture repeats every <see cref="MaxTilesPerBlock"/>
    /// tiles or so. Keep features small -- anything approaching the size of the block is what
    /// makes the repeat findable -- and leave everything broader to the overlay field, which
    /// has no such period.
    /// </para>
    /// </summary>
    internal sealed class TiledGround
    {
        /// <summary>
        /// Held near a fixed pixel size rather than a fixed number of tiles, so the cost barely
        /// moves with the zoom level.
        /// </summary>
        private const int TargetEdge = 384;

        private const int MinTilesPerBlock = 2;
        private const int MaxTilesPerBlock = 12;

        private const long MaxCacheBytes = 64L * 1024 * 1024;

        private readonly GroundPainter _painter;
        private readonly MapOverlayField _tone;
        private readonly ZoomLevelCache<Surface> _surfaces;

        public TiledGround(GroundPainter painter, MapOverlayField tone)
        {
            _painter = painter;
            _tone = tone;
            _surfaces = new ZoomLevelCache<Surface>(MaxCacheBytes, Build);
        }

        public Task Render(TileRenderContext context)
        {
            var canvas = context.Canvas;
            var tileSize = context.TileSize;
            var tiles = context.Tiles;

            if (canvas is null || tileSize <= 0 || tiles.Length == 0)
                return Task.CompletedTask;

            TileRuns.Fill(canvas, tiles, tileSize, _surfaces.Get(tileSize).Paint);
            TileRuns.Fill(canvas, tiles, tileSize, _tone.PaintFor(tileSize));

            // Last, over the finished ground -- see ElevationShade. Every ground built on this
            // gets height for nothing, which is the point of them sharing it.
            ElevationShade.Apply(context);

            return Task.CompletedTask;
        }

        /// <inheritdoc cref="ZoomLevelCache{T}.Prewarm"/>
        public Task Prewarm(int tileSize) => _surfaces.Prewarm(tileSize);

        /// <inheritdoc cref="ZoomLevelCache{T}.Clear"/>
        public void ClearCache()
        {
            _surfaces.Clear();
            _tone.Clear();
        }

        private sealed class Surface : IZoomLevelResource
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

        private Surface Build(int tileSize)
        {
            var tilesPerBlock = Math.Clamp(TargetEdge / tileSize, MinTilesPerBlock, MaxTilesPerBlock);
            var edge = tilesPerBlock * tileSize;

            var pixels = new byte[edge * edge * 4];
            _painter(pixels, edge, tilesPerBlock);

            var info = new SKImageInfo(edge, edge, SKColorType.Rgba8888, SKAlphaType.Opaque);
            var image = SKImage.FromPixelCopy(info, pixels);

            return new Surface
            {
                Image = image,
                Paint = new SKPaint
                {
                    // No local matrix: the texture is built at exactly the size it repeats at,
                    // so anchoring it to the canvas origin puts tile (0,0) on texture (0,0) and
                    // keeps every tile agreeing about where the ground is. That is also the
                    // precondition TileRuns.Fill needs to cover a run with one rect.
                    Shader = SKShader.CreateImage(image, SKShaderTileMode.Repeat, SKShaderTileMode.Repeat),
                    FilterQuality = SKFilterQuality.Low,
                    IsAntialias = false,
                },
                Bytes = 4L * edge * edge,
            };
        }
    }
}
