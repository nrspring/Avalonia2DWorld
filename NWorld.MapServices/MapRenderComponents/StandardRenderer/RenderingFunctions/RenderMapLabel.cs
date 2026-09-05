using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NWorld.Map.Models;
using SkiaSharp;

namespace NWorld.MapServices.MapRenderComponents.StandardRenderer.RenderingFunctions
{
    /// <summary>
    /// Writes the labels placed on a map: a line of text on a rounded plate, centred on the
    /// point it was put at.
    /// <para>
    /// Not a component, and not in <c>RenderHelperFunctions</c>'s table with the ones that are.
    /// Everything in that table is dispatched by the type on a tile and drawn across a batch of
    /// tiles; a label has no tile -- see <see cref="MapPixel"/> -- so there is nothing to look
    /// it up by and nothing to batch it with. Every renderer calls this directly from its own
    /// <c>RenderLabels</c>, which is the pass that exists for exactly this.
    /// </para>
    /// <para>
    /// Drawn with a <c>DrawText</c> apiece and no atlas, which is the opposite of what
    /// <see cref="RenderElevationLabel"/> does and right for the opposite reason. That one is a
    /// number over every tile on screen -- thousands of draws a frame, eleven characters that
    /// never change -- so an atlas per zoom level pays for itself many times over. These are a
    /// handful of arbitrary strings placed by hand, so an atlas would have to hold every
    /// character anybody ever typed and would save a few dozen draw calls a frame. Nothing is
    /// cached here at all, and that is why there is no prewarm to go with it.
    /// </para>
    /// <para>
    /// The whole label scales with the zoom -- the plate, the text and the gap between them --
    /// because it is written on the map rather than on the window. A label that held its size
    /// in screen pixels would cover a county when zoomed out and a doorstep when zoomed in,
    /// and would name neither.
    /// </para>
    /// </summary>
    public static class RenderMapLabel
    {
        /// <summary>
        /// Below this the labels are left off entirely.
        /// <para>
        /// Legibility rather than cost, the same as the elevation label's own floor: writing a
        /// few pixels tall is a smudge, and a map covered in smudges is worse than a map with
        /// nothing written on it. It is also what keeps the mini-map clear -- the inset draws
        /// the whole map at a few pixels a tile, so it falls under this on any map worth
        /// insetting.
        /// </para>
        /// </summary>
        public const int MinTileSize = 8;

        /// <summary>
        /// Cap height of the writing, in map pixels. Around three quarters of a tile at the
        /// zoom the map opens at: big enough to read over any ground, small enough that a
        /// short name does not swallow the thing it is naming.
        /// </summary>
        private const float TextPixels = 24f;

        /// <summary>Gap between the writing and the edge of its plate, in map pixels.</summary>
        private const float PaddingX = 10f;

        /// <inheritdoc cref="PaddingX"/>
        private const float PaddingY = 5f;

        /// <summary>Corner radius of the plate, in map pixels.</summary>
        private const float Corner = 6f;

        /// <summary>
        /// Whatever the machine has of these, in order. Proportional and not the monospaced
        /// face the elevation numbers use: those are columns of digits that should line up,
        /// and this is prose. Null where none of them is present, which Skia reads as "use the
        /// default" -- the layout holds either way, being measured from the face actually used.
        /// </summary>
        private static readonly SKTypeface? LabelTypeface =
            SKTypeface.FromFamilyName("Segoe UI")
            ?? SKTypeface.FromFamilyName("Inter")
            ?? SKTypeface.FromFamilyName("Arial");

