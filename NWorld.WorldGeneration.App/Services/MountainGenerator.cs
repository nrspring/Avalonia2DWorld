using NWorld.Map.Viewer;

namespace NWorld.WorldGeneration.App.Services;

public sealed record MountainGenerationResult(
    IReadOnlyList<MapTile> Tiles,
    int PlannedRangeCount,
    int RaisedTileCount,
    int PeakElevation);

public static class MountainGenerator
{
    private const int HillMinElevation = 2;
    private const int MountainMinElevation = 11;
    private const int MountainMaxElevation = 25;
    private const int BaseEdgeBuffer = 10;
    private const int EdgeBufferVariation = 3;
    private const int ShoreBuffer = 5;
    private const double MinimumPassWidth = 2.0;

    public static MountainGenerationResult Generate(
        IReadOnlyList<MapTile> sourceTiles,
        int mapWidth,
        int mapHeight,
        MountainGenerationOptions options)
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
        var rangeWidth = Math.Max(2, options.Radius);
        var edgeBuffer = GetUsableEdgeBuffer(mapWidth, mapHeight);
        var distanceFromWater = BuildDistanceFromWater(tiles, mapWidth, mapHeight);
        var hillIndexes = CollectHillIndexes(tiles, mapWidth, mapHeight, edgeBuffer, distanceFromWater);
        if (hillIndexes.Count == 0 || options.Count <= 0)
        {
            return new MountainGenerationResult(tiles, 0, 0, 0);
        }

        var random = new Random(HashSeed(mapWidth, mapHeight, options));
        var minRangeLength = Math.Max(rangeWidth * 3, Math.Min(mapWidth, mapHeight) / 18);
        var maxRangeLength = Math.Max(minRangeLength + 1, Math.Min(mapWidth, mapHeight) / 6);
        var ranges = PlanRanges(tiles, hillIndexes, mapWidth, mapHeight, edgeBuffer, options, random, minRangeLength, maxRangeLength);

        if (ranges.Count == 0)
        {
            return new MountainGenerationResult(tiles, 0, 0, 0);
        }

        var raisedTileCount = 0;
        var peakElevation = 0;

        for (var index = 0; index < tiles.Length; index++)
        {
            var tile = tiles[index];
            if (tile.Terrain != TileTerrain.Land ||
                !IsHillElevation(tile.Elevation) ||
                !IsInsideBufferedArea(tile.Row, tile.Column, mapWidth, mapHeight, edgeBuffer) ||
                !IsPastShoreBuffer(distanceFromWater[index]))
            {
                continue;
            }

            var mountainStrength = 0d;
            foreach (var range in ranges)
            {
                var warpRow = (SampleMountainNoise(range.Seed + 11, tile.Row, tile.Column) - 0.5d) * range.Width * 0.85d;
                var warpColumn = (SampleMountainNoise(range.Seed + 23, tile.Row, tile.Column) - 0.5d) * range.Width * 0.85d;
                var distance = GetDistanceToSegment(
                    tile.Row + warpRow,
                    tile.Column + warpColumn,
                    range.StartRow,
                    range.StartColumn,
                    range.EndRow,
                    range.EndColumn,
                    out var travel);
                if (distance > range.Width)
                {
                    continue;
                }

                var ridgeStrength = Math.Pow(1d - distance / range.Width, 1.65d);
                var endFade = Math.Sqrt(Math.Max(0d, Math.Sin(travel * Math.PI)));
                var passCut = GetPassCut(range, travel);
                var shoulderNoise = 0.72d + SampleMountainNoise(range.Seed + 53, tile.Row, tile.Column) * 0.36d;
                var hillBase = Math.Clamp((tile.Elevation - HillMinElevation) / 8d, 0.35d, 1d);
                mountainStrength = Math.Max(mountainStrength, ridgeStrength * endFade * passCut * shoulderNoise * hillBase);
            }

            if (mountainStrength <= 0.10d)
            {
                continue;
            }

            var strength = Math.Clamp(options.Strength / 24d, 0.05d, 2d);
            var targetElevation = Math.Clamp(
                MountainMinElevation + (int)Math.Round(mountainStrength * strength * (MountainMaxElevation - MountainMinElevation)),
                MountainMinElevation,
                MountainMaxElevation);

            if (targetElevation <= tile.Elevation)
            {
                continue;
            }

            tiles[index] = tile with { Elevation = targetElevation };
            peakElevation = Math.Max(peakElevation, targetElevation);
            raisedTileCount++;
        }

