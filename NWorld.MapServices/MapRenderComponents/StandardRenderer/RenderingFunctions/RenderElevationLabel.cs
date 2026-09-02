using System;
using System.Threading.Tasks;
using NWorld.Map.Models;
using SkiaSharp;

namespace NWorld.MapServices.MapRenderComponents.RenderingFunctions
{
    /// <summary>
    /// Writes a number over the middle of a tile -- its elevation, taken from the component's
    /// first parameter rather than read off the tile, because a render function is given
    /// placements and never the map.
    /// <para>
    /// An instrument rather than scenery. It is meant to be readable over any ground the map
    /// can put under it, which is what the dark halo behind the glyphs is for: white alone
    /// disappears into a sunlit meadow and black alone into deep water, and an outlined glyph
    /// reads over both.
    /// </para>
    /// <para>
    /// Drawn from a glyph atlas with one <c>DrawAtlas</c> for the whole batch, the way the
    /// grass is -- not a <c>DrawText</c> per tile. A screenful at the smallest size this draws
    /// at is thousands of tiles, and thousands of text draws a frame is a cost the map view
    /// notices; the digits are eleven characters that never change, so an atlas per zoom level
    /// is small and the per-tile work is a quad per character.
    /// </para>
    /// </summary>
    public static class RenderElevationLabel
    {
        /// <summary>
        /// Below this the label is drawn as nothing at all.
        /// <para>
        /// Not a performance guard -- it is that a number a few pixels tall is unreadable, and
        /// a map covered in unreadable smudges is worse than a map with no labels on it. This
        /// is also what keeps the mini-map clear: the inset draws every tile at a size of a
        /// few pixels, so it falls under this on any map worth insetting.
        /// </para>
        /// </summary>
        private const int MinTileSize = 12;

        /// <summary>
        /// How many characters the glyph size is chosen to fit across a tile. A longer label
        /// is scaled down to fit rather than allowed to run over its neighbours, so this is
        /// the length that is drawn at full size rather than a limit.
        /// </summary>
        private const int FitCharacters = 3;

        /// <summary>
        /// Everything a label may be made of. A placement carrying anything else is skipped:
        /// the atlas has no cell for it, and a missing glyph should not be a missing frame.
        /// </summary>
        private const string Glyphs = "0123456789-";

        /// <summary>One atlas per zoom level, each a strip a few hundred pixels wide.</summary>
        private const long MaxCacheBytes = 8L * 1024 * 1024;

        /// <summary>
        /// Monospaced, so a two-digit label is twice a one-digit one and the atlas needs a
        /// single advance rather than one per glyph. Null on a machine without it, which Skia
        /// reads as "use the default" -- the layout still holds, since the advance is measured
        /// from whatever face is actually used.
        /// </summary>
        private static readonly SKTypeface? LabelTypeface = SKTypeface.FromFamilyName("Consolas");

        /// <summary>
        /// The glyphs are blitted at the size they were built at, so there is nothing for a
        /// filter to do at full size; Low is here for the long labels that are scaled down to
        /// fit, where nearest turns thin strokes into gaps. Read-only, hence shared.
        /// </summary>
        private static readonly SKPaint BlitPaint = new()
        {
            FilterQuality = SKFilterQuality.Low,
            IsAntialias = false,
        };

        private static readonly ZoomLevelCache<GlyphAtlas> Atlases = new(MaxCacheBytes, Build);

        /// <summary>
        /// Draws every label in the batch. Nothing here moves with
        /// <see cref="TileRenderContext.TimeSeconds"/>, so the atlas stays cached across frames.
        /// </summary>
        public static Task Render(TileRenderContext context)
        {
            var canvas = context.Canvas;
            var tileSize = context.TileSize;
            var tiles = context.Tiles;

            if (canvas is null || tileSize < MinTileSize || tiles.Length == 0)
                return Task.CompletedTask;

            // A quad per character, so the batch is walked once to count them before anything
            // is built: DrawAtlas takes whole arrays and they have to be exactly as long as
            // the draw. Labels are a handful of characters, so this is far cheaper than the
            // alternative of growing the buffers mid-fill.
            var quads = 0;
            foreach (var tile in tiles)
                quads += Label(tile.Params)?.Length ?? 0;

            if (quads == 0)
                return Task.CompletedTask;

            var atlas = Atlases.Get(tileSize);
            var (sprites, transforms) = SpriteBatch.Reserve(quads);

            var next = 0;

            foreach (var tile in tiles)
            {
                if (Label(tile.Params) is not { } text)
                    continue;

                // Longer than the size was chosen for, so it is shrunk to fit rather than
                // spilling onto the tiles either side -- two labels running together read as
                // one wrong number.
                var scale = text.Length > FitCharacters ? (float)FitCharacters / text.Length : 1f;

                var step = atlas.Advance * scale;
                var left = (tile.X * tileSize) + ((tileSize - (step * text.Length)) / 2f);
                var top = (tile.Y * tileSize) + ((tileSize - (atlas.CellHeight * scale)) / 2f);

                for (var i = 0; i < text.Length; i++)
                {
                    sprites[next] = atlas.Source(text[i]);

                    // Whole pixels. The glyphs are small and their strokes are a pixel or two
                    // wide, so half a pixel of offset is the difference between a crisp digit
                    // and a grey one.
                    transforms[next] = new SKRotationScaleMatrix(
                        scale,
                        0f,
                        MathF.Round(left + (step * i) + ((step - (atlas.CellWidth * scale)) / 2f)),
                        MathF.Round(top));

                    next++;
                }
            }

            SpriteBatch.Draw(canvas, atlas.Image, sprites, transforms, next, BlitPaint);

            return Task.CompletedTask;
        }

