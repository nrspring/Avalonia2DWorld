using System;
using System.Collections.Generic;

namespace Avalonia2DWorld.Generation.App.Generation;

/// <summary>
/// What the Base Land panel asks for when it asks for islands.
/// </summary>
/// <param name="Count">How many to scatter. Some may find nowhere to go; see the builder.</param>
/// <param name="Size">Average radius in tiles. Each island varies either side of it.</param>
/// <param name="CoastHug">
/// From 0 to 1, with 0.5 as no preference at all: above it islands are drawn to existing
/// coastlines, below it they are pushed out into open water. Both ends pull against the fact
/// that most of the sea on a map with land in it is near that land, which is why the middle
/// is a setting worth having rather than the same as either side.
/// </param>
/// <param name="Seed">Makes a run repeatable, and is separate from the continents' seed so
/// that re-rolling the islands leaves the mainland alone.</param>
public readonly record struct IslandSettings(int Count, double Size, double CoastHug, int Seed);

/// <summary>
/// Scatters islands through the sea around land that is already there.
/// <para>
/// Islands are placed rather than grown, which is the whole difference from
/// <see cref="ContinentBuilder"/>. A continent is a region of a field that covers the map, and
/// the interesting question is what shape it takes; an island is a small thing in a specific
/// place, and the interesting question is <em>where</em> -- off a headland, or out in open
/// water. So this picks positions first, weighted by how far they are from a coast, and gives
/// each island the same warped-ellipse treatment a continent gets, at a fraction of the size.
/// </para>
/// <para>
/// Nothing here removes the land it was given. What comes back is what was passed in, plus
/// islands -- less anything the <see cref="LandTopology"/> rules disallow, which is the one
/// way a tile that was land on the way in may be sea on the way out.
/// </para>
/// </summary>
public static class IslandBuilder
{
    /// <summary>Tries at finding a home for one island before giving up on it.</summary>
    private const int Attempts = 400;

    /// <summary>
    /// The distance over which the dial's preference fades, as a fraction of the furthest
    /// point from land on this map.
    /// <para>
    /// Measured against the map rather than fixed in tiles, so the dial means the same thing
    /// on a crowded map and an empty one: a third of the way out to the deepest water is a
    /// long way in both.
    /// </para>
    /// </summary>
    private const double ReachFraction = 0.3;

    /// <summary>
    /// The reach at the very ends of the dial, in tiles. Short: a setting that says hug the
    /// coast should put islands in sight of it, not merely nearer than average.
    /// </summary>
    private const double EndReach = 8;

    /// <summary>Clear water to leave between an island and the coast it is hugging.</summary>
    private const double Channel = 1.5;

    /// <summary>
    /// <paramref name="land"/> with islands added, as a new mask. The input is left alone.
    /// </summary>
    public static bool[] Add(int width, int height, bool[] land, IslandSettings settings)
    {
        ArgumentNullException.ThrowIfNull(land);

        var result = (bool[])land.Clone();

        var count = Math.Max(0, settings.Count);
        var size = Math.Max(1.0, settings.Size);
        var hug = Math.Clamp(settings.CoastHug, 0, 1);

        if (count == 0)
            return result;

        var random = new Random(settings.Seed);
        var noise = new SimplexNoise(unchecked((uint)settings.Seed * 2654435761u) + 0x51E5u);

        var (distance, hasLand) = DistanceToLand(width, height, land);

        var deepest = Deepest(distance);

        // Which way the dial leans, and how hard: 1 hugs the coast, -1 makes for open water,
        // 0 takes the sea as it comes.
        var lean = (hug - 0.5) * 2;

        // Narrowed as the dial is turned towards the coast, so hugging means in sight of it
        // rather than merely nearer than average.
        //
        // Only that way, though. The two ends are not mirror images: a map has a great deal
        // of sea near its coasts and only a point or two at the very deepest, so pushing
        // outwards can afford to be gentle and still land everything in open water, while
        // pushing that hard the other way would leave almost nowhere to put an island.
        var reach = lean > 0
            ? Math.Max(4, (deepest * ReachFraction * (1 - lean)) + (EndReach * lean))
            : Math.Max(6, deepest * ReachFraction);

        // Where the islands went, so the next one can be told to sit somewhere else. Without
        // this they only avoid the land they were given, and a scatter meant to be spread out
        // arrives in heaps.
        var placed = new List<(double X, double Y, double Radius)>(count);

        for (var i = 0; i < count; i++)
        {
            // Islands of one size read as a spill of identical dots.
            var radius = size * (0.55 + (random.NextDouble() * 0.9));

            if (!TryPlace(width, height, distance, hasLand, deepest, radius, reach, lean, placed, random,
                    out var cx, out var cy))
            {
                continue;
            }

            placed.Add((cx, cy, radius));
            Raise(result, width, height, noise, random, cx, cy, radius);
        }

        // The same rules the continents obey. An island can seal a bay it was dropped across,
        // and a warped outline can leave a tile hanging off its own coast by a corner; both
        // are places nothing could reach.
        LandTopology.FillEnclosedWater(result, width, height);
        LandTopology.RemoveCornerJoins(result, width, height);

        return result;
    }

