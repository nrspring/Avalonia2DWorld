using System;
using System.IO;
using System.IO.Compression;
using NWorld.Map.Models;
using NWorld.Map.Persistence;

namespace NWorld.Generation.TestApp.Persistence;

/// <summary>
/// Opens a saved world and hands back its tiles.
/// <para>
/// The generation app writes these as a zip of two entries: <c>map.json</c>, which is the
/// settings its panels were on plus the shape of the map, and <c>tiles.bin</c>, which is the
/// tiles. This reads the second and ignores the first -- the settings describe dials this app
/// does not have, and a viewer that carried them would be pretending it could build a world.
/// </para>
/// <para>
/// A reader of its own rather than the generation app's <c>MapFile</c>, because this project
/// does not reference that one and should not: opening a finished world is not a reason to
/// depend on the machine that makes them. The cost is that the two entry names and the
/// extension are written down twice, so a change to the container has to be made in both
/// places. That is one line of duplication against a project reference from a viewer to a
/// generator, and it is the cheaper of the two -- but it is real, and this is the note that
/// says so.
/// </para>
/// <para>
/// The tile block itself is not duplicated: <see cref="TileMapFormat"/> lives in the map
/// library and both apps read it from there, which is where the version check and everything
/// that could actually go wrong lives.
/// </para>
/// </summary>
public static class MapArchive
{
    /// <summary>The extension worlds are saved under. Must match the generation app.</summary>
    public const string Extension = "nworld";

    /// <inheritdoc cref="Extension"/>
    private const string TilesEntry = "tiles.bin";

    /// <summary>
    /// Reads the tiles out of a saved world. Throws <see cref="InvalidDataException"/> if the
    /// stream is not one of these, or is one this build cannot read.
    /// </summary>
    public static TileMap Load(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        // Zip reading seeks, and a stream from a file picker may not. Cheap next to the map
        // that is about to be built out of it.
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        buffer.Position = 0;

        using var archive = new ZipArchive(buffer, ZipArchiveMode.Read);

        var tiles = archive.GetEntry(TilesEntry)
            ?? throw new InvalidDataException($"This file has no {TilesEntry}, so it is not a map.");

        using var payload = tiles.Open();
        return TileMapFormat.Read(payload);
    }
}
