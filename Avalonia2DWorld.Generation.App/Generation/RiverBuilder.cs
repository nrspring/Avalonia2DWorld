using System;
using System.Collections.Generic;
using Avalonia2DWorld.MapServices.Constants;

namespace Avalonia2DWorld.Generation.App.Generation;

/// <summary>
/// What the Rivers panel asks for.
/// </summary>
/// <param name="Count">
/// How many rivers to add to whatever is already there. A target rather than a promise: a
/// source that cannot reach the sea, or that only ever makes a short river, is passed over,
/// and so is one too near water already on the map -- so a world with little high ground left
/// unclaimed comes back with fewer, and eventually with none.
/// </param>
/// <param name="Winding">
/// From 0 (straight for the sea) to 1 (wandering all over the country). It is the only dial
/// that changes the shape of a river rather than how many there are or how long.
/// </param>
/// <param name="MinLength">
/// The shortest river worth drawing, in tiles. A source that cannot make one at least this
/// long is passed over for one further inland.
/// </param>
/// <param name="Seed">Makes a run repeatable.</param>
public readonly record struct RiverSettings(int Count, double Winding, int MinLength, int Seed);

/// <summary>
/// Runs rivers down off the high ground to the sea, as a cover per tile in reading order.
/// <para>
/// A river is a cover and not a height. It takes the elevation of the tile it replaces --
/// nothing here digs a channel, and a river crossing the flat land at one and coming down
/// through hills at six is at six up there and one down here. That is what lets the panel be
/// pressed over a finished world: the relief and the coastline both come back exactly as they
/// were handed in, so every pass that drew them still holds.
/// </para>
/// <para>
/// Each river is a <b>route</b> rather than a walk downhill, found by cheapest path from a
/// source to the sea. A walk was the obvious thing and it does not work on this map: settled
/// hill country falls away to flat land at elevation one, and once a walk is out on the flat
/// there is no lower tile anywhere and it stops in the middle of a plain. A route asks the
/// other question -- what is the least bad way from here to the water -- and there is always
/// an answer to that, so a river that starts always arrives.
/// </para>
/// <para>
/// What makes the route a river rather than a line is what the steps cost. Climbing is dear
/// and running level is cheap, so a route falls where it can and only ever climbs where
/// nothing else will do -- the saddle between two hills, which is where a river crosses one
/// anyway. Laid over that is a noise field the winding dial weights: it costs nothing in
/// particular to go through one part of the country rather than the next, so the route bends
/// around the expensive ground, and a bend that has no reason behind it is a meander.
/// </para>
/// <para>
/// Rivers run into each other. A route stops at the sea or at a river already drawn, whichever
/// it reaches first, so the second river down a valley arrives as a tributary of the first
/// instead of running alongside it to the coast. That also makes the pass order-dependent in
/// the small way the cover passes are: which one is the trunk is which one was drawn first.
/// </para>
/// <para>
/// Nothing here removes a river it was given. A pass adds to what is on the map, the way
/// islands are added to a coastline and unlike the way swamps replace swamps -- so the panel
/// can be pressed again for another set, and the set it adds sees everything already there.
/// New sources keep clear of the water that is already down, and new routes end at it, which
/// is what makes a second press grow the systems rather than lay a second map of rivers over
/// the first.
/// </para>
/// <para>
/// The one thing a pass may take back from an older river is width, and only where a new one
/// runs up against it -- see <see cref="Thin"/>. Water that is already there is held to the
/// same terms as a route being drawn now: it goes only if the channel is too broad without it
/// and the river stays joined up.
/// </para>
/// <para>
/// They widen going down, from <see cref="NarrowWidth"/> along nearly the whole of their
/// length to <see cref="BroadWidth"/> at the mouth -- see <see cref="BroadFrom"/>. Which way a
/// river is flowing is the one thing about it you can see from directly overhead, and this is
/// the whole of how it is said; it is deliberately a small tell rather than a broad one, since
/// a river that spends much of its length at its full width reads as an estuary.
/// </para>
/// <para>
/// <see cref="BroadWidth"/> is a cap on the <b>water</b> and not merely on the square that is
/// stamped, which is a stronger promise and a necessary one. A winding route bends back within
/// a tile or two of itself often enough to matter, and two two-wide runs laid over the same
/// ground are a pool rather than a river -- before this was measured on the map rather than
/// assumed from the stamp, the worst of it came out twenty-six tiles across.
/// </para>
/// <para>
/// What comes out is lowland, every tile of it, by construction: sources are hills and no
/// route or bank ever touches a mountain. That is what lets a river be an ordinary cover
/// rather than a special case -- the rule that a range raised over a marsh takes the marsh
/// with it needs no exception written into it for rivers, because a river was never up there
/// to begin with.
/// </para>
/// </summary>
public static class RiverBuilder
{
    /// <summary>The four ways a river may run. The same four everything else on this map moves in.</summary>
    private static readonly (int X, int Y)[] Steps = [(1, 0), (-1, 0), (0, 1), (0, -1)];