    /// <summary>
    /// Somewhere in open water for an island of <paramref name="radius"/> to sit: far enough
    /// from the coast not to join it, and near enough that the hug setting is happy.
    /// <para>
    /// Rejection sampling, and it may fail. An island that cannot be placed is dropped rather
    /// than forced somewhere silly, which is what keeps a count of two hundred on a map with
    /// no room for them from turning the sea into gravel.
    /// </para>
    /// </summary>
    private static bool TryPlace(
        int width, int height, int[] distance, bool hasLand, int deepest, double radius,
        double reach, double lean, List<(double X, double Y, double Radius)> placed,
        Random random, out double x, out double y)
    {
        // Room for the whole island, and then some: an island half off the map is a coastline
        // the map cannot explain.
        var margin = radius + 2;

        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            x = margin + (random.NextDouble() * (width - (2 * margin)));
            y = margin + (random.NextDouble() * (height - (2 * margin)));

            if (x < 0 || y < 0 || x >= width || y >= height)
                break;

            var toLand = distance[((int)y * width) + (int)x];

            // Would run aground.
            if (toLand <= radius + Channel)
                continue;

            // Room from the islands already dropped, on the same terms as the coast: they
            // are land too, and the map does not know the difference a moment later.
            var crowded = false;

            foreach (var other in placed)
            {
                var dx = x - other.X;
                var dy = y - other.Y;

                if (Math.Sqrt((dx * dx) + (dy * dy)) < radius + other.Radius + Channel + 1)
                {
                    crowded = true;
                    break;
                }
            }

            if (crowded)
                continue;

            // With nothing to hug, every stretch of water is as good as another.
            if (!hasLand || lean == 0)
                return true;

            // How far out this candidate is, measured from whichever end of the sea the dial
            // is leaning towards: the preferred end scores 1 and the other falls away.
            var offshore = toLand - radius;
            var against = lean > 0 ? offshore : deepest - offshore;

            if (random.NextDouble() <= Math.Exp(-Math.Abs(lean) * against / reach))
                return true;
        }

