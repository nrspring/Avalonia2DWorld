using System;
using System.Globalization;
using System.Threading.Tasks;
using NWorld.Map.Models;
using SkiaSharp;

namespace NWorld.MapServices.MapRenderComponents.StandardRenderer
{
    /// <summary>
    /// Draws one shape of a span: whichever arms it has, at one tile size, in one of its
    /// variants. What a road looks like and what a bridge looks like are the only things that
    /// differ between them.
    /// </summary>
    /// <param name="arms">Which sides the span reaches to, as <see cref="RoadNetwork"/> bits.</param>
    /// <param name="seed">Already mixed with the variant, so two variants differ throughout.</param>
    internal delegate void SpanArt(SKCanvas canvas, int tileSize, int arms, uint seed);

    /// <summary>
    /// Something built along the enhancement layer that joins up with its neighbours: a road, a
    /// bridge, and whatever comes next.
    /// <para>
    /// The shape of a tile is not stored on the tile. It is read off the map at draw time from
    /// the four neighbours -- see <see cref="RoadNetwork"/> -- so laying one next to another
    /// makes both fit together on the very next frame. Sixteen shapes fall out of four
    /// neighbours, and they are the usual set: a crossing, four tees, four corners, two
    /// straights, four stubs, and the one with no neighbours at all. That last is the only tile
    /// with nothing to tell it which way to lie, so it is the only one that reads the rotation
    /// in its parameters.
    /// </para>
    /// <para>
    /// Drawn from a sprite per shape per variant per zoom level rather than stroked per tile, so
    /// a tile costs one blit. Built like <see cref="TileHighlight"/> and used the same way: a
    /// render function holds one of these, hands it the art, and is otherwise nothing but a
    /// name and a component type.
    /// </para>
    /// </summary>
    internal sealed class StoneSpan
    {
        /// <summary>
        /// How many shapes there are: sixteen from the neighbours, and the lone tile counted
        /// twice because it can lie either way.
        /// </summary>
        private const int Shapes = 17;

        /// <summary>Where the second orientation of the lone tile sits in the sprite set.</summary>
        private const int LoneUpright = 16;

        /// <summary>
        /// How many differently laid versions of each shape there are.
        /// <para>
        /// Four. One version of a shape means every straight on the map is the same straight,
        /// and a mile of road comes out as a stencil repeated -- which is the single loudest
        /// tell that a thing was drawn rather than built. Four is enough that the eye stops
        /// finding the period at a glance; the whole set is still under a megabyte at the
        /// largest zoom.
        /// </para>
        /// </summary>
        private const int Variants = 4;

        /// <summary>Below this nothing is drawn -- there is nothing legible left.</summary>
        public const int MinTileSize = 4;

        private readonly uint _seed;
        private readonly SpanArt _art;
        private readonly ZoomLevelCache<Sprites> _cache;

        /// <param name="seed">Fixes the wear and the tone of every stone in this kind of span.</param>
        /// <param name="art">What one shape of it looks like.</param>
        public StoneSpan(uint seed, SpanArt art)
        {
            ArgumentNullException.ThrowIfNull(art);

            _seed = seed;
            _art = art;
            _cache = new ZoomLevelCache<Sprites>(24L * 1024 * 1024, Build);
        }

        /// <summary>Draws every tile in the batch, each in the shape its neighbours call for.</summary>
        public Task Render(TileRenderContext context)
        {
            var canvas = context.Canvas;
            var tileSize = context.TileSize;
            var tiles = context.Tiles;

            if (canvas is null || tileSize < MinTileSize || tiles.Length == 0)
                return Task.CompletedTask;

            var sprites = _cache.Get(tileSize);
            var world = context.World;

            foreach (var tile in tiles)
            {
                var shape = world is null
                    ? Lone(tile.Params)
                    : Shape(world, tile.X, tile.Y, tile.Params);

                // Which laying this tile got, from where it is: fixed for the tile, so it does
                // not reshuffle itself as the map is panned or the zoom changes, and different
                // from its neighbour's, which is the whole point of having four.
                var variant = (int)(RenderNoise.Hash(tile.X, tile.Y, _seed) % Variants);

                canvas.DrawImage(
                    sprites.Shape[(shape * Variants) + variant],
                    new SKPoint(tile.X * tileSize, tile.Y * tileSize));
            }

            return Task.CompletedTask;
        }

