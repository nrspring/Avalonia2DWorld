using System.Threading.Tasks;
using Avalonia2DWorld.Map.Models;
using SkiaSharp;

namespace Avalonia2DWorld.MapServices.MapRenderComponents.StandardRenderer.RenderingFunctions
{
    /// <summary>
    /// Archers: a violet token, struck true, with an A on it.
    /// <para>
    /// The fourth land token, and violet because it is the last hue left that is nobody's.
    /// Ochre is the militia's, navy the soldiers', crimson the cavalry's and the warships', and
    /// green would sink into the grass it mostly stands on. Violet appears nowhere in the ground,
    /// the water or the stonework, so like the cavalry's red it can never be half-read as
    /// terrain.
    /// </para>
    /// <para>
    /// Pitched between the soldiers and the cavalry in value and well clear of both in hue: lighter
    /// than the navy, so the two cold tokens do not run together at a handful of pixels, and
    /// bluer than the crimson, so the two struck-true tokens with dark rims do not either.
    /// </para>
    /// </summary>
    public static class RenderArcher
    {
        /// <summary>Unused while the edge is struck true, and kept so that turning that on needs nothing else.</summary>
        private const uint Seed = 0xA4407B07u;

        private static readonly UnitToken Token = new(
            new TokenStyle(
                Face: new SKColor(0x6E, 0x4C, 0xA0),
                Rim: new SKColor(0x26, 0x18, 0x40),
                RimShare: 0.09f,
                Ink: new SKColor(0xF2, 0xEC, 0xFA),
                Mark: TokenMark.Letter,
                Rough: false,
                Letter: 'A'),
            Seed);

        public static Task Render(TileRenderContext context) => Token.Render(context);

        /// <inheritdoc cref="UnitToken.Prewarm"/>
        public static Task Prewarm(int tileSize) => Token.Prewarm(tileSize);

        /// <inheritdoc cref="UnitToken.ClearCache"/>
        public static void ClearCache() => Token.ClearCache();
    }
}
