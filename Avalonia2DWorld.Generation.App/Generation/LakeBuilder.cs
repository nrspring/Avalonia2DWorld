using System;
using System.Collections.Generic;
using Avalonia2DWorld.MapServices.Constants;

namespace Avalonia2DWorld.Generation.App.Generation;

/// <summary>
/// What the Lakes panel asks for.
/// </summary>
/// <param name="Count">
/// How many lakes to add to whatever is already there. A target rather than a promise: a hollow
/// too small to hold the size asked for, or too near water already on the map, is passed over --
/// so a world with little unclaimed low ground comes back with fewer, and eventually with none.
/// </param>
/// <param name="Size">
/// Roughly how many tiles a lake covers. An average: each one varies either side of it, since
/// lakes of one area read as a spill of identical ponds.
/// </param>
/// <param name="Irregularity">
/// From 0 (as round as the ground allows) to 1 (reaching well up every hollow it can find). It
/// is the only dial that changes the shape of a lake rather than how many there are or how big.
/// </param>
/// <param name="Seed">Makes a run repeatable.</param>
public readonly record struct LakeSettings(int Count, double Size, double Irregularity, int Seed);

/// <summary>
/// Fills hollows in the low ground with standing water, as a cover per tile in reading order.
/// <para>
/// A lake is <see cref="GroundCover.River"/>, the same cover a river is, and that is a decision
/// rather than a shortcut. Both are fresh water lying on land at the height of the land, both
/// are drawn as the same blue surface, and making them one cover is what lets a river run into a
/// lake as one unbroken sheet instead of two textures meeting at a shoreline. It also means
/// <see cref="RiverBuilder"/> already knows what a lake is without being told: a later set of
/// rivers keeps its sources clear of one and ends its routes at one, which is what a river
/// reaching a lake does.
/// </para>
/// <para>
/// A lake is not a shape dropped on the map. It is the ground under it filled from the lowest
/// tile outwards, cheapest first -- so its outline is the contour of the hollow it sits in,
/// which is where an organic shape comes from here. A blob of noise laid over the terrain would
/// have given a lumpy circle that could as easily lie up a hillside; growing into the ground
/// instead cannot, because climbing is what the growth is unwilling to pay for.
/// </para>
/// <para>
/// Laid over that is a broad noise field the irregularity dial weights, which is what keeps a
/// lake in gentle country from coming out round. On a plain the terrain has no opinion about
/// which way the water should spread, and without the field the growth would answer that with a
/// disc; with it, the water reaches into whichever quarter the field happens to make cheap.
/// </para>
/// <para>
/// Cover only. The relief and the coastline both come back exactly as they were handed in -- no
/// lake here digs a basin or floods one, and a lake takes the elevation of the tile it replaces,
/// exactly as a river does. That is what lets the panel be pressed over a finished world.
/// </para>
/// <para>
/// It also means a lake is not perfectly level, which a real one is. What keeps that from
/// showing is <see cref="ShoreRise"/>: the water will not climb more than a step above the tile
/// it started from, so a lake spans two elevations at the most and reads flat. Digging the
/// basin out to one height would be truer, and would cost this pass the thing that makes it
/// safe to press over finished terrain.
/// </para>
/// <para>
/// Nothing here removes water it was given. A pass adds to what is on the map, the way islands
/// are added to a coastline and unlike the way swamps replace swamps -- so the panel can be
/// pressed again for another set, and the set it adds sees everything already there. New lakes
/// keep clear of the sea, of the rivers, and of the lakes an earlier press left, which is what
/// makes a second press find new country rather than lay a second map of lakes over the first.
/// </para>
/// <para>
/// What comes out is lowland, every tile of it, by construction: a lake starts on the flats or
/// in the hills and never climbs, so it cannot reach a mountain. That is what lets it be an
/// ordinary cover rather than a special case -- the rule that a range raised over a marsh takes
/// the marsh with it needs no exception written into it for lakes, because a lake was never up
/// there to begin with.
/// </para>
/// </summary>
public static class LakeBuilder
{
    /// <summary>The four ways water may spread. The same four everything else on this map moves in.</summary>
    private static readonly (int X, int Y)[] Steps = [(1, 0), (-1, 0), (0, 1), (0, -1)];

    /// <summary>
    /// The smallest lake worth drawing, in tiles. Below this it is a puddle, and a puddle reads
    /// as a stray tile of blue rather than as water.
    /// </summary>
    private const int MinArea = 6;

