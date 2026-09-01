using System;
using System.Collections.Generic;

namespace NWorld.Generation.App.Generation;

/// <summary>
/// The rules a finished land mask has to obey, whatever drew it.
/// <para>
/// Both of them come from the same place: things move on this map a square at a time, north,
/// south, east or west. So water that cannot be sailed to is not sea, and land that cannot be
/// walked to is not land -- however they may look. A generator that leaves either behind has
/// drawn a picture rather than a map.
/// </para>
/// <para>
/// Applied after a pass has done its shaping rather than woven into it, because they are
/// facts about the result and not about how it was arrived at. Every pass ends by putting its
/// mask through here.
/// </para>
/// </summary>
public static class LandTopology
{
    /// <summary>The four ways a unit may step, and so the four ways land connects.</summary>
    private static readonly (int X, int Y)[] Steps = [(1, 0), (-1, 0), (0, 1), (0, -1)];

    /// <summary>The four ways it may not.</summary>
    private static readonly (int X, int Y)[] Corners = [(1, 1), (1, -1), (-1, 1), (-1, -1)];

    /// <summary>
    /// Turns water that cannot reach the outside into land, and reports how much land there
    /// is afterwards.
    /// <para>
    /// A continent with a sea inside it is a puzzle: nothing meant to put it there, and it
    /// reads as a hole rather than as a lake -- its shore is the ocean's shore, and there is
    /// no river feeding it. Until something makes lakes on purpose, water is the sea, and the
    /// sea is what you can sail to.
    /// </para>
    /// <para>
    /// Found by flooding inwards from the edges of the map rather than by hunting for
    /// enclosed pockets: everything the flood does not reach is enclosed, by definition, and
    /// the flood visits each tile once.
    /// </para>
    /// </summary>
    public static int FillEnclosedWater(bool[] land, int width, int height)
    {
        var reached = new bool[land.Length];
        var queue = new int[land.Length];
        var head = 0;
        var tail = 0;

        void Enter(int index)
        {
            if (land[index] || reached[index])
                return;

            reached[index] = true;
            queue[tail++] = index;
        }

        for (var x = 0; x < width; x++)
        {
            Enter(x);
            Enter(((height - 1) * width) + x);
        }

        for (var y = 0; y < height; y++)
        {
            Enter(y * width);
            Enter((y * width) + width - 1);
        }

        while (head < tail)
        {
            var index = queue[head++];
            var x = index % width;
            var y = index / width;

            if (x > 0) Enter(index - 1);
            if (x < width - 1) Enter(index + 1);
            if (y > 0) Enter(index - width);
            if (y < height - 1) Enter(index + width);
        }

        var total = 0;

        for (var i = 0; i < land.Length; i++)
        {
            if (!reached[i])
                land[i] = true;

            if (land[i])
                total++;
        }

        return total;
    }

    /// <summary>
    /// Breaks every join that exists only across a corner, and reports how much land is left.
    /// <para>
    /// Nothing can step across a corner, so land touching a coast diagonally is a place that
    /// looks joined to the continent and cannot be walked to from it. The repair is to drown
    /// the tile on the smaller side of the join, which is the least the map can lose and
    /// still tell the truth: a lone speck against a headland is the join, so it disappears
    /// altogether, while a piece with something behind it keeps what is behind it and becomes
    /// a proper islet with water around it.
    /// </para>
    /// <para>
    /// A tile at a time, rather than the whole piece. Two continents that happen to graze
    /// corners are both perfectly walkable and neither is a mistake -- drowning either of them
    /// for touching would throw away a continent to fix a pixel.
    /// </para>
    /// <para>
    /// Land separated by actual water is left alone. An island is not a mistake: it is
    /// somewhere you need a boat to get to, which is a different thing from somewhere you can
    /// see across a corner and never reach.
    /// </para>
    /// <para>
    /// Repeated until nothing changes, since drowning a tile can expose a corner that was
    /// behind it.
    /// </para>
    /// </summary>
    public static int RemoveCornerJoins(bool[] land, int width, int height)
    {
        int total;

        do
        {
            total = 0;
        }
        while (Sweep(land, width, height, ref total));

        return total;
    }

