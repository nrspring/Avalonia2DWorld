using NWorld.Map.Viewer;

namespace NWorld.WorldGeneration.App.Services;

public sealed record HillGenerationResult(
    IReadOnlyList<MapTile> Tiles,
    int PlannedRangeCount,
    int RaisedTileCount,
    int PeakElevation);

public static class HillGenerator
{
    private const int HillMinElevation = 2;
    private const int HillMaxElevation = 10;
    private const int HillShoreFadeDistance = 10;

    public static HillGenerationResult Generate(IReadOnlyList<MapTile> sourceTiles, int mapWidth, int mapHeight, HillGenerationOptions options)
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
        for (var index = 0; index < tiles.Length; index++)
        {
            if (tiles[index].Terrain == TileTerrain.Land)
            {
                tiles[index] = tiles[index] with { Elevation = 1 };
            }
        }

        var landIndexes = CollectLandIndexes(tiles);
        if (landIndexes.Count == 0 || options.Count <= 0)
        {
            return new HillGenerationResult(tiles, 0, 0, 0);
        }

        var distanceFromWater = BuildDistanceFromWater(tiles, mapWidth, mapHeight);
        var random = new Random(HashSeed(mapWidth, mapHeight, options));
        var minRangeLength = Math.Max(6, Math.Min(mapWidth, mapHeight) / 14);
        var maxRangeLength = Math.Max(10, Math.Min(mapWidth, mapHeight) / 4);
        var plans = new List<HillRangePlan>();

        for (var rangeIndex = 0; rangeIndex < Math.Min(options.Count, 48); rangeIndex++)
        {
            if (TryCreateRangePlan(tiles, landIndexes, mapWidth, mapHeight, distanceFromWater, options, random, rangeIndex, minRangeLength, maxRangeLength, out var plan))
            {
                plans.Add(plan);
            }
        }

        if (plans.Count == 0)
        {
            return new HillGenerationResult(tiles, 0, 0, 0);
        }

        var raisedTileCount = 0;
        var peakElevation = 0;

        for (var index = 0; index < tiles.Length; index++)
        {
            var tile = tiles[index];
            if (tile.Terrain != TileTerrain.Land)
            {
                continue;
            }

            var shoreFade = GetHillShoreFade(distanceFromWater[index]);
            if (shoreFade <= 0)
            {
                continue;
            }

            var addedElevation = 0d;
            foreach (var plan in plans)
            {
                var warpRow = (SampleSmoothNoise(plan.Seed + 11, tile.Row, tile.Column) - 0.5d) * plan.Width * 0.9d;
                var warpColumn = (SampleSmoothNoise(plan.Seed + 23, tile.Row, tile.Column) - 0.5d) * plan.Width * 0.9d;
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

                var ridgeStrength = Math.Pow(1d - distance / plan.Width, 1.35d);
                var shoulderNoise = 0.68d + SampleSmoothNoise(plan.Seed + 53, tile.Row, tile.Column) * 0.34d;
                var passCut = GetPassCut(plan, travel);
                addedElevation = Math.Max(addedElevation, options.Strength * ridgeStrength * shoulderNoise * passCut * shoreFade);
            }

            if (addedElevation <= 0.35d)
            {
                continue;
            }

            var hillStrength = Math.Clamp(addedElevation / 18d, 0d, 1d);
            var nextElevation = Math.Clamp(
                HillMinElevation + (int)Math.Round(hillStrength * (HillMaxElevation - HillMinElevation)),
                HillMinElevation,
                HillMaxElevation);

            if (nextElevation <= tile.Elevation)
            {
                continue;
            }

            tiles[index] = tile with { Elevation = nextElevation };
            peakElevation = Math.Max(peakElevation, nextElevation);
            raisedTileCount++;
        }

