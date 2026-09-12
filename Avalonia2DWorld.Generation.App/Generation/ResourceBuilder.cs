using System;
using Avalonia2DWorld.MapServices.Constants;

namespace Avalonia2DWorld.Generation.App.Generation;

/// <summary>
/// What a tile is worth digging, cutting or drilling. One per tile at most, and
/// <see cref="None"/> for the great majority of them.
/// <para>
/// Separate from <see cref="GroundCover"/> because it answers a different question. A cover is
/// what a tile <em>is</em> -- a tile is bog or it is sand, and it cannot be both. A resource is
/// what a tile <em>has</em>, which is why it is drawn over the ground rather than instead of
/// it, and why iron in a marsh is a perfectly ordinary thing for a map to say.
/// </para>
/// </summary>
public enum TileResource
{
    /// <summary>Nothing worth taking. What every tile is until a pass says otherwise.</summary>
    None,

    /// <summary>Ore. Wants the high ground, where the rock is at the surface.</summary>
    Iron,

    /// <summary>Timber. Wants the well-watered lowlands: not sand, and not above the treeline.</summary>
    Wood,

    /// <summary>Crude. Wants the flat basins, and never a mountain.</summary>
    Oil,

    /// <summary>Brimstone. Wants the mountains and their skirts, where the ground vents.</summary>
    Sulphur,

    /// <summary>Building stone. Wants high ground like the ore, but is far less fussy about it.</summary>
    Stone,
}

/// <summary>
/// What the Resources panel asks for, for one of the five.
/// </summary>
/// <param name="Coverage">
/// Fraction of the land to end up carrying this resource, from 0 to 1. Of all the land, so it
/// reads the same as every other coverage dial -- but each resource has its own idea of what
/// ground it can sit on, so asking for more than there is suitable ground gets all there is.
/// </param>
/// <param name="PatchSize">Roughly how many tiles across one field of it runs.</param>
/// <param name="Clustering">
/// How much the fields gather together, from 0 to 1. At nothing they are sprinkled over
/// whatever ground suits them, wherever on the map that is; at full they are heaped into a few
/// districts -- an ore country, an oil country -- and the rest of the land is left bare. It
/// takes no tile away from the coverage.
/// </param>
/// <param name="Seed">Makes a run repeatable.</param>
public readonly record struct DepositSettings(
    double Coverage, double PatchSize, double Clustering, int Seed);

/// <summary>
/// Scatters resource deposits over the land, as a resource per tile in reading order.
/// <para>
/// Built the same way as <see cref="GroundCoverBuilder"/> and for the same reasons: score every
/// tile that could hold the resource, sort, take the top slice. So each dial is met to the
/// tile, and each pass replaces its own resource rather than adding to it -- a button can be
/// pressed twice without the second press piling onto the first.
/// </para>
/// <para>
/// Where the covers only ever sat on lowland grass, these go almost anywhere the sea does not:
/// ore and stone want the mountains the covers were forbidden, and only timber and oil have a
/// ceiling. What stops a tile is running water -- a stand of trees drawn over a river would
/// break the one thing a river has to be, which is continuous -- and, for each pass, the other
/// four resources: a tile carries one deposit or none, because it is drawn on one layer.
/// </para>
/// <para>
/// Which leaves the order the buttons are pressed in mattering for the ground two resources
/// both want, exactly as it does for the swamps and the deserts: whichever went first keeps it
/// and the second takes its next choice. Both dials are still met in full.
/// </para>
/// <para>
/// The three shape dials are the cover panel's three, doing the same three jobs and kept
/// separate in the same way: how much there is, is the coverage; how big one field of it runs,
/// is the patch size; whether those fields arrive spread over the whole world or heaped into a
/// few districts, is the clustering. Their own dials rather than the cover panel's, because
/// there is no reason a world of small marshes should also be a world of small ore fields.
/// </para>
/// </summary>
public static class ResourceBuilder
{
    /// <inheritdoc cref="GroundCoverBuilder"/>
    private const double MinFeatureTiles = 4;

    /// <inheritdoc cref="MinFeatureTiles"/>
    private const int MaxOctaves = 5;

    /// <summary>
    /// How far inland the sea's influence reaches, as a multiple of the patch size. Beyond it
    /// a tile is as coastal as it is going to get, or as continental.
    /// </summary>
    private const double SeaReach = 2.5;

    /// <summary>
    /// How big a district is: a share of the shorter side of the map, so a handful of them
    /// fall across it whatever size the map is. The cover panel's reasoning exactly -- see
    /// <see cref="GroundCoverBuilder"/>, which is where these three were worked out.
    /// </summary>
    private const double DistrictShare = 0.45;

    /// <inheritdoc cref="DistrictShare"/>
    private const double MinDistrictPatches = 3;

