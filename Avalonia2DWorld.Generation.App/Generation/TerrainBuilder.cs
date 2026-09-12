using System;
using Avalonia2DWorld.MapServices.Constants;

namespace Avalonia2DWorld.Generation.App.Generation;

/// <summary>
/// What the Hills and Mountains panel asks for.
/// </summary>
/// <param name="MountainCoverage">
/// Fraction of the <b>land</b> to end up as mountain, from 0 to 1. Of the land and not of the
/// map, because that is the figure somebody raising a range is thinking in: a tenth of the
/// land is the same amount of mountain whether the world is mostly ocean or mostly continent.
/// Met exactly -- see <see cref="TerrainBuilder"/>.
/// </param>
/// <param name="HillCoverage">
/// Fraction of the land to end up as hills, from 0 to 1, and again exact. Measured
/// independently of the mountains rather than including them, so moving one dial does not
/// silently move the other.
/// </param>
/// <param name="Ruggedness">
/// From 0 (broad rounded uplands) to 1 (sharp ranges with the peaks strung along their crests).
/// A mountain dial: it shapes the relief the ranges are cut from.
/// </param>
/// <param name="RangeSize">Roughly how many tiles a range runs for, before it breaks.</param>
/// <param name="HillSpread">
/// How far, in tiles, the hills reach out from the high ground. A hill dial, and the only one
/// that is: at zero the hills are the ring immediately below the ranges, and every tile further
/// widens the apron they make around them.
/// </param>
/// <param name="Seed">Makes a run repeatable. The same seed and settings give the same relief.</param>
public readonly record struct TerrainSettings(
    double MountainCoverage,
    double HillCoverage,
    double Ruggedness,
    double RangeSize,
    double HillSpread,
    int Seed);

/// <summary>
/// Raises hills and mountains out of land that is already there, as an elevation per tile in
/// reading order: 0 at sea, 1 for flat land, 2 to 10 for hills, 11 to 30 for mountains.
/// <para>
/// Two passes rather than one, because they are two buttons. Each reads the heights already
/// there and puts back only its own band, so raising hills does not disturb a range and
/// raising a range does not disturb the hills -- and either can be pressed twice without the
/// second press piling onto the first.
/// </para>
/// <para>
/// What keeps them looking like one landscape is that both are cut from the same field, off
/// the same seed. The mountains take the top of it. The hills take the top of that same field
/// with its high ground allowed to <b>reach outwards</b>, by however many tiles the spread dial
/// asks for -- see <see cref="Widen"/>. Nothing moves a peak or invents one, so the hills come
/// out as an apron around the ranges however far they are pushed. That is the whole of the
/// relationship: one field, one seed, and a reach.
/// </para>
/// <para>
/// Both passes end by settling the hills, so that no hill stands more than a step above any
/// ground touching it -- see <see cref="Settle"/>. The band is assigned by rank, and rank
/// knows only which tile scores higher than which; it has nothing to say about whether two
/// neighbours are heights a hillside could actually run between. Left alone it puts a nine
/// beside a two often enough to matter, and a one-tile cliff in the middle of hill country
/// is read as a mountain by anyone looking at the map.
/// </para>
/// <para>
/// Both, and not only the pass that draws them, because a hill's height is a claim about the
/// ground around it and either button can move that ground. Raising a range takes the old one
/// down to flat land before putting the new one up, and every hillside that was resting on
/// the old range's shoulder is left standing over a meadow. The rule is about the relief as it
/// ends up, so it is applied wherever the relief stops changing.
/// </para>
/// <para>
/// It also means the two have no dial in common. Ruggedness and range length shape the relief
/// and so belong to the mountains; the spread belongs to the hills and does nothing to a range.
/// Only the seed is shared, because a seed is what says which world this is.
/// </para>
/// <para>
/// It shapes only the height and never the coast. The land mask it is given comes back
/// untouched -- every tile that was land is still land, and no tile of sea becomes any --
/// which is what lets this run over a finished world without undoing the passes that drew it.
/// </para>
/// <para>
/// The height comes from two noise fields blended by the ruggedness dial: a plain one, which
/// makes broad swells, and a ridged one, which is folded about its own zero and so has sharp
/// crests running in lines. The ridged field is the whole reason a mountain range comes out as
/// a range rather than as a patch of high ground -- crests are where the noise crosses zero,
/// and a zero crossing of a smooth field is a line.
/// </para>
/// <para>
/// Where the high ground goes is decided by <b>rank</b> and not by a threshold on the field,
/// exactly as <see cref="ContinentBuilder"/> decides the coast: the tiles are sorted and the
/// top slice taken. That is what makes both dials exact at every setting of the others --
/// ruggedness changes where the mountains are without changing how much mountain there is.
/// </para>
/// </summary>
public static class TerrainBuilder
{
    // The bands come from Elevations, which is what the shading and the panels read too.
    private const int Flat = Elevations.Flat;
    private const int HillsFrom = Elevations.HillsFrom;
    private const int HillsTo = Elevations.HillsTo;
    private const int MountainsFrom = Elevations.MountainsFrom;
    private const int MountainsTo = Elevations.MountainsTo;