        x = 0;
        y = 0;
        return false;
    }

    /// <summary>
    /// Draws one island: an ellipse at an angle, with its coordinates warped by noise, exactly
    /// as a continent is shaped and for the same reason -- an unwarped ellipse reads as a
    /// pebble.
    /// </summary>
    private static void Raise(
        bool[] land, int width, int height, SimplexNoise noise, Random random,
        double cx, double cy, double radius)
    {
        var aspect = Math.Sqrt(0.7 + (random.NextDouble() * 0.9));
        var angle = random.NextDouble() * Math.PI;
        var cos = Math.Cos(angle);
        var sin = Math.Sin(angle);

        var warp = radius * 0.4;
        var frequency = 1.9 / radius;

        // Each island reads the noise field somewhere else, so a dozen of them are not a
        // dozen copies of the same coast.
        var offsetX = random.NextDouble() * 500;
        var offsetY = random.NextDouble() * 500;

        // The warp can carry the outline a little past the ellipse, so the box is generous.
        var extent = (radius * Math.Max(aspect, 1 / aspect)) + warp + 2;

        var left = Math.Max(0, (int)(cx - extent));
        var right = Math.Min(width - 1, (int)(cx + extent));
        var top = Math.Max(0, (int)(cy - extent));
        var bottom = Math.Min(height - 1, (int)(cy + extent));

        // Drawn into a patch of its own first. The warp can pinch an outline in two, and an
        // island that arrives as three specks is not the island that was asked for -- so only
        // the biggest piece of what was drawn is kept, and the crumbs are left in the sea.
        var span = right - left + 1;
        var rows = bottom - top + 1;

        if (span <= 0 || rows <= 0)
            return;

        var shape = new bool[span * rows];

        for (var y = top; y <= bottom; y++)
        {
            for (var x = left; x <= right; x++)
            {
                var sampleX = x + (warp * noise.Fbm(
                    ((x * frequency) + offsetX) + 3.1, ((y * frequency) + offsetY) - 5.7, 3));

                var sampleY = y + (warp * noise.Fbm(
                    ((x * frequency) + offsetX) - 8.3, ((y * frequency) + offsetY) + 2.9, 3));

                var dx = sampleX - cx;
                var dy = sampleY - cy;

                var along = ((dx * cos) + (dy * sin)) / aspect;
                var across = ((dy * cos) - (dx * sin)) * aspect;

                if (Math.Sqrt((along * along) + (across * across)) < radius)
                    shape[((y - top) * span) + (x - left)] = true;
            }
        }

        CommitLargest(land, width, shape, span, rows, left, top);
    }

    /// <summary>
    /// Copies the largest four-connected piece of <paramref name="shape"/> onto the map, and
    /// drops the rest.
    /// </summary>
    private static void CommitLargest(
        bool[] land, int width, bool[] shape, int span, int rows, int left, int top)
    {
        var seen = new bool[shape.Length];
        var stack = new Stack<int>();

        var bestSize = 0;
        var best = new List<int>();
        var current = new List<int>();

        for (var start = 0; start < shape.Length; start++)
        {
            if (!shape[start] || seen[start])
                continue;

            current.Clear();
            stack.Push(start);
            seen[start] = true;

            while (stack.Count > 0)
            {
                var index = stack.Pop();
                current.Add(index);

                var x = index % span;
                var y = index / span;

                void Step(int neighbour)
                {
                    if (!shape[neighbour] || seen[neighbour])
                        return;

                    seen[neighbour] = true;
                    stack.Push(neighbour);
                }

                if (x > 0) Step(index - 1);
                if (x < span - 1) Step(index + 1);
                if (y > 0) Step(index - span);
                if (y < rows - 1) Step(index + span);
            }

            if (current.Count <= bestSize)
                continue;

            bestSize = current.Count;
            best.Clear();
            best.AddRange(current);
        }

        foreach (var index in best)
            land[((top + (index / span)) * width) + left + (index % span)] = true;
    }

    /// <summary>The furthest any water on the map is from land, in tiles.</summary>
    private static int Deepest(int[] distance)
    {
        var deepest = 0;

        foreach (var value in distance)
        {
            if (value != int.MaxValue && value > deepest)
                deepest = value;
        }

        return deepest;
    }

    /// <summary>
    /// How far every tile is from the nearest land, in tiles, and whether there was any.
    /// <para>
    /// A breadth-first flood out from every coast at once, which visits each tile once no
    /// matter how much land there is -- the alternative, asking each candidate position how
    /// far the nearest land is, is the same question asked hundreds of times over a map that
    /// has not changed in between.
    /// </para>
    /// <para>
    /// Four-neighbour, so the distances are along the grid rather than as the crow flies.
    /// Close enough for deciding where an island goes, and a queue rather than a heap.
    /// </para>
    /// </summary>
    private static (int[] Distance, bool HasLand) DistanceToLand(int width, int height, bool[] land)
    {
        var distance = new int[width * height];
        var queue = new int[width * height];
        var head = 0;
        var tail = 0;

        for (var i = 0; i < land.Length; i++)
        {
            if (land[i])
            {
                distance[i] = 0;
                queue[tail++] = i;
            }
            else
            {
                distance[i] = int.MaxValue;
            }
        }

        var hasLand = tail > 0;

        while (head < tail)
        {
            var index = queue[head++];
            var x = index % width;
            var y = index / width;
            var next = distance[index] + 1;

            if (x > 0) Visit(index - 1);
            if (x < width - 1) Visit(index + 1);
            if (y > 0) Visit(index - width);
            if (y < height - 1) Visit(index + width);

            void Visit(int neighbour)
            {
                if (distance[neighbour] <= next)
                    return;

                distance[neighbour] = next;
                queue[tail++] = neighbour;
            }
        }

        return (distance, hasLand);
    }
}
