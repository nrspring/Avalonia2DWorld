using NWorld.Map.Viewer;

namespace NWorld.WorldGeneration.App.Services;

public sealed record IslandSprinkleResult(
    IReadOnlyList<MapTile> Tiles,
    int PlacedIslandCount,
    int AddedLandTileCount);

public static class IslandSprinkler
{
    public static IslandSprinkleResult PlantIsland(
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
            return new IslandSprinkleResult(tiles, 0, 0);
        }

        var minRadius = Math.Max(1, minimumRadius);
        var maxRadius = Math.Max(minRadius, maximumRadius);
        var islandSeed = HashIslandSeed(seed, centerRow, centerColumn);
        var radius = minRadius == maxRadius
            ? minRadius
            : new Random(islandSeed).Next(minRadius, maxRadius + 1);
        var irregularity = Math.Clamp(irregularityPercent / 100d, 0d, 1d);
        var added = AddIsland(tiles, mapWidth, mapHeight, centerRow, centerColumn, radius, irregularity, islandSeed);

        return new IslandSprinkleResult(tiles, added > 0 ? 1 : 0, added);
    }

    public static IslandSprinkleResult Sprinkle(
        IReadOnlyList<MapTile> sourceTiles,
        int mapWidth,
        int mapHeight,
        IslandSprinkleOptions options)
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
            return new IslandSprinkleResult(tiles, 0, 0);
        }

        var random = new Random(HashSeed(mapWidth, mapHeight, options));
        var minRadius = Math.Max(1, options.MinimumRadius);
        var maxRadius = Math.Max(minRadius, options.MaximumRadius);
        var irregularity = Math.Clamp(options.IrregularityPercent / 100d, 0d, 1d);
        var placedIslandCount = 0;
        var addedLandTileCount = 0;
        var attemptLimit = Math.Max(96, options.Count * 24);

        for (var attempt = 0; attempt < attemptLimit && placedIslandCount < options.Count; attempt++)
        {
            var centerRow = random.Next(0, mapHeight);
            var centerColumn = random.Next(0, mapWidth);
            var centerIndex = GetIndex(centerRow, centerColumn, mapWidth);
            if ((uint)centerIndex >= (uint)tiles.Length || tiles[centerIndex].Terrain == TileTerrain.Land)
            {
                continue;
            }

            var radius = random.Next(minRadius, maxRadius + 1);
            if (IsTooCloseToLargeLand(tiles, mapWidth, mapHeight, centerRow, centerColumn, Math.Max(2, radius / 2)))
            {
                continue;
            }

            var islandSeed = HashIslandSeed(options.Seed, placedIslandCount, attempt);
            var added = AddIsland(tiles, mapWidth, mapHeight, centerRow, centerColumn, radius, irregularity, islandSeed);
            if (added == 0)
            {
                continue;
            }

            placedIslandCount++;
            addedLandTileCount += added;
        }

        return new IslandSprinkleResult(tiles, placedIslandCount, addedLandTileCount);
    }

    private static int AddIsland(
        MapTile[] tiles,
        int mapWidth,
        int mapHeight,
        int centerRow,
        int centerColumn,
        int radius,
        double irregularity,
        int seed)
    {
        var added = 0;
        var plan = CreateShapePlan(radius, irregularity, seed);
        var searchRadius = (int)Math.Ceiling(radius * plan.SearchScale);

        for (var row = Math.Max(0, centerRow - searchRadius); row <= Math.Min(mapHeight - 1, centerRow + searchRadius); row++)
        {
            for (var column = Math.Max(0, centerColumn - searchRadius); column <= Math.Min(mapWidth - 1, centerColumn + searchRadius); column++)
            {
                var index = GetIndex(row, column, mapWidth);
                if ((uint)index >= (uint)tiles.Length || tiles[index].Terrain == TileTerrain.Land)
                {
                    continue;
                }

                if (!IsInsideIslandShape(row, column, centerRow, centerColumn, plan, seed))
                {
                    continue;
                }

                tiles[index] = tiles[index] with
                {
                    Terrain = TileTerrain.Land,
                    Elevation = 1,
                    Resource = TileResource.None
                };
                added++;
            }
        }

        return added;
    }

    private static IslandShapePlan CreateShapePlan(int radius, double irregularity, int seed)
    {
        var shape = Hash01(seed, 31) switch
        {
            < 0.26d => IslandShape.Compact,
            < 0.56d => IslandShape.Elongated,
            < 0.82d => IslandShape.Lobed,
            _ => IslandShape.Chain
        };

        var rotation = Hash01(seed, 41) * Math.PI * 2d;
        var elongation = shape switch
        {
            IslandShape.Compact => 0.82d + Hash01(seed, 43) * 0.36d,
            IslandShape.Elongated => 0.42d + Hash01(seed, 43) * 0.28d,
            IslandShape.Chain => 0.48d + Hash01(seed, 43) * 0.26d,
            _ => 0.58d + Hash01(seed, 43) * 0.34d
        };
        var rowRadius = radius * (shape is IslandShape.Elongated or IslandShape.Chain ? 1.05d + Hash01(seed, 47) * 0.55d : 0.82d + Hash01(seed, 47) * 0.38d);
        var columnRadius = Math.Max(1.0, rowRadius * elongation);
        var lobeCount = 2 + (int)Math.Floor(Hash01(seed, 53) * 2d);
        var lobeStrength = (shape == IslandShape.Lobed ? 0.10d : 0.07d) + irregularity * 0.30d;
        var waistStrength = shape == IslandShape.Lobed ? 0.08d + irregularity * 0.14d : irregularity * 0.05d;
        var asymmetryStrength = irregularity * 0.20d;
        var searchScale = shape switch
        {
            IslandShape.Elongated => 1.92d,
            IslandShape.Chain => 2.05d,
            IslandShape.Lobed => 1.55d,
            _ => 1.42d
        };

        return new IslandShapePlan(
            shape,
            rowRadius,
            columnRadius,
            rotation,
            lobeCount,
            lobeStrength,
            waistStrength,
            asymmetryStrength,
            Hash01(seed, 59) * Math.PI * 2d,
            searchScale);
    }

    private static bool IsInsideIslandShape(
        int row,
        int column,
        int centerRow,
        int centerColumn,
        IslandShapePlan plan,
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
        var distance = GetIslandShapeDistance(plan, normalizedRow, normalizedColumn);
        var angle = Math.Atan2(normalizedRow, normalizedColumn);
        var shorelineNoise = SampleIslandNoise(seed, row, column) - 0.5d;
        var broadBend = Math.Sin((angle * 2d) + plan.Phase * 0.73d);
        var lobeWave = Math.Sin((angle * plan.LobeCount) + plan.Phase);
        var secondaryWave = Math.Sin((angle * (plan.LobeCount + 1)) - plan.Phase * 0.61d);
        var edge = 1d +
                   broadBend * plan.AsymmetryStrength +
                   lobeWave * plan.LobeStrength +
                   secondaryWave * plan.LobeStrength * 0.34d +
                   shorelineNoise * plan.LobeStrength * 0.90d;

        if (plan.Shape == IslandShape.Lobed)
        {
            var waist = Math.Pow(Math.Abs(Math.Sin(angle - plan.Phase)), 2d) * plan.WaistStrength;
            edge -= waist;
        }

        return distance <= Math.Clamp(edge, 0.52d, 1.40d);
    }

    private static double GetIslandShapeDistance(IslandShapePlan plan, double normalizedRow, double normalizedColumn)
    {
        var distance = Math.Sqrt((normalizedRow * normalizedRow) + (normalizedColumn * normalizedColumn));
        if (plan.Shape != IslandShape.Chain)
        {
            return distance;
        }

        var firstOffset = 0.72d + Math.Sin(plan.Phase) * 0.12d;
        var secondOffset = -0.58d + Math.Cos(plan.Phase * 0.7d) * 0.14d;
        var firstScale = 0.70d + Math.Abs(Math.Sin(plan.Phase)) * 0.18d;
        var secondScale = 0.55d + Math.Abs(Math.Cos(plan.Phase)) * 0.20d;
        var firstLobe = Math.Sqrt(Math.Pow(normalizedRow / firstScale, 2d) + Math.Pow((normalizedColumn - firstOffset) / firstScale, 2d));
        var secondLobe = Math.Sqrt(Math.Pow(normalizedRow / secondScale, 2d) + Math.Pow((normalizedColumn - secondOffset) / secondScale, 2d));
        return Math.Min(distance, Math.Min(firstLobe, secondLobe));
    }

    private static bool IsTooCloseToLargeLand(
        IReadOnlyList<MapTile> tiles,
        int mapWidth,
        int mapHeight,
        int centerRow,
        int centerColumn,
        int minimumDistance)
    {
        for (var row = Math.Max(0, centerRow - minimumDistance); row <= Math.Min(mapHeight - 1, centerRow + minimumDistance); row++)
        {
            for (var column = Math.Max(0, centerColumn - minimumDistance); column <= Math.Min(mapWidth - 1, centerColumn + minimumDistance); column++)
            {
                var rowDelta = row - centerRow;
                var columnDelta = column - centerColumn;
                if ((rowDelta * rowDelta) + (columnDelta * columnDelta) > minimumDistance * minimumDistance)
                {
                    continue;
                }

                var index = GetIndex(row, column, mapWidth);
                if ((uint)index < (uint)tiles.Count && tiles[index].Terrain == TileTerrain.Land)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static int GetIndex(int row, int column, int mapWidth)
    {
        return (row * mapWidth) + column;
    }

    private static double SampleIslandNoise(int seed, int row, int column)
    {
        var x = (column + Hash01(seed, 0) * 997d) / 8.5d;
        var y = (row + Hash01(seed, 1) * 997d) / 8.5d;
        return (SimplexNoise.Noise((float)x, (float)y) + 1d) * 0.5d;
    }

    private static int HashSeed(int mapWidth, int mapHeight, IslandSprinkleOptions options)
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

    private static int HashIslandSeed(int seed, int islandIndex, int attempt)
    {
        unchecked
        {
            var hash = seed;
            hash = hash * 397 ^ islandIndex;
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

    private enum IslandShape
    {
        Compact,
        Elongated,
        Lobed,
        Chain
    }

    private readonly record struct IslandShapePlan(
        IslandShape Shape,
        double RowRadius,
        double ColumnRadius,
        double Rotation,
        int LobeCount,
        double LobeStrength,
        double WaistStrength,
        double AsymmetryStrength,
        double Phase,
        double SearchScale);
}
