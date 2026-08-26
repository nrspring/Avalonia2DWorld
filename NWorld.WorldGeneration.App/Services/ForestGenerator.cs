using NWorld.Map.Viewer;

namespace NWorld.WorldGeneration.App.Services;

public sealed record ForestGenerationResult(
    IReadOnlyList<MapTile> Tiles,
    int PlannedForestCount,
    int ForestTileCount);

public static class ForestGenerator
{
    private const int MountainMinElevation = 11;
    private const int ShorelineBufferTiles = 5;

    public static ForestGenerationResult Generate(
        IReadOnlyList<MapTile> sourceTiles,
        int mapWidth,
        int mapHeight,
        ForestGenerationOptions options)
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
            return new ForestGenerationResult(tiles, 0, 0);
        }

        var shorelineBuffer = BuildShorelineBuffer(tiles, mapWidth, mapHeight);
        var eligibleIndexes = CollectEligibleIndexes(tiles, shorelineBuffer);
        if (eligibleIndexes.Count == 0)
        {
            return new ForestGenerationResult(tiles, 0, 0);
        }

        var random = new Random(HashSeed(mapWidth, mapHeight, options));
        var minLength = Math.Max(options.Width * 3, Math.Min(mapWidth, mapHeight) / 16);
        var maxLength = Math.Max(minLength + 1, Math.Min(mapWidth, mapHeight) / 5);
        var plans = PlanForests(tiles, eligibleIndexes, mapWidth, mapHeight, options, random, minLength, maxLength);
        var plantedForestMask = new bool[tiles.Length];
        var forestTileCount = 0;

        foreach (var plan in plans)
        {
            for (var index = 0; index < tiles.Length; index++)
            {
                var tile = tiles[index];
                if (!IsEligibleForestTile(tile) || shorelineBuffer[index] || tile.Resource != TileResource.None)
                {
                    continue;
                }

                var warpRow = (SampleForestNoise(plan.Seed + 11, tile.Row, tile.Column) - 0.5d) * plan.Width * 0.65d;
                var warpColumn = (SampleForestNoise(plan.Seed + 23, tile.Row, tile.Column) - 0.5d) * plan.Width * 0.65d;
                var distance = GetDistanceToSegment(
                    tile.Row + warpRow,
                    tile.Column + warpColumn,
                    plan.StartRow,
                    plan.StartColumn,
                    plan.EndRow,
                    plan.EndColumn,
                    out var travel);
                if (distance > plan.Width)
                {
                    continue;
                }

                var coreStrength = Math.Pow(1d - distance / plan.Width, 0.85d);
                var endFade = Math.Sqrt(Math.Max(0d, Math.Sin(travel * Math.PI)));
                var noise = 0.58d + SampleForestNoise(plan.Seed + 53, tile.Row, tile.Column) * 0.48d;
                var density = Math.Clamp(options.DensityPercent / 100d, 0d, 1d);
                var threshold = 0.24d + (1d - density) * 0.34d;
                var forestStrength = coreStrength * endFade * noise;
                if (forestStrength < threshold)
                {
                    continue;
                }

                tiles[index] = tile with { Resource = TileResource.Forest };
                plantedForestMask[index] = true;
                forestTileCount++;
            }
        }

        foreach (var plan in plans)
        {
            forestTileCount -= CarveForestPaths(tiles, plantedForestMask, mapWidth, mapHeight, plan);
        }

        return new ForestGenerationResult(tiles, plans.Count, forestTileCount);
    }

    private static List<int> CollectEligibleIndexes(IReadOnlyList<MapTile> tiles, IReadOnlyList<bool> shorelineBuffer)
    {
        var indexes = new List<int>();
        for (var index = 0; index < tiles.Count; index++)
        {
            if (IsEligibleForestTile(tiles[index]) && !shorelineBuffer[index])
            {
                indexes.Add(index);
            }
        }

        return indexes;
    }

    private static bool IsEligibleForestTile(MapTile tile)
    {
        return tile.Terrain == TileTerrain.Land &&
               tile.Elevation < MountainMinElevation;
    }

    private static bool[] BuildShorelineBuffer(IReadOnlyList<MapTile> tiles, int mapWidth, int mapHeight)
    {
        var buffer = new bool[tiles.Count];
        var bufferDistanceSquared = ShorelineBufferTiles * ShorelineBufferTiles;

        for (var index = 0; index < tiles.Count; index++)
        {
            var tile = tiles[index];
            if (!IsSeaWater(tile))
            {
                continue;
            }

            var minRow = Math.Max(0, tile.Row - ShorelineBufferTiles);
            var maxRow = Math.Min(mapHeight - 1, tile.Row + ShorelineBufferTiles);
            var minColumn = Math.Max(0, tile.Column - ShorelineBufferTiles);
            var maxColumn = Math.Min(mapWidth - 1, tile.Column + ShorelineBufferTiles);

            for (var row = minRow; row <= maxRow; row++)
            {
                var rowDelta = row - tile.Row;
                for (var column = minColumn; column <= maxColumn; column++)
                {
                    var columnDelta = column - tile.Column;
                    if (rowDelta * rowDelta + columnDelta * columnDelta > bufferDistanceSquared)
                    {
                        continue;
                    }

                    var bufferIndex = row * mapWidth + column;
                    if (bufferIndex >= 0 && bufferIndex < buffer.Length)
                    {
                        buffer[bufferIndex] = true;
                    }
                }
            }
        }

        return buffer;
    }

    private static bool IsSeaWater(MapTile tile)
    {
        return tile.Terrain != TileTerrain.Land &&
               tile.Elevation <= 0;
    }

    private static List<ForestPlan> PlanForests(
        IReadOnlyList<MapTile> tiles,
        IReadOnlyList<int> eligibleIndexes,
        int mapWidth,
        int mapHeight,
        ForestGenerationOptions options,
        Random random,
        int minLength,
        int maxLength)
    {
        var plans = new List<ForestPlan>(Math.Min(options.Count, 120));
        var width = Math.Max(2, options.Width);
        var minimumDistanceSquared = width * width;
        var attemptLimit = Math.Max(128, options.Count * 32);

        for (var attempt = 0; attempt < attemptLimit && plans.Count < Math.Min(options.Count, 120); attempt++)
        {
            var center = tiles[eligibleIndexes[random.Next(eligibleIndexes.Count)]];
            if (plans.Any(plan => GetDistance(center.Row, center.Column, plan.CenterRow, plan.CenterColumn) < width))
            {
                continue;
            }

            var angle = random.NextDouble() * Math.PI * 2d;
            var length = random.Next(minLength, maxLength + 1);
            var halfLength = length / 2d;
            var rowDelta = Math.Sin(angle) * halfLength;
            var columnDelta = Math.Cos(angle) * halfLength;
            var startRow = Math.Clamp(center.Row - rowDelta, 0, mapHeight - 1);
            var startColumn = Math.Clamp(center.Column - columnDelta, 0, mapWidth - 1);
            var endRow = Math.Clamp(center.Row + rowDelta, 0, mapHeight - 1);
            var endColumn = Math.Clamp(center.Column + columnDelta, 0, mapWidth - 1);

            plans.Add(new ForestPlan(
                center.Row,
                center.Column,
                startRow,
                startColumn,
                endRow,
                endColumn,
                width,
                HashForestSeed(options.Seed, plans.Count, attempt),
                BuildPassPositions(options.PassFrequencyPercent, random)));
        }

        return plans;
    }

    private static IReadOnlyList<double> BuildPassPositions(int passFrequencyPercent, Random random)
    {
        var clamped = Math.Clamp(passFrequencyPercent, 0, 100);
        var passCount = clamped switch
        {
            0 => 0,
            < 35 => random.NextDouble() < clamped / 100d ? 1 : 0,
            < 70 => random.Next(1, 3),
            _ => random.Next(2, 5)
        };

        var positions = new List<double>(passCount);
        for (var attempt = 0; attempt < 32 && positions.Count < passCount; attempt++)
        {
            var position = 0.14d + random.NextDouble() * 0.72d;
            if (positions.Any(existing => Math.Abs(existing - position) < 0.12d))
            {
                continue;
            }

            positions.Add(position);
        }

        return positions;
    }

    private static double GetDistanceToSegment(
        double row,
        double column,
        double startRow,
        double startColumn,
        double endRow,
        double endColumn,
        out double travel)
    {
        var rowDelta = endRow - startRow;
        var columnDelta = endColumn - startColumn;
        var lengthSquared = rowDelta * rowDelta + columnDelta * columnDelta;

        if (lengthSquared <= 0.0001d)
        {
            travel = 0d;
            return GetDistance(row, column, startRow, startColumn);
        }

        travel = Math.Clamp(((row - startRow) * rowDelta + (column - startColumn) * columnDelta) / lengthSquared, 0d, 1d);
        var closestRow = startRow + travel * rowDelta;
        var closestColumn = startColumn + travel * columnDelta;
        return GetDistance(row, column, closestRow, closestColumn);
    }

    private static int CarveForestPaths(
        MapTile[] tiles,
        IList<bool> forestMask,
        int mapWidth,
        int mapHeight,
        ForestPlan plan)
    {
        var carvedCount = 0;
        var pathWidth = plan.Width >= 5d ? 2 : 1;
        var extensionLimit = Math.Max(8, (int)Math.Ceiling(plan.Width * 3d));
        carvedCount += CarveCardinalPath(
            tiles,
            forestMask,
            mapWidth,
            mapHeight,
            (int)Math.Round(plan.StartRow),
            (int)Math.Round(plan.StartColumn),
            (int)Math.Round(plan.EndRow),
            (int)Math.Round(plan.EndColumn),
            pathWidth,
            extensionLimit,
            plan.Seed + 101);

        foreach (var passPosition in plan.PassPositions)
        {
            var centerRow = plan.StartRow + (plan.EndRow - plan.StartRow) * passPosition;
            var centerColumn = plan.StartColumn + (plan.EndColumn - plan.StartColumn) * passPosition;
            var rowDelta = plan.EndRow - plan.StartRow;
            var columnDelta = plan.EndColumn - plan.StartColumn;
            var length = Math.Sqrt(rowDelta * rowDelta + columnDelta * columnDelta);
            if (length <= 0.0001d)
            {
                continue;
            }

            var normalRow = -columnDelta / length;
            var normalColumn = rowDelta / length;
            var reach = plan.Width + 2d;
            carvedCount += CarveCardinalPath(
                tiles,
                forestMask,
                mapWidth,
                mapHeight,
                (int)Math.Round(centerRow - normalRow * reach),
                (int)Math.Round(centerColumn - normalColumn * reach),
                (int)Math.Round(centerRow + normalRow * reach),
                (int)Math.Round(centerColumn + normalColumn * reach),
                pathWidth,
                extensionLimit,
                plan.Seed + (int)Math.Round(passPosition * 1000d));
        }

        return carvedCount;
    }

    private static int CarveCardinalPath(
        MapTile[] tiles,
        IList<bool> forestMask,
        int mapWidth,
        int mapHeight,
        int startRow,
        int startColumn,
        int endRow,
        int endColumn,
        int pathWidth,
        int extensionLimit,
        int seed)
    {
        var carvedCount = 0;
        var row = Math.Clamp(startRow, 0, mapHeight - 1);
        var column = Math.Clamp(startColumn, 0, mapWidth - 1);
        endRow = Math.Clamp(endRow, 0, mapHeight - 1);
        endColumn = Math.Clamp(endColumn, 0, mapWidth - 1);
        var random = new Random(seed);
        var side = random.Next(2) == 0 ? -1 : 1;
        var stepLimit = Math.Max(8, mapWidth + mapHeight);
        var route = new List<PathStep>(stepLimit + extensionLimit * 2);

        for (var step = 0; step <= stepLimit; step++)
        {
            if (row == endRow && column == endColumn)
            {
                var previous = route.Count > 0 ? route[^1] : default;
                route.Add(new PathStep(row, column, previous.RowStep, previous.ColumnStep));
                break;
            }

            var rowDistance = Math.Abs(endRow - row);
            var columnDistance = Math.Abs(endColumn - column);
            var moveRow = rowDistance > 0 &&
                          (columnDistance == 0 || random.Next(rowDistance + columnDistance) < rowDistance);

            var rowStep = 0;
            var columnStep = 0;
            if (moveRow)
            {
                rowStep = Math.Sign(endRow - row);
            }
            else if (columnDistance > 0)
            {
                columnStep = Math.Sign(endColumn - column);
            }

            route.Add(new PathStep(row, column, rowStep, columnStep));
            row += rowStep;
            column += columnStep;
        }

        ExtendForestRoute(route, forestMask, mapWidth, mapHeight, pathWidth, side, extensionLimit);

        foreach (var pathStep in route)
        {
            carvedCount += ClearForestPathTile(
                tiles,
                forestMask,
                mapWidth,
                mapHeight,
                pathStep.Row,
                pathStep.Column,
                pathStep.RowStep,
                pathStep.ColumnStep,
                pathWidth,
                side);
        }

        return carvedCount;
    }

    private static void ExtendForestRoute(
        List<PathStep> route,
        IList<bool> forestMask,
        int mapWidth,
        int mapHeight,
        int pathWidth,
        int side,
        int extensionLimit)
    {
        if (route.Count == 0)
        {
            return;
        }

        var firstDirection = GetFirstRouteDirection(route);
        if (firstDirection != default)
        {
            ExtendForestRouteStart(route, forestMask, mapWidth, mapHeight, pathWidth, side, extensionLimit, firstDirection);
        }

        var lastDirection = GetLastRouteDirection(route);
        if (lastDirection != default)
        {
            ExtendForestRouteEnd(route, forestMask, mapWidth, mapHeight, pathWidth, side, extensionLimit, lastDirection);
        }
    }

    private static PathStep GetFirstRouteDirection(IReadOnlyList<PathStep> route)
    {
        foreach (var step in route)
        {
            if (step.RowStep != 0 || step.ColumnStep != 0)
            {
                return step;
            }
        }

        return default;
    }

    private static PathStep GetLastRouteDirection(IReadOnlyList<PathStep> route)
    {
        for (var index = route.Count - 1; index >= 0; index--)
        {
            var step = route[index];
            if (step.RowStep != 0 || step.ColumnStep != 0)
            {
                return step;
            }
        }

        return default;
    }

    private static void ExtendForestRouteStart(
        List<PathStep> route,
        IList<bool> forestMask,
        int mapWidth,
        int mapHeight,
        int pathWidth,
        int side,
        int extensionLimit,
        PathStep direction)
    {
        for (var extension = 0; extension < extensionLimit; extension++)
        {
            var first = route[0];
            var row = first.Row - direction.RowStep;
            var column = first.Column - direction.ColumnStep;
            if (!IsInBounds(row, column, mapWidth, mapHeight) ||
                !HasForestNearPath(forestMask, mapWidth, mapHeight, row, column, direction.RowStep, direction.ColumnStep, pathWidth, side))
            {
                break;
            }

            route.Insert(0, new PathStep(row, column, direction.RowStep, direction.ColumnStep));
        }
    }

    private static void ExtendForestRouteEnd(
        List<PathStep> route,
        IList<bool> forestMask,
        int mapWidth,
        int mapHeight,
        int pathWidth,
        int side,
        int extensionLimit,
        PathStep direction)
    {
        for (var extension = 0; extension < extensionLimit; extension++)
        {
            var last = route[^1];
            var row = last.Row + direction.RowStep;
            var column = last.Column + direction.ColumnStep;
            if (!IsInBounds(row, column, mapWidth, mapHeight) ||
                !HasForestNearPath(forestMask, mapWidth, mapHeight, row, column, direction.RowStep, direction.ColumnStep, pathWidth, side))
            {
                break;
            }

            route.Add(new PathStep(row, column, direction.RowStep, direction.ColumnStep));
        }
    }

    private static bool HasForestNearPath(
        IList<bool> forestMask,
        int mapWidth,
        int mapHeight,
        int centerRow,
        int centerColumn,
        int rowStep,
        int columnStep,
        int pathWidth,
        int side)
    {
        const int searchRadius = 2;
        for (var rowOffset = -searchRadius; rowOffset <= searchRadius; rowOffset++)
        {
            for (var columnOffset = -searchRadius; columnOffset <= searchRadius; columnOffset++)
            {
                if (IsForestPathFootprint(
                        forestMask,
                        mapWidth,
                        mapHeight,
                        centerRow + rowOffset,
                        centerColumn + columnOffset,
                        rowStep,
                        columnStep,
                        pathWidth,
                        side))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsForestPathFootprint(
        IList<bool> forestMask,
        int mapWidth,
        int mapHeight,
        int centerRow,
        int centerColumn,
        int rowStep,
        int columnStep,
        int pathWidth,
        int side)
    {
        if (IsForestMaskTile(forestMask, mapWidth, mapHeight, centerRow, centerColumn))
        {
            return true;
        }

        if (pathWidth <= 1)
        {
            return false;
        }

        return rowStep != 0
            ? IsForestMaskTile(forestMask, mapWidth, mapHeight, centerRow, centerColumn + side)
            : IsForestMaskTile(forestMask, mapWidth, mapHeight, centerRow + side, centerColumn);
    }

    private static bool IsForestMaskTile(IList<bool> forestMask, int mapWidth, int mapHeight, int row, int column)
    {
        if (!IsInBounds(row, column, mapWidth, mapHeight))
        {
            return false;
        }

        var index = row * mapWidth + column;
        return index >= 0 && index < forestMask.Count && forestMask[index];
    }

    private static bool IsInBounds(int row, int column, int mapWidth, int mapHeight)
    {
        return row >= 0 && row < mapHeight && column >= 0 && column < mapWidth;
    }

    private static int ClearForestPathTile(
        MapTile[] tiles,
        IList<bool> plantedForestMask,
        int mapWidth,
        int mapHeight,
        int centerRow,
        int centerColumn,
        int rowStep,
        int columnStep,
        int pathWidth,
        int side)
    {
        var carvedCount = ClearForestTile(tiles, plantedForestMask, mapWidth, mapHeight, centerRow, centerColumn);
        if (pathWidth <= 1)
        {
            return carvedCount;
        }

        if (rowStep != 0)
        {
            carvedCount += ClearForestTile(tiles, plantedForestMask, mapWidth, mapHeight, centerRow, centerColumn + side);
        }
        else if (columnStep != 0)
        {
            carvedCount += ClearForestTile(tiles, plantedForestMask, mapWidth, mapHeight, centerRow + side, centerColumn);
        }
        else
        {
            carvedCount += ClearForestTile(tiles, plantedForestMask, mapWidth, mapHeight, centerRow, centerColumn + side);
        }

        return carvedCount;
    }

    private static int ClearForestTile(
        MapTile[] tiles,
        IList<bool> plantedForestMask,
        int mapWidth,
        int mapHeight,
        int row,
        int column)
    {
        if (row < 0 || row >= mapHeight || column < 0 || column >= mapWidth)
        {
            return 0;
        }

        var index = row * mapWidth + column;
        if (index < 0 ||
            index >= tiles.Length ||
            index >= plantedForestMask.Count ||
            !plantedForestMask[index] ||
            tiles[index].Resource != TileResource.Forest)
        {
            return 0;
        }

        tiles[index] = tiles[index] with { Resource = TileResource.None };
        plantedForestMask[index] = false;
        return 1;
    }

    private static double GetDistance(double firstRow, double firstColumn, double secondRow, double secondColumn)
    {
        var row = firstRow - secondRow;
        var column = firstColumn - secondColumn;
        return Math.Sqrt(row * row + column * column);
    }

    private static double SampleForestNoise(int seed, int row, int column)
    {
        var x = (column + Hash01(seed, 0) * 997d) / 19d;
        var y = (row + Hash01(seed, 1) * 997d) / 19d;
        return (SimplexNoise.Noise((float)x, (float)y) + 1d) * 0.5d;
    }

    private static int HashSeed(int mapWidth, int mapHeight, ForestGenerationOptions options)
    {
        unchecked
        {
            var hash = options.Seed;
            hash = hash * 397 ^ mapWidth;
            hash = hash * 397 ^ mapHeight;
            hash = hash * 397 ^ options.Count;
            hash = hash * 397 ^ options.DensityPercent;
            hash = hash * 397 ^ options.Width;
            hash = hash * 397 ^ options.PassFrequencyPercent;
            return hash & 0x7fffffff;
        }
    }

    private static int HashForestSeed(int seed, int forestIndex, int attempt)
    {
        unchecked
        {
            var hash = seed;
            hash = hash * 397 ^ forestIndex;
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

    private readonly record struct ForestPlan(
        int CenterRow,
        int CenterColumn,
        double StartRow,
        double StartColumn,
        double EndRow,
        double EndColumn,
        double Width,
        int Seed,
        IReadOnlyList<double> PassPositions);

    private readonly record struct PathStep(int Row, int Column, int RowStep, int ColumnStep);
}
