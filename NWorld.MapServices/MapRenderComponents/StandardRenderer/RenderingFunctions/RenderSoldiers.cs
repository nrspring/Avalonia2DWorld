using System.Threading.Tasks;
using NWorld.Map.Models;
using SkiaSharp;

namespace NWorld.MapServices.MapRenderComponents.StandardRenderer.RenderingFunctions
{
    /// <summary>
    /// Soldiers: a dark steel token, struck true, with a bright rim and three uprights ranked
    /// across it.
    /// <para>
    /// Read against <see cref="RenderMilitia"/> and never on its own, because that is how it will
    /// be seen. Dark where the militia is pale, cold where it is warm, ringed in bright metal
    /// where it has a thin dark edge, perfectly round where it is hand-cut, and marked with a
    /// rank where it is marked with a mess.
    /// </para>
    /// <para>
    /// The bright rim is the one that carries furthest. A device closes to a smudge and a hue
    /// washes out against strong ground, but a ring of light round a dark disc is a shape rather
    /// than a detail, and it is still a ring at a handful of pixels across -- which is exactly
    /// the range where the men these replaced stopped being tellable apart.
    /// </para>
    /// </summary>
    public static class RenderSoldiers
    {
        /// <summary>Unused while the edge is struck true, and kept so that turning that on needs nothing else.</summary>
        private const uint Seed = 0xF11E3C08u;

        private static readonly UnitToken Token = new(
            new TokenStyle(
                Face: new SKColor(0x22, 0x33, 0x4E),
                Rim: new SKColor(0xC2, 0xCA, 0xD2),

                // Thin, and thinner than it looks like it needs to be, because a ring lies at
                // the outside of a disc where the area is: a rim of a seventh of the radius is a
                // seventh of the width and getting on for half of what anybody sees. At a quarter
                // it beat the face outright and the token came back pale with dark slots in it --
                // lighter than the militia rather than darker, which throws away the distinction
                // that survives being smallest. It has to be a line round a dark disc, not a
                // second disc.
                RimShare: 0.09f,
                Ink: new SKColor(0xE2, 0xE8, 0xEE),
                Mark: TokenMark.Ranked,
                Rough: false),
            Seed);

        public static Task Render(TileRenderContext context) => Token.Render(context);

        /// <inheritdoc cref="UnitToken.Prewarm"/>
        public static Task Prewarm(int tileSize) => Token.Prewarm(tileSize);

        /// <inheritdoc cref="UnitToken.ClearCache"/>
        public static void ClearCache() => Token.ClearCache();
    }
}