        /// <summary>
        /// Draws every label that lands inside the canvas clip.
        /// <para>
        /// The paints are built once per call and recoloured per label rather than held in a
        /// static, because a renderer instance is per rendering thread and a shared mutable
        /// paint would be the one piece of state two frames could collide over. Two paints a
        /// frame is nothing next to what the tiles under them cost.
        /// </para>
        /// </summary>
        public static Task Render(SKCanvas canvas, RenderFrame frame, IReadOnlyList<MapLabel> labels)
        {
            if (canvas is null || labels is null || labels.Count == 0 || frame.TileSize < MinTileSize)
                return Task.CompletedTask;

            var scale = frame.TileSize / (float)MapPixel.PixelsPerTile;
            var clip = canvas.LocalClipBounds;

            // An empty clip means nothing is visible. Checked rather than left to Skia, since
            // every label below would otherwise be measured before being thrown away.
            if (clip.Width <= 0 || clip.Height <= 0)
                return Task.CompletedTask;

            using var plate = new SKPaint
            {
                Style = SKPaintStyle.Fill,
                IsAntialias = true,
            };

            using var pen = Pen(scale);
            var metrics = pen.FontMetrics;

            foreach (var label in labels)
            {
                if (label is null || string.IsNullOrEmpty(label.Text))
                    continue;

                var box = Box(pen, label, frame.TileSize, scale);

                // Off screen, and the only cull worth making: a map may carry labels the length
                // of it, and the ones behind you cost a measure each and nothing more.
                if (!box.IntersectsWith(clip))
                    continue;

                plate.Color = Colour(label.Background);
                canvas.DrawRoundRect(box, Corner * scale, Corner * scale, plate);

                pen.Color = Colour(label.Foreground);

                // The baseline placed off the face's own metrics rather than by halving the
                // text size: what should sit in the middle of the plate is the line box, and
                // only the face knows how much of it is above the baseline.
                canvas.DrawText(
                    label.Text,
                    box.MidX,
                    box.Top + (PaddingY * scale) - metrics.Ascent,
                    pen);
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// The plate a label covers, in map pixels -- what it would be drawn as at
        /// <see cref="MapPixel.PixelsPerTile"/>, which is the one size that does not depend on
        /// how the map is being looked at.
        /// <para>
        /// Here rather than worked out again by whoever needs it, because the only honest
        /// answer involves measuring the text in the face it will be drawn in, and a second
        /// guess at it would be a box that does not match the one on screen. What it is for is
        /// picking a label off the map: a click lands on a point, and this is what says which
        /// label -- if any -- is under it.
        /// </para>
        /// </summary>
        public static SKRect Bounds(MapLabel label)
        {
            ArgumentNullException.ThrowIfNull(label);

            using var pen = Pen(1f);
            return Box(pen, label, MapPixel.PixelsPerTile, 1f);
        }

        /// <summary>
        /// The label under <paramref name="point"/>, or null where there is none.
        /// <para>
        /// The last one wins where two overlap, because the last is the one drawn on top: what
        /// a click should pick is what the eye sees, and what the eye sees is whatever is not
        /// covered by anything else.
        /// </para>
        /// </summary>
        public static MapLabel? At(IReadOnlyList<MapLabel>? labels, MapPixel point)
        {
            if (labels is null)
                return null;

            MapLabel? found = null;

            for (var i = 0; i < labels.Count; i++)
            {
                var label = labels[i];

                if (label is not null
                    && !string.IsNullOrEmpty(label.Text)
                    && Bounds(label).Contains((float)point.X, (float)point.Y))
                {
                    found = label;
                }
            }

            return found;
        }

        /// <summary>
        /// The plate for one label, in whatever coordinates <paramref name="pen"/> was built
        /// for. The one place the shape of a label is decided, so that what is drawn and what
        /// is clicked on are the same rectangle.
        /// <para>
        /// Sized to the ink across and to the face's line box down. The line box rather than
        /// the ink, so that two labels beside each other are the same height whether or not
        /// either happens to contain a descender.
        /// </para>
        /// </summary>
        private static SKRect Box(SKPaint pen, MapLabel label, int tileSize, float scale)
        {
            var (x, y) = label.Anchor.Canvas(tileSize);

            var metrics = pen.FontMetrics;
            var width = pen.MeasureText(label.Text) + (PaddingX * scale * 2f);
            var height = (metrics.Descent - metrics.Ascent) + (PaddingY * scale * 2f);

            return new SKRect(
                x - (width / 2f), y - (height / 2f), x + (width / 2f), y + (height / 2f));
        }

        /// <summary>The paint the writing is measured and drawn with, at a given zoom.</summary>
        private static SKPaint Pen(float scale) => new()
        {
            Typeface = LabelTypeface,
            TextSize = TextPixels * scale,
            TextAlign = SKTextAlign.Center,
            IsAntialias = true,
        };

        /// <summary>A packed <c>0xAARRGGBB</c> as the paints want it.</summary>
        private static SKColor Colour(uint colour)
        {
            var (alpha, red, green, blue) = MapColour.Channels(colour);
            return new SKColor(red, green, blue, alpha);
        }
    }
}