    /// <summary>
    /// The smallest feature the fields are allowed to carry, in tiles, and the most octaves
    /// they may use to get there. See <see cref="SimplexNoise.OctavesFor"/> for why the first
    /// of those is what separates terrain from speckle.
    /// </summary>
    private const double MinFeatureTiles = 5;

    /// <inheritdoc cref="MinFeatureTiles"/>
    private const int MaxOctaves = 6;

    /// <summary>
    /// How strongly the sea pushes the high ground inland, and how far that reach is as a
    /// fraction of the range size.
    /// <para>
    /// Mountains that run right down to the water read as a drowned world rather than a
    /// continent. This is not a hard rule -- a coastal range is a real thing, and a high
    /// enough score still gets one -- it only tilts the odds towards the interior.
    /// </para>
    /// </summary>
    private const double InlandBias = 0.45;
    private const double InlandReach = 1.2;

    /// <summary>
    /// The most a walker climbs in one step, and so the most any walkable tile may stand above
    /// the land beside it.
    /// <para>
    /// One, which is what makes a hill a hill. Anything a walker could not walk up is a crag,
    /// and the mountains are where the crags belong. The same figure grades the passes cut
    /// through a range, for the same reason: a crossing nobody can climb into is not a
    /// crossing.
    /// </para>
    /// </summary>
    private const int MaxStep = 1;

    /// <summary>
    /// An elevation for every tile, in reading order.
    /// </summary>
    /// <param name="land">
    /// Which tiles are land. Read and never written; the relief that comes back is above sea
    /// level exactly where this is true.
    /// </param>
    /// <param name="relief">
    /// The heights as they stand, which the pass builds on rather than replacing: hills leave
    /// the mountains alone and mountains leave the hills alone, so either button can be
    /// pressed on its own and pressed again without disturbing the other. Left untouched --
    /// what comes back is a copy. Null, or the wrong size, starts from flat land.
    /// </param>
    public static int[] RaiseHills(int width, int height, bool[] land, int[] relief, TerrainSettings settings)
    {
        var raised = Prepare(width, height, land, relief, out var landCount);

        if (landCount == 0)
            return raised;

        // The same relief the mountains are cut from, with the high ground allowed to reach
        // outwards by the spread dial. It moves no peak and invents no new one -- it only lets
        // what is already high carry downhill -- so the hills stay an apron around the ranges
        // however far the dial pushes them out.
        var score = Widen(
            Score(width, height, land, settings), land, width, height, settings.HillSpread);

        // Only ground the mountains have not already taken. Hills fill in around a range
        // rather than competing with it, which is why they come out as its skirts.
        var eligible = new bool[land.Length];
        var room = 0;

        for (var i = 0; i < land.Length; i++)
        {
            eligible[i] = land[i] && raised[i] < MountainsFrom;

            if (eligible[i])
                room++;
        }

        // Of the land, as the dial says, but it cannot have what the mountains are standing on.
        var wanted = Math.Min(
            (int)Math.Round(Math.Clamp(settings.HillCoverage, 0, 1) * landCount), room);

        // Cleared first, so a second press replaces the hills rather than adding to them.
        for (var i = 0; i < raised.Length; i++)
        {
            if (eligible[i])
                raised[i] = Flat;
        }

        AssignBand(raised, eligible, score, wanted, HillsFrom, HillsTo);

        // The heights rank has handed out, made into slopes something could climb. This takes
        // no tile out of the hills -- see Settle -- so the coverage dial is still met exactly.
        Settle(raised, land, width, height);

        // No passes to cut. Hills are walkable and so is the flat land they replace, so this
        // moves no tile in or out of the walkable set -- it only changes how high the walk is.
        return raised;
    }