    /// <summary>
    /// One pass: label the land, drown the weaker tile of every corner-only join, and say
    /// whether anything went.
    /// </summary>
    private static bool Sweep(bool[] land, int width, int height, ref int total)
    {
        var (labels, sizes) = Label(land, width, height);

        var drown = new bool[land.Length];
        var drowned = false;

        for (var index = 0; index < land.Length; index++)
        {
            var piece = labels[index];
            if (piece == 0)
                continue;

            var x = index % width;
            var y = index / width;

            foreach (var (dx, dy) in Corners)
            {
                var nx = x + dx;
                var ny = y + dy;

                if (nx < 0 || ny < 0 || nx >= width || ny >= height)
                    continue;

                var neighbour = (ny * width) + nx;
                var other = labels[neighbour];

                // Same piece across the corner means there is a way round through tiles that
                // do connect, so nothing here is unreachable.
                if (other == 0 || other == piece)
                    continue;

                // The weaker side gives way: bigger wins, and between two the same size the
                // one found first does. A rule that always picks one of the pair is what
                // stops each waiting for the other to move.
                if (!Stronger(other, piece, sizes))
                    continue;

                drown[index] = true;
                drowned = true;
                break;
            }
        }

        total = 0;

        for (var index = 0; index < land.Length; index++)
        {
            if (drown[index])
                land[index] = false;

            if (land[index])
                total++;
        }

        return drowned;
    }

    /// <summary>
    /// Cuts passes through mountain ranges until every part of a landmass can be walked to
    /// from every other part without climbing one, and reports how many tiles were lowered.
    /// <para>
    /// This is the same rule as the other two, applied one level up. Land that cannot be
    /// walked to is not land -- and a valley ringed by mountains is exactly that, however
    /// green it looks. A range is meant to be something you go around or through, not a wall
    /// that quietly cuts a continent in half.
    /// </para>
    /// <para>
    /// The passes are found rather than placed. Every tile is given the cost of lowering it to
    /// a walkable height -- nothing for ground that already is, and the height above it for
    /// anything else -- and the cheapest crossings between the walled-in regions are the ones
    /// cut. The cheapest crossing of a range is its lowest saddle, so the passes come out where
    /// a pass belongs, and a range with a natural gap in it is joined through the gap at no
    /// cost at all.
    /// </para>
    /// <para>
    /// One breadth-first sweep for all of them, not one per region: every region floods
    /// outwards at once, whichever reaches a tile first owns it, and the places where two
    /// floods meet are the candidate crossings. Taking those cheapest-first and skipping any
    /// whose two sides are already joined is a minimum spanning forest over the regions, so
    /// the map is opened up for the least mountain cut.
    /// </para>
    /// <para>
    /// Separate landmasses are left alone. The flood only ever walks on land, so two regions
    /// on either side of open water never meet and no pass is cut across the sea. An island
    /// with nothing but mountain on it is left as it is -- there is no walkable ground on it
    /// to join.
    /// </para>
    /// </summary>
    /// <param name="relief">Elevation per tile, edited in place. Sea is 0.</param>
    /// <param name="mountainFrom">The lowest elevation that counts as impassable.</param>
    /// <param name="passHeight">What a cut tile is lowered to. Below <paramref name="mountainFrom"/>.</param>
    public static int OpenMountainPasses(
        int[] relief, int width, int height, int mountainFrom, int passHeight)
    {
        ArgumentNullException.ThrowIfNull(relief);

        if (passHeight >= mountainFrom)
            throw new ArgumentOutOfRangeException(
                nameof(passHeight), "A pass has to come out below the mountains.");

        var walkable = new bool[relief.Length];
        for (var i = 0; i < relief.Length; i++)
            walkable[i] = relief[i] > 0 && relief[i] < mountainFrom;

        var (region, sizes) = Label(walkable, width, height);

        // One region, or none at all, is nothing to join.
        if (sizes.Count <= 2)
            return 0;

        // What it costs to enter a tile: what has to come off it. Sea is never entered.
        int Cost(int index) =>
            relief[index] <= passHeight ? 0 : relief[index] - passHeight;

        var owner = new int[relief.Length];
        var from = new int[relief.Length];
        var spent = new long[relief.Length];

        Array.Fill(from, -1);
        Array.Fill(spent, long.MaxValue);

        var frontier = new PriorityQueue<int, long>();

        for (var index = 0; index < relief.Length; index++)
        {
            if (region[index] == 0)
                continue;

            owner[index] = region[index];
            spent[index] = 0;
            frontier.Enqueue(index, 0);
        }

        // Where two floods met, and what the crossing between them cost.
        var crossings = new List<(long Cost, int Near, int Far)>();

        while (frontier.TryDequeue(out var index, out var cost))
        {
            // A stale copy, left behind when a cheaper route to the same tile was found.
            if (cost > spent[index])
                continue;

            var x = index % width;
            var y = index / width;

            foreach (var (dx, dy) in Steps)
            {
                var nx = x + dx;
                var ny = y + dy;

                if (nx < 0 || ny < 0 || nx >= width || ny >= height)
                    continue;

                var neighbour = (ny * width) + nx;

                // Never off the land, which is what keeps a pass from being cut through
                // the sea between two islands.
                if (relief[neighbour] <= 0)
                    continue;

                if (owner[neighbour] != 0 && owner[neighbour] != owner[index])
                {
                    // Two different floods touching. Kept as a candidate rather than cut
                    // now: a cheaper crossing of the same pair may still turn up.
                    crossings.Add((spent[index] + spent[neighbour] + Cost(neighbour), index, neighbour));
                    continue;
                }

                var reached = spent[index] + Cost(neighbour);

                if (reached >= spent[neighbour])
                    continue;

                spent[neighbour] = reached;
                owner[neighbour] = owner[index];
                from[neighbour] = index;
                frontier.Enqueue(neighbour, reached);
            }
        }

        crossings.Sort(static (a, b) => a.Cost.CompareTo(b.Cost));

        var joined = new int[sizes.Count];
        for (var i = 0; i < joined.Length; i++)
            joined[i] = i;

        int Root(int piece)
        {
            while (joined[piece] != piece)
                piece = joined[piece] = joined[joined[piece]];

            return piece;
        }

        var lowered = 0;

        foreach (var (_, near, far) in crossings)
        {
            var a = Root(owner[near]);
            var b = Root(owner[far]);

            // Already joined, by a crossing cheaper than this one.
            if (a == b)
                continue;

            joined[a] = b;

            lowered += Cut(relief, from, near, mountainFrom, passHeight);
            lowered += Cut(relief, from, far, mountainFrom, passHeight);
        }

        return lowered;
    }