    /// <summary>
    /// How wide a river is for nearly all of its length, and the most it ever reaches.
    /// <para>
    /// Two and three. One tile of water reads as a ditch rather than a river, so two is the
    /// floor; three is the ceiling because a river is a line on a map of this scale and a
    /// broad one stops reading as one -- it becomes an inlet, or a lake with ends.
    /// </para>
    /// <para>
    /// The width is stamped as a square of the given side, laid off-centre by half a tile
    /// where the side is even, so a river two across is two and not one or three.
    /// </para>
    /// </summary>
    private const int NarrowWidth = 2;

    /// <inheritdoc cref="NarrowWidth"/>
    private const int BroadWidth = 3;

    /// <summary>
    /// How far down its route a river has to be before it takes its full width.
    /// <para>
    /// Late, and that is the point. Widening evenly from source to mouth splits a river's
    /// length between the two widths and the broad half is what the eye reads, so the whole
    /// river looks broad. Held back to the last stretch instead, a river is two tiles across
    /// for four fifths of the way down and only opens out where it meets the sea -- which is
    /// both what one does and what keeps three from becoming the width of the map's rivers.
    /// </para>
    /// </summary>
    private const double BroadFrom = 0.85;

    /// <summary>
    /// How far above the route the water may spread sideways, in elevation.
    /// <para>
    /// One. The route falls, but the width around it is stamped rather than chosen, so
    /// without this a river at the foot of a range takes half its width up the mountainside
    /// and its surface reads twenty units across. A river is level across and falls along; a
    /// step up out of it is the bank.
    /// </para>
    /// </summary>
    private const int MaxBankRise = 1;

    /// <summary>
    /// What one step of climb costs, against a level step's one.
    /// <para>
    /// High enough that a route will go a long way round rather than climb, and not so high
    /// that it will not climb at all: a saddle between two hills is a step or two up and a
    /// river does cross one. This is the whole of "generally not uphill" -- it is a price and
    /// not a prohibition, which is why a source ringed by high ground still gets a river out.
    /// </para>
    /// </summary>
    private const double ClimbCost = 60;

    /// <summary>
    /// What running downhill is worth, per step of drop. A small credit rather than a free
    /// ride: it tips the route towards the fall line where nothing else separates two ways
    /// on, without letting a river throw itself off the nearest hill and ignore the winding.
    /// </summary>
    private const double FallCredit = 0.35;

    /// <summary>
    /// The most the winding field can add to one step, at the dial's full setting. Measured
    /// against the base step of one, so at full winding crossing the country in the wrong
    /// place costs many times what following the channel does -- which is what makes it worth
    /// a route's while to go the long way round.
    /// </summary>
    private const double WindingCost = 30;

    /// <summary>
    /// How many tiles across one bend of the winding field runs, and the octaves it gets.
    /// <para>
    /// Broad, deliberately. A fine field is gravel for a route to pick between and comes out
    /// as jitter -- a river a tile wide wobbling every other step, which reads as a bad line
    /// rather than as a meander. A bend has to be bigger than the river is wide before it
    /// looks like a bend.
    /// </para>
    /// </summary>
    private const double WindingScale = 22;

    /// <inheritdoc cref="WindingScale"/>
    private const int WindingOctaves = 3;

    /// <summary>
    /// How far apart two sources have to be, in tiles, and how many candidates are tried
    /// before the pass gives up on a river.
    /// <para>
    /// Spaced, or every river on the map starts on the same hill: the highest ground is one
    /// place, and the top few hundred tiles of a ranking are all on it.
    /// </para>
    /// </summary>
    private const int SourceSpacing = 24;

