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

    /// <summary>
    /// Running water. Unlike the other two it is not spread over the ground that suits it but
    /// drawn along a route -- see <see cref="RiverBuilder"/> -- and unlike them it is at home
    /// at any height, since a river comes down off the high ground rather than sitting on the
    /// low.
    /// </summary>
    River,
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
/// <param name="Clustering">
/// How much the patches gather together, from 0 to 1. At nothing they are sprinkled over
/// whatever ground suits them, wherever on the map that is; at full they are heaped into a few
/// districts and the rest of the ground is left clear. It takes no tile away from the coverage
/// -- it only decides whether what there is arrives spread out or gathered up, though gathered
/// patches do run into one another and make bigger pieces of it.
/// </param>
/// <param name="Seed">Makes a run repeatable.</param>
public readonly record struct CoverSettings(
    double Coverage, double PatchSize, double Clustering, int Seed);

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
/// other -- so each takes only grass and its own kind. A river is left alone by both for the
/// same reason and one better: it is somewhere the water already is, and a marsh laid over it
/// would break the one thing a river has to be, which is continuous from source to sea.
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
/// <para>
/// The three shape dials answer three separate questions, and are built to stay separate. How
/// much there is, is the coverage. How big one piece of it is, is the patch size. Whether
/// those pieces arrive spread over the whole map or heaped into a few places, is the
/// clustering -- a second, much broader field laid over the first, saying where the country is
/// marshy or sandy as opposed to which tiles of it are. Turning one up does not turn another
/// down: the cut is still a quantile, so the amount is met to the tile at every setting of the
/// other two.
/// </para>
/// <para>
/// Gathering does grow the pieces, and it is worth being straight about how. No patch is drawn
/// any larger -- the grain of the field is the patch size and nothing else touches it -- but
/// patches crowded into a district run into their neighbours, and what was two marshes with a
/// mile of grass between them is one marsh. So the count falls and the average piece gets
/// bigger while the total does not move: on a map at a tenth swamp, ninety-odd patches
/// averaging thirteen tiles become sixty averaging twenty-two.
/// </para>
/// </summary>
public static class GroundCoverBuilder
{
    /// <summary>
    /// The smallest feature a patch field may carry, in tiles, and the most octaves it may use
    /// -- see <see cref="SimplexNoise.OctavesFor"/>. Below that a patch stops being a patch and
    /// becomes speckle.
    /// </summary>
    private const double MinFeatureTiles = 4;

    /// <inheritdoc cref="MinFeatureTiles"/>
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

    /// <summary>
    /// How big a district is: a share of the shorter side of the map, so that a handful of
    /// them fall across it whatever size the map is.
    /// <para>
    /// Off the map rather than off the patch size, which is the whole point of the dial being
    /// its own dial. "Gathered into a few great fens" is a statement about the world, not
    /// about the grain of the marsh, and tying it to the patch size would mean the districts
    /// shrank every time somebody asked for finer patches -- which is the coupling the panel
    /// is built to avoid.
    /// </para>
    /// </summary>
    private const double DistrictShare = 0.45;

    /// <summary>
    /// The fewest patches a district is ever made wide, whatever the map size says.
    /// <para>
    /// A gathering field finer than the thing it gathers is not gathering anything -- it is a
    /// second patch field, and two patch fields multiplied together are one patch field with
    /// holes in it. This is what keeps the dial doing what it says on a small map with a
    /// coarse patch setting.
    /// </para>
    /// </summary>
    private const double MinDistrictPatches = 3;

    /// <summary>
    /// How many octaves the districts get. Few, deliberately: a district is a broad swell
    /// saying where the country is marshy, and detail in it would only be patch grain again.
    /// </summary>
    private const int DistrictOctaves = 2;

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
        var taken = ScoreCut.Take(eligible, score, wanted);

        for (var i = 0; i < spread.Length; i++)
        {
            if (eligible[i] && taken.Takes(score[i]))
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

        var octaves = SimplexNoise.OctavesFor(patch, MinFeatureTiles, MaxOctaves);

        // Where the country is marshy, or is sand country, as opposed to which tiles of it
        // are. Broader than a patch by construction -- see MinDistrictPatches -- so what it
        // does to the ranking is gather patches rather than nibble at them.
        var clustering = Math.Clamp(settings.Clustering, 0, 1);
        var district = Math.Max(patch * MinDistrictPatches, Math.Min(width, height) * DistrictShare);
        var districtFrequency = 1.0 / district;

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

                // How much this part of the map is the kind of country this cover gathers in.
                // Sampled off the patch field's own coordinates, or the two would peak
                // together and the dial between them would do nothing.
                var gathering = (noise.Fbm(
                    (x * districtFrequency) + 47.3,
                    (y * districtFrequency) - 19.6,
                    DistrictOctaves) + 1) * 0.5;

                // Nothing at all when the dial is down, which is what makes it safe to leave
                // there: the score is exactly what it was before this dial existed.
                var gathered = 1 - clustering + (clustering * gathering);

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

                score[index] = patchiness * weight * gathered;
            }
        }

        return score;
    }
}
