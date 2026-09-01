using System;
using NWorld.MapServices.Constants;

namespace NWorld.Generation.App.Generation;

/// <summary>
/// What a land tile is made of, above and beyond how high it stands.
/// <para>
/// Only the three a tile can be. Water is not here: whether a tile is sea is settled by its
/// elevation, and nothing in this file can make one or unmake one.
/// </para>
/// </summary>
public enum GroundCover
{
    /// <summary>Ordinary ground. What every land tile is until something says otherwise.</summary>
    Grass,

    /// <summary>Standing water and bog. Wants low ground, and the coast.</summary>
    Swamp,

    /// <summary>Sand. Wants the far interior, where the weather does not reach.</summary>
    Desert,
}

/// <summary>
/// What the Swamps and Deserts panel asks for, for one of the two.
/// </summary>
/// <param name="Coverage">
/// Fraction of the land to end up as this cover, from 0 to 1. Of all the land, so it reads the
/// same as the dials on the panel above -- but only lowland can hold it, so asking for more
/// than there is lowland gets all the lowland there is and no more.
/// </param>
/// <param name="PatchSize">Roughly how many tiles across one patch runs.</param>
/// <param name="Seed">Makes a run repeatable.</param>
public readonly record struct CoverSettings(double Coverage, double PatchSize, int Seed);

/// <summary>
/// Spreads swamp and desert over the lowlands, as a cover per tile in reading order.
/// <para>
/// Neither can go anywhere but grass at flat or hill height. Mountains are rock and ice and
/// have no business being either; the sea is not ground at all. So the eligible ground is the
/// lowland -- <see cref="Elevations.IsLowland"/> -- and everything else is left as it was
/// found. Raise a range over a marsh and the marsh is gone, which is the honest answer: it is
/// eleven thousand feet up now.
/// </para>
/// <para>
/// Two passes, one per cover, built the same way as the hills and the mountains: score every
/// eligible tile, sort, take the top slice. The dial is therefore met to the tile, and each
/// pass replaces its own cover rather than adding to it, so either button can be pressed twice
/// without the second press piling onto the first.
/// </para>
/// <para>
/// Neither pass ever writes over the other's ground. There is no hierarchy between a swamp and
/// a desert the way there is between a hill and a mountain -- one is not simply more of the
/// other -- so each takes only grass and its own kind.
/// </para>
/// <para>
/// Which does leave the order they are pressed in mattering, for the ground both of them want:
/// whichever went first keeps it, and the second takes its next choice instead. Both dials are
/// still met to the tile either way -- a pass that finds its first choice taken simply carries
/// on down its own ranking -- so what the order changes is where a few of them sit and never
/// how many there are. On a map with a tenth swamp and an eighth desert it comes to about one
/// tile of cover in thirteen.
/// </para>
/// <para>
/// Where they go is not the same question for the two of them, which is the point of scoring
/// them separately rather than dealing one field out twice. A swamp is water that has nowhere
/// to drain: it wants the lowest ground and it wants the coast. A desert is land the weather
/// cannot reach: it wants the deep interior, and it does not care how high it is so long as it
/// is not a mountain.
/// </para>
/// </summary>
public static class GroundCoverBuilder
{
    /// <summary>Octaves in the patch field, and the smallest feature it may carry.</summary>
    private const double MinFeatureTiles = 4;
    private const int MaxOctaves = 5;

    /// <summary>
    /// How much of a swamp's score is its wetness rather than the noise, and how that wetness
    /// is split between lying low and lying near the sea.
    /// </summary>
    private const double SwampBias = 0.6;
    private const double SwampLowShare = 0.55;

    /// <summary>How much of a desert's score is its distance from the sea.</summary>
    private const double DesertBias = 0.55;

    /// <summary>
    /// How far inland the sea's influence reaches, as a multiple of the patch size. Beyond it
    /// a tile is as coastal as it is going to get, or as continental.
    /// </summary>
    private const double SeaReach = 2.5;

    /// <inheritdoc cref="Spread"/>
    public static GroundCover[] SpreadSwamps(
        int width, int height, int[] relief, GroundCover[] cover, CoverSettings settings) =>
        Spread(width, height, relief, cover, settings, GroundCover.Swamp);

    /// <inheritdoc cref="Spread"/>
    public static GroundCover[] SpreadDeserts(
        int width, int height, int[] relief, GroundCover[] cover, CoverSettings settings) =>
        Spread(width, height, relief, cover, settings, GroundCover.Desert);

