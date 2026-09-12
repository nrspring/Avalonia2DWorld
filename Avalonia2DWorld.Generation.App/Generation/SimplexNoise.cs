using System;

namespace Avalonia2DWorld.Generation.App.Generation;

/// <summary>
/// Simplex noise, summed over octaves.
/// <para>
/// Gradient noise rather than value noise, and that is the whole reason it is here.
/// Value noise interpolates between numbers sitting on a square lattice, and the lattice
/// shows: features line up with the axes and meet at right angles, which on a coastline
/// reads as something built rather than something eroded. Gradient noise interpolates
/// between <em>slopes</em> on a triangular grid, and has no direction it prefers.
/// </para>
/// <para>
/// Its own rather than the renderers', which tile to a period -- exactly what a one-off
/// landmass must not do.
/// </para>
/// </summary>
internal sealed class SimplexNoise
{
    /// <summary>Skew and unskew between the square grid this is sampled on and the
    /// triangular one it is built on.</summary>
    private static readonly double Skew = 0.5 * (Math.Sqrt(3.0) - 1.0);
    private static readonly double Unskew = (3.0 - Math.Sqrt(3.0)) / 6.0;

    /// <summary>
    /// The twelve gradient directions, as the edges of a cube. More than enough in two
    /// dimensions, and an established set: too few directions and the noise shows them.
    /// </summary>
    private static readonly int[,] Gradients =
    {
        { 1, 1 }, { -1, 1 }, { 1, -1 }, { -1, -1 },
        { 1, 0 }, { -1, 0 }, { 1, 0 }, { -1, 0 },
        { 0, 1 }, { 0, -1 }, { 0, 1 }, { 0, -1 },
    };

    /// <summary>
    /// A shuffle of 0..255, laid down twice so a lookup can add two indices without
    /// wrapping by hand.
    /// </summary>
    private readonly byte[] _permutation = new byte[512];

    public SimplexNoise(uint seed)
    {
        var order = new byte[256];
        for (var i = 0; i < 256; i++)
            order[i] = (byte)i;

        // Fisher-Yates from the seed, so the whole field is determined by it.
        var state = seed == 0 ? 1u : seed;
        for (var i = 255; i > 0; i--)
        {
            // xorshift: small, fast, and good enough to shuffle a table with.
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;

            var j = (int)(state % (uint)(i + 1));
            (order[i], order[j]) = (order[j], order[i]);
        }

        for (var i = 0; i < 512; i++)
            _permutation[i] = order[i & 255];
    }

    /// <summary>
    /// How many octaves a field can carry before its finest one drops below
    /// <paramref name="minFeatureTiles"/>.
    /// <para>
    /// This is what separates terrain from speckle, and it is the same arithmetic wherever a
    /// field is sampled against a feature size. Octaves double in frequency, so the finest of
    /// five spanning a twenty-six tile range has a wavelength under two tiles -- noise at the
    /// size of a tile, which sends neighbouring elevations to 1 and 13 and back, and leaves a
    /// mountain range looking like static rather than like ground somebody could walk up.
    /// </para>
    /// <para>
    /// Counted from the feature size rather than fixed, so a small feature gets fewer octaves.
    /// There is no room for four scales of detail inside a massif eight tiles across.
    /// </para>
    /// </summary>
    /// <param name="featureTiles">How many tiles across the coarsest octave runs.</param>
    /// <param name="minFeatureTiles">The finest wavelength to allow, in tiles.</param>
    /// <param name="maxOctaves">A ceiling, for fields that could otherwise ask for many.</param>
    public static int OctavesFor(double featureTiles, double minFeatureTiles, int maxOctaves) =>
        Math.Clamp(1 + (int)Math.Log2(featureTiles / minFeatureTiles), 1, maxOctaves);

    /// <summary>
    /// Octaves of noise, halving in weight and doubling in frequency. Roughly -1 to 1:
    /// the octaves rarely peak together, so the sum stays well inside its bounds, which
    /// is what makes a coast wander rather than lurch.
    /// </summary>
    public double Fbm(double x, double y, int octaves)
    {
        double sum = 0, amplitude = 1, frequency = 1, total = 0;

        for (var i = 0; i < octaves; i++)
        {
            sum += amplitude * Noise(x * frequency, y * frequency);
            total += amplitude;
            amplitude *= 0.5;
            frequency *= 2;
        }

        return sum / total;
    }