    /// <summary>
    /// How far above its lowest tile the water may reach, in elevation.
    /// <para>
    /// One. The growth is unwilling to climb but not forbidden from it, and without a hard stop
    /// a lake asked for a large area on rolling ground would buy its way up a hillside once the
    /// cheap ground ran out. A step up out of the water is the shore.
    /// </para>
    /// </summary>
    private const int ShoreRise = 1;

    /// <summary>
    /// What a step of climb costs, against the noise field's zero-to-one and the pull's tile.
    /// <para>
    /// High enough that every tile at the basin's own level is taken before any tile above it,
    /// which is what makes the lake fill the hollow rather than spill out of it.
    /// </para>
    /// </summary>
    private const double RiseCost = 40;

    /// <summary>
    /// The most the shape field can add to one tile, at the dial's full setting. Measured
    /// against the pull below, so at full irregularity the field decides which way the water
    /// reaches and the pull only stops it reaching forever.
    /// </summary>
    private const double ShapeCost = 12;

    /// <summary>
    /// What one tile of distance from the middle costs.
    /// <para>
    /// Small, but never zero. The shape field is cheap along whole filaments of the map, and
    /// without a price on distance a lake would follow one of them clean across the country as
    /// a tendril a tile or two wide. This is what keeps a lake a lake.
    /// </para>
    /// </summary>
    private const double Pull = 0.6;

    /// <summary>
    /// How many tiles across one lobe of the shape field runs, and the octaves it gets.
    /// <para>
    /// Broader than the lakes are wide, so the field pulls a whole side of one outwards rather
    /// than fraying its edge. A fine field would come out as a rim of speckle, which reads as a
    /// dithering artefact and not as a shoreline.
    /// </para>
    /// </summary>
    private const double ShapeScale = 14;

    /// <inheritdoc cref="ShapeScale"/>
    private const int ShapeOctaves = 3;

    /// <summary>
    /// Dry land to leave between a lake and any other water, in tiles.
    /// <para>
    /// A lake that comes within a tile of the sea is a lagoon the map cannot explain, and one
    /// that touches a river turns the river's mouth into a bay. Kept apart rather than sorted
    /// out afterwards: unlike a river's width, there is nothing here that can be thinned back.
    /// </para>
    /// </summary>
    private const double Clearance = 3;

    /// <summary>
    /// The window a hollow is judged over, in tiles either side. Wide enough to see the shape of
    /// a valley and not so wide that every tile inland of a range looks like a basin.
    /// </summary>
    private const int BasinWindow = 3;

    /// <summary>Candidate middles tried before the pass gives up on a lake.</summary>
    private const int MaxAttempts = 400;

    /// <summary>
    /// Fills the lakes and returns the covers as they now stand.
    /// </summary>
    /// <param name="relief">Elevation per tile. Read and never written -- a lake digs nothing.</param>
    /// <param name="cover">
    /// The covers as they stand, which the pass builds on: swamp and desert are left exactly
    /// where they are, and a lake simply lies over whichever it covers. Left untouched -- what
    /// comes back is a copy. Null, or the wrong size, starts from grass.
    /// </param>
    /// <param name="made">
    /// How many lakes this pass added, which is not always how many were asked for.
    /// <para>
    /// Reported rather than left to be inferred from the map, because it cannot be inferred from
    /// the map: a lake laid against a river is one shape of water on the ground, and counting
    /// the shapes would say one. It is also the only way the panel can tell somebody that the
    /// size they have asked for is bigger than any hollow their world has left -- which
    /// otherwise shows up as a button that appears to do nothing.
    /// </para>
    /// </param>
    public static GroundCover[] Add(
        int width, int height, int[] relief, GroundCover[] cover, LakeSettings settings,
        out int made)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentNullException.ThrowIfNull(relief);

        if (relief.Length != width * height)
            throw new ArgumentException("Relief does not match the map size.", nameof(relief));

        var run = cover is not null && cover.Length == relief.Length
            ? (GroundCover[])cover.Clone()
            : new GroundCover[relief.Length];

        made = 0;

        var wanted = Math.Max(0, settings.Count);

        if (wanted == 0)
            return run;

        var size = Math.Max(MinArea, settings.Size);
        var irregular = Math.Clamp(settings.Irregularity, 0, 1);

        var random = new Random(settings.Seed);
        var shape = Shape(width, height, settings.Seed);

        // Every kind of water there is, read as one thing: the sea, and the rivers and lakes an
        // earlier press left. What this pass has to keep clear of does not depend on which.
        var water = new bool[relief.Length];