    /// <summary>
    /// Lays one cover over the lowlands and returns the covers as they now stand.
    /// </summary>
    /// <param name="relief">Elevation per tile. Read and never written.</param>
    /// <param name="cover">
    /// The covers as they stand, which the pass builds on rather than replacing: the other
    /// cover is left exactly where it is. Left untouched -- what comes back is a copy. Null,
    /// or the wrong size, starts from grass.
    /// </param>
    private static GroundCover[] Spread(
        int width, int height, int[] relief, GroundCover[] cover, CoverSettings settings, GroundCover laying)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentNullException.ThrowIfNull(relief);

        if (relief.Length != width * height)
            throw new ArgumentException("Relief does not match the map size.", nameof(relief));

        var spread = cover is not null && cover.Length == relief.Length
            ? (GroundCover[])cover.Clone()
            : new GroundCover[relief.Length];

        // Ground this pass may take: lowland that the other cover is not already on. Cleared
        // back to grass as we go, so a second press replaces rather than adds.
        var eligible = new bool[relief.Length];
        var room = 0;
        var landCount = 0;

        for (var i = 0; i < relief.Length; i++)
        {
            if (relief[i] > 0)
                landCount++;

            // Anything of this cover that has since been built over -- a range raised across
            // it -- goes back to grass wherever it now stands.
            if (spread[i] == laying && !Elevations.IsLowland(relief[i]))
                spread[i] = GroundCover.Grass;

            if (!Elevations.IsLowland(relief[i]) || (spread[i] != GroundCover.Grass && spread[i] != laying))
                continue;

            eligible[i] = true;
            spread[i] = GroundCover.Grass;
            room++;
        }

        var wanted = Math.Min((int)Math.Round(Math.Clamp(settings.Coverage, 0, 1) * landCount), room);

        if (wanted <= 0 || room == 0)
            return spread;

        var score = Score(width, height, relief, settings, laying);

        var ranked = new double[room];
        var next = 0;

        for (var i = 0; i < score.Length; i++)
        {
            if (eligible[i])
                ranked[next++] = score[i];
        }

        Array.Sort(ranked);

        // Above every score when nothing is wanted, so a dial at zero means none rather than
        // one: the best tile would otherwise always tie its way in.
        var cut = ranked[Math.Clamp(ranked.Length - wanted, 0, ranked.Length - 1)];

        for (var i = 0; i < spread.Length; i++)
        {
            if (eligible[i] && score[i] >= cut)
                spread[i] = laying;
        }

        return spread;
    }

    /// <summary>
    /// How much a tile wants the cover being laid. Sea and mountain score below every lowland
    /// tile, so a cut never lands on one.
    /// </summary>
    private static double[] Score(
        int width, int height, int[] relief, CoverSettings settings, GroundCover laying)
    {
        var patch = Math.Max(2, settings.PatchSize);
        var frequency = 1.0 / patch;

        // Held so the finest octave stays broader than a few tiles, for the same reason the
        // relief holds its own: below that a patch stops being a patch and becomes speckle.
        var octaves = Math.Clamp(1 + (int)Math.Log2(patch / MinFeatureTiles), 1, MaxOctaves);

        // The two covers are dealt off the same seed but not the same field, or a swamp and a
        // desert would want exactly the same ground and only the first one pressed would ever
        // get any.
        var salt = laying == GroundCover.Swamp ? 0x51F3u : 0xC20Bu;
        var noise = new SimplexNoise(unchecked((uint)settings.Seed * 3266489917u) + salt);

        var land = new bool[relief.Length];
        for (var i = 0; i < relief.Length; i++)
            land[i] = relief[i] > 0;

        var sea = LandTopology.DistanceFromSea(land, width, height);
        var reach = patch * SeaReach;

        var score = new double[relief.Length];

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var index = (y * width) + x;

                if (!Elevations.IsLowland(relief[index]))
                {
                    score[index] = double.MinValue;
                    continue;
                }

                var patchiness = (noise.Fbm(x * frequency, y * frequency, octaves) + 1) * 0.5;

                // 0 at the shore, 1 out in the deep interior.
                var inland = Math.Min(1, sea[index] / reach);

                double weight;

                if (laying == GroundCover.Swamp)
                {
                    // Lowest ground first, and the coast over the interior. Both matter: a bog
                    // on a hilltop is wrong however near the sea it is, and one in the middle
                    // of a continent has nothing feeding it.
                    var low = 1 - ((double)(relief[index] - Elevations.Flat)
                        / (Elevations.HillsTo - Elevations.Flat));

                    var wet = (low * SwampLowShare) + ((1 - inland) * (1 - SwampLowShare));

                    weight = 1 - SwampBias + (SwampBias * wet);
                }
                else
                {
                    // Nothing about height. A desert is as happy on a hill as on a plain --
                    // what makes it a desert is how far it is from the water.
                    weight = 1 - DesertBias + (DesertBias * inland);
                }

                score[index] = patchiness * weight;
            }
        }

        return score;
    }
}
