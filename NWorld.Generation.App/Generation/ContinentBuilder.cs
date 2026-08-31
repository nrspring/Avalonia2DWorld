using System;
using System.Collections.Generic;

namespace NWorld.Generation.App.Generation;

/// <summary>
/// What the Base Land panel asks for when it asks for continents.
/// </summary>
/// <param name="Count">How many landmasses to grow.</param>
/// <param name="Coverage">
/// Fraction of the map to end up as land, from 0 to 1. Met exactly rather than approximately
/// -- see <see cref="ContinentBuilder"/> -- so it is a dial that does what it says at every
/// setting of the others.
/// </param>
/// <param name="Roughness">
/// How ragged the coasts are, from 0 (near-circular) to 1 (deeply bitten, with pieces
/// breaking off into offshore islands).
/// </param>
/// <param name="Seed">Makes a run repeatable. The same seed and settings give the same map.</param>
public readonly record struct ContinentSettings(int Count, double Coverage, double Roughness, int Seed);

/// <summary>
/// Grows a handful of continents across a map, as a land mask in reading order.
/// <para>
/// A distance field does the shaping and noise does the coastline: every tile scores how far
/// inside the nearest landmass it is, noise pushes that score around, and the tiles that score
/// highest become land. Because the cut is a <b>quantile</b> of the scores rather than a fixed
/// value, the amount of land is exactly what was asked for whatever the other settings do --
/// raggedness eats coast in one place and adds it in another, and the total does not drift.
/// </para>
/// <para>
/// The land is deliberately kept off the edges of the map. A continent sliced by the border
/// reads as a mistake, and the generator has no way of knowing what was supposed to be out
/// there.
/// </para>
/// </summary>
public static class ContinentBuilder
{
    /// <summary>How much of the shorter side stays sea, as a fraction.</summary>
    private const double EdgeMargin = 0.10;

    /// <summary>
    /// Cycles of the coarsest noise across one continent radius.
    /// <para>
    /// Per continent and not per map, which is the difference between a coastline and an
    /// oval. Noise measured against the map varies barely at all across a landmass a fraction
    /// of its size, so every continent gets the same gentle lean; measured against the
    /// continent, the coarsest octave is what makes a bay, and the ones under it are what
    /// make the headlands inside the bay.
    /// </para>
    /// </summary>
    private const double CoastFrequency = 1.6;

    private const int Octaves = 5;

    /// <summary>
    /// How far the coast noise drags the map itself, as a fraction of a continent radius at
    /// full roughness.
    /// <para>
    /// This is the difference between a shape with a rough edge and a shape that grew. Adding
    /// noise to the distance field only ever nibbles at the outline it was given, so a circle
    /// stays recognisably a circle; displacing the <em>coordinates</em> before the outline is
    /// measured bends the whole landmass, and peninsulas, bays and trailing islands come out
    /// of the same field that made the continent.
    /// </para>
    /// </summary>
    private const double WarpStrength = 0.5;

    /// <summary>Octaves in the displacement field. Few: warping wants broad drift, and the
    /// fine detail is what the coast noise is for.</summary>
    private const int WarpOctaves = 3;

    /// <summary>
    /// Land or sea for every tile, in reading order.
    /// </summary>
    public static bool[] Build(int width, int height, ContinentSettings settings)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        var count = Math.Max(1, settings.Count);
        var coverage = Math.Clamp(settings.Coverage, 0.01, 0.95);
        var roughness = Math.Clamp(settings.Roughness, 0, 1);

        var random = new Random(settings.Seed);
        var noise = new SimplexNoise(unchecked((uint)settings.Seed * 2654435761u) + 0x9E37u);

        var centres = PlaceCentres(width, height, count, coverage, random);

        // Circles of this radius would cover the requested fraction between them. A little
        // over, because noise erodes more coast than it adds at the same threshold -- and
        // because the quantile below makes the final figure exact either way.
        var radius = Math.Sqrt(coverage * width * height / (count * Math.PI)) * 1.25;

        var frequency = CoastFrequency / radius;

        // In tiles. Nothing at all when the coast is meant to be smooth, so the two dials
        // still meet at "near-circular".
        var warp = radius * WarpStrength * roughness;

        var field = new double[width * height];

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                // Where this tile asks the shape about itself. Displaced by a noise field
                // of its own, so the question "how far inside am I" is answered about a bent
                // version of the map -- which is what makes the outline organic rather than
                // an ellipse with a fringe.
                var sampleX = x + (warp * noise.Fbm(
                    (x * frequency) + 11.3, (y * frequency) - 7.9, WarpOctaves));

                var sampleY = y + (warp * noise.Fbm(
                    (x * frequency) - 4.1, (y * frequency) + 19.7, WarpOctaves));

                // How far inside the nearest continent this tile is: 1 at a centre, 0 at the
                // nominal coast, negative out at sea.
                var inland = double.MinValue;

