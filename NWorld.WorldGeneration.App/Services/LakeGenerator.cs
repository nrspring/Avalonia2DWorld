using NWorld.Map.Viewer;

namespace NWorld.WorldGeneration.App.Services;

public sealed record LakeGenerationResult(
    IReadOnlyList<MapTile> Tiles,
    int PlacedLakeCount,
    int AddedWaterTileCount,
    int MinimumLakeElevation);

public static class LakeGenerator
{
    private const int MountainMinElevation = 11;

    public static LakeGenerationResult PlantLake(
        IReadOnlyList<MapTile> sourceTiles,
        int mapWidth,
        int mapHeight,
        int centerRow,
        int centerColumn,
        int minimumRadius,
        int maximumRadius,
        int irregularityPercent,
        int seed)
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
        if (tiles.Length == 0 ||
            (uint)centerRow >= (uint)mapHeight ||
            (uint)centerColumn >= (uint)mapWidth)
        {
            return new LakeGenerationResult(tiles, 0, 0, 0);
        }

        var minRadius = Math.Max(1, minimumRadius);
        var maxRadius = Math.Max(minRadius, maximumRadius);
        var lakeSeed = HashLakeSeed(seed, centerRow, centerColumn);
        var radius = minRadius == maxRadius
            ? minRadius
            : new Random(lakeSeed).Next(minRadius, maxRadius + 1);
        var irregularity = Math.Clamp(irregularityPercent / 100d, 0d, 1d);
        if (!TryCreateLake(tiles, mapWidth, mapHeight, centerRow, centerColumn, radius, irregularity, lakeSeed, out var added, out var elevation))
        {
            return new LakeGenerationResult(tiles, 0, 0, 0);
        }