    /// <inheritdoc cref="DistrictShare"/>
    private const int DistrictOctaves = 2;

    /// <inheritdoc cref="Spread"/>
    public static TileResource[] SpreadIron(
        int width, int height, int[] relief, GroundCover[] cover, TileResource[] deposits, DepositSettings settings) =>
        Spread(width, height, relief, cover, deposits, settings, TileResource.Iron);

    /// <inheritdoc cref="Spread"/>
    public static TileResource[] SpreadWood(
        int width, int height, int[] relief, GroundCover[] cover, TileResource[] deposits, DepositSettings settings) =>
        Spread(width, height, relief, cover, deposits, settings, TileResource.Wood);

    /// <inheritdoc cref="Spread"/>
    public static TileResource[] SpreadOil(
        int width, int height, int[] relief, GroundCover[] cover, TileResource[] deposits, DepositSettings settings) =>
        Spread(width, height, relief, cover, deposits, settings, TileResource.Oil);

    /// <inheritdoc cref="Spread"/>
    public static TileResource[] SpreadSulphur(
        int width, int height, int[] relief, GroundCover[] cover, TileResource[] deposits, DepositSettings settings) =>
        Spread(width, height, relief, cover, deposits, settings, TileResource.Sulphur);

    /// <inheritdoc cref="Spread"/>
    public static TileResource[] SpreadStone(
        int width, int height, int[] relief, GroundCover[] cover, TileResource[] deposits, DepositSettings settings) =>
        Spread(width, height, relief, cover, deposits, settings, TileResource.Stone);

    /// <summary>
    /// Whether a tile of this height and this ground could carry <paramref name="resource"/>.
    /// <para>
    /// Public because the rule outlives the pass that applied it. A map goes on being worked
    /// on after its deposits are laid -- a range raised across an oil field, a river run
    /// through a wood -- and whoever rebuilds the tiles has to ask this again of every deposit
    /// it is carrying over, or the map ends up with derricks up a mountain.
    /// </para>
    /// </summary>
    public static bool CanHold(TileResource resource, int elevation, GroundCover ground)
    {
        if (resource == TileResource.None)
            return true;

        // Nothing at sea, and nothing on running water -- see the class remarks.
        if (elevation <= Elevations.Sea || ground is GroundCover.River or GroundCover.Shallow)
            return false;

        return resource switch
        {
            // Above the treeline there are no trees, and nothing grows in sand.
            TileResource.Wood => Elevations.IsLowland(elevation) && ground != GroundCover.Desert,

            // Oil gathers in basins. A derrick on a crag is not a thing.
            TileResource.Oil => Elevations.IsLowland(elevation),

            // Ore, brimstone and stone are all happiest high up, but none of them is barred
            // from anywhere on land -- that is a preference, and it belongs in the score.
            _ => true,
        };
    }

    /// <summary>
    /// Lays one resource over the ground that suits it and returns the deposits as they now
    /// stand.
    /// </summary>
    /// <param name="relief">Elevation per tile. Read and never written.</param>
    /// <param name="cover">What each tile is made of. Read and never written.</param>
    /// <param name="deposits">
    /// The deposits as they stand, which the pass builds on rather than replacing: the other
    /// four resources are left exactly where they are. Left untouched -- what comes back is a
    /// copy. Null, or the wrong size, starts from bare ground.
    /// </param>
    private static TileResource[] Spread(
        int width,
        int height,
        int[] relief,
        GroundCover[] cover,
        TileResource[] deposits,
        DepositSettings settings,
        TileResource laying)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentNullException.ThrowIfNull(relief);
        ArgumentNullException.ThrowIfNull(cover);

        if (relief.Length != width * height)
            throw new ArgumentException("Relief does not match the map size.", nameof(relief));

        if (cover.Length != relief.Length)
            throw new ArgumentException("Cover does not match the map size.", nameof(cover));

        var spread = deposits is not null && deposits.Length == relief.Length
            ? (TileResource[])deposits.Clone()
            : new TileResource[relief.Length];

        // Ground this pass may take: whatever suits the resource and no other resource is
        // already on. Its own kind is cleared as we go, so a second press replaces rather than
        // adds -- and any of it that the world has since made impossible goes with it.
        var eligible = new bool[relief.Length];
        var room = 0;
        var landCount = 0;

        for (var i = 0; i < relief.Length; i++)
        {
            if (relief[i] > Elevations.Sea)
                landCount++;

            if (spread[i] == laying)
                spread[i] = TileResource.None;

            if (spread[i] != TileResource.None || !CanHold(laying, relief[i], cover[i]))
                continue;

            eligible[i] = true;
            room++;
        }

        var wanted = Math.Min((int)Math.Round(Math.Clamp(settings.Coverage, 0, 1) * landCount), room);

