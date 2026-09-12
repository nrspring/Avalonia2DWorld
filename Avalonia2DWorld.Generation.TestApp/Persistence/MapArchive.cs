using System;
using System.IO;
using Avalonia2DWorld.Map.Models;
using Avalonia2DWorld.Map.Persistence;

namespace Avalonia2DWorld.Generation.TestApp.Persistence;

/// <summary>
/// Opens a saved world and hands back its tiles.
/// <para>
/// A <c>.nworld</c> file is a length-prefixed JSON header -- the settings the generation app's
/// panels were on, plus the shape of the map -- immediately followed by the tile block. This
/// reads past the header without parsing it and hands the rest to <see cref="TileMapFormat"/>:
/// the settings describe dials this app does not have, and a viewer that carried them would be
/// pretending it could build a world.
/// </para>
/// <para>
/// A reader of its own rather than the generation app's <c>MapFile</c>, because this project
/// does not reference that one and should not: opening a finished world is not a reason to
/// depend on the machine that makes them. The cost is that the header framing is written down
/// twice, so a change to it has to be made in both places. That is a few lines of duplication
/// against a project reference from a viewer to a generator, and it is the cheaper of the two --
/// but it is real, and this is the note that says so.
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

    /// <summary>
    /// Reads the tiles out of a saved world. Throws <see cref="InvalidDataException"/> if the
    /// stream is not one of these, or is one this build cannot read.
    /// </summary>
    public static TileMap Load(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);

        var headerLength = reader.ReadInt32();
        var header = reader.ReadBytes(headerLength);

        if (header.Length != headerLength)
            throw new InvalidDataException("This file is truncated, so it is not a map.");

        return TileMapFormat.Read(stream);
    }
}