    /// <summary>
    /// Raises the mountains, leaving any hills where they are, and cuts passes through what it
    /// raises.
    /// </summary>
    /// <inheritdoc cref="RaiseHills" path="/param"/>
    public static int[] RaiseMountains(int width, int height, bool[] land, int[] relief, TerrainSettings settings)
    {
        var raised = Prepare(width, height, land, relief, out var landCount);

        if (landCount == 0)
            return raised;

        var score = Score(width, height, land, settings);

        var wanted = (int)Math.Round(Math.Clamp(settings.MountainCoverage, 0, 1) * landCount);

        // Every mountain comes down before any goes back up, so a second press replaces the
        // range rather than piling onto it. Hills are left standing: they are the other
        // button's business, and a mountain that steps down to hill height is a hillside.
        for (var i = 0; i < raised.Length; i++)
        {
            if (raised[i] >= MountainsFrom)
                raised[i] = Flat;
        }

        AssignBand(raised, land, score, wanted, MountainsFrom, MountainsTo);

        // Last, over finished heights: a pass is cut through the mountains as they came out,
        // and cutting one before they existed would mean guessing where a range was going to
        // be. It costs a few tiles of mountain -- the crossings come out at hill height or
        // below, graded so they can be walked -- so the mountain figure is met before the
        // passes are cut rather than after. On the maps this makes that is a fraction of a
        // percent, and a walled-in valley is a worse thing to hand somebody than a dial that
        // reads a shade high.
        LandTopology.OpenMountainPasses(raised, width, height, MountainsFrom, HillsTo, MaxStep);

        // The hills, settled again over the heights this pass has left. Raising a range takes
        // the old one down to flat ground first, and the hillsides that were skirting it are
        // still standing where its shoulder used to hold them up -- a nine with a meadow
        // beside it, which is the one thing a hill may not be. Nothing here is the hills'
        // own business, so this moves no tile into the band or out of it; it only takes back
        // the height the ground the mountains removed was holding up.
        Settle(raised, land, width, height);

        return raised;
    }

    /// <summary>
    /// Checks the arguments, copies the relief so the caller's is left alone, and counts the
    /// land. A relief of the wrong size, or none at all, starts from flat land.
    /// </summary>
    private static int[] Prepare(int width, int height, bool[] land, int[] relief, out int landCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentNullException.ThrowIfNull(land);

        if (land.Length != width * height)
            throw new ArgumentException("Land mask does not match the map size.", nameof(land));

        var raised = relief is not null && relief.Length == land.Length
            ? (int[])relief.Clone()
            : new int[land.Length];

        landCount = 0;

        for (var i = 0; i < land.Length; i++)
        {
            if (land[i])
            {
                landCount++;

                // A tile the land pass has raised since the last relief was built.
                if (raised[i] < Flat)
                    raised[i] = Flat;
            }
            else
            {
                // And one it has drowned. The sea has no height.
                raised[i] = 0;
            }
        }

        return raised;
    }

