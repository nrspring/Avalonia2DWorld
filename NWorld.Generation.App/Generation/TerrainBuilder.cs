using System;
using NWorld.MapServices.Constants;

namespace NWorld.Generation.App.Generation;

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
/// From 0 (broad rounded uplands) to 1 (sharp, broken ranges with the peaks strung out along
/// crests).
/// </param>
/// <param name="RangeSize">Roughly how many tiles a range runs for, before it breaks.</param>
/// <param name="Seed">Makes a run repeatable. The same seed and settings give the same relief.</param>
public readonly record struct TerrainSettings(
    double MountainCoverage, double HillCoverage, double Ruggedness, double RangeSize, int Seed);

/// <summary>
/// Raises hills and mountains out of land that is already there, as an elevation per tile in
/// reading order: 0 at sea, 1 for flat land, 2 to 10 for hills, 11 to 30 for mountains.
/// <para>
/// Two passes rather than one, because they are two buttons. Each reads the heights already
/// there and puts back only its own band, so raising hills does not disturb a range and
/// raising a range does not disturb the hills -- and either can be pressed twice without the
/// second press piling onto the first. What keeps them looking like one landscape is that both
/// take their heights from the same field, off the same seed: the two bands are neighbouring
/// slices of one ranking, so hills come out as the skirts of the mountains rather than as
/// something scattered independently.
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
    /// The smallest feature the fields are allowed to carry, in tiles.
    /// <para>
    /// This is what separates terrain from speckle. Octaves double in frequency, so the finest
    /// one in a five-octave field spanning a twenty-six tile range has a wavelength under two
    /// tiles -- noise at the size of a tile, which sends neighbours to 1 and 13 and back, and
    /// leaves a mountain range looking like static rather than like ground somebody could walk
    /// up. Octaves are counted from the range size so the finest never gets below this.
    /// </para>
    /// </summary>
    private const double MinFeatureTiles = 5;

    /// <summary>Detail past this is below a tile on any range worth having.</summary>
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

        var score = Score(width, height, land, settings);

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
        // be. It costs a few tiles of mountain -- the crossings are lowered to hill height --
        // so the mountain figure is met before the passes are cut rather than after. On the
        // maps this makes that is a fraction of a percent, and a walled-in valley is a worse
        // thing to hand somebody than a dial that reads a shade high.
        LandTopology.OpenMountainPasses(raised, width, height, MountainsFrom, HillsTo);

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

        // Held so the finest octave stays broader than a handful of tiles -- see
        // MinFeatureTiles. A short range therefore gets fewer octaves, which is right: there is
        // no room for four scales of detail inside a massif eight tiles across.
        var octaves = Math.Clamp(
            1 + (int)Math.Log2(rangeSize / MinFeatureTiles), 1, MaxOctaves);

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

                // Folded about zero, so what was a smooth crossing becomes a crease. Sampled
                // off the swell's own coordinates, or the two fields would peak together and
                // the dial between them would do nothing.
                var crest = 1 - Math.Abs(noise.Fbm(
                    (x * frequency) + 31.7, (y * frequency) - 12.4, octaves));

                // Squared, which sharpens the crease into a ridge rather than leaving it a
                // rounded fold. Only at the rugged end, where that is what is being asked for.
                var ridged = crest * crest;

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
        if (wanted <= 0)
            return;

        var room = 0;

        for (var i = 0; i < eligible.Length; i++)
        {
            if (eligible[i])
                room++;
        }

        if (room == 0)
            return;

        wanted = Math.Min(wanted, room);

        var ranked = new double[room];
        var next = 0;

        for (var i = 0; i < score.Length; i++)
        {
            if (eligible[i])
                ranked[next++] = score[i];
        }

        Array.Sort(ranked);

        var cut = Cut(ranked, wanted);

        // Where the band starts in the sorted scores, so a tile's place in it is the distance
        // from here to where its own score sits.
        var floor = ranked.Length - wanted;

        for (var i = 0; i < relief.Length; i++)
        {
            if (eligible[i] && score[i] >= cut)
                relief[i] = Step(Rank(ranked, score[i]) - floor, wanted, low, high);
        }
    }

    /// <summary>
    /// Where a score sits in the sorted scores: the number of tiles standing lower than it.
    /// <para>
    /// Found rather than carried alongside, which costs a binary search per tile and saves
    /// building and sorting a second index over the whole map. Tiles that score exactly the
    /// same rank together, which is the only honest answer for ground of the same height.
    /// </para>
    /// </summary>
    private static int Rank(double[] ranked, double value)
    {
        var found = Array.BinarySearch(ranked, value);

        return found >= 0 ? found : ~found;
    }

    /// <summary>
    /// The score a tile has to beat to be in the top <paramref name="take"/> of the land.
    /// <para>
    /// Above every score when nothing is being taken, which is what makes a dial at zero mean
    /// none rather than one -- the highest tile would otherwise always tie its way in.
    /// </para>
    /// </summary>
    private static double Cut(double[] ranked, int take) =>
        take <= 0
            ? double.MaxValue
            : ranked[Math.Clamp(ranked.Length - take, 0, ranked.Length - 1)];

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
