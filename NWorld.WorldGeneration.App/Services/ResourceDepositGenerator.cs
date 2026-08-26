using NWorld.Map.Viewer;

namespace NWorld.WorldGeneration.App.Services;

public sealed record ResourceDepositGenerationResult(
    IReadOnlyList<MapTile> Tiles,
    int IronTileCount,
    int StoneTileCount,
    int CoalTileCount,
    int RareMetalsTileCount)
{
    public int TotalTileCount => IronTileCount + StoneTileCount + CoalTileCount + RareMetalsTileCount;
}

public static class ResourceDepositGenerator
{
    private const int MountainMinElevation = 11;

    public static ResourceDepositGenerationResult Generate(
        IReadOnlyList<MapTile> sourceTiles,
        int mapWidth,
        int mapHeight,
        ResourceDepositGenerationOptions options)
    {
        if (mapWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(mapWidth), mapWidth, "Map width must be greater than zero.");
        }

        if (mapHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(mapHeight), mapHeight, "Map height must be greater than zero.");
        }

        var tiles = sourceTiles.ToArray();
        if (tiles.Length == 0 || options.Count <= 0)
        {
            return CreateResult(tiles, options.Resource, 0);
        }

        var random = new Random(HashSeed(mapWidth, mapHeight, options));
        var placedCount = PlaceResourceRegions(tiles, mapWidth, mapHeight, options, random);
        return CreateResult(tiles, options.Resource, placedCount);
    }

    private static int PlaceResourceRegions(
        MapTile[] tiles,
        int mapWidth,
        int mapHeight,
        ResourceDepositGenerationOptions options,
        Random random)
    {
        var placedCount = 0;
        var width = Math.Max(2, options.Width);
        var centers = new List<MapTile>(Math.Min(options.Count, 240));
        var attemptLimit = Math.Max(128, options.Count * 32);

        for (var attempt = 0; attempt < attemptLimit && centers.Count < Math.Min(options.Count, 240); attempt++)
        {
            var centerIndex = PickWeightedEligibleIndex(tiles, options.Resource, random);
            if (centerIndex < 0)
            {
                break;
            }

            var center = tiles[centerIndex];
            if (centers.Any(existing => GetDistance(center.Row, center.Column, existing.Row, existing.Column) < width))
            {
                continue;
            }

            centers.Add(center);
            placedCount += PlaceResourceRegion(tiles, mapWidth, mapHeight, options, center, centers.Count, attempt, random);
        }

        return placedCount;
    }

    private static int PlaceResourceRegion(
        MapTile[] tiles,
        int mapWidth,
        int mapHeight,
        ResourceDepositGenerationOptions options,
        MapTile center,
        int regionIndex,
        int attempt,
        Random random)
    {
        var placedCount = 0;
        var width = Math.Max(2, options.Width);
        var angle = random.NextDouble() * Math.PI * 2d;
        var longRadius = width * (0.85d + random.NextDouble() * 0.45d);
        var shortRadius = Math.Max(1.5d, width * (0.45d + random.NextDouble() * 0.25d));
        var density = Math.Clamp(options.DensityPercent / 100d, 0d, 1d);
        var threshold = 0.18d + (1d - density) * 0.42d;
        var seed = HashRegionSeed(options.Seed, regionIndex, attempt);
        var searchRadius = (int)Math.Ceiling(longRadius + 3d);

        for (var row = Math.Max(0, center.Row - searchRadius); row <= Math.Min(mapHeight - 1, center.Row + searchRadius); row++)
        {
            for (var column = Math.Max(0, center.Column - searchRadius); column <= Math.Min(mapWidth - 1, center.Column + searchRadius); column++)
            {
                var index = row * mapWidth + column;
                if (index < 0 || index >= tiles.Length || !IsEligibleTile(tiles[index], options.Resource))
                {
                    continue;
                }

                var rowDelta = row - center.Row;
                var columnDelta = column - center.Column;
                var along = (Math.Cos(angle) * columnDelta) + (Math.Sin(angle) * rowDelta);
                var across = (-Math.Sin(angle) * columnDelta) + (Math.Cos(angle) * rowDelta);
                var distance = Math.Sqrt(Math.Pow(along / longRadius, 2d) + Math.Pow(across / shortRadius, 2d));
                var edgeNoise = (SampleDepositNoise(seed + 17, row, column) - 0.5d) * 0.32d;
                if (distance + edgeNoise > 1d)
                {
                    continue;
                }

                var coreStrength = Math.Pow(Math.Max(0d, 1d - distance), 0.72d);
                var texture = 0.62d + SampleDepositNoise(seed + 41, row, column) * 0.48d;
                if (coreStrength * texture < threshold)
                {
                    continue;
                }

                tiles[index] = tiles[index] with { Resource = options.Resource };
                placedCount++;
            }
        }

        return placedCount;
    }

    private static int PickWeightedEligibleIndex(IReadOnlyList<MapTile> tiles, TileResource resource, Random random)
    {
        var totalWeight = 0d;
        for (var index = 0; index < tiles.Count; index++)
        {
            totalWeight += GetPlacementWeight(tiles[index], resource);
        }

        if (totalWeight <= 0)
        {
            return -1;
        }

        var choice = random.NextDouble() * totalWeight;
        for (var index = 0; index < tiles.Count; index++)
        {
            choice -= GetPlacementWeight(tiles[index], resource);
            if (choice <= 0)
            {
                return index;
            }
        }

        return -1;
    }

    private static double GetPlacementWeight(MapTile tile, TileResource resource)
    {
        if (!IsEligibleTile(tile, resource))
        {
            return 0d;
        }

        return resource switch
        {
            TileResource.Iron => tile.Elevation >= MountainMinElevation ? 5d : tile.Elevation >= 2 ? 3d : 0.8d,
            TileResource.Stone => tile.Elevation >= MountainMinElevation ? 4d : tile.Elevation >= 2 ? 2d : 1d,
            TileResource.Coal => tile.Elevation >= MountainMinElevation ? 0.5d : tile.Elevation >= 2 ? 2.5d : 1.2d,
            TileResource.RareMetals => tile.Elevation >= MountainMinElevation ? 6d : tile.Elevation >= 8 ? 2d : 0d,
            _ => 0d
        };
    }

    private static bool IsEligibleTile(MapTile tile, TileResource resource)
    {
        if (tile.Terrain != TileTerrain.Land || tile.Resource != TileResource.None)
        {
            return false;
        }

        return resource switch
        {
            TileResource.RareMetals => tile.Elevation >= 8,
            _ => true
        };
    }

    private static ResourceDepositGenerationResult CreateResult(
        IReadOnlyList<MapTile> tiles,
        TileResource resource,
        int placedCount)
    {
        return resource switch
        {
            TileResource.Iron => new ResourceDepositGenerationResult(tiles, placedCount, 0, 0, 0),
            TileResource.Stone => new ResourceDepositGenerationResult(tiles, 0, placedCount, 0, 0),
            TileResource.Coal => new ResourceDepositGenerationResult(tiles, 0, 0, placedCount, 0),
            TileResource.RareMetals => new ResourceDepositGenerationResult(tiles, 0, 0, 0, placedCount),
            _ => new ResourceDepositGenerationResult(tiles, 0, 0, 0, 0)
        };
    }

    private static double GetDistance(double firstRow, double firstColumn, double secondRow, double secondColumn)
    {
        var row = firstRow - secondRow;
        var column = firstColumn - secondColumn;
        return Math.Sqrt(row * row + column * column);
    }

    private static double SampleDepositNoise(int seed, int row, int column)
    {
        var x = (column + Hash01(seed, 0) * 997d) / 11d;
        var y = (row + Hash01(seed, 1) * 997d) / 11d;
        return (SimplexNoise.Noise((float)x, (float)y) + 1d) * 0.5d;
    }

    private static int HashSeed(int mapWidth, int mapHeight, ResourceDepositGenerationOptions options)
    {
        unchecked
        {
            var hash = options.Seed;
            hash = hash * 397 ^ mapWidth;
            hash = hash * 397 ^ mapHeight;
            hash = hash * 397 ^ (int)options.Resource;
            hash = hash * 397 ^ options.Count;
            hash = hash * 397 ^ options.DensityPercent;
            hash = hash * 397 ^ options.Width;
            return hash & 0x7fffffff;
        }
    }

    private static int HashRegionSeed(int seed, int regionIndex, int attempt)
    {
        unchecked
        {
            var hash = seed;
            hash = hash * 397 ^ regionIndex;
            hash = hash * 397 ^ attempt;
            return hash & 0x7fffffff;
        }
    }

    private static double Hash01(int seed, int channel)
    {
        unchecked
        {
            uint hash = (uint)(seed + channel * 374761393);
            hash ^= hash >> 13;
            hash *= 1274126177u;
            hash ^= hash >> 16;
            return (hash & 0x00FFFFFFu) / 16777215d;
        }
    }
}