    /// <inheritdoc cref="SourceSpacing"/>
    private const int MaxAttempts = 400;

    /// <summary>
    /// Runs the rivers and returns the covers as they now stand.
    /// </summary>
    /// <param name="relief">Elevation per tile. Read and never written -- a river digs nothing.</param>
    /// <param name="cover">
    /// The covers as they stand, which the pass builds on: swamp and desert are left exactly
    /// where they are, and a river simply runs over whichever it crosses. Left untouched --
    /// what comes back is a copy. Null, or the wrong size, starts from grass.
    /// </param>
    /// <param name="made">
    /// How many rivers this pass added, which is not always how many were asked for.
    /// <para>
    /// Reported rather than left to be inferred from the map, because it cannot be inferred
    /// from the map: rivers join, so two that meet are one shape on the ground and counting
    /// the shapes would say one. It is also the only way the panel can tell somebody that a
    /// length they have asked for is longer than their world can manage -- which otherwise
    /// shows up as a button that appears to do nothing.
    /// </para>
    /// </param>
    public static GroundCover[] Add(
        int width, int height, int[] relief, GroundCover[] cover, RiverSettings settings,
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

        var winding = Math.Clamp(settings.Winding, 0, 1);
        var minLength = Math.Max(1, settings.MinLength);

        var meander = Meander(width, height, settings.Seed);
        var sources = Sources(width, height, relief);

        // Which tiles a river is already on, which is both what a later route may finish at
        // and what it must not start on. Seeded from the map rather than started empty: a
        // river laid by an earlier press is a river, and this pass has to see it as one.
        var drawn = new bool[relief.Length];

        // And which of those may not simply be thinned away -- the routes this pass draws,
        // and every tile of every river already on the map. An older river has no route left
        // to point at, so all of it is treated as one; the alternative is a second press
        // quietly eating the first press's work.
        var spine = new bool[relief.Length];

        // What an earlier press left. These are read as water by everything below and are
        // written by nothing: a press adds, the way scattering islands adds, and a river that
        // is already on the map is not this pass's to edit.
        var kept = new bool[relief.Length];

        for (var i = 0; i < run.Length; i++)
        {
            if (run[i] != GroundCover.River)
                continue;

            drawn[i] = true;
            kept[i] = true;
        }

        var tried = 0;

        // How far from any source already used, so the next one is somewhere else on the map.
        var taken = new List<int>();

        foreach (var source in sources)
        {
            if (made >= wanted || tried >= MaxAttempts)
                break;

            if (drawn[source] || Crowded(source, taken, width) || NearWater(source, drawn, width, height))
                continue;

            tried++;

            if (Route(relief, drawn, kept, meander, winding, width, height, source) is not { } route)
                continue;

            if (route.Count < minLength)
                continue;

            Stamp(run, drawn, spine, kept, relief, route, width, height);

            taken.Add(source);
            made++;
        }

        Thin(run, drawn, spine, kept, width, height);
        Strand(run, relief, kept, width, height);

        return run;
    }

