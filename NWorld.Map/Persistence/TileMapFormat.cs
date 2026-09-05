using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using NWorld.Map.Models;

namespace NWorld.Map.Persistence
{
    /// <summary>
    /// Reads and writes the tiles of a map as a block of bytes.
    /// <para>
    /// Binary rather than text, because this is the part that is a million of something. The
    /// same map as JSON is an order of magnitude larger and slower to parse, and none of it is
    /// ever read by a person: what someone wants to look at -- how big the map is, what it was
    /// generated from -- belongs in whatever file this block is stored in, next to it.
    /// </para>
    /// <para>
    /// Positions are not written. Tiles go out in reading order and come back the same way, so
    /// every X and Y is implied by where the tile sits in the block.
    /// </para>
    /// <para>
    /// Component types are written once into a palette and referenced by index. A map has a
    /// handful of them and a million uses of them, so the difference is sixteen bytes a tile
    /// against two.
    /// </para>
    /// </summary>
    public static class TileMapFormat
    {
        /// <summary>
        /// Bumped when the layout below changes in a way an older reader would misread.
        /// <see cref="Read"/> refuses anything newer than it knows and still reads everything
        /// older.
        /// </summary>
        public const int Version = 2;

        /// <summary>
        /// What version 2 changed: a component's parameters went from a list to named pairs.
        /// <para>
        /// Version 1 wrote a count and then that many strings, and what each one meant was
        /// whatever the code reading it happened to think. Version 2 writes a count and then
        /// that many pairs. Both are read; a version 1 parameter comes back under the index it
        /// was written at, which is exactly what it was called before it was called anything.
        /// </para>
        /// </summary>
        private const int NamedParamsFrom = 2;

        /// <summary>Writes <paramref name="tiles"/> to <paramref name="stream"/>.</summary>
        public static void Write(Stream stream, TileGrid tiles)
        {
            ArgumentNullException.ThrowIfNull(stream);
            ArgumentNullException.ThrowIfNull(tiles);

            using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);

            writer.Write(Version);
            writer.Write(tiles.Width);
            writer.Write(tiles.Height);
            writer.Write(tiles.OriginX);
            writer.Write(tiles.OriginY);

            var palette = BuildPalette(tiles);

            writer.Write(palette.Count);
            foreach (var type in palette.Keys)
                writer.Write(type.ToByteArray());

            foreach (var tile in tiles)
            {
                writer.Write(tile.Elevation);
                writer.Write((byte)tile.MapRenderComponents.Count);

                foreach (var (layer, component) in tile.MapRenderComponents)
                {
                    writer.Write(layer);
                    writer.Write(palette[component.ComponentType]);

                    var parameters = component.Params;

                    writer.Write((byte)(parameters?.Count ?? 0));

                    if (parameters is null)
                        continue;

                    foreach (var (name, value) in parameters)
                    {
                        writer.Write(name);
                        writer.Write(value ?? string.Empty);
                    }
                }
            }
        }

        /// <summary>
        /// Reads a map back, at any version up to this build's own. Throws
        /// <see cref="InvalidDataException"/> on anything it cannot read, rather than returning
        /// half a map.
        /// </summary>
        public static TileMap Read(Stream stream)
        {
            ArgumentNullException.ThrowIfNull(stream);

            using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);

            // Newer refused, older read. A file this build wrote is a file it can read, and the
            // whole point of a version is that the day the layout changes there is somewhere to
            // put the old way of reading it -- which there now is, below.
            var version = reader.ReadInt32();

            if (version < 1 || version > Version)
            {
                throw new InvalidDataException(
                    $"Map tiles are version {version}; this build reads version 1 to {Version}.");
            }

            var width = reader.ReadInt32();
            var height = reader.ReadInt32();
            var originX = reader.ReadInt32();
            var originY = reader.ReadInt32();

            if (width <= 0 || height <= 0)
                throw new InvalidDataException($"A map cannot be {width} x {height} tiles.");

            var palette = new Guid[reader.ReadInt32()];
            for (var i = 0; i < palette.Length; i++)
                palette[i] = new Guid(reader.ReadBytes(16));

            // TileMap fills in reading order, which is the order these were written in, so
            // the fill and the file walk the map together and no second copy is needed.
            return new TileMap(width, height, originX, originY, coordinate =>
                ReadTile(reader, palette, version, coordinate));
        }

        private static MapTile ReadTile(
            BinaryReader reader, Guid[] palette, int version, TileCoordinate coordinate)
        {
            var tile = new MapTile
            {
                X = coordinate.X,
                Y = coordinate.Y,
                Elevation = reader.ReadInt32(),
            };

            var components = reader.ReadByte();

            for (var i = 0; i < components; i++)
            {
                var layer = reader.ReadInt32();
                var type = reader.ReadInt32();

                if (type < 0 || type >= palette.Length)
                    throw new InvalidDataException($"Component type {type} is not in the palette.");

                var count = reader.ReadByte();
                var parameters = count == 0 ? null : new Dictionary<string, string>(count);

                for (var p = 0; p < count; p++)
                {
                    // A version 1 file has no names in it, so the index a parameter was written
                    // at becomes its name -- see NamedParamsFrom. Nothing is lost: that index is
                    // precisely what identified it before.
                    var name = version >= NamedParamsFrom
                        ? reader.ReadString()
                        : p.ToString(CultureInfo.InvariantCulture);

                    parameters![name] = reader.ReadString();
                }

                tile.SetMapRenderComponent(layer, new MapRenderComponent
                {
                    ComponentType = palette[type],

                    // Back to null when there were none, which is what a component with no
                    // parameters looks like everywhere else.
                    Params = parameters,
                });
            }

            return tile;
        }

        /// <summary>
        /// Every component type on the map, in the order first met, mapped to its index.
        /// </summary>
        private static Dictionary<Guid, int> BuildPalette(TileGrid tiles)
        {
            var palette = new Dictionary<Guid, int>();

            foreach (var tile in tiles)
            {
                foreach (var component in tile.MapRenderComponents.Values)
                {
                    if (!palette.ContainsKey(component.ComponentType))
                        palette[component.ComponentType] = palette.Count;
                }
            }

            return palette;
        }
    }
}
