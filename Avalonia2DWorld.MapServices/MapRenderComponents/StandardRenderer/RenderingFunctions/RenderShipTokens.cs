using System.Threading.Tasks;
using Avalonia2DWorld.Map.Models;
using SkiaSharp;

namespace Avalonia2DWorld.MapServices.MapRenderComponents.StandardRenderer.RenderingFunctions
{
    /// <summary>
    /// The three ships, as tokens: one sea-green family told apart by size, by depth of colour,
    /// and by a count of pips.
    /// <para>
    /// One file for the three of them, which is not how the rest of this folder is arranged and
    /// is right here. These are not three things that happen to look similar; they are one thing
    /// at three sizes, and every number below is chosen against the other two rather than on its
    /// own. Split across three files the ladder would be three unrelated constants in three
    /// places, and the first change anybody made would flatten it.
    /// </para>
    /// <para>
    /// <b>Green, so that a ship is never a soldier.</b> The land tokens took warm ochre and cold
    /// navy, and navy is the one colour a ship must not be: a blue token on blue water beside a
    /// blue token on green grass is two readings of the same mark.
    /// </para>
    /// <para>
    /// <b>And pale, because these are the only tokens chosen against water.</b> A ship is never
    /// drawn on ground, so the thing its colour has to beat is the sea -- and the sea is already
    /// a mid blue-green, which is most of the way to being a ship token. Pitched at the same
    /// depth as the water the family was legible on a page beside itself and nearly gone on the
    /// map: the deepest of the three all but disappeared, which is the opposite of what a token
    /// is for. All three now sit plainly lighter than any water under them, and the ladder from
    /// small to large runs downwards from there rather than through it.
    /// </para>
    /// <para>
    /// <b>Three cues, failing in order.</b> The pips close up first, then the shades of green run
    /// together, and the sizes are still three sizes at four pixels across -- see
    /// <see cref="TokenStyle.Radius"/>. It is the same stacking the land pair uses and it matters
    /// more here, because these three are a family: they are meant to be recognised together and
    /// then told apart, where <see cref="RenderMilitia"/> and <see cref="RenderSoldiers"/> only
    /// ever have to be told apart.
    /// </para>
    /// <para>
    /// The pips are what the masts were when these were drawn as ships. A hull half again as long
    /// as another is hard to judge with nothing beside it; one, two and three is a count, and a
    /// count needs no comparison.
    /// </para>
    /// </summary>
    internal static class ShipTokens
    {
        /// <summary>The edge on all three: dark, so the family reads as one whatever the face.</summary>
        private static readonly SKColor Rim = new(0x0F, 0x2E, 0x29);

        /// <summary>
        /// The pips, dark rather than pale.
        /// <para>
        /// Pale would be the soldiers' ink, and the whole point of the green is not to be them.
        /// Dark also holds across the family's own range of face colours, which a pale ink would
        /// not: it is legible on the deepest of the three and on the lightest, where pale washes
        /// out against the light one.
        /// </para>
        /// </summary>
        private static readonly SKColor Ink = new(0x0B, 0x27, 0x22);

        /// <summary>
        /// One of the three. Struck true rather than hand-cut, which is the militia's mark and
        /// stays the militia's: a hull is not a thing anybody whittles.
        /// </summary>
        public static UnitToken Of(SKColor face, float radius, int pips) =>
            new(new TokenStyle(
                Face: face,
                Rim: Rim,
                RimShare: 0.09f,
                Ink: Ink,
                Mark: TokenMark.Pips,
                Rough: false,
                Radius: radius,
                Count: pips),
                seed: 0u);
    }

    /// <summary>A boat: the smallest token of the three, palest, and carrying one pip.</summary>
    /// <inheritdoc cref="ShipTokens" path="/summary/para[2]"/>
    public static class RenderSmallShip
    {
        private static readonly UnitToken Token =
            ShipTokens.Of(new SKColor(0xBC, 0xE2, 0xD4), radius: 0.21f, pips: 1);

        public static Task Render(TileRenderContext context) => Token.Render(context);

        /// <inheritdoc cref="UnitToken.Prewarm"/>
        public static Task Prewarm(int tileSize) => Token.Prewarm(tileSize);

        /// <inheritdoc cref="UnitToken.ClearCache"/>
        public static void ClearCache() => Token.ClearCache();
    }

    /// <summary>A cog: the middle of the three in every respect, and two pips.</summary>
    public static class RenderMediumShip
    {
        private static readonly UnitToken Token =
            ShipTokens.Of(new SKColor(0x90, 0xCD, 0xB8), radius: 0.26f, pips: 2);

        public static Task Render(TileRenderContext context) => Token.Render(context);

        /// <inheritdoc cref="UnitToken.Prewarm"/>
        public static Task Prewarm(int tileSize) => Token.Prewarm(tileSize);

        /// <inheritdoc cref="UnitToken.ClearCache"/>
        public static void ClearCache() => Token.ClearCache();
    }

    /// <summary>
    /// A carrack: the largest token, the deepest green, and three pips.
    /// <para>
    /// Held short of the tile edge like the rest -- see <see cref="TokenStyle.Radius"/>. A fleet
    /// is drawn in a line far more often than forts are, and two tokens that touched would read
    /// as one long mark.
    /// </para>
    /// </summary>
    public static class RenderLargeShip
    {
        private static readonly UnitToken Token =
            ShipTokens.Of(new SKColor(0x64, 0xB5, 0x9D), radius: 0.31f, pips: 3);

        public static Task Render(TileRenderContext context) => Token.Render(context);

        /// <inheritdoc cref="UnitToken.Prewarm"/>
        public static Task Prewarm(int tileSize) => Token.Prewarm(tileSize);

        /// <inheritdoc cref="UnitToken.ClearCache"/>
        public static void ClearCache() => Token.ClearCache();
    }
}