    /// <summary>
    /// Walks back from where two floods met to the walkable ground the flood started on,
    /// lowering every mountain on the way. Returns how many tiles it took down.
    /// </summary>
    private static int Cut(int[] relief, int[] from, int index, int mountainFrom, int passHeight)
    {
        var lowered = 0;

        while (index >= 0)
        {
            if (relief[index] >= mountainFrom)
            {
                relief[index] = passHeight;
                lowered++;
            }

            index = from[index];
        }

        return lowered;
    }

/// <summary>
        /// How far each tile is from the nearest sea, in four-way steps, by a flood outwards
        /// from the coast. Sea itself is zero.
        /// <para>
        /// A breadth-first walk rather than a distance transform: it visits each tile once, it
        /// measures the same four-way steps everything else on this map moves in, and on the
        /// largest map allowed it is a single pass over an array.
        /// </para>
        /// </summary>
    public static int[] DistanceFromSea(bool[] land, int width, int height)
        {
            var distance = new int[land.Length];
            var queue = new int[land.Length];
            var head = 0;
            var tail = 0;

            for (var i = 0; i < land.Length; i++)
            {
                if (land[i])
                    distance[i] = -1;
                else
                    queue[tail++] = i;
            }

            // Land with no sea anywhere: every tile is as far inland as it can be.
            if (tail == 0)
            {
                Array.Fill(distance, width + height);
                return distance;
            }

            while (head < tail)
            {
                var index = queue[head++];
                var x = index % width;
                var y = index / width;
                var step = distance[index] + 1;

                if (x > 0) Visit(index - 1);
                if (x < width - 1) Visit(index + 1);
                if (y > 0) Visit(index - width);
                if (y < height - 1) Visit(index + width);

                void Visit(int neighbour)
                {
                    if (distance[neighbour] != -1)
                        return;

                    distance[neighbour] = step;
                    queue[tail++] = neighbour;
                }
            }

            return distance;
        }

    /// <summary>Whether piece <paramref name="a"/> beats piece <paramref name="b"/>.</summary>
    private static bool Stronger(int a, int b, List<int> sizes) =>
        sizes[a] > sizes[b] || (sizes[a] == sizes[b] && a < b);

    /// <summary>
    /// Every four-connected piece of land, numbered from 1, with how many tiles each has.
    /// Water is 0.
    /// </summary>
    private static (int[] Labels, List<int> Sizes) Label(bool[] land, int width, int height)
    {
        var labels = new int[land.Length];
        var sizes = new List<int> { 0 };
        var queue = new int[land.Length];

        for (var start = 0; start < land.Length; start++)
        {
            if (!land[start] || labels[start] != 0)
                continue;

            var piece = sizes.Count;
            var size = 0;

            var head = 0;
            var tail = 0;

            labels[start] = piece;
            queue[tail++] = start;

            while (head < tail)
            {
                var index = queue[head++];
                size++;

                var x = index % width;
                var y = index / width;

                foreach (var (dx, dy) in Steps)
                {
                    var nx = x + dx;
                    var ny = y + dy;

                    if (nx < 0 || ny < 0 || nx >= width || ny >= height)
                        continue;

                    var neighbour = (ny * width) + nx;

                    if (!land[neighbour] || labels[neighbour] != 0)
                        continue;

                    labels[neighbour] = piece;
                    queue[tail++] = neighbour;
                }
            }

            sizes.Add(size);
        }

        return (labels, sizes);
    }
}
