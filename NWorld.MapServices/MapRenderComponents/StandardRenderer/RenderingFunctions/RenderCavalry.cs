using System.Threading.Tasks;
using NWorld.Map.Models;
using SkiaSharp;

namespace NWorld.MapServices.MapRenderComponents.StandardRenderer.RenderingFunctions
{
    /// <summary>
    /// Cavalry: a crimson token, struck true, with a chevron pointing down the map.
    /// <para>
    /// The third land token, and the one with the least room to move. Two hues were already
    /// spoken for on ground -- the militia's warm ochre and the soldiers' cold navy -- and a
    /// third of the ship family's pale green would put horse and boats in one colour. Crimson is
    /// what is left that is nobody else's, and it is a good answer rather than a leftover: red is
    /// the one hue on this map that appears nowhere in the ground, the water or the stonework, so
    /// it can never be half-read as terrain.
    /// </para>
    /// <para>
    /// Pushed towards pink rather than towards orange, and that is the whole of what keeps it off
    /// the militia. Ochre and brick are both warm, and at a handful of pixels on green they
    /// converge; a crimson with some blue in it does not, however small it gets.
    /// </para>
    /// <para>
    /// The chevron is the only device here that says what its owner does rather than how it is
    /// drawn up. The militia's crossed staves and the soldiers' rank are both statements about
    /// order, because that is what separates those two from each other. Horse are not
    /// better-ordered foot -- they are faster -- so the mark is an arrowhead.
    /// </para>
    /// </summary>
    public static class RenderCavalry
    {
        /// <summary>Unused while the edge is struck true, and kept so that turning that on needs nothing else.</summary>
        private const uint Seed = 0x5ADD1E77u;

        private static readonly UnitToken Token = new(
            new TokenStyle(
                Face: new SKColor(0xA8, 0x36, 0x48),
                Rim: new SKColor(0x40, 0x12, 0x1C),
                RimShare: 0.09f,
                Ink: new SKColor(0xF6, 0xE4, 0xE2),
                Mark: TokenMark.Chevron,
                Rough: false),
            Seed);

        public static Task Render(TileRenderContext context) => Token.Render(context);

        /// <inheritdoc cref="UnitToken.Prewarm"/>
        public static Task Prewarm(int tileSize) => Token.Prewarm(tileSize);

        /// <inheritdoc cref="UnitToken.ClearCache"/>
        public static void ClearCache() => Token.ClearCache();
    }
}
