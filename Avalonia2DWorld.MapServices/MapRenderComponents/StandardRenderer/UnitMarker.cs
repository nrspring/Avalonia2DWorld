using System;
using System.Threading.Tasks;
using Avalonia2DWorld.Map.Models;
using SkiaSharp;

namespace Avalonia2DWorld.MapServices.MapRenderComponents.StandardRenderer
{
    /// <summary>
    /// Whatever is standing on a tile, drawn once per zoom level and repeated across the tiles
    /// that carry it.
    /// <para>
    /// The same arrangement <see cref="ResourceMarker"/> and <see cref="TileHighlight"/> use,
    /// and for their reasons. A unit is an overlay -- it runs above the ground and above
    /// anything built on it, so it carries alpha and lets what is underneath through; and every
    /// tile holding one shows the same thing, so it is a canvas-anchored repeat shader rather
    /// than a sprite blitted per tile, and a run of them costs a single rect.
    /// </para>
    /// <para>
    /// Identical from tile to tile, which for a unit matters more than it does for a deposit.
    /// Ore that varied a little would still read as ore; a body of men that varied would read as
    /// a <em>different</em> body of men, and telling one unit from another at a glance is most
    /// of what a unit marker is for. Whatever irregularity a unit wants -- and a militia wants a
    /// good deal -- belongs inside the sprite, where it is the same irregularity everywhere,
    /// rather than between copies of it.
    /// </para>
    /// <para>
    /// The sprite is drawn into a surface rather than sampled per pixel, which is where this
    /// parts company with the two above. They draw fields -- coverage at a point, a distance
    /// from an edge -- and a loop is the natural way to say that. A unit is figures: bodies,
    /// heads, a shadow, a spear held at an angle. Those are strokes and fills, and writing them
    /// as a sampler would mean solving each of them backwards for every pixel of the tile.
    /// </para>
    /// <para>
    /// Everything drawn must fit inside the tile, shadow and weapons included. The shader
    /// repeats, so a figure hanging over an edge does not lean onto the next tile: it reappears
    /// against the far edge of its own.
    /// </para>
    /// </summary>
    /// <param name="draw">
    /// Lays one unit into a canvas whose origin is the top-left of the tile and whose extent is
    /// <c>tileSize</c> square. Called once per zoom level.
    /// </param>
    internal sealed class UnitMarker(Action<SKCanvas, int> draw)
    {
        /// <summary>Tiny -- one tile-sized image per zoom level -- but bounded on principle.</summary>
        private const long MaxCacheBytes = 16L * 1024 * 1024;

        private readonly ZoomLevelCache<Sprite> _sprites = new(MaxCacheBytes, size => Build(draw, size));

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

        private static Sprite Build(Action<SKCanvas, int> draw, int tileSize)
        {
            var info = new SKImageInfo(tileSize, tileSize, SKColorType.Rgba8888, SKAlphaType.Premul);

            using var surface = SKSurface.Create(info);

            // Transparent, and left that way everywhere the unit does not cover: this is laid
            // over ground somebody has already drawn.
            surface.Canvas.Clear(SKColors.Transparent);

            // A backstop and not a licence. The layouts are meant to fit inside the tile on
            // their own -- a spear laid out to reach past the edge is a spear drawn wrong -- but
            // the failure if one ever does is far worse than a clipped weapon: the shader
            // repeats, so what hangs over an edge does not lean onto the next tile, it comes
            // back against the far edge of its own, and a unit sprouts half a spear on the wrong
            // side of itself.
            surface.Canvas.ClipRect(SKRect.Create(tileSize, tileSize));

            draw(surface.Canvas, tileSize);

            var image = surface.Snapshot();

            return new Sprite
            {
                Image = image,
                Paint = new SKPaint
                {
                    // No local matrix, so the sprite is anchored to the canvas and one copy
                    // lands on each tile -- which is also what lets TileRuns.Fill cover a whole
                    // run with a single rect.
                    Shader = SKShader.CreateImage(image, SKShaderTileMode.Repeat, SKShaderTileMode.Repeat),

                    // Nearest and drawn at 1:1. The sprite was built at this exact tile size and
                    // its own antialiasing is baked in, so there is nothing for filtering to
                    // improve -- and at the sprite edge it would bleed the neighbouring repeat
                    // back across the boundary.
                    FilterQuality = SKFilterQuality.None,
                    IsAntialias = false,
                    BlendMode = SKBlendMode.SrcOver,
                },
                Bytes = 4L * tileSize * tileSize,
            };
        }
    }
}
