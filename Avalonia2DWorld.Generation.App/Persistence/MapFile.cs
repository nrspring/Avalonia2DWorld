using System;
using System.IO;
using System.Text.Json;
using Avalonia2DWorld.Map.Models;
using Avalonia2DWorld.Map.Persistence;

namespace Avalonia2DWorld.Generation.App.Persistence;

/// <summary>
/// The settings a map is saved with: everything the panels were set to when it was made.
/// <para>
/// Saved because the settings are how a map is worked on. Opening one and finding the dials
/// back at their defaults means guessing what the coastline was built from before another
/// pass can be run over it; opening it with the dials where they were means carrying on.
/// </para>
/// <para>
/// Every member is optional on the way in. A file written by an older build simply has fewer
/// of them, and what is missing keeps whatever the app already had.
/// </para>
/// </summary>
public sealed record MapSettings
{
    public int? ContinentCount { get; init; }
    public double? LandCoverage { get; init; }
    public double? CoastRoughness { get; init; }
    public string? ContinentSeed { get; init; }

    public int? IslandCount { get; init; }
    public double? IslandSize { get; init; }
    public double? IslandCoastHug { get; init; }
    public string? IslandSeed { get; init; }

    public double? ShallowsReach { get; init; }
    public double? ShallowsVariation { get; init; }

    public double? MountainCoverage { get; init; }
    public double? HillCoverage { get; init; }
    public double? Ruggedness { get; init; }
    public double? RangeSize { get; init; }
    public double? HillSpread { get; init; }
    public string? TerrainSeed { get; init; }

    public double? SwampCoverage { get; init; }
    public double? DesertCoverage { get; init; }
    public double? PatchSize { get; init; }
    public double? CoverClustering { get; init; }
    public string? CoverSeed { get; init; }

    public double? RiverCount { get; init; }
    public double? RiverWinding { get; init; }
    public double? RiverLength { get; init; }
    public string? RiverSeed { get; init; }

    public double? LakeCount { get; init; }
    public double? LakeSize { get; init; }
    public double? LakeShape { get; init; }
    public string? LakeSeed { get; init; }

    public double? IronCoverage { get; init; }
    public double? WoodCoverage { get; init; }
    public double? OilCoverage { get; init; }
    public double? SulphurCoverage { get; init; }
    public double? StoneCoverage { get; init; }
    public double? ResourcePatchSize { get; init; }
    public double? ResourceClustering { get; init; }
    public string? ResourceSeed { get; init; }

    /// <summary>Zoom, as a tile size in pixels, and where the view was looking.</summary>
    public int? TileSize { get; init; }

    public double? OriginX { get; init; }

    public double? OriginY { get; init; }
}

/// <summary>
/// A saved map: the tiles, and the settings that were on the panels when it was saved.
/// </summary>
/// <param name="Map">The map itself.</param>
/// <param name="Settings">What the panels were set to. Never null; empty for a file that
/// carried none.</param>
public readonly record struct MapDocument(TileMap Map, MapSettings Settings);

/// <summary>
/// Reads and writes a whole map document.
/// <para>
/// A plain sequential file: a length-prefixed JSON header carrying the settings and the shape
/// of the map, immediately followed by the tile block <see cref="TileMapFormat"/> writes. No
/// zip, no compression -- the header is small and meant to be read by a person if they open the
/// file in a hex viewer, and the tile block already stores its component types through a
/// palette rather than repeating them, so there is little left for a general-purpose compressor
/// to buy back.
/// </para>
/// </summary>
public static class MapFile
{
    /// <summary>The extension these are saved under.</summary>
    public const string Extension = "nworld";

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    /// <summary>Writes a map and its settings to <paramref name="stream"/>.</summary>
    public static void Save(Stream stream, TileGrid tiles, MapSettings settings)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(tiles);

        using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);

        var header = JsonSerializer.SerializeToUtf8Bytes(
            new Header(TileMapFormat.Version, tiles.Width, tiles.Height, tiles.OriginX, tiles.OriginY, settings),
            Json);

        writer.Write(header.Length);
        writer.Write(header);
        writer.Flush();

        TileMapFormat.Write(stream, tiles);
    }

    /// <summary>
    /// Reads a map document back. Throws <see cref="InvalidDataException"/> if the file is not
    /// one of these.
    /// </summary>
    public static MapDocument Load(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);

        var headerLength = reader.ReadInt32();
        var headerBytes = reader.ReadBytes(headerLength);

        if (headerBytes.Length != headerLength)
            throw new InvalidDataException("This file is truncated, so it is not a map.");

        var header = JsonSerializer.Deserialize<Header>(headerBytes, Json)
            ?? throw new InvalidDataException("This file's header could not be read, so it is not a map.");

        return new MapDocument(TileMapFormat.Read(stream), header.Settings);
    }

    /// <summary>
    /// What the header holds. The shape is repeated from the tile block on purpose: it is what
    /// lets a reader say how big the map is without unpacking a million tiles to find out.
    /// </summary>
    private sealed record Header(
        int Version, int Width, int Height, int OriginX, int OriginY, MapSettings Settings);
}