        if (wanted <= 0 || room == 0)
            return spread;

        var score = Score(width, height, relief, cover, settings, laying);
        var taken = ScoreCut.Take(eligible, score, wanted);

        for (var i = 0; i < spread.Length; i++)
        {
            if (eligible[i] && taken.Takes(score[i]))
                spread[i] = laying;
        }

        return spread;
    }

    /// <summary>
    /// How much a tile wants the resource being laid. Anything that cannot hold it scores
    /// below every tile that can, so a cut never lands on one.
    /// </summary>
    private static double[] Score(
        int width, int height, int[] relief, GroundCover[] cover, DepositSettings settings, TileResource laying)
    {
        var patch = Math.Max(2, settings.PatchSize);
        var frequency = 1.0 / patch;

        var octaves = SimplexNoise.OctavesFor(patch, MinFeatureTiles, MaxOctaves);

        var clustering = Math.Clamp(settings.Clustering, 0, 1);
        var district = Math.Max(patch * MinDistrictPatches, Math.Min(width, height) * DistrictShare);
        var districtFrequency = 1.0 / district;

        // The five are dealt off the same seed but not the same field, or they would all want
        // exactly the same ground and only the first one pressed would ever get any.
        var noise = new SimplexNoise(unchecked((uint)settings.Seed * 3266489917u) + Salt(laying));

        var land = new bool[relief.Length];
        for (var i = 0; i < relief.Length; i++)
            land[i] = relief[i] > Elevations.Sea;

        var sea = LandTopology.DistanceFromSea(land, width, height);
        var reach = patch * SeaReach;

        var bias = Bias(laying);
        var score = new double[relief.Length];

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var index = (y * width) + x;

                if (!CanHold(laying, relief[index], cover[index]))
                {
                    score[index] = double.MinValue;
                    continue;
                }

                var patchiness = (noise.Fbm(x * frequency, y * frequency, octaves) + 1) * 0.5;

                // Where the world is ore country, as opposed to which tiles of it are.
                // Sampled off the patch field's own coordinates, or the two would peak
                // together and the dial between them would do nothing.
                var gathering = (noise.Fbm(
                    (x * districtFrequency) + 47.3,
                    (y * districtFrequency) - 19.6,
                    DistrictOctaves) + 1) * 0.5;

                // Nothing at all when the dial is down, which is what makes it safe to leave
                // there: the score is exactly what it was before this dial existed.
                var gathered = 1 - clustering + (clustering * gathering);

                // 0 down at flat land, 1 at the highest ground a map can have.
                var high = Math.Clamp(
                    (double)(relief[index] - Elevations.Flat) / (Elevations.MountainsTo - Elevations.Flat),
                    0,
                    1);

                // 0 at the shore, 1 out in the deep interior.
                var inland = Math.Min(1, sea[index] / reach);

                var wants = laying switch
                {
                    // Ore at the surface is a fact about high ground: it is where the rock is
                    // not buried under everything that has since settled on it.
                    TileResource.Iron => high,

                    // Brimstone is sharper about it again -- squared, so it gathers on the
                    // ranges themselves rather than over the whole of the upland.
                    TileResource.Sulphur => high * high,

                    // Stone wants the same ground with far less conviction. Half of what makes
                    // a quarry is the rock and half is somebody wanting to build with it.
                    TileResource.Stone => (high * 0.6) + 0.4,

                    // Timber wants the weather, which is the inverse of what a desert wants,
                    // and it thins out as the ground climbs towards the treeline.
                    TileResource.Wood => ((1 - inland) * 0.55) + ((1 - high) * 0.45),

                    // Oil lies where the ground has been low for a long time, and the deep
                    // interior basins are the likeliest of those.
                    TileResource.Oil => ((1 - high) * 0.5) + (inland * 0.5),

                    _ => 0.5,
                };

                score[index] = patchiness * (1 - bias + (bias * wants)) * gathered;
            }
        }

        return score;
    }

    /// <summary>
    /// How much of a resource's score is where it belongs rather than the patch field. High
    /// means it is found where it ought to be and nowhere else; low means it turns up
    /// anywhere, in patches.
    /// </summary>
    private static double Bias(TileResource resource) => resource switch
    {
        TileResource.Sulphur => 0.75,
        TileResource.Iron => 0.6,
        TileResource.Oil => 0.55,
        TileResource.Wood => 0.5,
        _ => 0.35,
    };

    /// <summary>What keeps the five off one another's fields. Any distinct values will do.</summary>
    private static uint Salt(TileResource resource) => resource switch
    {
        TileResource.Iron => 0x1F6Cu,
        TileResource.Wood => 0x3D91u,
        TileResource.Oil => 0x2A73u,
        TileResource.Sulphur => 0x6B2Du,
        _ => 0x58E2u,
    };
}