        for (var i = 0; i < relief.Length; i++)
            water[i] = relief[i] <= Elevations.Sea || run[i] is GroundCover.River or GroundCover.Shallow;

        // Distance from all of it at once, flooded from every shore -- the same measure the
        // shallows are drawn from, handed the mask upside down. Once, before any lake is laid:
        // the lakes this pass adds are kept apart by the crowding test below instead, which
        // costs a handful of comparisons rather than a flood of the whole map per lake.
        var dry = new bool[relief.Length];

        for (var i = 0; i < relief.Length; i++)
            dry[i] = !water[i];

        var fromWater = LandTopology.DistanceFromSea(dry, width, height);

        var basins = Basins(width, height, relief, water, fromWater);

        // Reused across attempts rather than allocated per lake: this is one array per tile of
        // the map, four hundred attempts are allowed, and a growth only ever touches the tiles
        // around its own hollow -- so it puts back what it marked and the next one starts clean.
        var seen = new bool[relief.Length];
        var touched = new List<int>();

        var placed = new List<(int Index, double Radius)>();
        var tried = 0;

        foreach (var centre in basins)
        {
            if (made >= wanted || tried >= MaxAttempts)
                break;

            // Lakes of one size read as a spill of identical ponds.
            var target = Math.Max(MinArea, (int)Math.Round(size * (0.6 + (random.NextDouble() * 0.8))));

            // What that much water would measure across if it came out round, which is what the
            // clearances are in terms of. It will not come out round, but it will not come out
            // far from this either -- the pull above is what sees to that.
            var radius = Math.Sqrt(target / Math.PI);

            if (fromWater[centre] < radius + Clearance || Crowded(centre, radius, placed, width))
                continue;

            tried++;

            if (Grow(relief, water, shape, irregular, seen, touched, width, height, centre, target)
                is not { } pool)
            {
                continue;
            }

            foreach (var index in pool)
            {
                run[index] = GroundCover.River;
                water[index] = true;
            }

            placed.Add((centre, radius));
            made++;
        }

