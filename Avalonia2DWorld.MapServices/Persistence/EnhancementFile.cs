using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia2DWorld.Map.Models;
using Avalonia2DWorld.MapServices.Constants;

namespace Avalonia2DWorld.MapServices.Persistence
{
    /// <summary>
    /// One thing built on one tile: where it is, what it is, and however it was set down.
    /// </summary>
    /// <param name="X">A map coordinate, not an index into the grid.</param>
    /// <param name="Y"><inheritdoc cref="X" path="/summary"/></param>
    /// <param name="Type">The component type, as <c>MapRenderComponentConstants</c> names it.</param>
    /// <param name="Params">
    /// Whatever that kind keeps, by name: a road's rotation, say. Null where it keeps nothing,
    /// which is what a component with no parameters looks like everywhere else.
    /// </param>
    public sealed record Enhancement(
        int X,
        int Y,
        Guid Type,
        [property: JsonConverter(typeof(ParamsConverter))] Dictionary<string, string>? Params);

    /// <summary>
    /// Reads a component's parameters whether they were written as names or as a bare list.
    /// <para>
    /// Files written before version 3 hold <c>["1"]</c> where one written since holds
    /// <c>{"turns": "1"}</c>. Both are still opened, and a list comes back keyed by the index
    /// each value sat at -- which is exactly what identified it when that was all there was.
    /// </para>
    /// <para>
    /// A converter rather than a second shape of the whole document, because this is the only
    /// field that changed and the rest of the file reads the same at every version. Writing is
    /// left to the default: what goes out is always the new shape.
    /// </para>
    /// </summary>
    internal sealed class ParamsConverter : JsonConverter<Dictionary<string, string>?>
    {
        public override Dictionary<string, string>? Read(
            ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
                return null;

            if (reader.TokenType == JsonTokenType.StartObject)
                return JsonSerializer.Deserialize<Dictionary<string, string>>(ref reader, options);

            if (reader.TokenType != JsonTokenType.StartArray)
                throw new JsonException("Parameters must be a list or a set of named values.");

            var parameters = new Dictionary<string, string>();

            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                parameters[parameters.Count.ToString(CultureInfo.InvariantCulture)] =
                    reader.GetString() ?? string.Empty;
            }

            return parameters.Count == 0 ? null : parameters;
        }

