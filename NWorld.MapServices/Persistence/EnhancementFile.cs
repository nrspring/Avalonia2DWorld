using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using NWorld.Map.Models;
using NWorld.MapServices.Constants;

namespace NWorld.MapServices.Persistence
{
    /// <summary>
    /// One thing built on one tile: where it is, what it is, and however it was set down.
    /// </summary>
    /// <param name="X">A map coordinate, not an index into the grid.</param>
    /// <param name="Y"><inheritdoc cref="X" path="/summary"/></param>
    /// <param name="Type">The component type, as <c>MapRenderComponentConstants</c> names it.</param>
    /// <param name="Params">
    /// Whatever that kind keeps: a road's rotation, say. Null where it keeps nothing, which is
    /// what a component with no parameters looks like everywhere else.
    /// </param>
    public sealed record Enhancement(int X, int Y, Guid Type, string[]? Params);

    /// <summary>
    /// Everything read out of an enhancements file: what has been built, and the shape of the
    /// map it was built on.
    /// </summary>
    /// <param name="Built">The enhancements, in the reading order they were saved in.</param>
    public sealed record EnhancementDocument(
        int Width, int Height, int OriginX, int OriginY, IReadOnlyList<Enhancement> Built);

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
        public const int Version = 1;

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
        public static void Save(Stream stream, TileGrid tiles)
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
                        component.Params is { Length: > 0 } parameters ? parameters : null));
                }
            }

            JsonSerializer.Serialize(
                stream,
                new Contents(Version, tiles.Width, tiles.Height, tiles.OriginX, tiles.OriginY, built),
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
                contents.Width, contents.Height, contents.OriginX, contents.OriginY, contents.Built ?? []);
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
        /// </summary>
        /// <returns>How many enhancements were laid.</returns>
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
        /// What the file holds. The map's shape is written beside the enhancements so the file
        /// says which world it belongs on rather than only what stands on it.
        /// </summary>
        private sealed record Contents(
            int Version, int Width, int Height, int OriginX, int OriginY, List<Enhancement>? Built);
    }
}