        /// <inheritdoc cref="ZoomLevelCache{T}.Prewarm"/>
        public Task Prewarm(int tileSize) =>
            tileSize < MinTileSize ? Task.CompletedTask : _cache.Prewarm(tileSize);

        /// <inheritdoc cref="ZoomLevelCache{T}.Clear"/>
        public void ClearCache() => _cache.Clear();

        /// <summary>
        /// Which of the shapes a tile takes: its neighbour mask, or one of the two lone
        /// orientations when it has no neighbours at all.
        /// </summary>
        private static int Shape(TileGrid world, int x, int y, string[] parameters)
        {
            var mask = RoadNetwork.Mask(world, x, y);

            return mask == 0 ? Lone(parameters) : mask;
        }

        /// <summary>
        /// Which way a span with no neighbours lies, from the rotation in its parameters: an odd
        /// number of quarter turns stands it upright, anything else lays it flat.
        /// <para>
        /// The one thing about a span that is stored rather than worked out, because it is the
        /// one thing the map cannot answer. A tile with any neighbour at all takes its shape
        /// from them and this is not consulted.
        /// </para>
        /// </summary>
        private static int Lone(string[] parameters) =>
            parameters.Length > 0
            && int.TryParse(parameters[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var quarters)
            && (((quarters % 2) + 2) % 2) == 1
                ? LoneUpright
                : 0;

        /// <summary>Builds every shape, in every variant, at one tile size.</summary>
        private Sprites Build(int tileSize)
        {
            var images = new SKImage[Shapes * Variants];
            var info = new SKImageInfo(tileSize, tileSize, SKColorType.Rgba8888, SKAlphaType.Premul);

            for (var shape = 0; shape < Shapes; shape++)
            {
                for (var variant = 0; variant < Variants; variant++)
                {
                    using var surface = SKSurface.Create(info);

                    if (surface is null)
                    {
                        // Nothing to draw with. An empty sprite is a span that does not show,
                        // which is a great deal better than a frame that throws.
                        images[(shape * Variants) + variant] = SKImage.FromBitmap(new SKBitmap(info));
                        continue;
                    }

                    surface.Canvas.Clear(SKColors.Transparent);
                    Draw(surface.Canvas, tileSize, shape, variant);

                    images[(shape * Variants) + variant] = surface.Snapshot();
                }
            }

            return new Sprites(images, (long)tileSize * tileSize * 4 * Shapes * Variants);
        }

        /// <summary>Draws one shape in one variant, turning the canvas for the lone upright.</summary>
        private void Draw(SKCanvas canvas, int tileSize, int shape, int variant)
        {
            // The lone upright is the lone flat turned a quarter, which is the whole of what the
            // rotation does -- so it is drawn by turning the canvas rather than by a second set
            // of arms.
            if (shape == LoneUpright)
            {
                canvas.Save();
                canvas.RotateDegrees(90, tileSize / 2f, tileSize / 2f);
                Draw(canvas, tileSize, 0, variant);
                canvas.Restore();
                return;
            }

            // A tile with no neighbours still gets a length of it rather than a blob: it is
            // something somebody has started, and a square of stone reads as a floor.
            var arms = shape == 0 ? RoadNetwork.East | RoadNetwork.West : shape;

            _art(canvas, tileSize, arms, _seed + ((uint)variant * 0x9E3779B9u));
        }

        /// <summary>One sprite per shape per variant at one zoom level.</summary>
        private sealed class Sprites : IZoomLevelResource
        {
            public Sprites(SKImage[] shapes, long bytes)
            {
                Shape = shapes;
                Bytes = bytes;
            }

            public SKImage[] Shape { get; }

            public long Bytes { get; }

            public void Dispose()
            {
                foreach (var image in Shape)
                    image.Dispose();
            }
        }
    }
}