    /// <summary>
    /// Ridged noise, summed over octaves: 0 mostly, rising to 1 along sharp connected crests.
    /// <para>
    /// This is what makes a mountain range a range rather than a patch of high ground. Folding
    /// each octave about zero -- <c>1 - |n|</c> -- turns its zero crossings into creases, and
    /// the zero crossing of a smooth field is a <em>line</em>, so the high ground comes out
    /// strung along curves instead of pooled in blobs. Squaring sharpens the crease into a
    /// ridge.
    /// </para>
    /// <para>
    /// The weighting is what separates this from simply folding <see cref="Fbm"/>. Each octave
    /// is multiplied by how high the one above it stood, so detail only accumulates where a
    /// ridge already runs: spurs and side-valleys form along the main crest, and the flats
    /// between ranges stay flat instead of filling up with small crests of their own. Fold an
    /// ordinary sum of octaves instead and every octave creases independently, which gives a
    /// field of creases at every scale -- the blobby, evenly-rough terrain this replaced.
    /// </para>
    /// <para>
    /// Roughly 0 to 1 rather than -1 to 1, since a fold has no negative side.
    /// </para>
    /// </summary>
    public double RidgedFbm(double x, double y, int octaves)
    {
        double sum = 0, amplitude = 1, frequency = 1, total = 0;

        // How much the next octave counts for. Starts open, and from then on is whatever the
        // last octave left standing.
        var weight = 1.0;

        for (var i = 0; i < octaves; i++)
        {
            var crest = 1 - Math.Abs(Noise(x * frequency, y * frequency));
            crest *= crest;
            crest *= weight;

            // Doubled before clamping, so a middling crest still passes most of itself on and
            // only the flats shut the next octave out.
            weight = Math.Clamp(crest * 2, 0, 1);

            sum += crest * amplitude;
            total += amplitude;
            amplitude *= 0.5;
            frequency *= 2;
        }

        return sum / total;
    }

    /// <summary>
    /// One octave. The point sits inside a triangle of the skewed grid; each of the
    /// triangle's three corners contributes its own gradient, faded out by distance, and
    /// the three are summed.
    /// </summary>
    private double Noise(double x, double y)
    {
        // Into the skewed grid, where the triangles are right-angled and easy to find.
        var skew = (x + y) * Skew;
        var i = Floor(x + skew);
        var j = Floor(y + skew);

        // And back out, to get the offset from the corner in real coordinates.
        var unskew = (i + j) * Unskew;
        var x0 = x - (i - unskew);
        var y0 = y - (j - unskew);

        // Which half of the cell: lower triangle if the offset leans along x.
        var i1 = x0 > y0 ? 1 : 0;
        var j1 = x0 > y0 ? 0 : 1;

        var x1 = x0 - i1 + Unskew;
        var y1 = y0 - j1 + Unskew;
        var x2 = x0 - 1 + (2 * Unskew);
        var y2 = y0 - 1 + (2 * Unskew);

        var ii = i & 255;
        var jj = j & 255;

        var sum =
            Corner(x0, y0, _permutation[ii + _permutation[jj]] % 12) +
            Corner(x1, y1, _permutation[ii + i1 + _permutation[jj + j1]] % 12) +
            Corner(x2, y2, _permutation[ii + 1 + _permutation[jj + 1]] % 12);

        // The factor that brings the sum into -1..1; it falls out of the kernel and the
        // gradient lengths, and is the usual one for this formulation.
        return 70.0 * sum;
    }

    /// <summary>
    /// One corner's contribution: its gradient dotted with the offset to it, faded by a
    /// radial kernel that reaches zero before the next triangle begins -- which is what
    /// lets three corners be summed with no seam between cells.
    /// </summary>
    private static double Corner(double dx, double dy, int gradient)
    {
        var falloff = 0.5 - (dx * dx) - (dy * dy);
        if (falloff < 0)
            return 0;

        falloff *= falloff;

        return falloff * falloff * ((Gradients[gradient, 0] * dx) + (Gradients[gradient, 1] * dy));
    }

    /// <summary>Floor as an int, which is not what a cast does for negatives.</summary>
    private static int Floor(double value)
    {
        var truncated = (int)value;
        return value < truncated ? truncated - 1 : truncated;
    }
}
