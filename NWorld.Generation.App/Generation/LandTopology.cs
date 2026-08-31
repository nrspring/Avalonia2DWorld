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