        return new LakeGenerationResult(tiles, 1, added, elevation);
    }

    public static LakeGenerationResult Generate(
        IReadOnlyList<MapTile> sourceTiles,
        int mapWidth,
        int mapHeight,
        LakeGenerationOptions options)
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
            return new LakeGenerationResult(tiles, 0, 0, 0);
        }

        var random = new Random(HashSeed(mapWidth, mapHeight, options));
        var minRadius = Math.Max(1, options.MinimumRadius);
        var maxRadius = Math.Max(minRadius, options.MaximumRadius);
        var irregularity = Math.Clamp(options.IrregularityPercent / 100d, 0d, 1d);
        var placedLakeCount = 0;
        var addedWaterTileCount = 0;
        var minimumLakeElevation = 0;
        var attemptLimit = Math.Max(128, options.Count * 36);

        for (var attempt = 0; attempt < attemptLimit && placedLakeCount < options.Count; attempt++)
        {
            var centerRow = random.Next(0, mapHeight);
            var centerColumn = random.Next(0, mapWidth);
            var centerIndex = GetIndex(centerRow, centerColumn, mapWidth);
            if ((uint)centerIndex >= (uint)tiles.Length || !IsEligibleLakeBase(tiles[centerIndex]))
            {
                continue;
            }

            var radius = random.Next(minRadius, maxRadius + 1);
            var lakeSeed = HashLakeSeed(options.Seed, placedLakeCount, attempt);
            if (!TryCreateLake(tiles, mapWidth, mapHeight, centerRow, centerColumn, radius, irregularity, lakeSeed, out var added, out var elevation))
            {
                continue;
            }

            placedLakeCount++;
            addedWaterTileCount += added;
            minimumLakeElevation = minimumLakeElevation == 0 ? elevation : Math.Min(minimumLakeElevation, elevation);
        }

        return new LakeGenerationResult(tiles, placedLakeCount, addedWaterTileCount, minimumLakeElevation);
    }

    private static bool TryCreateLake(
        MapTile[] tiles,
        int mapWidth,
        int mapHeight,
        int centerRow,
        int centerColumn,
        int radius,
        double irregularity,
        int seed,
        out int addedWaterTileCount,
        out int lakeElevation)
    {
        addedWaterTileCount = 0;
        lakeElevation = 0;

        var plan = CreateShapePlan(radius, irregularity, seed);
        var searchRadius = (int)Math.Ceiling(radius * plan.SearchScale);
        var lakeIndexes = new List<int>();
        var minimumElevation = int.MaxValue;

        for (var row = Math.Max(0, centerRow - searchRadius); row <= Math.Min(mapHeight - 1, centerRow + searchRadius); row++)
        {
            for (var column = Math.Max(0, centerColumn - searchRadius); column <= Math.Min(mapWidth - 1, centerColumn + searchRadius); column++)
            {
                if (!IsInsideLakeShape(row, column, centerRow, centerColumn, plan, seed))
                {
                    continue;
                }

                var index = GetIndex(row, column, mapWidth);
                if ((uint)index >= (uint)tiles.Length || !IsEligibleLakeBase(tiles[index]))
                {
                    return false;
                }

                lakeIndexes.Add(index);
                minimumElevation = Math.Min(minimumElevation, tiles[index].Elevation);
            }
        }

        if (lakeIndexes.Count == 0 || minimumElevation == int.MaxValue)
        {
            return false;
        }

        var lakeIndexSet = lakeIndexes.ToHashSet();
        foreach (var index in lakeIndexes)
        {
            var row = index / mapWidth;
            var column = index % mapWidth;
            if (TouchesWaterOrMountain(tiles, lakeIndexSet, mapWidth, mapHeight, row, column))
            {
                return false;
            }
        }

        foreach (var index in lakeIndexes)
        {
            tiles[index] = tiles[index] with
            {
                Terrain = TileTerrain.Water,
                Elevation = minimumElevation,
                Resource = TileResource.None
            };
        }

        addedWaterTileCount = lakeIndexes.Count;
        lakeElevation = minimumElevation;
        return true;
    }

    private static bool TouchesWaterOrMountain(
        IReadOnlyList<MapTile> tiles,
        IReadOnlySet<int> lakeIndexes,
        int mapWidth,
        int mapHeight,
        int row,
        int column)
    {
        for (var neighborRow = row - 1; neighborRow <= row + 1; neighborRow++)
        {
            for (var neighborColumn = column - 1; neighborColumn <= column + 1; neighborColumn++)
            {
                if (neighborRow == row && neighborColumn == column)
                {
                    continue;
                }

                if ((uint)neighborRow >= (uint)mapHeight || (uint)neighborColumn >= (uint)mapWidth)
                {
                    return true;
                }

                var neighborIndex = GetIndex(neighborRow, neighborColumn, mapWidth);
                if (lakeIndexes.Contains(neighborIndex))
                {
                    continue;
                }

                if ((uint)neighborIndex >= (uint)tiles.Count || IsWaterOrMountain(tiles[neighborIndex]))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsEligibleLakeBase(MapTile tile)
    {
        return tile.Terrain == TileTerrain.Land && tile.Elevation < MountainMinElevation;
    }

    private static bool IsWaterOrMountain(MapTile tile)
    {
        return tile.Terrain is TileTerrain.Water or TileTerrain.DeepWater ||
               tile.Elevation >= MountainMinElevation;
    }

    private static LakeShapePlan CreateShapePlan(int radius, double irregularity, int seed)
    {
        var rotation = Hash01(seed, 41) * Math.PI * 2d;
        var rowRadius = radius * (0.86d + Hash01(seed, 43) * 0.38d);
        var columnRadius = Math.Max(1.0, radius * (0.78d + Hash01(seed, 47) * 0.46d));
        var lobeCount = 2 + (int)Math.Floor(Hash01(seed, 53) * 2d);
        var lobeStrength = 0.07d + irregularity * 0.30d;
        var phase = Hash01(seed, 59) * Math.PI * 2d;
        var asymmetryStrength = irregularity * 0.20d;
        var searchScale = 1.48d + irregularity * 0.30d;

        return new LakeShapePlan(rowRadius, columnRadius, rotation, lobeCount, lobeStrength, asymmetryStrength, phase, searchScale);
    }

    private static bool IsInsideLakeShape(
        int row,
        int column,
        int centerRow,
        int centerColumn,
        LakeShapePlan plan,
        int seed)
    {
        var rowDelta = row - centerRow;
        var columnDelta = column - centerColumn;
        var cos = Math.Cos(plan.Rotation);
        var sin = Math.Sin(plan.Rotation);
        var rotatedRow = rowDelta * cos - columnDelta * sin;
        var rotatedColumn = rowDelta * sin + columnDelta * cos;
        var normalizedRow = rotatedRow / plan.RowRadius;
        var normalizedColumn = rotatedColumn / plan.ColumnRadius;
        var distance = Math.Sqrt((normalizedRow * normalizedRow) + (normalizedColumn * normalizedColumn));
        var angle = Math.Atan2(normalizedRow, normalizedColumn);
        var shorelineNoise = SampleLakeNoise(seed, row, column) - 0.5d;
        var broadBend = Math.Sin((angle * 2d) + plan.Phase * 0.73d);
        var lobeWave = Math.Sin((angle * plan.LobeCount) + plan.Phase);
        var secondaryWave = Math.Sin((angle * (plan.LobeCount + 1)) - plan.Phase * 0.61d);
        var edge = 1d +
                   broadBend * plan.AsymmetryStrength +
                   lobeWave * plan.LobeStrength +
                   secondaryWave * plan.LobeStrength * 0.34d +
                   shorelineNoise * plan.LobeStrength * 0.90d;

        return distance <= Math.Clamp(edge, 0.52d, 1.40d);
    }

    private static int GetIndex(int row, int column, int mapWidth)
    {
        return (row * mapWidth) + column;
    }

    private static double SampleLakeNoise(int seed, int row, int column)
    {
        var x = (column + Hash01(seed, 0) * 997d) / 8.5d;
        var y = (row + Hash01(seed, 1) * 997d) / 8.5d;
        return (SimplexNoise.Noise((float)x, (float)y) + 1d) * 0.5d;
    }

    private static int HashSeed(int mapWidth, int mapHeight, LakeGenerationOptions options)
    {
        unchecked
        {
            var hash = options.Seed;
            hash = hash * 397 ^ mapWidth;
            hash = hash * 397 ^ mapHeight;
            hash = hash * 397 ^ options.Count;
            hash = hash * 397 ^ options.MinimumRadius;
            hash = hash * 397 ^ options.MaximumRadius;
            hash = hash * 397 ^ options.IrregularityPercent;
            return hash & 0x7fffffff;
        }
    }

    private static int HashLakeSeed(int seed, int lakeIndex, int attempt)
    {
        unchecked
        {
            var hash = seed;
            hash = hash * 397 ^ lakeIndex;
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

    private readonly record struct LakeShapePlan(
        double RowRadius,
        double ColumnRadius,
        double Rotation,
        int LobeCount,
        double LobeStrength,
        double AsymmetryStrength,
        double Phase,
        double SearchScale);
}
