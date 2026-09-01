using System;

namespace NWorld.Generation.App.Generation;

/// <summary>
/// Takes the highest-scoring slice of a set of tiles: how every dial on the generation panels
/// is made to mean what it says.
/// <para>
/// The alternative is a threshold on the score, and it does not work. A field's values move
/// when any of its settings do, so a fixed threshold gives a different amount of land, or hill,
/// or marsh every time something else is nudged -- the dial reads as a percentage and behaves
/// like a suggestion. Sorting the scores and cutting at a <b>rank</b> instead means the figure
/// asked for is the figure delivered, whatever the rest of the panel is set to, and the other
/// dials are left free to decide only <em>where</em>.
/// </para>
/// <para>
/// The same shape is wanted in two ways. Whether a tile is in the slice at all is the question
/// the covers ask -- a tile is swamp or it is not. Where a tile sits <em>within</em> the slice
/// is the question the relief asks, since a mountain has to come out somewhere between 11 and
/// 30. So a cut answers both.
/// </para>
/// <para>
/// This is not what <see cref="ContinentBuilder"/> does, and the difference is worth knowing:
/// there, taking a rank is not enough, because filling the enclosed water afterwards adds land
/// that the cut did not choose. It searches for the cut instead. Nothing else has a rule that
/// changes the count after the fact, so nothing else needs to.
/// </para>
/// </summary>
internal readonly struct ScoreCut
{
    private readonly double[] _ranked;

    private ScoreCut(double[] ranked, double cut, int taken)
    {
        _ranked = ranked;
        Cut = cut;
        Taken = taken;
    }

    /// <summary>The score a tile has to reach to be in the slice.</summary>
    public double Cut { get; }

    /// <summary>How many tiles the slice holds. What was asked for, or all there was.</summary>
    public int Taken { get; }

    /// <summary>
    /// Sorts the eligible tiles by score and cuts the top <paramref name="wanted"/> of them.
    /// </summary>
    /// <param name="eligible">Which tiles are in the running. The rest are not scored against.</param>
    /// <param name="score">Every tile's score. Only the eligible entries are read.</param>
    /// <param name="wanted">How many to take. More than there are takes all of them.</param>
    public static ScoreCut Take(bool[] eligible, double[] score, int wanted)
    {
        ArgumentNullException.ThrowIfNull(eligible);
        ArgumentNullException.ThrowIfNull(score);

        var room = 0;

        for (var i = 0; i < eligible.Length; i++)
        {
            if (eligible[i])
                room++;
        }

        wanted = Math.Clamp(wanted, 0, room);

        if (wanted == 0)
        {
            // A cut above every possible score, so Takes says no to everything. That is what
            // makes a dial at zero mean none rather than one: the best tile would otherwise
            // tie its way in.
            return new ScoreCut([], double.MaxValue, 0);
        }

        var ranked = new double[room];
        var next = 0;

        for (var i = 0; i < score.Length; i++)
        {
            if (eligible[i])
                ranked[next++] = score[i];
        }

        Array.Sort(ranked);

        return new ScoreCut(ranked, ranked[room - wanted], wanted);
    }

    /// <summary>Whether a score is in the slice.</summary>
    public bool Takes(double score) => score >= Cut;

    /// <summary>
    /// Where a score sits inside the slice, from 0 at the bottom of it to
    /// <see cref="Taken"/> - 1 at the top.
    /// <para>
    /// Found by searching the sorted scores rather than carried alongside them, which costs a
    /// binary search per tile and saves building and sorting a second index over the whole
    /// map. Tiles that score exactly the same place together, which is the only honest answer
    /// for ground of the same height.
    /// </para>
    /// </summary>
    public int PlaceOf(double score)
    {
        var found = Array.BinarySearch(_ranked, score);
        var rank = found >= 0 ? found : ~found;

        return rank - (_ranked.Length - Taken);
    }
}
