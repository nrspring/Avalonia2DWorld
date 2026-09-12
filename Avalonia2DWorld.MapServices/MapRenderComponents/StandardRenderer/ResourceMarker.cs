using System;
using System.Threading.Tasks;
using Avalonia2DWorld.Map.Models;
using SkiaSharp;
using static Avalonia2DWorld.MapServices.MapRenderComponents.StandardRenderer.RenderNoise;

namespace Avalonia2DWorld.MapServices.MapRenderComponents.StandardRenderer
{
    /// <summary>
    /// What one piece of a deposit looks like at a point: how much of the point it covers, and
    /// in what colour. Coverage between 0 and 1 is the edge, which is where a shape gets to say
    /// how hard or soft it is.
    /// </summary>
    internal readonly record struct PieceSample(float Coverage, SKColor Color);

    /// <summary>
    /// One piece of a deposit -- an ore chunk, a tree, a pool of oil -- sampled at
    /// (<paramref name="dx"/>, <paramref name="dy"/>), measured from its centre in units of its
    /// own radius, with y downwards.
    /// <para>
    /// Must return no coverage outside the box the marker samples -- see
    /// <see cref="ResourceMarker.Reach"/>. Everything a piece draws has to fit inside it,
    /// because the sprite repeats: a piece hanging over an edge does not spill onto the
    /// neighbouring tile, it reappears against the far edge of its own.
    /// </para>
    /// <para>
    /// <paramref name="index"/> identifies the piece within the deposit and is fixed for the
    /// life of the sprite, so a shape can hash on it to vary height, facets or tone from one
    /// piece to the next and still be deterministic.
    /// </para>
    /// </summary>
    internal delegate PieceSample ResourcePiece(float dx, float dy, int index);

    /// <summary>
    /// How a deposit is arranged on its tile. What the material looks like is the piece shape's
    /// business; this is only the scatter.
    /// </summary>
    /// <param name="Seed">Fixes the scatter. Two resources sharing a seed would drop their pieces in the same places.</param>
    /// <param name="Pieces">How many lumps the deposit is made of.</param>
    /// <param name="Radius">A piece's radius, as a fraction of the tile.</param>
    /// <param name="Spread">How far from the tile centre the pieces scatter, as a fraction of the tile.</param>
    /// <param name="SizeJitter">How much piece radii vary, 0 to 1.</param>
    /// <param name="Shadow">What the pieces cast onto the ground beneath them.</param>
    /// <param name="ShadowAlpha">How dark that is at its darkest.</param>
    internal readonly record struct ResourceStyle(
        uint Seed,
        int Pieces,
        float Radius,
        float Spread,
        float SizeJitter,
        SKColor Shadow,
        byte ShadowAlpha);

    /// <summary>
    /// A resource deposit sitting on a tile: a few pieces scattered about the middle, each with
    /// a shadow under it, and ground showing through everywhere else.
    /// <para>
    /// Built like <see cref="TileHighlight"/> rather than like a ground, and for its reasons.
    /// This is an overlay, so it carries alpha and is composited over whatever has already been
    /// drawn; every marked tile shows the same mark, so it is one tile-sized sprite in a repeat
    /// shader anchored to the canvas rather than a sprite blitted per tile, and a run of tiles
    /// costs a single rect.
    /// </para>
    /// <para>
    /// Deliberately identical from tile to tile, which is the opposite of what the grounds do.
    /// A ground has to hide the grid, so it varies wherever it can; a deposit has to be
    /// recognised at a glance across a whole map, and a mark that changed shape tile to tile
    /// would read as a different thing rather than as more of the same one.
    /// </para>
    /// <para>
    /// The shadow lives here and not in the shapes because it is not about the material: it is
    /// what keeps a dark ore chunk legible over bog and a pale boulder legible over sand, which
    /// every deposit needs equally.
    /// </para>
    /// </summary>
    internal sealed class ResourceMarker
    {
        /// <summary>Tiny -- one tile-sized image per zoom level -- but bounded on principle.</summary>
        private const long MaxCacheBytes = 16L * 1024 * 1024;