    /// <summary>
    /// Takes back any water that leaves a channel broader than <see cref="BroadWidth"/>, and
    /// keeps taking it until none is left.
    /// <para>
    /// The cap is applied as the water goes down as well, but that pass is greedy and cannot
    /// see what is coming: a tile laid where the channel was two across can find itself in the
    /// middle of four once a later stretch of the same river has been drawn beside it. Only a
    /// pass over the finished water knows how wide the water finished up.
    /// </para>
    /// <para>
    /// Width before route, and by two different tests. The tiles either side of a route are
    /// decoration: the route runs source to sea and holds the river together on its own, so
    /// any number of them can go without breaking anything and none of them is asked about.
    /// Route tiles are asked -- a tile is only taken if the water around it stays joined up
    /// without it, which is the classic test for a pixel a thinning may remove.
    /// </para>
    /// <para>
    /// Route tiles have to be on the table at all because of where the last of the width comes
    /// from. Two strands of one river passing corner to corner, or a tributary meeting its
    /// trunk, put four route tiles across a channel, and no amount of taking decoration away
    /// will narrow that. It is a handful of tiles on a map -- but a cap with a handful of
    /// exceptions is not a cap, and the test that closes it is cheap because it only ever runs
    /// on the handful.
    /// </para>
    /// <para>
    /// Water an earlier press laid is never touched, whatever it costs in width. A pass adds,
    /// and a pass that quietly narrowed the rivers already on the map would not be adding --
    /// it would be editing work somebody had already accepted. Where a new river runs up
    /// against an old one there is therefore ground the cap cannot reach, and the new river's
    /// own tiles are given up first to make room; what is left over is a junction, which is
    /// the one place on a map a channel is allowed to look broad.
    /// </para>
    /// <para>
    /// Repeated until a sweep takes nothing, since narrowing a channel in one place can bring
    /// the tile beside it back inside the cap and there is no sense taking that one too.
    /// </para>
    /// </summary>
    private static void Thin(
        GroundCover[] cover, bool[] drawn, bool[] spine, bool[] kept, int width, int height)
    {
        var taken = true;

        while (taken)
        {
            taken = false;

            for (var index = 0; index < cover.Length; index++)
            {
                if (kept[index] || cover[index] != GroundCover.River)
                    continue;

                if (!Exceeds(cover, index, width, height))
                    continue;

                if (spine[index] && !Spare(cover, index, width, height))
                    continue;

                cover[index] = GroundCover.Grass;
                drawn[index] = false;
                spine[index] = false;
                taken = true;
            }
        }
    }

    /// <summary>
    /// Takes back any water this pass laid that ended up joined to nothing that reaches the
    /// sea.
    /// <para>
    /// <see cref="Thin"/> asks its question of one tile at a time and answers it against the
    /// map as it stands, which is not quite enough: two tiles can each be safe to take on
    /// their own and strand a third between them once both are gone. The stranded piece is
    /// almost always a single tile, and always small, but a puddle in the hills with no river
    /// attached to it is exactly the thing this whole file exists not to draw.
    /// </para>
    /// <para>
    /// Simply dropped rather than repaired. What is left after a thinning has taken the wrong
    /// tile is not a river that wants mending -- it is a few tiles of water with no route
    /// under them, and the honest thing is for them not to be there.
    /// </para>
    /// <para>
    /// Water from an earlier press is never dropped, and cannot need to be: it is never taken
    /// either, so whatever reached the sea before this pass still does.
    /// </para>
    /// </summary>
    private static void Strand(
        GroundCover[] cover, int[] relief, bool[] kept, int width, int height)
    {
        var seen = new bool[cover.Length];
        var piece = new List<int>();
        var queue = new int[cover.Length];

        for (var start = 0; start < cover.Length; start++)
        {
            if (cover[start] != GroundCover.River || seen[start])
                continue;

            piece.Clear();

            var head = 0;
            var tail = 0;
            var reaches = false;
            var older = false;

            seen[start] = true;
            queue[tail++] = start;

            while (head < tail)
            {
                var index = queue[head++];
                piece.Add(index);
                older |= kept[index];

                var x = index % width;
                var y = index / width;

                foreach (var (dx, dy) in Steps)
                {
                    var nx = x + dx;
                    var ny = y + dy;

                    if (nx < 0 || ny < 0 || nx >= width || ny >= height)
                        continue;

                    var neighbour = (ny * width) + nx;

                    if (relief[neighbour] <= Elevations.Sea)
                        reaches = true;

                    if (cover[neighbour] != GroundCover.River || seen[neighbour])
                        continue;

                    seen[neighbour] = true;
                    queue[tail++] = neighbour;
                }
            }

            if (reaches || older)
                continue;

            foreach (var index in piece)
                cover[index] = GroundCover.Grass;
        }
    }

