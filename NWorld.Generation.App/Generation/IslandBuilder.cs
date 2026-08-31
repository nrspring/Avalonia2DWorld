using System;

namespace NWorld.Generation.App.Generation;

/// <summary>
/// What the Base Land panel asks for when it asks for islands.
/// </summary>
/// <param name="Count">How many to scatter. Some may find nowhere to go; see the builder.</param>
/// <param name="Size">Average radius in tiles. Each island varies either side of it.</param>
/// <param name="CoastHug">
/// From 0 to 1: how strongly islands are drawn to existing coastlines. At 0 they are
/// scattered across open water, at 1 they crowd the shores as archipelagos.
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
/// Nothing here removes land. What comes back is what was passed in, plus islands.
/// </para>
/// </summary>
public static class IslandBuilder
{
    /// <summary>Tries at finding a home for one island before giving up on it.</summary>
    private const int Attempts = 400;

    /// <summary>
    /// How far from a coast an island may still be drawn to it, in tiles, at each end of the
    /// hug dial. The wide end is far enough to be nearly uniform on any map anyone will make;
    /// the near end is close enough to read as an archipelago rather than as scatter.
    /// </summary>
    private const double LooseRange = 250;
    private const double TightRange = 7;

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

        // The distance at which the pull of a coast has faded to nothing.
        var reach = (LooseRange * (1 - hug)) + (TightRange * hug);

        for (var i = 0; i < count; i++)
        {
            // Islands of one size read as a spill of identical dots.
            var radius = size * (0.55 + (random.NextDouble() * 0.9));

            if (!TryPlace(width, height, distance, hasLand, radius, reach, random, out var cx, out var cy))
                continue;

            Raise(result, width, height, noise, random, cx, cy, radius);
        }

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
        int width, int height, int[] distance, bool hasLand, double radius, double reach,
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

            // With nothing to hug, every stretch of water is as good as another.
            if (!hasLand)
                return true;

            // Falls away with distance from the coast, so a candidate far out to sea is
            // accepted rarely at a tight setting and nearly always at a loose one.
            var offshore = toLand - radius;
            if (random.NextDouble() <= Math.Exp(-offshore / reach))
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
                    land[(y * width) + x] = true;
            }
        }
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