                foreach (var centre in centres)
                {
                    // Measured in the continent's own frame, where it is a circle: turn the
                    // offset by the continent's angle, then stretch one axis against the
                    // other. A landmass longer one way than the other is most of what makes
                    // it read as a continent rather than as a blob.
                    var dx = sampleX - centre.X;
                    var dy = sampleY - centre.Y;

                    var along = ((dx * centre.Cos) + (dy * centre.Sin)) / centre.Aspect;
                    var across = ((dy * centre.Cos) - (dx * centre.Sin)) * centre.Aspect;

                    var distance = Math.Sqrt((along * along) + (across * across))
                        / (radius * centre.Scale);

                    inland = Math.Max(inland, 1 - distance);
                }

                // Noise moves the coast in and out. Bounded by the roughness, so a low
                // setting can only nibble at a circle while a high one can bite lumps out of
                // it and leave the pieces offshore.
                // Sampled at the displaced position too, so the fine detail follows the
                // bends rather than cutting across them.
                var wander = noise.Fbm(sampleX * frequency, sampleY * frequency, Octaves);

                field[(y * width) + x] =
                    inland + (roughness * 0.5 * wander) - EdgePenalty(x, y, width, height);
            }
        }

        return CutAt(field, coverage);
    }

    /// <summary>
    /// Where the continents are centred. Spread by rejection sampling: the best of a handful
    /// of candidates each time, judged by how far it is from the centres already placed.
    /// <para>
    /// Best-of rather than a hard minimum distance, because a hard one has no answer when the
    /// map is too small or the count too high -- and "no answer" on a slider someone is
    /// dragging has to mean something. This always places what it was asked for, and simply
    /// crowds them when there is no room to do better.
    /// </para>
    /// </summary>
    private static List<Continent> PlaceCentres(
        int width, int height, int count, double coverage, Random random)
    {
        var margin = EdgeMargin * Math.Min(width, height);
        var centres = new List<Continent>(count);

        for (var i = 0; i < count; i++)
        {
            var bestX = 0d;
            var bestY = 0d;
            var bestScore = double.MinValue;

            for (var attempt = 0; attempt < 24; attempt++)
            {
                var x = margin + (random.NextDouble() * (width - (2 * margin)));
                var y = margin + (random.NextDouble() * (height - (2 * margin)));

                var score = double.MaxValue;
                foreach (var placed in centres)
                {
                    var dx = x - placed.X;
                    var dy = y - placed.Y;
                    score = Math.Min(score, (dx * dx) + (dy * dy));
                }

                if (score <= bestScore)
                    continue;

                bestScore = score;
                bestX = x;
                bestY = y;
            }

            // Continents of one size and one shape read as a pattern rather than as
            // geography. The spread is modest: one enormous landmass beside three specks is
            // its own kind of odd, and an aspect much past this is a sandbar.
            var angle = random.NextDouble() * Math.PI;

            centres.Add(new Continent(
                bestX,
                bestY,
                0.75 + (random.NextDouble() * 0.55),
                Math.Sqrt(0.65 + (random.NextDouble() * 1.15)),
                Math.Cos(angle),
                Math.Sin(angle)));
        }

        return centres;
    }

    /// <summary>
    /// One landmass before the coast is drawn on it: an ellipse, at an angle, of a size.
    /// <see cref="Aspect"/> stretches one axis and squeezes the other by the same factor, so
    /// it changes the shape without changing the area.
    /// </summary>
    private readonly record struct Continent(
        double X, double Y, double Scale, double Aspect, double Cos, double Sin);

    /// <summary>
    /// How much the map's edge pushes a tile towards sea: nothing across the middle, rising
    /// steeply through the outer <see cref="EdgeMargin"/> of the shorter side.
    /// </summary>
    private static double EdgePenalty(int x, int y, int width, int height)
    {
        var margin = EdgeMargin * Math.Min(width, height);
        if (margin <= 0)
            return 0;

        var distance = Math.Min(Math.Min(x, width - 1 - x), Math.Min(y, height - 1 - y));
        if (distance >= margin)
            return 0;

        var t = 1 - (distance / margin);
        return 1.5 * t * t;
    }

    /// <summary>
    /// Land for the highest-scoring <paramref name="coverage"/> of the tiles.
    /// <para>
    /// A sorted copy rather than a threshold anybody has to tune: the number of land tiles is
    /// then exactly the fraction asked for, and the settings that shape the coast cannot
    /// quietly change how much land there is.
    /// </para>
    /// </summary>
    private static bool[] CutAt(double[] field, double coverage)
    {
        var ranked = (double[])field.Clone();
        Array.Sort(ranked);

        var index = Math.Clamp(
            (int)((1 - coverage) * (ranked.Length - 1)), 0, ranked.Length - 1);

        var threshold = ranked[index];

        var land = new bool[field.Length];
        for (var i = 0; i < field.Length; i++)
            land[i] = field[i] > threshold;

        return land;
    }
}
