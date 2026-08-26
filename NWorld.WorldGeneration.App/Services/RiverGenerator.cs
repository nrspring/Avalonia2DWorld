using NWorld.Map.Viewer;

namespace NWorld.WorldGeneration.App.Services;

public sealed record RiverGenerationResult(
    IReadOnlyList<MapTile> Tiles,
    int PlacedRiverCount,
    int AddedWaterTileCount);

public static class RiverGenerator
{
    private const int HillMinElevation = 2;
    private const int MountainMinElevation = 11;
    private static readonly (int RowOffset, int ColumnOffset)[] CardinalDirections =
    [
        (-1, 0),
        (0, 1),
        (1, 0),
        (0, -1)
    ];

    public static RiverGenerationResult Generate(
        IReadOnlyList<MapTile> sourceTiles,
        int mapWidth,
        int mapHeight,
        RiverGenerationOptions options)
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
            return new RiverGenerationResult(tiles, 0, 0);
        }

        var random = new Random(HashSeed(mapWidth, mapHeight, options));
        var placedRiverCount = 0;
        var addedWaterTileCount = 0;
        var attemptLimit = Math.Max(96, options.Count * 48);
        var maxPathLength = Math.Max(128, mapWidth + mapHeight);

        for (var attempt = 0; attempt < attemptLimit && placedRiverCount < options.Count; attempt++)
        {
            var riverSeed = HashRiverSeed(options.Seed, placedRiverCount, attempt);
            var shouldTerminateInSea = Sample01(riverSeed, 97, attempt) < Math.Clamp(options.SeaTerminationPercent / 100d, 0d, 1d);
            var distanceToWater = BuildDistanceToWater(tiles, mapWidth, mapHeight, shouldTerminateInSea);
            var startIndexes = CollectRiverStartIndexes(tiles, distanceToWater);
            if (startIndexes.Count == 0)
            {
                break;
            }

            var startIndex = PickWeightedStart(startIndexes, distanceToWater, random);
            if (!TryCreateRiverPath(tiles, distanceToWater, mapWidth, mapHeight, startIndex, options, shouldTerminateInSea, riverSeed, random, maxPathLength, out var path))
            {
                continue;
            }

            var added = ApplyRiverPath(tiles, path, mapWidth, mapHeight, options, riverSeed);
            if (added == 0)
            {
                continue;
            }

            placedRiverCount++;
            addedWaterTileCount += added;
        }

        return new RiverGenerationResult(tiles, placedRiverCount, addedWaterTileCount);
    }

    private static bool TryCreateRiverPath(
        IReadOnlyList<MapTile> tiles,
        IReadOnlyList<int> distanceToWater,
        int mapWidth,
        int mapHeight,
        int startIndex,
        RiverGenerationOptions options,
        bool seaOnly,
        int riverSeed,
        Random random,
        int maxPathLength,
        out List<int> path)
    {
        path = [];
        var visited = new HashSet<int>();
        var currentIndex = startIndex;
        var startDistance = Math.Max(1, distanceToWater[startIndex]);
        var minimumPathLength = Math.Min(maxPathLength / 2, Math.Max(16, (int)Math.Ceiling(startDistance * 0.75d)));
        (int RowOffset, int ColumnOffset)? previousDirection = null;

        for (var step = 0; step < maxPathLength; step++)
        {
            if (!visited.Add(currentIndex))
            {
                return false;
            }

            path.Add(currentIndex);

            var currentDistance = distanceToWater[currentIndex];
            if ((currentDistance <= 1 || IsAdjacentToTargetWater(tiles, currentIndex, mapWidth, mapHeight, seaOnly)) &&
                path.Count >= minimumPathLength)
            {
                return path.Count >= 4;
            }

            var nextOptions = CollectNextRiverSteps(
                tiles,
                distanceToWater,
                visited,
                currentIndex,
                mapWidth,
                mapHeight,
                previousDirection,
                options.WanderPercent);
            if (nextOptions.Count == 0)
            {
                return false;
            }

            var next = PickWeightedStep(nextOptions, random);
            previousDirection = next.Direction;
            currentIndex = next.Index;

            if (Sample01(riverSeed, step, currentIndex) < 0.06d)
            {
                previousDirection = null;
            }
        }

        return false;
    }

    private static int ApplyRiverPath(
        MapTile[] tiles,
        IReadOnlyList<int> path,
        int mapWidth,
        int mapHeight,
        RiverGenerationOptions options,
        int riverSeed)
    {
        var riverIndexes = new HashSet<int>(path);
        AddOccasionalWidth(tiles, riverIndexes, path, mapWidth, mapHeight, options.WideningPercent, riverSeed);

        var addedWaterTileCount = 0;
        foreach (var index in riverIndexes)
        {
            if ((uint)index >= (uint)tiles.Length || tiles[index].Terrain is TileTerrain.Water or TileTerrain.DeepWater)
            {
                continue;
            }

            tiles[index] = tiles[index] with
            {
                Terrain = TileTerrain.Water,
                Elevation = Math.Max(1, tiles[index].Elevation),
                Resource = TileResource.None
            };
            addedWaterTileCount++;
        }

        return addedWaterTileCount;
    }

    private static void AddOccasionalWidth(
        IReadOnlyList<MapTile> tiles,
        HashSet<int> riverIndexes,
        IReadOnlyList<int> path,
        int mapWidth,
        int mapHeight,
        int wideningPercent,
        int riverSeed)
    {
        var widening = Math.Clamp(wideningPercent / 100d, 0d, 1d);
        if (widening <= 0)
        {
            return;
        }

        for (var pathIndex = 1; pathIndex < path.Count - 1; pathIndex++)
        {
            var roll = Sample01(riverSeed, pathIndex, path[pathIndex]);
            if (roll > widening * 0.32d)
            {
                continue;
            }

            var previous = path[pathIndex - 1];
            var current = path[pathIndex];
            var next = path[pathIndex + 1];
            var previousRow = previous / mapWidth;
            var previousColumn = previous % mapWidth;
            var currentRow = current / mapWidth;
            var currentColumn = current % mapWidth;
            var nextRow = next / mapWidth;
            var nextColumn = next % mapWidth;
            var rowDirection = Math.Sign(nextRow - previousRow);
            var columnDirection = Math.Sign(nextColumn - previousColumn);

            var candidates = columnDirection != 0
                ? new[] { GetIndex(currentRow - 1, currentColumn, mapWidth), GetIndex(currentRow + 1, currentColumn, mapWidth) }
                : new[] { GetIndex(currentRow, currentColumn - 1, mapWidth), GetIndex(currentRow, currentColumn + 1, mapWidth) };

            var candidate = candidates[(int)Math.Floor(Sample01(riverSeed, pathIndex, current + 17) * candidates.Length)];
            if ((uint)candidate >= (uint)tiles.Count)
            {
                continue;
            }

            var candidateRow = candidate / mapWidth;
            var candidateColumn = candidate % mapWidth;
            if ((uint)candidateRow >= (uint)mapHeight ||
                (uint)candidateColumn >= (uint)mapWidth ||
                !IsPassableLand(tiles[candidate]))
            {
                continue;
            }

            riverIndexes.Add(candidate);
        }
    }

    private static List<RiverStepOption> CollectNextRiverSteps(
        IReadOnlyList<MapTile> tiles,
        IReadOnlyList<int> distanceToWater,
        IReadOnlySet<int> visited,
        int currentIndex,
        int mapWidth,
        int mapHeight,
        (int RowOffset, int ColumnOffset)? previousDirection,
        int wanderPercent)
    {
        var options = new List<RiverStepOption>();
        var currentRow = currentIndex / mapWidth;
        var currentColumn = currentIndex % mapWidth;
        var currentDistance = distanceToWater[currentIndex];
        var wander = Math.Clamp(wanderPercent / 100d, 0d, 1d);

        foreach (var direction in CardinalDirections)
        {
            var row = currentRow + direction.RowOffset;
            var column = currentColumn + direction.ColumnOffset;
            if ((uint)row >= (uint)mapHeight || (uint)column >= (uint)mapWidth)
            {
                continue;
            }

            var index = GetIndex(row, column, mapWidth);
            if (visited.Contains(index) ||
                (uint)index >= (uint)tiles.Count ||
                !IsPassableLand(tiles[index]))
            {
                continue;
            }

            var distance = distanceToWater[index];
            if (distance < 0)
            {
                continue;
            }

            var delta = distance - currentDistance;
            if (delta > 1 && wander < 0.85d)
            {
                continue;
            }

            var weight = delta switch
            {
                < 0 => 12d + Math.Min(8, -delta * 2),
                0 => 3d + wander * 8d,
                1 => wander * 4d,
                _ => wander * 1.4d
            };

            if (previousDirection is { } previous)
            {
                if (direction.RowOffset == previous.RowOffset && direction.ColumnOffset == previous.ColumnOffset)
                {
                    weight *= 0.78d;
                }
                else if (direction.RowOffset == -previous.RowOffset && direction.ColumnOffset == -previous.ColumnOffset)
                {
                    weight *= 0.18d;
                }
                else
                {
                    weight *= 1.22d;
                }
            }

            if (weight > 0)
            {
                options.Add(new RiverStepOption(index, direction, weight));
            }
        }

        return options;
    }

    private static List<int> CollectRiverStartIndexes(IReadOnlyList<MapTile> tiles, IReadOnlyList<int> distanceToWater)
    {
        var startIndexes = new List<int>();
        for (var index = 0; index < tiles.Count && index < distanceToWater.Count; index++)
        {
            if (tiles[index].Terrain == TileTerrain.Land &&
                tiles[index].Elevation is >= HillMinElevation and < MountainMinElevation &&
                distanceToWater[index] >= 14)
            {
                startIndexes.Add(index);
            }
        }

        return startIndexes;
    }

    private static int PickWeightedStart(IReadOnlyList<int> startIndexes, IReadOnlyList<int> distanceToWater, Random random)
    {
        var totalWeight = 0d;
        foreach (var index in startIndexes)
        {
            var distance = Math.Max(1, Math.Min(distanceToWater[index], 180));
            totalWeight += distance * distance;
        }

        var pick = random.NextDouble() * totalWeight;
        foreach (var index in startIndexes)
        {
            var distance = Math.Max(1, Math.Min(distanceToWater[index], 180));
            pick -= distance * distance;
            if (pick <= 0)
            {
                return index;
            }
        }

        return startIndexes[^1];
    }

    private static RiverStepOption PickWeightedStep(IReadOnlyList<RiverStepOption> options, Random random)
    {
        var totalWeight = options.Sum(option => option.Weight);
        var pick = random.NextDouble() * totalWeight;
        foreach (var option in options)
        {
            pick -= option.Weight;
            if (pick <= 0)
            {
                return option;
            }
        }

        return options[^1];
    }

    private static int[] BuildDistanceToWater(IReadOnlyList<MapTile> tiles, int mapWidth, int mapHeight, bool seaOnly)
    {
        var distances = Enumerable.Repeat(-1, mapWidth * mapHeight).ToArray();
        var queue = new Queue<int>();

        for (var index = 0; index < tiles.Count; index++)
        {
            if (!IsTargetWater(tiles[index], seaOnly))
            {
                continue;
            }

            distances[index] = 0;
            queue.Enqueue(index);
        }

        while (queue.Count > 0)
        {
            var index = queue.Dequeue();
            var row = index / mapWidth;
            var column = index % mapWidth;
            foreach (var direction in CardinalDirections)
            {
                var neighborRow = row + direction.RowOffset;
                var neighborColumn = column + direction.ColumnOffset;
                if ((uint)neighborRow >= (uint)mapHeight || (uint)neighborColumn >= (uint)mapWidth)
                {
                    continue;
                }

                var neighborIndex = GetIndex(neighborRow, neighborColumn, mapWidth);
                if (distances[neighborIndex] >= 0 ||
                    (uint)neighborIndex >= (uint)tiles.Count ||
                    !IsPassableForDistance(tiles[neighborIndex], seaOnly))
                {
                    continue;
                }

                distances[neighborIndex] = distances[index] + 1;
                queue.Enqueue(neighborIndex);
            }
        }

        return distances;
    }

    private static bool IsAdjacentToTargetWater(IReadOnlyList<MapTile> tiles, int index, int mapWidth, int mapHeight, bool seaOnly)
    {
        var row = index / mapWidth;
        var column = index % mapWidth;
        foreach (var direction in CardinalDirections)
        {
            var neighborRow = row + direction.RowOffset;
            var neighborColumn = column + direction.ColumnOffset;
            if ((uint)neighborRow >= (uint)mapHeight || (uint)neighborColumn >= (uint)mapWidth)
            {
                continue;
            }

            var neighborIndex = GetIndex(neighborRow, neighborColumn, mapWidth);
            if ((uint)neighborIndex < (uint)tiles.Count &&
                IsTargetWater(tiles[neighborIndex], seaOnly))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsPassableForDistance(MapTile tile, bool seaOnly)
    {
        return IsTargetWater(tile, seaOnly) ||
               IsPassableLand(tile);
    }

    private static bool IsTargetWater(MapTile tile, bool seaOnly)
    {
        if (tile.Terrain is not (TileTerrain.Water or TileTerrain.DeepWater))
        {
            return false;
        }

        return !seaOnly || tile.Elevation <= 0;
    }

    private static bool IsPassableLand(MapTile tile)
    {
        return tile.Terrain == TileTerrain.Land && tile.Elevation < MountainMinElevation;
    }

    private static int GetIndex(int row, int column, int mapWidth)
    {
        return (row * mapWidth) + column;
    }

    private static int HashSeed(int mapWidth, int mapHeight, RiverGenerationOptions options)
    {
        unchecked
        {
            var hash = options.Seed;
            hash = hash * 397 ^ mapWidth;
            hash = hash * 397 ^ mapHeight;
            hash = hash * 397 ^ options.Count;
            hash = hash * 397 ^ options.WanderPercent;
            hash = hash * 397 ^ options.WideningPercent;
            hash = hash * 397 ^ options.SeaTerminationPercent;
            return hash & 0x7fffffff;
        }
    }

    private static int HashRiverSeed(int seed, int riverIndex, int attempt)
    {
        unchecked
        {
            var hash = seed;
            hash = hash * 397 ^ riverIndex;
            hash = hash * 397 ^ attempt;
            return hash & 0x7fffffff;
        }
    }

    private static double Sample01(int seed, int channel, int value)
    {
        unchecked
        {
            uint hash = (uint)(seed + channel * 374761393 + value * 668265263);
            hash ^= hash >> 13;
            hash *= 1274126177u;
            hash ^= hash >> 16;
            return (hash & 0x00FFFFFFu) / 16777215d;
        }
    }

    private readonly record struct RiverStepOption(
        int Index,
        (int RowOffset, int ColumnOffset) Direction,
        double Weight);
}