        return new HillGenerationResult(tiles, plans.Count, raisedTileCount, peakElevation);
    }

    private static List<int> CollectLandIndexes(IReadOnlyList<MapTile> tiles)
    {
        var indexes = new List<int>();
        for (var index = 0; index < tiles.Count; index++)
        {
            if (tiles[index].Terrain == TileTerrain.Land)
            {
                indexes.Add(index);
            }
        }

        return indexes;
    }

    private static int[] BuildDistanceFromWater(IReadOnlyList<MapTile> tiles, int mapWidth, int mapHeight)
    {
        var distances = Enumerable.Repeat(-1, tiles.Count).ToArray();
        var queue = new Queue<int>();

        for (var index = 0; index < tiles.Count; index++)
        {
            if (tiles[index].Terrain != TileTerrain.Land)
            {
                distances[index] = 0;
                queue.Enqueue(index);
            }
        }

        while (queue.Count > 0)
        {
            var index = queue.Dequeue();
            var row = index / mapWidth;
            var column = index % mapWidth;
            var nextDistance = distances[index] + 1;

            TryEnqueue(row - 1, column, nextDistance);
            TryEnqueue(row + 1, column, nextDistance);
            TryEnqueue(row, column - 1, nextDistance);
            TryEnqueue(row, column + 1, nextDistance);
        }

        return distances;

        void TryEnqueue(int row, int column, int distance)
        {
            if ((uint)row >= (uint)mapHeight || (uint)column >= (uint)mapWidth)
            {
                return;
            }

            var neighborIndex = row * mapWidth + column;
            if (distances[neighborIndex] >= 0 || tiles[neighborIndex].Terrain != TileTerrain.Land)
            {
                return;
            }

            distances[neighborIndex] = distance;
            queue.Enqueue(neighborIndex);
        }
    }

    private static bool TryCreateRangePlan(
        IReadOnlyList<MapTile> tiles,
        IReadOnlyList<int> landIndexes,
        int mapWidth,
        int mapHeight,
        IReadOnlyList<int> distanceFromWater,
        HillGenerationOptions options,
        Random random,
        int rangeIndex,
        int minRangeLength,
        int maxRangeLength,
        out HillRangePlan plan)
    {
        for (var attempt = 0; attempt < 64; attempt++)
        {
            var centerIndex = landIndexes[random.Next(landIndexes.Count)];
            var center = tiles[centerIndex];
            if (distanceFromWater[centerIndex] >= 0 && distanceFromWater[centerIndex] < 2)
            {
                continue;
            }

            var angle = random.NextDouble() * Math.PI * 2d;
            var length = random.Next(minRangeLength, maxRangeLength + 1);
            var halfLength = length / 2d;
            var rowDelta = Math.Sin(angle) * halfLength;
            var columnDelta = Math.Cos(angle) * halfLength;
            var startRow = Math.Clamp(center.Row - rowDelta, 0, mapHeight - 1);
            var startColumn = Math.Clamp(center.Column - columnDelta, 0, mapWidth - 1);
            var endRow = Math.Clamp(center.Row + rowDelta, 0, mapHeight - 1);
            var endColumn = Math.Clamp(center.Column + columnDelta, 0, mapWidth - 1);
            var passPositions = BuildPassPositions(options.PassFrequencyPercent, random);

            plan = new HillRangePlan(
                startRow,
                startColumn,
                endRow,
                endColumn,
                Math.Max(4, options.Width),
                HashRangeSeed(options.Seed, rangeIndex, attempt),
                passPositions);
            return true;
        }

        plan = default;
        return false;
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
        for (var index = 0; index < passCount; index++)
        {
            positions.Add(0.16d + random.NextDouble() * 0.68d);
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
            var pointRowDelta = row - startRow;
            var pointColumnDelta = column - startColumn;
            return Math.Sqrt(pointRowDelta * pointRowDelta + pointColumnDelta * pointColumnDelta);
        }

        travel = Math.Clamp(((row - startRow) * rowDelta + (column - startColumn) * columnDelta) / lengthSquared, 0d, 1d);
        var closestRow = startRow + travel * rowDelta;
        var closestColumn = startColumn + travel * columnDelta;
        var distanceRow = row - closestRow;
        var distanceColumn = column - closestColumn;
        return Math.Sqrt(distanceRow * distanceRow + distanceColumn * distanceColumn);
    }

    private static double GetHillShoreFade(int distanceFromWater)
    {
        if (distanceFromWater < 0)
        {
            return 1d;
        }

        var normalized = Math.Clamp((distanceFromWater - 1d) / HillShoreFadeDistance, 0d, 1d);
        return normalized * normalized * (3d - 2d * normalized);
    }

    private static double GetPassCut(HillRangePlan plan, double travel)
    {
        var cut = 1d;
        foreach (var passPosition in plan.PassPositions)
        {
            var pass = Math.Exp(-Math.Pow((travel - passPosition) / 0.06d, 2d));
            cut = Math.Min(cut, 1d - pass * 0.46d);
        }

        return cut;
    }

    private static double SampleSmoothNoise(int seed, int row, int column)
    {
        var x = (column + Hash01(seed, 0) * 997d) / 31d;
        var y = (row + Hash01(seed, 1) * 997d) / 31d;
        return (SimplexNoise.Noise((float)x, (float)y) + 1d) * 0.5d;
    }

    private static int HashSeed(int mapWidth, int mapHeight, HillGenerationOptions options)
    {
        unchecked
        {
            var hash = options.Seed;
            hash = hash * 397 ^ mapWidth;
            hash = hash * 397 ^ mapHeight;
            hash = hash * 397 ^ options.Count;
            hash = hash * 397 ^ options.Strength;
            hash = hash * 397 ^ options.Width;
            hash = hash * 397 ^ options.PassFrequencyPercent;
            return hash;
        }
    }

    private static int HashRangeSeed(int seed, int rangeIndex, int attempt)
    {
        unchecked
        {
            var hash = seed;
            hash = hash * 397 ^ rangeIndex;
            hash = hash * 397 ^ attempt;
            return hash;
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

    private readonly record struct HillRangePlan(
        double StartRow,
        double StartColumn,
        double EndRow,
        double EndColumn,
        double Width,
        int Seed,
        IReadOnlyList<double> PassPositions);
}