    /// <summary>
    /// Lets high ground reach outwards, falling away with distance: every tile takes the best
    /// of its own score and its neighbours' one step downhill, so a peak casts an influence
    /// that fades over <paramref name="spreadTiles"/> tiles and then is gone.
    /// <para>
    /// This is what the hills' spread dial does, and it is deliberately not a blur. Averaging
    /// the field was the obvious thing and it does the opposite of what is wanted: a mean pulls
    /// the ranking towards whatever is broadest, so the hills bunch <em>harder</em> onto the
    /// big ranges and the outliers vanish. Taking a falling maximum instead extends the high
    /// ground outward without lowering it, which is the apron.
    /// </para>
    /// <para>
    /// Graded rather than flat, which matters because the band is assigned by rank: were the
    /// apron a plateau of equal scores the hills inside it would have no order, and the height
    /// across a hillside would come out as noise. Falling with distance means the tiles nearest
    /// the range rank highest and a hillside runs downhill the way it should.
    /// </para>
    /// <para>
    /// Two sweeps, one from each corner, propagating only between neighbours that are both
    /// land. That makes the reach a four-way distance -- the same steps everything else on this
    /// map moves in -- and it stops an apron jumping a strait to the next island.
    /// </para>
    /// </summary>
    /// <param name="spreadTiles">How far the influence carries. Under a tile, nothing happens.</param>
    private static double[] Widen(
        double[] score, bool[] land, int width, int height, double spreadTiles)
    {
        if (spreadTiles < 1)
            return score;

        // The scores run roughly 0 to 1, so losing this much a tile puts the reach at about
        // the number of tiles asked for.
        var decay = 1.0 / spreadTiles;

        var widened = (double[])score.Clone();

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var index = (y * width) + x;

                if (!land[index])
                    continue;

                if (x > 0 && land[index - 1])
                    widened[index] = Math.Max(widened[index], widened[index - 1] - decay);

                if (y > 0 && land[index - width])
                    widened[index] = Math.Max(widened[index], widened[index - width] - decay);
            }
        }

        for (var y = height - 1; y >= 0; y--)
        {
            for (var x = width - 1; x >= 0; x--)
            {
                var index = (y * width) + x;

                if (!land[index])
                    continue;

                if (x < width - 1 && land[index + 1])
                    widened[index] = Math.Max(widened[index], widened[index + 1] - decay);

                if (y < height - 1 && land[index + width])
                    widened[index] = Math.Max(widened[index], widened[index + width] - decay);
            }
        }

        return widened;
    }

    /// <summary>
    /// Brings every hill down until none stands more than <see cref="MaxStep"/> above the
    /// land beside it: a tile is held to its lowest land neighbour's height plus one, and
    /// lowering it may in turn hold down the tile beyond, so the whole of the hill country
    /// comes out as slopes that can be walked.
    /// <para>
    /// All eight neighbours, and this is the one place on the map that counts the corners.
    /// Everywhere else four-way is the rule because four ways is how a unit moves; here the
    /// rule is about what the land looks like, and a hill standing two above the tile across
    /// its corner is a step somebody can see whether or not they can walk it. It costs a unit
    /// of height at the odd corner and nothing else -- a slope that already ran downhill four
    /// ways runs downhill eight.
    /// </para>
    /// <para>
    /// It only ever lowers, and it cannot lower a hill out of the band. A hill's neighbours on
    /// land are at flat ground or above, so the floor this can push one to is flat-plus-one,
    /// which is the foot of the hills. Every tile the rank cut made a hill is still a hill
    /// afterwards, and the coverage dial stays exact.
    /// </para>
    /// <para>
    /// What it costs is height, and only where the map was making a claim it could not keep:
    /// a hill is now no higher than its distance from the nearest flat ground allows, so a
    /// broad upland still reaches the top of the band and a lone tile in the middle of a plain
    /// no longer does. That is the shape of real hill country, and it is what the spread dial
    /// was always describing.
    /// </para>
    /// <para>
    /// Mountains are left out of it. A range is allowed its cliffs -- that is most of what
    /// tells one from an upland -- and a mountain neighbour, standing at eleven or more, never
    /// holds a hill down in any case.
    /// </para>
    /// <para>
    /// Swept forwards and then backwards, and repeated until a sweep changes nothing. Each
    /// sweep looks only the way it came, which between them is all eight neighbours. Two
    /// sweeps carry the constraint across open ground, but not around a headland the sea makes
    /// it walk about, and heights only ever fall and never below the foot of the band -- so
    /// the repeat ends, and in practice after the pass that proves it.
    /// </para>
    /// </summary>
    private static void Settle(int[] relief, bool[] land, int width, int height)
    {
        var changed = true;

        while (changed)
        {
            changed = false;

            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var index = (y * width) + x;

                    if (x > 0) Hold(index, index - 1);
                    if (y > 0) Hold(index, index - width);
                    if (x > 0 && y > 0) Hold(index, index - width - 1);
                    if (x < width - 1 && y > 0) Hold(index, index - width + 1);
                }
            }

            for (var y = height - 1; y >= 0; y--)
            {
                for (var x = width - 1; x >= 0; x--)
                {
                    var index = (y * width) + x;

                    if (x < width - 1) Hold(index, index + 1);
                    if (y < height - 1) Hold(index, index + width);
                    if (x < width - 1 && y < height - 1) Hold(index, index + width + 1);
                    if (x > 0 && y < height - 1) Hold(index, index + width - 1);
                }
            }
        }

        // Holds one hill down to what its neighbour allows. Sea is no neighbour at all here:
        // a shore is a step the coast has already drawn, and counting it would forbid a hill
        // anywhere within sight of the water.
        void Hold(int index, int neighbour)
        {
            if (relief[index] < HillsFrom || relief[index] > HillsTo || !land[neighbour])
                return;

            var limit = relief[neighbour] + MaxStep;

            if (relief[index] <= limit)
                return;

            relief[index] = limit;
            changed = true;
        }
    }

    /// <summary>
    /// How high each land tile wants to be, before any of it is cut into bands. Sea scores
    /// below every land tile, so a cut never lands on it.
    /// <para>
    /// One field for both passes, taken from the one seed, which is what puts the hills around
    /// the mountains instead of somewhere else entirely: the two bands are neighbouring slices
    /// of the same ranking, so the hill band is by construction the ground just below the
    /// range.
    /// </para>
    /// </summary>
    private static double[] Score(int width, int height, bool[] land, TerrainSettings settings)
    {
        var ruggedness = Math.Clamp(settings.Ruggedness, 0, 1);
        var rangeSize = Math.Max(2, settings.RangeSize);

        var noise = new SimplexNoise(unchecked((uint)settings.Seed * 2246822519u) + 0x85EBu);
        var frequency = 1.0 / rangeSize;

        // A short range gets fewer octaves, which is right: there is no room for four scales
        // of detail inside a massif eight tiles across.
        var octaves = SimplexNoise.OctavesFor(rangeSize, MinFeatureTiles, MaxOctaves);

        // How far from the sea each land tile is, which is what keeps the ranges inland.
        var inland = LandTopology.DistanceFromSea(land, width, height);
        var reach = rangeSize * InlandReach;

        var score = new double[land.Length];

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var index = (y * width) + x;

                if (!land[index])
                {
                    // Sorted below every land tile, so the cuts below never land on sea.
                    score[index] = double.MinValue;
                    continue;
                }

                var swell = (noise.Fbm(x * frequency, y * frequency, octaves) + 1) * 0.5;

                // The ranges. Sampled off the swell's own coordinates, or the two fields would
                // peak together and the dial between them would do nothing.
                var ridged = noise.RidgedFbm(
                    (x * frequency) + 31.7, (y * frequency) - 12.4, octaves);

                var height01 = swell + ((ridged - swell) * ruggedness);

                // Full strength out in the interior, nothing at the shore.
                var shore = Math.Min(1, inland[index] / reach);

                score[index] = height01 * (1 - InlandBias + (InlandBias * shore));
            }
        }

        return score;
    }

    /// <summary>
    /// Gives the highest-scoring <paramref name="wanted"/> of the eligible tiles an elevation
    /// in one band, and leaves everything else exactly as it found it.
    /// <para>
    /// The cut is a quantile of the eligible scores, so the dial is met to the tile.
    /// </para>
    /// <para>
    /// Within a band the elevation follows <b>rank</b> and not the raw score, which is what
    /// makes a mountain look like one. Scores bunch hard just above a cut -- there is far more
    /// land at the foot of a range than at the top of it -- so scaling the score straight onto
    /// 11 to 30 put four fifths of the mountains in the bottom third of the band and left the
    /// peaks to a few dozen tiles. Ranking spends the whole band. Rank is a monotone remap of
    /// the score, so nothing about where the high ground sits changes; only how the height is
    /// spread across it.
    /// </para>
    /// </summary>
    private static void AssignBand(
        int[] relief, bool[] eligible, double[] score, int wanted, int low, int high)
    {
        var band = ScoreCut.Take(eligible, score, wanted);

        for (var i = 0; i < relief.Length; i++)
        {
            if (eligible[i] && band.Takes(score[i]))
                relief[i] = Step(band.PlaceOf(score[i]), band.Taken, low, high);
        }
    }

    /// <summary>
    /// A tile's place in its band, as an elevation in that band: the lowest tile of the band
    /// comes out at <paramref name="low"/> and the highest at <paramref name="high"/>, with
    /// the rest spread evenly between them.
    /// </summary>
    private static int Step(int place, int count, int low, int high)
    {
        var position = count <= 1 ? 1d : (double)place / (count - 1);

        return Math.Clamp(low + (int)(position * (high - low + 1)), low, high);
    }
}
