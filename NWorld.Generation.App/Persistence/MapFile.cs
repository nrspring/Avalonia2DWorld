using System;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using NWorld.Map.Models;
using NWorld.Map.Persistence;

namespace NWorld.Generation.App.Persistence;

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
/// A zip holding two entries: <c>map.json</c>, which is the settings and the shape of the map
/// and is meant to be readable by a person, and <c>tiles.bin</c>, which is the tiles and is
/// not. Splitting them that way means the part worth reading stays small and legible while
/// the part that is a million of something stays compact -- and the container compresses the
/// tiles for free, which matters because a map is mostly the same tile over and over.
/// </para>
/// </summary>
public static class MapFile
{
    /// <summary>The extension these are saved under.</summary>
    public const string Extension = "nworld";

    private const string TilesEntry = "tiles.bin";
    private const string HeaderEntry = "map.json";

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    /// <summary>Writes a map and its settings to <paramref name="stream"/>.</summary>
    public static void Save(Stream stream, TileGrid tiles, MapSettings settings)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(tiles);

        using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);

        using (var header = archive.CreateEntry(HeaderEntry, CompressionLevel.Optimal).Open())
        {
            JsonSerializer.Serialize(header, new Header(
                TileMapFormat.Version, tiles.Width, tiles.Height, tiles.OriginX, tiles.OriginY, settings), Json);
        }

        using var payload = archive.CreateEntry(TilesEntry, CompressionLevel.Optimal).Open();
        TileMapFormat.Write(payload, tiles);
    }

    /// <summary>
    /// Reads a map document back. Throws <see cref="InvalidDataException"/> if the file is not
    /// one of these.
    /// </summary>
    public static MapDocument Load(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        // Zip reading seeks, and a stream from a file picker may not. Cheap next to the map
        // that is about to be built out of it.
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        buffer.Position = 0;

        using var archive = new ZipArchive(buffer, ZipArchiveMode.Read);

        var settings = new MapSettings();

        if (archive.GetEntry(HeaderEntry) is { } header)
        {
            using var reader = header.Open();
            settings = JsonSerializer.Deserialize<Header>(reader, Json)?.Settings ?? settings;
        }

        var tiles = archive.GetEntry(TilesEntry)
            ?? throw new InvalidDataException($"This file has no {TilesEntry}, so it is not a map.");

        using var payload = tiles.Open();
        return new MapDocument(TileMapFormat.Read(payload), settings);
    }

    /// <summary>
    /// What <c>map.json</c> holds. The shape is repeated from the tile block on purpose: it
    /// is what makes the file say how big the map is without unpacking a million tiles to
    /// find out.
    /// </summary>
    private sealed record Header(
        int Version, int Width, int Height, int OriginX, int OriginY, MapSettings Settings);
}
