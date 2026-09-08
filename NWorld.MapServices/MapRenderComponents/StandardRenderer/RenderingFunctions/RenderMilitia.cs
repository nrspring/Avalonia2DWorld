using System.Threading.Tasks;
using NWorld.Map.Models;
using SkiaSharp;

namespace NWorld.MapServices.MapRenderComponents.StandardRenderer.RenderingFunctions
{
    /// <summary>
    /// Militia: a rough ochre token with two staves crossed on it.
    /// <para>
    /// Everything about it is <see cref="RenderSoldiers"/>'s opposite rather than its neighbour,
    /// which is the whole design -- see <see cref="UnitToken"/> for why the differences are
    /// stacked five deep. Pale where the soldiers are dark, warm where they are cold, cut by hand
    /// where theirs is struck true, and marked with two staves thrown across each other where
    /// theirs carries a rank.
    /// </para>
    /// <para>
    /// Ochre and dark ink, so the token is a light shape with a dark mark on it. That inversion
    /// is doing more work than the hue is: colour is the first thing a small mark loses to the
    /// ground behind it, and light-on-dark against dark-on-light survives being washed out by
    /// bright sand or dark bog alike.
    /// </para>
    /// </summary>
    public static class RenderMilitia
    {
        /// <summary>Fixes which way the hand-cut edge wobbles. Not the soldiers' seed, though nothing of theirs is cut.</summary>
        private const uint Seed = 0xB1117A2Du;

        private static readonly UnitToken Token = new(
            new TokenStyle(
                Face: new SKColor(0xCE, 0x96, 0x46),
                Rim: new SKColor(0x4C, 0x35, 0x18),

                // Thin for the mirror of the soldiers' reason -- the dark rim is not the token,
                // the ochre is -- and thin enough here to keep the hand-cut edge legible as an
                // edge. A heavy rim on a ragged outline reads as a thick ring that happens to
                // wobble, rather than as a disc somebody cut badly.
                RimShare: 0.09f,
                Ink: new SKColor(0x3A, 0x28, 0x12),
                Mark: TokenMark.Crossed,
                Rough: true),
            Seed);

        public static Task Render(TileRenderContext context) => Token.Render(context);

        /// <inheritdoc cref="UnitToken.Prewarm"/>
        public static Task Prewarm(int tileSize) => Token.Prewarm(tileSize);

        /// <inheritdoc cref="UnitToken.ClearCache"/>
        public static void ClearCache() => Token.ClearCache();
    }
}