        return new MountainGenerationResult(tiles, ranges.Count, raisedTileCount, peakElevation);
    }

    private static List<int> CollectHillIndexes(
        IReadOnlyList<MapTile> tiles,
        int mapWidth,
        int mapHeight,
        int edgeBuffer,
        IReadOnlyList<int> distanceFromWater)
    {
        var indexes = new List<int>();
        for (var index = 0; index < tiles.Count; index++)
        {
            var tile = tiles[index];
            if (tile.Terrain == TileTerrain.Land &&
                IsHillElevation(tile.Elevation) &&
                IsInsideBufferedArea(tile.Row, tile.Column, mapWidth, mapHeight, edgeBuffer) &&
                IsPastShoreBuffer(distanceFromWater[index]))
            {
                indexes.Add(index);
            }
        }

        return indexes;
    }

    private static List<MountainRangePlan> PlanRanges(
        IReadOnlyList<MapTile> tiles,
        IReadOnlyList<int> hillIndexes,
        int mapWidth,
        int mapHeight,
        int edgeBuffer,
        MountainGenerationOptions options,
        Random random,
        int minRangeLength,
        int maxRangeLength)
    {
        var ranges = new List<MountainRangePlan>(Math.Min(options.Count, 64));
        var rangeWidth = Math.Max(2, options.Radius);
        var minimumDistance = Math.Max(2, rangeWidth * 2);
        var minimumDistanceSquared = minimumDistance * minimumDistance;
        var attemptLimit = Math.Max(128, options.Count * 32);

        for (var attempt = 0; attempt < attemptLimit && ranges.Count < Math.Min(options.Count, 64); attempt++)
        {
            var center = tiles[hillIndexes[random.Next(hillIndexes.Count)]];
            var tooClose = false;
            foreach (var range in ranges)
            {
                var rowDelta = center.Row - range.CenterRow;
                var columnDelta = center.Column - range.CenterColumn;
                if ((rowDelta * rowDelta) + (columnDelta * columnDelta) < minimumDistanceSquared)
                {
                    tooClose = true;
                    break;
                }
            }

            if (tooClose)
            {
                continue;
            }

            var angle = random.NextDouble() * Math.PI * 2d;
            var length = random.Next(minRangeLength, maxRangeLength + 1);
            var halfLength = length / 2d;
            var rowDeltaToEnd = Math.Sin(angle) * halfLength;
            var columnDeltaToEnd = Math.Cos(angle) * halfLength;
            var minRow = edgeBuffer;
            var maxRow = mapHeight - 1 - edgeBuffer;
            var minColumn = edgeBuffer;
            var maxColumn = mapWidth - 1 - edgeBuffer;
            var startRow = Math.Clamp(center.Row - rowDeltaToEnd, minRow, maxRow);
            var startColumn = Math.Clamp(center.Column - columnDeltaToEnd, minColumn, maxColumn);
            var endRow = Math.Clamp(center.Row + rowDeltaToEnd, minRow, maxRow);
            var endColumn = Math.Clamp(center.Column + columnDeltaToEnd, minColumn, maxColumn);
            var actualLength = GetDistance(startRow, startColumn, endRow, endColumn);
            var passPositions = BuildPassPositions(actualLength, rangeWidth, random);

            ranges.Add(new MountainRangePlan(
                center.Row,
                center.Column,
                startRow,
                startColumn,
                endRow,
                endColumn,
                rangeWidth,
                actualLength,
                HashRangeSeed(options.Seed, ranges.Count, attempt),
                passPositions));
        }

        return ranges;
    }

    private static IReadOnlyList<double> BuildPassPositions(double rangeLength, double rangeWidth, Random random)
    {
        var passThreshold = Math.Max(24d, rangeWidth * 1.5d);
        if (rangeLength < passThreshold)
        {
            return [];
        }

        var passCount = rangeLength switch
        {
            _ when rangeLength >= passThreshold * 2.7d => random.Next(2, 4),
            _ when rangeLength >= passThreshold * 1.8d => random.Next(1, 3),
            _ => 1
        };

        var positions = new List<double>(passCount);
        for (var attempt = 0; attempt < 24 && positions.Count < passCount; attempt++)
        {
            var position = 0.18d + random.NextDouble() * 0.64d;
            if (positions.Any(existing => Math.Abs(existing - position) < 0.16d))
            {
                continue;
            }

            positions.Add(position);
        }

        positions.Sort();
        return positions;
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

    private static bool IsPastShoreBuffer(int distanceFromWater)
    {
        return distanceFromWater < 0 || distanceFromWater > ShoreBuffer;
    }

    private static int GetUsableEdgeBuffer(int mapWidth, int mapHeight)
    {
        var maxBuffer = Math.Max(0, (Math.Min(mapWidth, mapHeight) - 1) / 2 - 1);
        return Math.Clamp(BaseEdgeBuffer, 0, maxBuffer);
    }

    private static bool IsInsideBufferedArea(int row, int column, int mapWidth, int mapHeight, int edgeBuffer)
    {
        var nearestEdgeDistance = Math.Min(
            Math.Min(row, column),
            Math.Min(mapHeight - 1 - row, mapWidth - 1 - column));
        return nearestEdgeDistance >= GetNaturalEdgeBuffer(row, column, mapWidth, mapHeight, edgeBuffer);
    }

    private static int GetNaturalEdgeBuffer(int row, int column, int mapWidth, int mapHeight, int edgeBuffer)
    {
        if (edgeBuffer <= 0)
        {
            return 0;
        }

        var maxBuffer = Math.Max(0, (Math.Min(mapWidth, mapHeight) - 1) / 2 - 1);
        var jitter = (SampleEdgeNoise(row, column, mapWidth, mapHeight) - 0.5d) * EdgeBufferVariation * 2d;
        return Math.Clamp(edgeBuffer + (int)Math.Round(jitter), 0, maxBuffer);
    }

    private static double SampleEdgeNoise(int row, int column, int mapWidth, int mapHeight)
    {
        var x = (column + mapWidth * 0.137d) / 41d;
        var y = (row + mapHeight * 0.193d) / 41d;
        return (SimplexNoise.Noise((float)x, (float)y) + 1d) * 0.5d;
    }

    private static double GetPassCut(MountainRangePlan range, double travel)
    {
        var cut = 1d;
        var rangeLength = Math.Max(range.Length, 0.0001d);
        var coreHalfWidth = MinimumPassWidth / 2d;
        var taperWidth = Math.Max(MinimumPassWidth, range.Width * 0.20d);

        foreach (var passPosition in range.PassPositions)
        {
            var distanceAlongRange = Math.Abs(travel - passPosition) * rangeLength;
            if (distanceAlongRange <= coreHalfWidth)
            {
                cut = Math.Min(cut, 0.04d);
                continue;
            }

            var taperProgress = Math.Clamp((distanceAlongRange - coreHalfWidth) / taperWidth, 0d, 1d);
            var smoothTaper = taperProgress * taperProgress * (3d - 2d * taperProgress);
            cut = Math.Min(cut, 0.04d + smoothTaper * 0.96d);
        }

        return cut;
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

    private static double GetDistance(double startRow, double startColumn, double endRow, double endColumn)
    {
        var rowDelta = endRow - startRow;
        var columnDelta = endColumn - startColumn;
        return Math.Sqrt(rowDelta * rowDelta + columnDelta * columnDelta);
    }

    private static double SampleMountainNoise(int seed, int row, int column)
    {
        var x = (column + Hash01(seed, 0) * 997d) / 11d;
        var y = (row + Hash01(seed, 1) * 997d) / 11d;
        return (SimplexNoise.Noise((float)x, (float)y) + 1d) * 0.5d;
    }

    private static bool IsHillElevation(int elevation)
    {
        return elevation is >= HillMinElevation and < MountainMinElevation;
    }

    private static int HashSeed(int mapWidth, int mapHeight, MountainGenerationOptions options)
    {
        unchecked
        {
            var hash = options.Seed;
            hash = hash * 397 ^ mapWidth;
            hash = hash * 397 ^ mapHeight;
            hash = hash * 397 ^ options.Count;
            hash = hash * 397 ^ options.Strength;
            hash = hash * 397 ^ options.Radius;
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

    private readonly record struct MountainRangePlan(
        double CenterRow,
        double CenterColumn,
        double StartRow,
        double StartColumn,
        double EndRow,
        double EndColumn,
        double Width,
        double Length,
        int Seed,
        IReadOnlyList<double> PassPositions);
}
