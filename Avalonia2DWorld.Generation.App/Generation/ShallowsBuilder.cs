using System;
using Avalonia2DWorld.MapServices.Constants;

namespace Avalonia2DWorld.Generation.App.Generation;

/// <summary>
/// What the Base Land panel asks for when it asks for shallows.
/// </summary>
/// <param name="Reach">
/// How far out from the land the shallows go, in tiles, before the bottom drops away. An
/// average rather than a rule -- see <paramref name="Variation"/>.
/// </param>
/// <param name="Variation">
/// How much that reach wanders along the coast, from 0 to 1. At nothing the shallows are a
/// ribbon of one width all the way round; at full they run from nothing at all to twice the
/// reach, so the sea comes close in at one headland and stays far out across the next bay.
/// </param>
/// <param name="Seed">Makes a run repeatable.</param>
public readonly record struct ShallowSettings(double Reach, double Variation, int Seed);

/// <summary>
/// Marks the sea near the land as shallow, leaving the rest of it deep.
/// <para>
/// Depth is drawn and not modelled. There is no seabed under this map -- the sea is one
/// elevation, zero, and always will be -- so what a shallow is here is a tile of water near
/// enough to the shore that a map would draw it paler. That is the whole of it, and it is
/// enough: a coastline reads as a coastline because of the band of light water around it, and
/// a map without one reads as a continent cut out and dropped on a dark floor.
/// </para>
/// <para>
/// The band is a distance from the land, flooded outwards from every shore at once, which
/// makes it a fact about the coast rather than about any one landmass. An island gets its own
/// shelf; a strait narrower than twice the reach is shallow all the way across, which is what
/// a strait is.
/// </para>
/// <para>
/// The reach wanders. A shelf of one width the whole way round a continent is the one thing
/// that would give this away as a distance transform rather than a sea, so a broad noise field
/// runs along the coast and moves the drop-off in and out -- see <see cref="ShallowSettings"/>.
/// The field is sampled where the <b>water</b> is rather than where the land is, so the
/// wandering belongs to the sea and a bay does not inherit the shape of the headland beside it.
/// </para>
/// <para>
/// Nothing here moves a coastline, and nothing here is land. The mask it is given comes back
/// untouched in every respect that matters to the passes above: what was sea is still sea, and
/// what was land was never asked about except to measure from.
/// </para>
/// </summary>
public static class ShallowsBuilder
{
    /// <summary>
    /// How many tiles across one swell of the wandering runs, and the octaves it gets.
    /// <para>
    /// Broad, so the drop-off moves over the length of a bay rather than tile by tile. A fine
    /// field would fray the edge of the shelf into speckle, which reads as a dithering
    /// artefact and not as water.
    /// </para>
    /// </summary>
    private const double VariationScale = 34;

    /// <inheritdoc cref="VariationScale"/>
    private const int VariationOctaves = 3;

    /// <summary>
    /// The covers as they now stand, with sea near the land marked
    /// <see cref="GroundCover.Shallow"/> and the rest of it left alone.
    /// </summary>
    /// <param name="land">Which tiles are land. Read and never written.</param>
    /// <param name="cover">
    /// The covers as they stand, which the pass builds on: everything on the land is left
    /// exactly where it is, since none of it is sea. Left untouched -- what comes back is a
    /// copy. Null, or the wrong size, starts from grass.
    /// </param>
    public static GroundCover[] Mark(
        int width, int height, bool[] land, GroundCover[] cover, ShallowSettings settings)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentNullException.ThrowIfNull(land);

        if (land.Length != width * height)
            throw new ArgumentException("Land mask does not match the map size.", nameof(land));

        var marked = cover is not null && cover.Length == land.Length
            ? (GroundCover[])cover.Clone()
            : new GroundCover[land.Length];

        // Every shallow goes back to deep before any is marked, so a second press replaces the
        // shelf rather than only ever widening it. Unlike the rivers, this is one band round
        // one coast and there is no sense in which there could be two of it.
        for (var i = 0; i < marked.Length; i++)
        {
            if (marked[i] == GroundCover.Shallow)
                marked[i] = GroundCover.Grass;
        }

        var reach = Math.Max(0, settings.Reach);

        if (reach <= 0)
            return marked;

        // How far each tile is from the nearest land, by the same four-way flood that measures
        // distance from the sea -- handed the mask upside down, since "distance from the sea"
        // with the land and the sea swapped over is distance from the land.
        var water = new bool[land.Length];

        for (var i = 0; i < land.Length; i++)
            water[i] = !land[i];

        var fromLand = LandTopology.DistanceFromSea(water, width, height);

        var variation = Math.Clamp(settings.Variation, 0, 1);
        var noise = new SimplexNoise(unchecked((uint)settings.Seed * 2135587861u) + 0x7F4Au);
        var frequency = 1.0 / VariationScale;

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var index = (y * width) + x;

                // Land is not water of any depth, and the land's own cover is not this pass's
                // business.
                if (land[index])
                    continue;

                // Runs -1 to 1, so the reach swings either side of what was asked for and the
                // dial's own number stays the average rather than becoming a floor.
                var swell = noise.Fbm(x * frequency, y * frequency, VariationOctaves);

                var here = reach * (1 + (variation * swell));

                if (fromLand[index] <= here)
                    marked[index] = GroundCover.Shallow;
            }
        }

        return marked;
    }
}