        return run;
    }

    /// <summary>
    /// Grows one lake out from <paramref name="centre"/> until it has <paramref name="target"/>
    /// tiles or runs out of ground it is willing to take, as the tiles it covers. Null if what
    /// it managed is too little to be worth drawing.
    /// <para>
    /// Cheapest tile first, out of everything the water is currently touching -- so this is a
    /// flood ordered by a price rather than by distance, and the price is what shapes it. Level
    /// ground next to the water is nearly free, ground a step up is dear, and the noise field
    /// decides between the many tiles that are otherwise the same. Every tile taken is a
    /// neighbour of one already taken, so what comes out is joined up by construction and there
    /// is no largest-piece pass to do afterwards.
    /// </para>
    /// <para>
    /// The cost is a property of the tile rather than of the way in, unlike a route's, which is
    /// why a lake fills a hollow evenly instead of favouring whichever side it started from.
    /// </para>
    /// </summary>
    private static List<int>? Grow(
        int[] relief, bool[] water, double[] shape, double irregular, bool[] seen,
        List<int> touched, int width, int height, int centre, int target)
    {
        var floor = relief[centre];

        var cx = centre % width;
        var cy = centre / width;

        var pool = new List<int>(target);
        var frontier = new PriorityQueue<int, double>();

        touched.Clear();

        seen[centre] = true;
        touched.Add(centre);
        frontier.Enqueue(centre, 0);

        while (pool.Count < target && frontier.TryDequeue(out var index, out _))
        {
            pool.Add(index);

            var x = index % width;
            var y = index / width;

            foreach (var (dx, dy) in Steps)
            {
                var nx = x + dx;
                var ny = y + dy;

                if (nx < 0 || ny < 0 || nx >= width || ny >= height)
                    continue;

                var neighbour = (ny * width) + nx;

                if (seen[neighbour])
                    continue;

                // Marked on the way in rather than on the way out, and marked even where the
                // tile is refused: a tile the water will not have is a tile it will not have
                // however many of its neighbours ask again.
                seen[neighbour] = true;
                touched.Add(neighbour);

                // Never the sea, never another river or lake -- the clearance keeps a lake well
                // away from all of it, and this is what holds if the clearance is ever loosened.
                if (water[neighbour] || relief[neighbour] <= Elevations.Sea)
                    continue;

                // Never a mountain, and never more than the shore's step above the floor. The
                // second makes the first redundant on any map a lake actually starts on, and
                // both are cheap.
                if (relief[neighbour] >= Elevations.MountainsFrom
                    || relief[neighbour] > floor + ShoreRise)
                {
                    continue;
                }

                var awayX = (double)nx - cx;
                var awayY = (double)ny - cy;

                var cost = ((relief[neighbour] - floor) * RiseCost)
                    + (shape[neighbour] * irregular * ShapeCost)
                    + (Math.Sqrt((awayX * awayX) + (awayY * awayY)) * Pull);

                frontier.Enqueue(neighbour, cost);
            }
        }

        foreach (var index in touched)
            seen[index] = false;

        // Half of what was asked for, or nothing. A hollow that can only hold a fraction of the
        // size on the dial is the wrong hollow for it, and the honest thing is to leave it for
        // the next press with a smaller number in the box rather than drop a pond in it.
        return pool.Count >= Math.Max(MinArea, target / 2) ? pool : null;
    }

    /// <summary>
    /// Where a lake might sit, best first: the hollows in the low ground, furthest from water.
    /// <para>
    /// A hollow is judged by how much higher the country around a tile is than the tile itself,
    /// over a window a valley wide. That one number does the work: a dip between two hills
    /// scores well, a hillside scores nothing because half its window is below it, and the coast
    /// scores badly because the sea in its window is lower than any land can be -- which keeps
    /// lakes inland without a rule saying so.
    /// </para>
    /// <para>
    /// Ground beside a range scores highest of all, and that is right rather than a quirk: the
    /// valley floor at the foot of the high ground is where the water gathers on a real map too.
    /// </para>
    /// <para>
    /// Lowland only. Not the mountains, where a lake would be a tarn hanging off a crag that the
    /// cover rules would strip the moment anything raised the ground again, and not the sea.
    /// </para>
    /// </summary>
    private static List<int> Basins(int width, int height, int[] relief, bool[] water, int[] fromWater)
    {
        var hollow = new double[relief.Length];
        var candidates = new List<int>();

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var index = (y * width) + x;

                if (water[index])
                    continue;

                if (relief[index] < Elevations.Flat || relief[index] > Elevations.HillsTo)
                    continue;

                var sum = 0;
                var counted = 0;

                for (var ny = Math.Max(0, y - BasinWindow); ny <= Math.Min(height - 1, y + BasinWindow); ny++)
                {
                    for (var nx = Math.Max(0, x - BasinWindow); nx <= Math.Min(width - 1, x + BasinWindow); nx++)
                    {
                        sum += relief[(ny * width) + nx];
                        counted++;
                    }
                }

                hollow[index] = ((double)sum / counted) - relief[index];
                candidates.Add(index);
            }
        }

        candidates.Sort((a, b) =>
        {
            var byHollow = hollow[b].CompareTo(hollow[a]);
            if (byHollow != 0)
                return byHollow;

            var byInland = fromWater[b].CompareTo(fromWater[a]);
            return byInland != 0 ? byInland : a.CompareTo(b);
        });

        return candidates;
    }

    /// <summary>Whether a middle sits too near a lake this pass has already laid.</summary>
    private static bool Crowded(
        int centre, double radius, List<(int Index, double Radius)> placed, int width)
    {
        var x = centre % width;
        var y = centre / width;

        foreach (var (other, otherRadius) in placed)
        {
            var dx = (double)(other % width) - x;
            var dy = (double)(other / width) - y;

            if (Math.Sqrt((dx * dx) + (dy * dy)) < radius + otherRadius + Clearance + 1)
                return true;
        }

        return false;
    }

    /// <summary>
    /// What crossing each tile adds to the growth, before the ground is asked about. Runs 0 to
    /// 1, in lobes broader than a lake.
    /// <para>
    /// The field itself rather than the distance from its zero, which is the opposite of what
    /// <see cref="RiverBuilder"/> wants and for the opposite reason. A river needs a filament to
    /// follow, so it folds the field about zero to get one; a lake needs whole quarters of its
    /// surroundings to be cheaper than the rest, which is what a plain field's lobes already
    /// are.
    /// </para>
    /// </summary>
    private static double[] Shape(int width, int height, int seed)
    {
        var noise = new SimplexNoise(unchecked((uint)seed * 2246822519u) + 0x2F1Bu);
        var frequency = 1.0 / ShapeScale;

        var field = new double[width * height];

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                // Folded from the noise's own range into nought to one, so the dial's weight is
                // the whole of how much it counts and no part of the field is a discount.
                field[(y * width) + x] =
                    (noise.Fbm(x * frequency, y * frequency, ShapeOctaves) + 1) / 2;
            }
        }

        return field;
    }
}