    /// <summary>
    /// Whether the water would still be joined up without this tile: every neighbour of it
    /// that is water can still be reached from every other, by water, without going through
    /// it.
    /// <para>
    /// Asked inside the five-by-five square around the tile and no further. A detour that
    /// leaves it has left the channel, and a river narrow enough for this question to arise is
    /// narrow enough that the answer is always inside the window -- so the test stays a fixed
    /// cost and cannot walk the whole map for one tile.
    /// </para>
    /// <para>
    /// Four-way, like everything else here. Eight-way would let a tile go whose water is only
    /// joined across a corner afterwards, and a river joined across a corner is two rivers as
    /// far as this map is concerned -- the same rule <see cref="LandTopology"/> applies to the
    /// coast, applied to the water.
    /// </para>
    /// </summary>
    private static bool Spare(GroundCover[] cover, int index, int width, int height)
    {
        var cx = index % width;
        var cy = index / width;

        // The window, with the tile in question taken out of it.
        var open = new bool[5, 5];
        var start = (-1, -1);

        for (var dy = -2; dy <= 2; dy++)
        {
            for (var dx = -2; dx <= 2; dx++)
            {
                var x = cx + dx;
                var y = cy + dy;

                if (x < 0 || y < 0 || x >= width || y >= height) continue;
                if (dx == 0 && dy == 0) continue;
                if (cover[(y * width) + x] != GroundCover.River) continue;

                open[dx + 2, dy + 2] = true;
            }
        }

        // What has to stay reachable: the water immediately touching the tile.
        var wanted = 0;

        foreach (var (dx, dy) in Steps)
        {
            if (!open[dx + 2, dy + 2]) continue;

            wanted++;
            if (start.Item1 < 0) start = (dx + 2, dy + 2);
        }

        // Nothing touching it, so nothing to disconnect.
        if (wanted <= 1)
            return true;

        // Flood the window from one of them and see how many of the rest it finds.
        var seen = new bool[5, 5];
        var stack = new Stack<(int X, int Y)>();

        seen[start.Item1, start.Item2] = true;
        stack.Push(start);

        var found = 0;

        while (stack.Count > 0)
        {
            var (x, y) = stack.Pop();

            if (Math.Abs(x - 2) + Math.Abs(y - 2) == 1)
                found++;

            foreach (var (dx, dy) in Steps)
            {
                var nx = x + dx;
                var ny = y + dy;

                if (nx < 0 || ny < 0 || nx > 4 || ny > 4) continue;
                if (!open[nx, ny] || seen[nx, ny]) continue;

                seen[nx, ny] = true;
                stack.Push((nx, ny));
            }
        }

        return found == wanted;
    }

    /// <summary>
    /// Where a river might start, best first: the tops of the hills, far from the sea.
    /// <para>
    /// The hills and not the mountains, which is the single decision that makes these look
    /// like rivers. A river takes the height of the ground it runs over rather than cutting a
    /// bed, so its surface is only ever as even as the country it crosses -- and a range is
    /// not even. A river four tiles wide started on a crag comes out with a twenty-unit step
    /// across its own width, which is a waterfall drawn sideways. Settled hill country changes
    /// by a unit a step in every direction, so a river laid over it lies almost flat across
    /// and falls steadily along, which is what one does. The foot of a range is where a river
    /// starts on a real map too, and for the same reason: it is the first place the water
    /// gathers rather than runs off.
    /// </para>
    /// <para>
    /// Far from the sea as the tie-break, because either measure alone picks the wrong place.
    /// Height alone puts every source on whichever upland is highest, and on this map that is
    /// as often a headland as an interior watershed -- a river off one is a brook. Distance
    /// alone puts them in the middle of whatever plain is widest, which is a spring rather
    /// than a source. High and inland together is a watershed.
    /// </para>
    /// </summary>
    private static List<int> Sources(int width, int height, int[] relief)
    {
        var land = new bool[relief.Length];
        for (var i = 0; i < relief.Length; i++)
            land[i] = relief[i] > 0;

        var sea = LandTopology.DistanceFromSea(land, width, height);

        var candidates = new List<int>();

        for (var i = 0; i < relief.Length; i++)
        {
            // Hills only. Not the flat land, where a source is a route across a plain rather
            // than a river; not the mountains, for the reason above.
            if (relief[i] >= Elevations.HillsFrom && relief[i] <= Elevations.HillsTo)
                candidates.Add(i);
        }

        candidates.Sort((a, b) =>
        {
            var byHeight = relief[b].CompareTo(relief[a]);
            if (byHeight != 0)
                return byHeight;

            var byInland = sea[b].CompareTo(sea[a]);
            return byInland != 0 ? byInland : a.CompareTo(b);
        });

        return candidates;
    }

    /// <summary>The four lines a channel's width may be measured along.</summary>
    private static readonly (int X, int Y)[] Axes = [(1, 0), (0, 1), (1, 1), (1, -1)];

