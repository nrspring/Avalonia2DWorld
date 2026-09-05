using System;
using System.Globalization;

namespace NWorld.Map.Models
{
    /// <summary>
    /// A piece of writing placed on the map: what it says, where it sits, and the two colours
    /// it is drawn in.
    /// <para>
    /// Positioned by <see cref="MapPixel"/> and not by tile, which is the whole reason it is
    /// its own kind of thing rather than another component on a tile. A label names a region,
    /// a coast, a stretch of road -- none of which begin and end on a tile boundary -- so it
    /// has to be free to sit between them, and it has to be able to be nudged half a tile
    /// without the map growing a square somewhere to hang it on.
    /// </para>
    /// <para>
    /// Immutable, like everything else the render thread reads. Change a label by replacing it
    /// in the array -- see <see cref="Controls.MapView.Labels"/> -- never by writing to one
    /// that has been published.
    /// </para>
    /// <para>
    /// The colours are the label's own rather than the renderer's, because a label is not
    /// scenery and there is no one right colour for it: the point of writing on a map by hand
    /// is to say which things belong together, and that is said in colour. They are stored as
    /// packed <c>0xAARRGGBB</c> -- see <see cref="MapColour"/> -- so that this model owes
    /// nothing to Skia or to Avalonia, both of which have a colour type and neither of which
    /// is this library's to prefer.
    /// </para>
    /// </summary>
    public sealed record MapLabel
    {
        /// <summary>What the label says. Drawn on one line, however long it is.</summary>
        public required string Text { get; init; }

        /// <summary>
        /// Where the middle of the label sits, in map pixels. The middle rather than a corner:
        /// a label is placed by pointing at the thing it names, and what should land under the
        /// finger is the writing, not the top-left of the box round it.
        /// </summary>
        public required MapPixel Anchor { get; init; }

        /// <summary>The plate behind the writing, as <c>0xAARRGGBB</c>.</summary>
        /// <inheritdoc cref="MapLabel" path="/summary/para[last()]"/>
        public uint Background { get; init; } = MapColour.DefaultBackground;

        /// <summary>The writing itself, as <c>0xAARRGGBB</c>.</summary>
        /// <inheritdoc cref="MapLabel" path="/summary/para[last()]"/>
        public uint Foreground { get; init; } = MapColour.DefaultForeground;

        /// <summary>A label at a position given in tiles and fractions of a tile.</summary>
        public static MapLabel AtTile(double x, double y, string text) =>
            new() { Text = text, Anchor = MapPixel.FromTiles(x, y) };
    }

    /// <summary>
    /// Colours as packed <c>0xAARRGGBB</c>, and the two ways they are written down: the hex a
    /// person types and a saved file holds, and the bytes a paint wants.
    /// <para>
    /// Here rather than as a type of its own because there is nothing to a colour that four
    /// bytes do not already say, and because everything that actually draws one -- Skia,
    /// Avalonia -- has its own colour type it would have to be converted to anyway. One uint
    /// converts to both in a line.
    /// </para>
    /// </summary>
    public static class MapColour
    {
        /// <summary>
        /// What a label is drawn on when nobody has said otherwise: the same near-black the
        /// map's own panels use, at the opacity that lets the ground read faintly through.
        /// </summary>
        public const uint DefaultBackground = 0xE6121A26;

        /// <inheritdoc cref="DefaultBackground"/>
        public const uint DefaultForeground = 0xFFEEF3FA;

        /// <summary>Packs four channels, alpha first.</summary>
        public static uint From(byte alpha, byte red, byte green, byte blue) =>
            ((uint)alpha << 24) | ((uint)red << 16) | ((uint)green << 8) | blue;

        /// <summary>Unpacks to the four channels, alpha first.</summary>
        public static (byte Alpha, byte Red, byte Green, byte Blue) Channels(uint colour) => (
            (byte)(colour >> 24),
            (byte)(colour >> 16),
            (byte)(colour >> 8),
            (byte)colour);

        /// <summary>
        /// The hex a person would type: <c>#AARRGGBB</c>, or <c>#RRGGBB</c> where the colour
        /// is opaque, since the alpha is the part nobody wants to read when it says nothing.
        /// </summary>
        public static string ToHex(uint colour) =>
            colour >> 24 == 0xFF
                ? $"#{colour & 0x00FFFFFF:X6}"
                : $"#{colour:X8}";

        /// <summary>
        /// Reads <see cref="ToHex"/> back, and the shapes near enough to it to be what was
        /// meant: with or without the hash, six digits or eight. Null for anything else.
        /// <para>
        /// Answers with null rather than throwing because both callers are reading something
        /// somebody typed -- into a box, or into a saved file with a text editor -- and a bad
        /// colour there is a thing to say something about, not a thing to fall over.
        /// </para>
        /// </summary>
        public static uint? FromHex(string? hex)
        {
            if (string.IsNullOrWhiteSpace(hex))
                return null;

            var digits = hex.Trim().TrimStart('#');

            if (!uint.TryParse(digits, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
                return null;

            // Six digits is a colour with no alpha said, which means opaque -- read the other
            // way it would be a colour that is entirely transparent, i.e. a label that draws
            // nothing at all, which nobody has ever typed on purpose.
            return digits.Length switch
            {
                6 => value | 0xFF000000,
                8 => value,
                _ => null,
            };
        }
    }
}