        /// <inheritdoc cref="ZoomLevelCache{T}.Prewarm"/>
        public static Task Prewarm(int tileSize) =>
            tileSize < MinTileSize ? Task.CompletedTask : Atlases.Prewarm(tileSize);

        /// <inheritdoc cref="ZoomLevelCache{T}.Clear"/>
        public static void ClearCache() => Atlases.Clear();

        /// <summary>
        /// The text a placement carries, or null if there is nothing here that can be drawn.
        /// <para>
        /// Checked rather than trusted: the parameters are strings that came from a tile and
        /// may have come from a file before that, and a character with no cell in the atlas
        /// would index off the end of it.
        /// </para>
        /// </summary>
        private static string? Label(string[] parameters)
        {
            if (parameters.Length == 0 || string.IsNullOrEmpty(parameters[0]))
                return null;

            var text = parameters[0];

            foreach (var character in text)
            {
                if (Glyphs.IndexOf(character) < 0)
                    return null;
            }

            return text;
        }

        /// <summary>
        /// Every glyph a label can be made of, at one zoom level, laid out in a single row of
        /// equal cells.
        /// </summary>
        private sealed class GlyphAtlas : IZoomLevelResource
        {
            public required SKImage Image { get; init; }

            /// <summary>
            /// How far one character moves the pen along. Not the cell width: a cell is wider
            /// than the advance by the halo it has to hold, and stepping by the cell would
            /// leave the label spaced out by twice that.
            /// </summary>
            public required float Advance { get; init; }

            public required int CellWidth { get; init; }

            public required int CellHeight { get; init; }

            public required long Bytes { get; init; }

            /// <summary>
            /// Where one character sits in the atlas. Only ever called for a character
            /// <see cref="Label"/> has already found a cell for.
            /// </summary>
            public SKRect Source(char glyph) =>
                SKRect.Create(Glyphs.IndexOf(glyph) * CellWidth, 0, CellWidth, CellHeight);

            public void Dispose() => Image.Dispose();
        }

        /// <summary>
        /// Draws the eleven characters into one strip at a size that fits the tile.
        /// <para>
        /// The size is measured rather than assumed. A face has its own idea of how wide a
        /// digit is at a given text size, and the fallback face used where Consolas is missing
        /// will have a different one; measuring is what keeps the label inside the tile on
        /// both.
        /// </para>
        /// </summary>
        private static GlyphAtlas Build(int tileSize)
        {
            using var pen = new SKPaint
            {
                Typeface = LabelTypeface,
                TextSize = tileSize * 0.52f,
                TextAlign = SKTextAlign.Center,
                IsAntialias = true,
            };

            // A pixel of clearance either side, so a full-width label does not sit against
            // the tile edge and touch its neighbour's.
            var room = tileSize - 2f;
            var advance = pen.MeasureText("0");

            if (advance > 0f && advance * FitCharacters > room)
            {
                pen.TextSize *= room / (advance * FitCharacters);
                advance = pen.MeasureText("0");
            }

            // Centred on the glyph outline, so half of it falls outside; the cell is padded by
            // the whole width rather than half of it, since the join at a corner reaches
            // further than the stroke is wide.
            var halo = MathF.Max(1f, pen.TextSize / 6f);
            var pad = (int)MathF.Ceiling(halo) + 1;

            var metrics = pen.FontMetrics;

            var cellWidth = (int)MathF.Ceiling(advance) + (pad * 2);
            var cellHeight = (int)MathF.Ceiling(metrics.Descent - metrics.Ascent) + (pad * 2);

            var info = new SKImageInfo(
                cellWidth * Glyphs.Length, cellHeight, SKColorType.Rgba8888, SKAlphaType.Premul);

            SKImage image;

            using (var bitmap = new SKBitmap(info))
            {
                using (var canvas = new SKCanvas(bitmap))
                {
                    canvas.Clear(SKColors.Transparent);

                    using var outline = new SKPaint
                    {
                        Typeface = pen.Typeface,
                        TextSize = pen.TextSize,
                        TextAlign = SKTextAlign.Center,
                        IsAntialias = true,
                        Style = SKPaintStyle.Stroke,
                        StrokeWidth = halo,

                        // Round, so the halo has no spurs at the sharp corners of a 4 or a 7.
                        StrokeJoin = SKStrokeJoin.Round,
                        StrokeCap = SKStrokeCap.Round,

                        // Not quite black and not quite opaque: a hard black box around every
                        // digit is heavier than the number it is there to make legible.
                        Color = new SKColor(0x0A, 0x0E, 0x14, 210),
                    };

                    using var fill = new SKPaint
                    {
                        Typeface = pen.Typeface,
                        TextSize = pen.TextSize,
                        TextAlign = SKTextAlign.Center,
                        IsAntialias = true,
                        Color = new SKColor(0xF2, 0xF6, 0xFF),
                    };

                    // The baseline sits one pad below the top of the cell, which puts the
                    // whole of the face's line box inside it -- ascender, descender and the
                    // halo around both.
                    var baseline = pad - metrics.Ascent;

                    for (var i = 0; i < Glyphs.Length; i++)
                    {
                        // Centred on the cell rather than laid out from its left edge: what
                        // should line up between one label and the next is the ink, and a
                        // hyphen carries far less of it than a digit does.
                        var x = (i * cellWidth) + (cellWidth / 2f);
                        var glyph = Glyphs[i].ToString();

                        canvas.DrawText(glyph, x, baseline, outline);
                        canvas.DrawText(glyph, x, baseline, fill);
                    }
                }

                image = SKImage.FromBitmap(bitmap);
            }

            return new GlyphAtlas
            {
                Image = image,
                Advance = advance,
                CellWidth = cellWidth,
                CellHeight = cellHeight,
                Bytes = 4L * info.Width * info.Height,
            };
        }
    }
}