    /// <summary>
    /// Whether the channel at a tile is broader than <see cref="BroadWidth"/>, counting that
    /// tile as water whether it is yet or not.
    /// <para>
    /// A channel's width at a tile is the shortest unbroken run of water through it, measured
    /// along all four lines -- across, down, and both diagonals. The shortest way through a
    /// channel is across it, whichever way the water happens to be going, so the measure needs
    /// no idea of the flow direction and can be asked tile by tile while the river is still
    /// being drawn.
    /// </para>
    /// <para>
    /// All four and not merely the two, which was wrong and quietly so. A river running
    /// diagonally crosses the grid as a staircase, and a staircase two tiles wide has runs of
    /// three and four along the rows and columns even though the water is two across -- so
    /// measuring on the axes alone called two-thirds of a well-drawn river too wide, and the
    /// cap built on it did nothing at all. The diagonal run is the true width there.
    /// </para>
    /// </summary>
    private static bool Exceeds(GroundCover[] cover, int index, int width, int height)
    {
        var x = index % width;
        var y = index / width;

        foreach (var (dx, dy) in Axes)
        {
            // Counting this tile as water either way, so that the same measure answers both
            // "how wide is this" and "how wide would this be".
            var run = 1
                + Reach(cover, x, y, dx, dy, width, height)
                + Reach(cover, x, y, -dx, -dy, width, height);

            if (run <= BroadWidth)
                return false;
        }

        return true;
    }

    /// <summary>How far the water runs unbroken from a tile in one direction.</summary>
    private static int Reach(
        GroundCover[] cover, int x, int y, int dx, int dy, int width, int height)
    {
        var run = 0;

        for (var step = 1; ; step++)
        {
            var nx = x + (dx * step);
            var ny = y + (dy * step);

            if (nx < 0 || ny < 0 || nx >= width || ny >= height)
                break;

            if (cover[(ny * width) + nx] != GroundCover.River)
                break;

            run++;
        }

        return run;
    }

    /// <summary>
    /// Whether a source sits too near water that is already on the map.
    /// <para>
    /// The same clearance two new sources are held to, applied to the rivers a previous press
    /// left behind. Without it a second press starts where the first one did -- the sources
    /// come off the relief and the relief has not moved -- and re-runs the same rivers a tile
    /// to one side of themselves. With it, each press works its way into country the last one
    /// did not reach, and eventually finds nowhere left and says so.
    /// </para>
    /// </summary>
    private static bool NearWater(int source, bool[] drawn, int width, int height)
    {
        var x = source % width;
        var y = source / width;

        for (var ny = Math.Max(0, y - SourceSpacing); ny <= Math.Min(height - 1, y + SourceSpacing); ny++)
        {
            for (var nx = Math.Max(0, x - SourceSpacing); nx <= Math.Min(width - 1, x + SourceSpacing); nx++)
            {
                if (drawn[(ny * width) + nx])
                    return true;
            }
        }

        return false;
    }

    /// <summary>Whether a source sits too near one already used.</summary>
    private static bool Crowded(int source, List<int> taken, int width)
    {
        var x = source % width;
        var y = source / width;

        foreach (var other in taken)
        {
            var dx = Math.Abs((other % width) - x);
            var dy = Math.Abs((other / width) - y);

            if (dx < SourceSpacing && dy < SourceSpacing)
                return true;
        }

        return false;
    }