        public override void Write(
            Utf8JsonWriter writer, Dictionary<string, string>? value, JsonSerializerOptions options) =>
            JsonSerializer.Serialize(writer, value, options);
    }

    /// <summary>
    /// Everything read out of an enhancements file: what has been built, and the shape of the
    /// map it was built on.
    /// </summary>
    /// <param name="Built">The enhancements, in the reading order they were saved in.</param>
    /// <param name="Labels">
    /// The writing placed on the map, in the order it was saved in. Empty where there is none,
    /// which includes every file written before labels existed.
    /// </param>
    public sealed record EnhancementDocument(
        int Width, int Height, int OriginX, int OriginY,
        IReadOnlyList<Enhancement> Built, IReadOnlyList<MapLabel> Labels);

    /// <summary>
    /// Reads and writes what has been built on a map, as a file of its own.
    /// <para>
    /// Separate from the map on purpose. A generated world is finished when it is saved and is
    /// never written to again; roads, bridges and towns are what happens to it afterwards, and
    /// keeping the two apart means a world can be handed round, regenerated or shipped
    /// read-only while what was built on it stays somebody's own. Two files, one drawn over the
    /// other.
    /// </para>
    /// <para>
    /// JSON rather than the packed block the tiles use, because this is the opposite kind of
    /// thing. Tiles are a million of something and nobody ever reads them; enhancements are the
    /// handful that were actually placed, and being able to open the file and see a road at
    /// (40, 12) is worth more here than the bytes it costs. The type goes out as a plain guid
    /// per entry rather than paletted for the same reason: a palette would save perhaps thirty
    /// bytes an entry and cost the one property that makes the file worth opening.
    /// </para>
    /// <para>
    /// Labels ride in the same file, in a list of their own beside the built tiles. They
    /// belong here for the reason everything else here does -- they are what somebody did to a
    /// world after it was finished, and the world itself is never written to again -- and they
    /// are a separate list because they are a separate kind of thing: a label is placed by
    /// <see cref="MapPixel"/> and has no tile to be found under. A third file would have been
    /// a third thing to remember to save.
    /// </para>
    /// <para>
    /// Only <see cref="RenderComponentLayers.Enhancement"/> is saved, and everything on it is.
    /// This file is that layer, not a list of the kinds of thing this build happens to know how
    /// to draw, so a file written by a later build that has learned something new loses nothing
    /// by passing through an older one.
    /// </para>
    /// </summary>
    public static class EnhancementFile
    {
        /// <summary>The extension these are saved under: an nworld map, plus what was built.</summary>
        public const string Extension = "nworldx";

        /// <summary>
        /// Bumped when the shape below changes in a way an older reader would misread.
        /// <see cref="Load"/> refuses anything newer than it knows.
        /// </summary>
        public const int Version = 3;

        /// <summary>
        /// What version 3 changed: a component's parameters went from a list to named pairs.
        /// Both shapes are still read -- see <see cref="ParamsConverter"/> -- so the bump is
        /// only so that an older build refuses a file it would misread rather than half-reading
        /// it.
        /// </summary>
        private const int NamedParamsFrom = 3;

        /// <summary>
        /// What version 2 added: the labels. Version 1 files are read as having none, which is
        /// what they have.
        /// <para>
        /// Bumped rather than slipped in silently, even though an older reader would ignore
        /// the new list rather than choke on it. Ignoring it is the problem: that build would
        /// open a map, show none of the writing on it, and drop every word of it on the next
        /// save. Better it refuses the file and says why.
        /// </para>
        /// </summary>
        private const int LabelsFrom = 2;

        private static readonly JsonSerializerOptions Json = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        /// <summary>
        /// Writes everything built on <paramref name="tiles"/> to <paramref name="stream"/>.
        /// <para>
        /// A map with nothing built on it writes an empty list rather than nothing at all, so
        /// that saving after pulling the last road down clears the file instead of leaving the
        /// road standing in it.
        /// </para>
        /// </summary>
        public static void Save(Stream stream, TileGrid tiles, IReadOnlyList<MapLabel>? labels = null)
        {
            ArgumentNullException.ThrowIfNull(stream);
            ArgumentNullException.ThrowIfNull(tiles);

            var built = new List<Enhancement>();

            foreach (var tile in tiles)
            {
                if (tile.MapRenderComponents.TryGetValue(RenderComponentLayers.Enhancement, out var component))
                {
                    built.Add(new Enhancement(
                        tile.X,
                        tile.Y,
                        component.ComponentType,
                        component.Params is { Count: > 0 } parameters ? parameters : null));
                }
            }

            var written = new List<LabelEntry>();

            if (labels is not null)
            {
                foreach (var label in labels)
                {
                    // A label with nothing written on it is not a label. Dropped rather than
                    // saved, so that an empty one left behind by a mis-click does not come
                    // back as an invisible thing to wonder about.
                    if (label is null || string.IsNullOrWhiteSpace(label.Text))
                        continue;

                    written.Add(new LabelEntry(
                        label.Anchor.X,
                        label.Anchor.Y,
                        label.Text,
                        MapColour.ToHex(label.Background),
                        MapColour.ToHex(label.Foreground)));
                }
            }

            JsonSerializer.Serialize(
                stream,
                new Contents(
                    Version, tiles.Width, tiles.Height, tiles.OriginX, tiles.OriginY, built, written),
                Json);
        }

        /// <summary>
        /// Reads an enhancements file back. Throws <see cref="InvalidDataException"/> if the
        /// stream is not one of these, or is one this build cannot read.
        /// </summary>
        public static EnhancementDocument Load(Stream stream)
        {
            ArgumentNullException.ThrowIfNull(stream);

            Contents? contents;

            try
            {
                contents = JsonSerializer.Deserialize<Contents>(stream, Json);
            }
            catch (JsonException error)
            {
                // Reported as bad data rather than let out as a parse error, because to
                // everything upstream that is what it is: somebody picked the wrong file.
                throw new InvalidDataException($"This is not an enhancements file: {error.Message}", error);
            }

            if (contents is null)
                throw new InvalidDataException("This file is empty, so it is not an enhancements file.");

            // Newer refused, older read. A file this build wrote is a file it can read, and the
            // whole point of a version is that the day the shape changes there is somewhere to
            // put the old way of reading it.
            if (contents.Version > Version)
            {
                throw new InvalidDataException(
                    $"These enhancements are version {contents.Version}; this build reads version {Version}.");
            }

            if (contents.Width <= 0 || contents.Height <= 0)
                throw new InvalidDataException($"A map cannot be {contents.Width} x {contents.Height} tiles.");

            return new EnhancementDocument(
                contents.Width, contents.Height, contents.OriginX, contents.OriginY,
                contents.Built ?? [],
                Labels(contents));
        }

        /// <summary>
        /// Lays <paramref name="document"/> onto <paramref name="map"/>, replacing whatever was
        /// built there.
        /// <para>
        /// Replacing and not merging: the file is that layer rather than a set of additions to
        /// it, so loading one twice leaves the same map and loading an older one really does go
        /// back to it. Anything the map had that the file does not is pulled down.
        /// </para>
        /// <para>
        /// The map's shape has to match. It is a weak check -- two maps of the same size pass
        /// it -- but it is the only one available, tiles carrying no identity to compare
        /// against, and it catches the mistake people actually make, which is opening a world
        /// and then loading the enhancements belonging to a different one. A guard, not a
        /// promise.
        /// </para>
        /// <para>
        /// The labels in the document are not laid by this and never could be: they belong to
        /// no tile, so there is nothing here to lay them on. Take
        /// <see cref="EnhancementDocument.Labels"/> and publish it -- the two halves of a load
        /// are two calls because they go to two different places.
        /// </para>
        /// </summary>
        /// <returns>How many enhancements were laid. Labels are not counted; see above.</returns>
        public static int Apply(TileMap map, EnhancementDocument document)
        {
            ArgumentNullException.ThrowIfNull(map);
            ArgumentNullException.ThrowIfNull(document);

            if (document.Width != map.Width || document.Height != map.Height
                || document.OriginX != map.OriginX || document.OriginY != map.OriginY)
            {
                throw new InvalidDataException(
                    $"These enhancements are for a {document.Width} x {document.Height} map, "
                    + $"and this one is {map.Width} x {map.Height}.");
            }

            // Cleared and laid inside one edit, so no frame is ever published halfway through
            // with the old work gone and the new work not yet on it.
            map.Edit(editor =>
            {
                foreach (var tile in map.Tiles)
                {
                    if (tile.MapRenderComponents.ContainsKey(RenderComponentLayers.Enhancement))
                    {
                        editor.Update(
                            new TileCoordinate(tile.X, tile.Y),
                            edited => edited.MapRenderComponents.Remove(RenderComponentLayers.Enhancement));
                    }
                }

                foreach (var enhancement in document.Built)
                {
                    // A coordinate off the map is dropped by the editor, which is the right
                    // answer for a file someone has edited by hand into saying something
                    // impossible: the rest of it still loads.
                    editor.Update(
                        new TileCoordinate(enhancement.X, enhancement.Y),
                        edited => edited.SetMapRenderComponent(
                            RenderComponentLayers.Enhancement,
                            new MapRenderComponent
                            {
                                ComponentType = enhancement.Type,
                                Params = enhancement.Params,
                            }));
                }
            });

            return document.Built.Count;
        }

        /// <summary>
        /// The labels out of a file, as the map wants them.
        /// <para>
        /// Everything read here is checked rather than trusted, the colours above all: this is
        /// a file people are meant to be able to open and edit, which is most of the reason it
        /// is JSON, and a hex string somebody has mistyped should cost that label its colour
        /// and nothing else. A label with no text is dropped -- there is nothing to draw and
        /// nothing to click on, so it would only be an invisible thing in the file.
        /// </para>
        /// </summary>
        private static List<MapLabel> Labels(Contents contents)
        {
            var labels = new List<MapLabel>();

            if (contents.Version < LabelsFrom || contents.Labels is not { } written)
                return labels;

            foreach (var entry in written)
            {
                if (entry is null || string.IsNullOrWhiteSpace(entry.Text))
                    continue;

                labels.Add(new MapLabel
                {
                    Text = entry.Text,
                    Anchor = new MapPixel(entry.X, entry.Y),
                    Background = MapColour.FromHex(entry.Background) ?? MapColour.DefaultBackground,
                    Foreground = MapColour.FromHex(entry.Foreground) ?? MapColour.DefaultForeground,
                });
            }

            return labels;
        }

        /// <summary>
        /// One label as the file holds it: the anchor in map pixels, the words, and the two
        /// colours as the hex somebody could read and change by hand.
        /// <para>
        /// Not <see cref="MapLabel"/> itself. That one keeps its colours packed into a uint,
        /// which is what a paint wants and the last thing a person reading the file wants --
        /// <c>4280424998</c> says nothing whatever, and <c>#E6121A26</c> says most of it.
        /// </para>
        /// </summary>
        private sealed record LabelEntry(
            double X, double Y, string Text, string Background, string Foreground);

        /// <summary>
        /// What the file holds. The map's shape is written beside the enhancements so the file
        /// says which world it belongs on rather than only what stands on it.
        /// </summary>
        private sealed record Contents(
            int Version, int Width, int Height, int OriginX, int OriginY,
            List<Enhancement>? Built, List<LabelEntry>? Labels);
    }
}
