using NWorld.Map.Viewer;

namespace NWorld.WorldGeneration.App.Services;

public static class BaseLandGenerator
{
    private const int MaximumConnectedMajorWaterBodies = 6;
    private const int MaximumClosestPairSamples = 1400;
    private const int MinimumMeanderConnectionLength = 18;
    private const double ChannelSampleSpacing = 0.55d;

    public static List<MapTile> Generate(int width, int height, NoiseGenerationOptions options)
    {
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), width, "Width must be greater than zero.");
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), height, "Height must be greater than zero.");
        }

        ArgumentNullException.ThrowIfNull(options);

        var land = new bool[height, width];
        var waterLevel = Math.Clamp(options.WaterLevelPercent / 100f, 0f, 0.98f);
        var colsScale = Math.Max(1f, width - 1f);
        var rowsScale = Math.Max(1f, height - 1f);
        var seedOffsetX = Hash01(options.Seed, 0) * 512f;
        var seedOffsetY = Hash01(options.Seed, 1) * 512f;

        for (var row = 0; row < height; row++)
        {
            var v = row / rowsScale;

            for (var column = 0; column < width; column++)
            {
                var u = column / colsScale;
                var normalized = FractalSimplex(u, v, options, seedOffsetX, seedOffsetY);
                normalized = ApplyFalloff(normalized, u, v, options);
                normalized = Math.Clamp(normalized + options.HeightBias, 0f, 1f);
                normalized = MathF.Pow(normalized, Math.Max(0.05f, options.Exponent));
                land[row, column] = normalized > waterLevel;
            }
        }

        RemoveSmallLandComponents(land);
        ConnectMajorWaterBodies(land, options.MinimumWaterConnectionWidth, options.Seed);
        return ToTiles(land);
    }

    private static void RemoveSmallLandComponents(bool[,] land)
    {
        var rows = land.GetLength(0);
        var columns = land.GetLength(1);
        var visited = new bool[rows, columns];
        var minimumContinentTiles = Math.Max(64, (rows * columns) / 1500);

        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                if (!land[row, column] || visited[row, column])
                {
                    continue;
                }

                var component = CollectLandComponent(land, visited, row, column);
                if (component.Count >= minimumContinentTiles)
                {
                    continue;
                }

                foreach (var tile in component)
                {
                    land[tile.Row, tile.Column] = false;
                }
            }
        }
    }

    private static List<(int Row, int Column)> CollectLandComponent(bool[,] land, bool[,] visited, int startRow, int startColumn)
    {
        var rows = land.GetLength(0);
        var columns = land.GetLength(1);
        var component = new List<(int Row, int Column)>();
        var queue = new Queue<(int Row, int Column)>();

        visited[startRow, startColumn] = true;
        queue.Enqueue((startRow, startColumn));

        while (queue.Count > 0)
        {
            var (row, column) = queue.Dequeue();
            component.Add((row, column));
            TryEnqueue(row - 1, column);
            TryEnqueue(row + 1, column);
            TryEnqueue(row, column - 1);
            TryEnqueue(row, column + 1);
        }

        return component;

        void TryEnqueue(int row, int column)
        {
            if ((uint)row >= (uint)rows ||
                (uint)column >= (uint)columns ||
                visited[row, column] ||
                !land[row, column])
            {
                return;
            }

            visited[row, column] = true;
            queue.Enqueue((row, column));
        }
    }

    private static List<MapTile> ToTiles(bool[,] land)
    {
        var rows = land.GetLength(0);
        var columns = land.GetLength(1);
        var tiles = new List<MapTile>(rows * columns);

        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                var isLand = land[row, column];
                tiles.Add(new MapTile(
                    row,
                    column,
                    isLand ? TileTerrain.Land : TileTerrain.DeepWater,
                    isLand ? 1 : 0));
            }
        }

        return tiles;
    }

    private static float FractalSimplex(float u, float v, NoiseGenerationOptions options, float seedOffsetX, float seedOffsetY)
    {
        var amplitude = 1f;
        var weight = 0f;
        var total = 0f;
        var frequency = options.Frequency;

        for (var octave = 0; octave < options.Octaves; octave++)
        {
            var raw = SimplexNoise.Noise((u * frequency) + seedOffsetX, (v * frequency) + seedOffsetY);
            var sample = options.BlendMode switch
            {
                NoiseBlendMode.Billow => MathF.Abs(raw),
                NoiseBlendMode.Ridged => 1f - MathF.Abs(raw),
                _ => (raw + 1f) * 0.5f
            };

            total += sample * amplitude;
            weight += amplitude;
            amplitude *= options.Persistence;
            frequency *= options.Lacunarity;
            seedOffsetX += 19.37f;
            seedOffsetY += 11.83f;
        }

        return weight <= 0f ? 0f : total / weight;
    }

    private static float ApplyFalloff(float normalized, float u, float v, NoiseGenerationOptions options)
    {
        if (!options.UseIslandFalloff || options.IslandStrength <= 0f)
        {
            return normalized;
        }

        var dx = (u * 2f) - 1f;
        var dy = (v * 2f) - 1f;
        var distance = MathF.Sqrt((dx * dx) + (dy * dy)) / 1.41421356237f;
        var falloff = 1f - MathF.Pow(Math.Clamp(distance, 0f, 1f), Math.Max(0.1f, options.FalloffExponent));
        var attenuation = Lerp(1f, Math.Clamp(falloff, 0f, 1f), Math.Clamp(options.IslandStrength, 0f, 1f));
        return normalized * attenuation;
    }

    private static void ConnectMajorWaterBodies(bool[,] land, int minimumWidth, int seed)
    {
        if (minimumWidth <= 0)
        {
            return;
        }

        var rows = land.GetLength(0);
        var columns = land.GetLength(1);
        var componentIds = new int[rows, columns];
        var components = new List<WaterComponent>();
        var componentId = 0;
        var majorWaterTileThreshold = Math.Max(16, minimumWidth * minimumWidth);

        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                if (land[row, column] || componentIds[row, column] != 0)
                {
                    continue;
                }

                componentId++;
                components.Add(FloodFillWaterComponent(land, componentIds, row, column, componentId));
            }
        }

        var majorComponents = components
            .Where(component => component.Tiles.Count >= majorWaterTileThreshold)
            .OrderByDescending(component => component.Tiles.Count)
            .Take(MaximumConnectedMajorWaterBodies)
            .ToList();

        if (majorComponents.Count <= 1)
        {
            return;
        }

        var connectedComponents = new List<WaterComponent> { majorComponents[0] };
        var remainingComponents = majorComponents.Skip(1).ToList();

        while (remainingComponents.Count > 0)
        {
            var bestConnectedIndex = 0;
            var bestRemainingIndex = 0;
            var bestStart = connectedComponents[0].Tiles[0];
            var bestEnd = remainingComponents[0].Tiles[0];
            var bestDistanceSquared = int.MaxValue;

            for (var connectedIndex = 0; connectedIndex < connectedComponents.Count; connectedIndex++)
            {
                var connected = connectedComponents[connectedIndex];

                for (var remainingIndex = 0; remainingIndex < remainingComponents.Count; remainingIndex++)
                {
                    var remaining = remainingComponents[remainingIndex];
                    var candidate = FindClosestTilePair(GetClosestPairCandidates(connected), GetClosestPairCandidates(remaining));

                    if (candidate.DistanceSquared < bestDistanceSquared)
                    {
                        bestConnectedIndex = connectedIndex;
                        bestRemainingIndex = remainingIndex;
                        bestStart = candidate.Start;
                        bestEnd = candidate.End;
                        bestDistanceSquared = candidate.DistanceSquared;
                    }
                }
            }

            var path = GetConnectionPathTiles(bestStart, bestEnd, seed + remainingComponents.Count);
            foreach (var (row, column) in path)
            {
                CarveWaterBrush(land, row, column, minimumWidth);
            }

            var connectedComponent = connectedComponents[bestConnectedIndex];
            var remainingComponent = remainingComponents[bestRemainingIndex];
            connectedComponent.AddTiles(path, land);
            connectedComponent.Tiles.AddRange(remainingComponent.Tiles);
            connectedComponent.BoundaryTiles.AddRange(remainingComponent.BoundaryTiles);
            remainingComponents.RemoveAt(bestRemainingIndex);
        }
    }

    private static WaterComponent FloodFillWaterComponent(bool[,] land, int[,] componentIds, int startRow, int startColumn, int componentId)
    {
        var rows = land.GetLength(0);
        var columns = land.GetLength(1);
        var queue = new Queue<(int Row, int Column)>();
        var component = new WaterComponent(componentId);

        componentIds[startRow, startColumn] = componentId;
        queue.Enqueue((startRow, startColumn));

        while (queue.Count > 0)
        {
            var (row, column) = queue.Dequeue();
            component.Tiles.Add((row, column));

            if (IsWaterBoundary(land, row, column))
            {
                component.BoundaryTiles.Add((row, column));
            }

            TryEnqueue(row - 1, column);
            TryEnqueue(row + 1, column);
            TryEnqueue(row, column - 1);
            TryEnqueue(row, column + 1);
        }

        return component;

        void TryEnqueue(int row, int column)
        {
            if ((uint)row >= (uint)rows ||
                (uint)column >= (uint)columns ||
                componentIds[row, column] != 0 ||
                land[row, column])
            {
                return;
            }

            componentIds[row, column] = componentId;
            queue.Enqueue((row, column));
        }
    }

    private static ((int Row, int Column) Start, (int Row, int Column) End, int DistanceSquared) FindClosestTilePair(
        IReadOnlyList<(int Row, int Column)> first,
        IReadOnlyList<(int Row, int Column)> second)
    {
        var bestStart = first[0];
        var bestEnd = second[0];
        var bestDistanceSquared = int.MaxValue;

        foreach (var start in first)
        {
            foreach (var end in second)
            {
                var rowDelta = start.Row - end.Row;
                var columnDelta = start.Column - end.Column;
                var distanceSquared = (rowDelta * rowDelta) + (columnDelta * columnDelta);

                if (distanceSquared < bestDistanceSquared)
                {
                    bestStart = start;
                    bestEnd = end;
                    bestDistanceSquared = distanceSquared;
                }
            }
        }

        return (bestStart, bestEnd, bestDistanceSquared);
    }

    private static IReadOnlyList<(int Row, int Column)> GetClosestPairCandidates(WaterComponent component)
    {
        var source = component.BoundaryTiles.Count > 0 ? component.BoundaryTiles : component.Tiles;

        if (source.Count <= MaximumClosestPairSamples)
        {
            return source;
        }

        var sampled = new List<(int Row, int Column)>(MaximumClosestPairSamples);
        var step = source.Count / (double)MaximumClosestPairSamples;

        for (var index = 0; index < MaximumClosestPairSamples; index++)
        {
            sampled.Add(source[(int)(index * step)]);
        }

        return sampled;
    }

    private static List<(int Row, int Column)> GetConnectionPathTiles((int Row, int Column) start, (int Row, int Column) end, int seed)
    {
        var directDistance = Math.Sqrt(GetDistanceSquared(start, end));

        if (directDistance < MinimumMeanderConnectionLength)
        {
            return GetLineTiles(start, end);
        }

        var rowDelta = end.Row - start.Row;
        var columnDelta = end.Column - start.Column;
        var perpendicularLength = Math.Sqrt((rowDelta * rowDelta) + (columnDelta * columnDelta));

        if (perpendicularLength < 0.0001d)
        {
            return GetLineTiles(start, end);
        }

        var perpendicularRow = -columnDelta / perpendicularLength;
        var perpendicularColumn = rowDelta / perpendicularLength;
        var amplitude = directDistance * (0.07d + Hash01(seed, 5) * 0.08d);
        var secondaryAmplitude = amplitude * (0.22d + Hash01(seed, 6) * 0.20d);
        var primaryPhase = Hash01(seed, 7) * Math.PI * 2d;
        var secondaryPhase = Hash01(seed, 8) * Math.PI * 2d;
        var primaryCycles = 1.0d + Hash01(seed, 9) * 1.15d;
        var secondaryCycles = primaryCycles + 1.25d + Hash01(seed, 10) * 1.25d;
        var sampleCount = Math.Max(2, (int)Math.Ceiling(directDistance / ChannelSampleSpacing));
        var path = new List<(int Row, int Column)>(sampleCount);
        (int Row, int Column)? previous = null;

        for (var sample = 0; sample <= sampleCount; sample++)
        {
            var t = sample / (double)sampleCount;
            var baseRow = start.Row + rowDelta * t;
            var baseColumn = start.Column + columnDelta * t;
            var envelope = Math.Sin(t * Math.PI);
            var primary = Math.Sin((t * Math.PI * 2d * primaryCycles) + primaryPhase);
            var secondary = Math.Sin((t * Math.PI * 2d * secondaryCycles) + secondaryPhase);
            var offset = envelope * ((primary * amplitude) + (secondary * secondaryAmplitude));
            var current = (
                Row: (int)Math.Round(baseRow + perpendicularRow * offset),
                Column: (int)Math.Round(baseColumn + perpendicularColumn * offset));

            if (previous is { } previousPoint)
            {
                foreach (var tile in GetLineTiles(previousPoint, current))
                {
                    AddUnique(path, tile);
                }
            }
            else
            {
                AddUnique(path, current);
            }

            previous = current;
        }

        return path;

        static void AddUnique(List<(int Row, int Column)> tiles, (int Row, int Column) tile)
        {
            if (tiles.Count == 0 || tiles[^1] != tile)
            {
                tiles.Add(tile);
            }
        }
    }

    private static List<(int Row, int Column)> GetLineTiles((int Row, int Column) start, (int Row, int Column) end)
    {
        var tiles = new List<(int Row, int Column)>();
        var row = start.Row;
        var column = start.Column;
        var rowDelta = Math.Abs(end.Row - start.Row);
        var columnDelta = Math.Abs(end.Column - start.Column);
        var rowStep = start.Row < end.Row ? 1 : -1;
        var columnStep = start.Column < end.Column ? 1 : -1;
        var error = columnDelta - rowDelta;

        while (true)
        {
            tiles.Add((row, column));

            if (row == end.Row && column == end.Column)
            {
                break;
            }

            var doubledError = error * 2;

            if (doubledError > -rowDelta)
            {
                error -= rowDelta;
                column += columnStep;
            }

            if (doubledError < columnDelta)
            {
                error += columnDelta;
                row += rowStep;
            }
        }

        return tiles;
    }

    private static int GetDistanceSquared((int Row, int Column) start, (int Row, int Column) end)
    {
        var rowDelta = start.Row - end.Row;
        var columnDelta = start.Column - end.Column;
        return (rowDelta * rowDelta) + (columnDelta * columnDelta);
    }

    private static void CarveWaterBrush(bool[,] land, int centerRow, int centerColumn, int width)
    {
        var rows = land.GetLength(0);
        var columns = land.GetLength(1);
        var radius = Math.Max(0.5d, width / 2.0d);
        var radiusSquared = radius * radius;
        var offset = Math.Max(0, (int)Math.Ceiling(radius));

        for (var row = Math.Max(0, centerRow - offset); row <= Math.Min(rows - 1, centerRow + offset); row++)
        {
            for (var column = Math.Max(0, centerColumn - offset); column <= Math.Min(columns - 1, centerColumn + offset); column++)
            {
                var rowDelta = row - centerRow;
                var columnDelta = column - centerColumn;
                if ((rowDelta * rowDelta) + (columnDelta * columnDelta) <= radiusSquared)
                {
                    land[row, column] = false;
                }
            }
        }
    }

    private static bool IsWaterBoundary(bool[,] land, int row, int column)
    {
        var rows = land.GetLength(0);
        var columns = land.GetLength(1);

        return IsBoundaryNeighbor(row - 1, column) ||
               IsBoundaryNeighbor(row + 1, column) ||
               IsBoundaryNeighbor(row, column - 1) ||
               IsBoundaryNeighbor(row, column + 1);

        bool IsBoundaryNeighbor(int neighborRow, int neighborColumn)
        {
            return (uint)neighborRow >= (uint)rows ||
                   (uint)neighborColumn >= (uint)columns ||
                   land[neighborRow, neighborColumn];
        }
    }

    private static float Lerp(float start, float end, float amount)
    {
        return start + ((end - start) * amount);
    }

    private static float Hash01(int seed, int channel)
    {
        unchecked
        {
            uint hash = (uint)(seed + (channel * 374761393));
            hash ^= hash >> 13;
            hash *= 1274126177u;
            hash ^= hash >> 16;
            return (hash & 0x00FFFFFFu) / 16777215f;
        }
    }

    private sealed class WaterComponent(int id)
    {
        public int Id { get; } = id;

        public List<(int Row, int Column)> Tiles { get; } = [];

        public List<(int Row, int Column)> BoundaryTiles { get; } = [];

        public void AddTiles(IEnumerable<(int Row, int Column)> tiles, bool[,] land)
        {
            foreach (var tile in tiles)
            {
                Tiles.Add(tile);

                if (IsWaterBoundary(land, tile.Row, tile.Column))
                {
                    BoundaryTiles.Add(tile);
                }
            }
        }
    }
}