    /// <summary>
    /// The cost of crossing each tile, before anything is asked about height. Runs 0 to 1, and
    /// is near zero along a set of winding lines that cross the whole map.
    /// <para>
    /// The distance from a noise field's <b>zero</b>, and not the field itself, and the whole
    /// of the winding is in that difference. A plain field is blobs: crossing one costs about
    /// the same whichever way you go over it, so a route weighs a detour against a saving that
    /// is not there and comes out straight however the dial is turned. Folding the field about
    /// zero instead makes the cheap ground a <em>filament</em> -- the zero contour of a smooth
    /// field is a line, and a smooth field's line wanders -- and following one of those costs
    /// nothing while cutting across them costs everything.
    /// </para>
    /// <para>
    /// So the meander is not a route being nudged off its course a step at a time. It is a
    /// river finding a channel that was already winding and staying in it, which is both what
    /// produces a real bend and why the shape survives being asked to run downhill at the same
    /// time.
    /// </para>
    /// </summary>
    private static double[] Meander(int width, int height, int seed)
    {
        var noise = new SimplexNoise(unchecked((uint)seed * 2654435761u) + 0x9E37u);
        var frequency = 1.0 / WindingScale;

        var field = new double[width * height];

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                field[(y * width) + x] = Math.Abs(
                    noise.Fbm(x * frequency, y * frequency, WindingOctaves));
            }
        }

        return field;
    }

    /// <summary>
    /// The cheapest way from <paramref name="source"/> to the sea, or to a river already
    /// drawn, as the tiles it runs through in order. Null if there is no way to either.
    /// <para>
    /// Dijkstra rather than a walk, for the reason given on the class: on flat land a walk has
    /// nowhere lower to go and stops. The cost of entering a tile is a level step, plus what
    /// climbing into it costs and less what falling into it is worth, plus the winding field
    /// at whatever weight the dial gives it. All of those are positive in total -- the fall
    /// credit is well under the base step -- so the search is sound.
    /// </para>
    /// </summary>
    private static List<int>? Route(
        int[] relief, bool[] drawn, bool[] kept, double[] meander, double winding,
        int width, int height, int source)
    {
        var spent = new double[relief.Length];
        var from = new int[relief.Length];
        var settled = new bool[relief.Length];

        Array.Fill(spent, double.MaxValue);
        Array.Fill(from, -1);

        var frontier = new PriorityQueue<int, double>();

        spent[source] = 0;
        frontier.Enqueue(source, 0);

        var mouth = -1;

        while (frontier.TryDequeue(out var index, out var cost))
        {
            if (settled[index])
                continue;

            settled[index] = true;

            // The sea, or a river that is already there. Either is where this one ends.
            if (index != source && (relief[index] <= Elevations.Sea || drawn[index]))
            {
                mouth = index;
                break;
            }

            var x = index % width;
            var y = index / width;

            foreach (var (dx, dy) in Steps)
            {
                var nx = x + dx;
                var ny = y + dy;

                if (nx < 0 || ny < 0 || nx >= width || ny >= height)
                    continue;

                var neighbour = (ny * width) + nx;

                if (settled[neighbour])
                    continue;

                // Never into the mountains. Not a price like climbing is, but a wall: a river
                // is a lowland cover like the swamps and the deserts, and ground raised out of
                // the lowlands loses whatever was on it -- so a route that took a shortcut
                // over a range would have its middle cut out the moment it was drawn. Barring
                // it here instead means a source walled in by mountains simply fails and the
                // pass moves on to the next one, which is the honest outcome and one the
                // count dial already promises.
                if (relief[neighbour] >= Elevations.MountainsFrom)
                    continue;

                // Nor alongside a river an earlier press laid. Meeting one is the whole point
                // -- that is a tributary, and the search stops there -- but running beside one
                // is two rivers a tile apart, which is a channel too broad to be either. The
                // thinning cannot fix it afterwards, since half the width belongs to a river
                // this pass is not allowed to edit, so it is kept from happening here.
                if (!kept[neighbour] && Beside(kept, neighbour, width, height))
                    continue;

                var reached = cost + Step(relief, meander, winding, index, neighbour);

                if (reached >= spent[neighbour])
                    continue;

                spent[neighbour] = reached;
                from[neighbour] = index;
                frontier.Enqueue(neighbour, reached);
            }
        }

        if (mouth < 0)
            return null;

        var route = new List<int>();

        for (var index = mouth; index >= 0; index = from[index])
            route.Add(index);

        // Walked back from the mouth, so it comes out mouth-first; the stamp wants it the way
        // the water runs, which is what makes the width grow rather than shrink.
        route.Reverse();

        return route;
    }

    /// <summary>Whether a tile touches, corner or edge, water that was already on the map.</summary>
    private static bool Beside(bool[] kept, int index, int width, int height)
    {
        var x = index % width;
        var y = index / width;

        for (var dy = -1; dy <= 1; dy++)
        {
            for (var dx = -1; dx <= 1; dx++)
            {
                var nx = x + dx;
                var ny = y + dy;

                if (nx < 0 || ny < 0 || nx >= width || ny >= height)
                    continue;

                if (kept[(ny * width) + nx])
                    return true;
            }
        }

        return false;
    }

    /// <summary>What it costs to run from one tile into the next.</summary>
    private static double Step(
        int[] relief, double[] meander, double winding, int index, int neighbour)
    {
        // The sea is the destination and costs nothing to arrive at. Charging for it would
        // have a route prefer a longer way round to a cheaper stretch of coast.
        if (relief[neighbour] <= Elevations.Sea)
            return 0;

        var rise = relief[neighbour] - relief[index];

        var cost = 1.0
            + (rise > 0 ? rise * ClimbCost : rise * FallCredit)
            + (meander[neighbour] * winding * WindingCost);

        // The credit for a long fall could in principle outrun the step; it must not, or the
        // search is no longer looking for the cheapest anything.
        return Math.Max(0.01, cost);
    }

    /// <summary>
    /// Lays the water down along a route, widening from <see cref="NarrowWidth"/> at the
    /// source to <see cref="BroadWidth"/> at the mouth.
    /// <para>
    /// A square of the run's width at each tile of the route rather than a disc. A disc cannot
    /// be an even number of tiles across -- it is symmetric about a centre tile, so it comes
    /// out odd whatever radius it is given -- and the widths wanted here are two and four. A
    /// square can be laid off-centre by half a tile, so two across is two.
    /// </para>
    /// <para>
    /// The water does not climb its own bank -- see <see cref="MaxBankRise"/> -- and this is
    /// what keeps the width from undoing the care taken over the route. The route is chosen to
    /// fall; the stamp around it is not chosen at all, so a river running along the foot of a
    /// range would otherwise put two of its four tiles up the mountainside and stand twenty
    /// units above the other two. Held to the valley floor instead, a river narrows where the
    /// ground closes in on it and takes its full width where there is room, which is both
    /// truer and the only way its surface stays level across.
    /// </para>
    /// </summary>
    private static void Stamp(
        GroundCover[] cover, bool[] drawn, bool[] spine, bool[] kept, int[] relief,
        List<int> route, int width, int height)
    {
        for (var step = 0; step < route.Count; step++)
        {
            var along = route.Count <= 1 ? 1d : (double)step / (route.Count - 1);

            // A step rather than a ramp, there being only the two widths to move between.
            var run = along < BroadFrom ? NarrowWidth : BroadWidth;

            var x = route[step] % width;
            var y = route[step] / width;

            // An even width has no centre tile, so it reaches one further one way than the
            // other. Which way is fixed rather than chosen, or the bank would wander.
            var back = (run - 1) / 2;
            var forward = run / 2;

            // The route itself always, whatever it costs in width. A river with a hole in it
            // is not a river, and the checks below are about how broad the water is rather
            // than whether it is joined up.
            Lay(route[step], route[step]);
            spine[route[step]] = true;

            for (var ny = y - back; ny <= y + forward; ny++)
            {
                for (var nx = x - back; nx <= x + forward; nx++)
                {
                    if (nx < 0 || ny < 0 || nx >= width || ny >= height)
                        continue;

                    Lay((ny * width) + nx, route[step]);
                }
            }

            void Lay(int index, int centre)
            {
                // The sea is already water and is not a river. A route ends at the coast, so
                // without this every mouth would push a square of river out into the ocean --
                // and a river tile at elevation zero is a contradiction besides.
                if (relief[index] <= Elevations.Sea || drawn[index])
                    return;

                // The bank. Water does not run up it, so the river takes what width the
                // ground gives it and no more.
                if (relief[index] > relief[centre] + MaxBankRise)
                    return;

                // Nor does the width crowd an older river, for the reason the route does not:
                // what this pass lays beside water it may not edit can never be thinned back
                // to a sensible channel. The route is exempt -- it has to be able to arrive.
                if (index != centre && Beside(kept, index, width, height) && !kept[index])
                    return;

                // And the cap, asked of the water rather than of the square being stamped.
                // The square is only ever two or three across, but a route that bends back
                // within a couple of tiles of itself stamps twice over the same ground and
                // the two runs merge into a pool -- which on a winding river is not rare. So
                // the width is measured where it actually shows, on the map, and a tile that
                // would push the channel past the cap simply is not laid.
                if (index != centre && Exceeds(cover, index, width, height))
                    return;

                cover[index] = GroundCover.River;
                drawn[index] = true;
            }
        }
    }
}