        /// <summary>
        /// How far out a piece is sampled, in radii. Anything a shape draws past this is simply
        /// not there, and the placement below reserves room for exactly this much.
        /// </summary>
        public const float Reach = 1.05f;

        /// <summary>
        /// Samples per axis. The shapes are curved and the sprite is only ever drawn at 1:1, so
        /// nothing else is going to smooth their edges; supersampling the build is much the
        /// cheapest place to do it, happening once per zoom level rather than once per frame.
        /// </summary>
        private const int Samples = 2;

        // The shadow is the piece's own silhouette, slightly larger and pushed down and right.
        // Cheaper than a blur and, at the size these are drawn, not tellable from one.
        private const float ShadowScale = 1.16f;
        private const float ShadowOffsetX = 0.14f;
        private const float ShadowOffsetY = 0.20f;

        private readonly ResourceStyle _style;
        private readonly ResourcePiece _piece;
        private readonly ZoomLevelCache<Sprite> _sprites;

        public ResourceMarker(ResourceStyle style, ResourcePiece piece)
        {
            _style = style;
            _piece = piece;
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

        /// <summary>Where one piece sits on the sprite, in pixels.</summary>
        private readonly record struct Placement(float X, float Y, float Radius, int Index);

        private Sprite Build(int tileSize)
        {
            var image = BuildImage(tileSize);

            return new Sprite
            {
                Image = image,
                Paint = new SKPaint
                {
                    // No local matrix, so the sprite is anchored to the canvas and one copy
                    // lands on each tile -- the same arrangement, and the same precondition
                    // TileRuns.Fill needs to cover a run with one rect, as TileHighlight.
                    Shader = SKShader.CreateImage(image, SKShaderTileMode.Repeat, SKShaderTileMode.Repeat),

                    // Nearest: the sprite is built at the size it is drawn at, so there is
                    // nothing to interpolate, and filtering would bleed the neighbouring
                    // repeat's pieces in across the edge.
                    FilterQuality = SKFilterQuality.None,
                    IsAntialias = false,
                    BlendMode = SKBlendMode.SrcOver,
                },
                Bytes = 4L * tileSize * tileSize,
            };
        }

        /// <summary>
        /// Scatters the pieces, deterministically in the style's seed, so a deposit comes out
        /// the same arrangement at every zoom level and on every run.
        /// </summary>
        private Placement[] Place(int tileSize)
        {
            var count = Math.Max(1, _style.Pieces);
            var placements = new Placement[count];
            var rng = new Rng(_style.Seed);
            var centre = tileSize * 0.5f;

            for (var i = 0; i < count; i++)
            {
                var radius = _style.Radius * tileSize *
                    (1f + rng.Range(-_style.SizeJitter, _style.SizeJitter));

                // Round the middle at roughly even angles rather than at free positions. A
                // handful of independent draws clumps often enough to look like a mistake, and
                // a deposit wants to read as several things rather than as one blob.
                var angle = (i + rng.Range(-0.3f, 0.3f)) * (MathF.Tau / count);

                // Square-rooted so the pieces spread evenly over the disc instead of gathering
                // at its centre, and flattened in y because the ground is being looked at from
                // above and a little way in front.
                var distance = _style.Spread * tileSize * MathF.Sqrt(rng.NextFloat());
                var x = centre + MathF.Cos(angle) * distance;
                var y = centre + MathF.Sin(angle) * distance * 0.72f;

                // Everything a piece draws, its shadow included, has to land inside the sprite
                // -- see ResourcePiece for why an overhang is worse here than a clip.
                var margin = radius * (Reach * ShadowScale + ShadowOffsetY);
                var limit = tileSize - margin;

                placements[i] = new Placement(
                    margin >= limit ? centre : Math.Clamp(x, margin, limit),
                    margin >= limit ? centre : Math.Clamp(y, margin, limit),
                    radius,
                    i);
            }

            // Back to front, so a piece further down the tile overlaps one standing behind it
            // rather than the other way about. The index travels with the placement, so sorting
            // changes what covers what and not how any piece looks.
            Array.Sort(placements, static (a, b) => a.Y.CompareTo(b.Y));

            return placements;
        }

        private SKImage BuildImage(int tileSize)
        {
            var pieces = Place(tileSize);
            var pixels = new byte[tileSize * tileSize * 4];
            var step = 1f / Samples;
            var perSample = 1f / (Samples * Samples);

            var i = 0;
            for (var py = 0; py < tileSize; py++)
            {
                for (var px = 0; px < tileSize; px++)
                {
                    var pixel = default(Premultiplied);

                    for (var sy = 0; sy < Samples; sy++)
                    {
                        for (var sx = 0; sx < Samples; sx++)
                            pixel.Add(Sample(pieces, px + (sx + 0.5f) * step, py + (sy + 0.5f) * step));
                    }

                    pixel.Scale(perSample);

                    var alpha = (byte)MathF.Round(Clamp01(pixel.A) * 255f);

                    // Held down to the alpha as well as to 255: the image is premultiplied, and
                    // a channel rounded up past its own alpha is a colour brighter than opaque,
                    // which Skia is entitled to make a mess of.
                    pixels[i++] = Channel(pixel.R, alpha);
                    pixels[i++] = Channel(pixel.G, alpha);
                    pixels[i++] = Channel(pixel.B, alpha);
                    pixels[i++] = alpha;
                }
            }

            var info = new SKImageInfo(tileSize, tileSize, SKColorType.Rgba8888, SKAlphaType.Premul);
            return SKImage.FromPixelCopy(info, pixels);
        }

        /// <summary>What the deposit looks like at one point on the sprite: shadows, then pieces.</summary>
        private Premultiplied Sample(Placement[] pieces, float x, float y)
        {
            var pixel = default(Premultiplied);

            // Every shadow first, and as a single silhouette -- the strongest of them rather
            // than each composited in turn -- so two pieces standing close together do not
            // darken the ground twice where their shadows overlap.
            var shadow = 0f;
            foreach (var piece in pieces)
            {
                var scale = 1f / (piece.Radius * ShadowScale);
                var dx = (x - piece.X - piece.Radius * ShadowOffsetX) * scale;
                var dy = (y - piece.Y - piece.Radius * ShadowOffsetY) * scale;

                if (MathF.Abs(dx) > Reach || MathF.Abs(dy) > Reach)
                    continue;

                shadow = MathF.Max(shadow, _piece(dx, dy, piece.Index).Coverage);
            }

            pixel.Over(_style.Shadow, shadow * (_style.ShadowAlpha / 255f));

            foreach (var piece in pieces)
            {
                var scale = 1f / piece.Radius;
                var dx = (x - piece.X) * scale;
                var dy = (y - piece.Y) * scale;

                if (MathF.Abs(dx) > Reach || MathF.Abs(dy) > Reach)
                    continue;

                var sample = _piece(dx, dy, piece.Index);
                pixel.Over(sample.Color, sample.Coverage);
            }

            return pixel;
        }

        private static byte Channel(float value, byte alpha) =>
            (byte)MathF.Min(MathF.Round(Clamp255(value)), alpha);

        /// <summary>
        /// A colour being built up: premultiplied, because that is what compositing wants and
        /// what the image stores, and unrounded, because a subsample is a fraction of a pixel
        /// and rounding one would throw away the detail supersampling was for.
        /// </summary>
        private struct Premultiplied
        {
            public float R, G, B, A;

            /// <summary>Lays <paramref name="color"/> over whatever is already here.</summary>
            public void Over(SKColor color, float coverage)
            {
                var alpha = Clamp01(coverage);
                if (alpha <= 0f)
                    return;

                var behind = 1f - alpha;
                R = color.Red * alpha + R * behind;
                G = color.Green * alpha + G * behind;
                B = color.Blue * alpha + B * behind;
                A = alpha + A * behind;
            }

            public void Add(Premultiplied other)
            {
                R += other.R;
                G += other.G;
                B += other.B;
                A += other.A;
            }

            public void Scale(float factor)
            {
                R *= factor;
                G *= factor;
                B *= factor;
                A *= factor;
            }
        }
    }
}
