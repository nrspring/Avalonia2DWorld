using System.Threading.Tasks;
using Avalonia2DWorld.Map.Models;
using SkiaSharp;

namespace Avalonia2DWorld.MapServices.MapRenderComponents.StandardRenderer.RenderingFunctions
{
    /// <summary>
    /// The ships, as tokens: two families of three. Troop transports and warships, each at
    /// three levels, told apart from each other by colour and rim and within a family by size,
    /// depth of colour, and a count of pips.
    /// <para>
    /// One file for all six of them, which is not how the rest of this folder is arranged and
    /// is right here. These are not six things that happen to look similar; they are two
    /// ladders of three, and every number below is chosen against the others rather than on its
    /// own. Split across six files the ladders would be unrelated constants in six places, and
    /// the first change anybody made would flatten them.
    /// </para>
    /// <para>
    /// <b>Two families, told apart the way militia and soldiers are.</b> The difference that has
    /// to survive is which kind of ship, and it is carried by every cue that is not the level:
    /// pale sea-green against crimson, a thin dark rim against a bright one, dark pips against
    /// pale. The level is carried by size, depth and count, which are the same in both families,
    /// so a level-two warship and a level-two transport are the same size and the same count and
    /// differ only in what they are.
    /// </para>
    /// <para>
    /// <b>Both chosen against water, because ships are the only tokens drawn on it.</b> The sea
    /// is a mid blue-green, which is most of the way to being a transport token -- so transports
    /// sit plainly lighter than any water under them, and the ladder runs downwards from there
    /// rather than through it. Warships go the other way round the wheel: crimson is as far from
    /// the sea as a colour can be, and it is the colour a hostile thing has been on every chart
    /// ever drawn. Navy stays off limits to both: it is the soldiers' colour, and a blue token on
    /// blue water would be no token at all.
    /// </para>
    /// <para>
    /// <b>Cues failing in order.</b> The pips close up first, then the shades within a family run
    /// together, and the sizes are still three sizes at four pixels across -- see
    /// <see cref="TokenStyle.Radius"/>. Through all of that green is still green and red is still
    /// red, so the family is the last thing to go, which is the right way round: at the zoom
    /// where only one thing can be told, whose ship it is matters more than how big.
    /// </para>
    /// <para>
    /// The pips are what the masts were when these were drawn as ships. A hull half again as long
    /// as another is hard to judge with nothing beside it; one, two and three is a count, and a
    /// count needs no comparison.
    /// </para>
    /// </summary>
    internal static class ShipTokens
    {
        /// <summary>How big each level is, as a fraction of the tile. Shared by both families.</summary>
        private static readonly float[] Radii = [0.21f, 0.26f, 0.31f];

        /// <summary>
        /// The transports' edge: dark, so the family reads as one whatever the face.
        /// </summary>
        private static readonly SKColor TransportRim = new(0x0F, 0x2E, 0x29);

        /// <summary>
        /// The transports' pips, dark rather than pale.
        /// <para>
        /// Pale would be the warships' ink. Dark also holds across the family's own range of face
        /// colours, which a pale ink would not: it is legible on the deepest of the three and on
        /// the lightest, where pale washes out against the light one.
        /// </para>
        /// </summary>
        private static readonly SKColor TransportInk = new(0x0B, 0x27, 0x22);

        /// <summary>The transports, palest first: deeper green as they get bigger.</summary>
        private static readonly SKColor[] TransportFaces =
        [
            new(0xBC, 0xE2, 0xD4),
            new(0x90, 0xCD, 0xB8),
            new(0x64, 0xB5, 0x9D),
        ];

        /// <summary>
        /// The warships' edge: a bright ring of bone, the same device the soldiers carry and
        /// for the same reason -- a ring of light round a deep disc is a shape rather than a
        /// detail, and it is still a ring when every mark inside it has closed up.
        /// </summary>
        private static readonly SKColor WarshipRim = new(0xF2, 0xE8, 0xD8);

        /// <summary>The warships' pips: pale, against a face that is dark at every level.</summary>
        private static readonly SKColor WarshipInk = new(0xF6, 0xEE, 0xE2);

        /// <summary>
        /// The warships, lightest first. Never as light as the lightest transport: even the
        /// smallest has to read as a deep red disc with a bright ring rather than as a pink one.
        /// </summary>
        private static readonly SKColor[] WarshipFaces =
        [
            new(0xC4, 0x4A, 0x40),
            new(0xA8, 0x30, 0x2C),
            new(0x86, 0x1E, 0x20),
        ];

        /// <summary>A troop transport at a level from one to three.</summary>
        public static UnitToken Transport(int level) =>
            Of(TransportFaces[level - 1], TransportRim, TransportInk, level);

        /// <summary>A warship at a level from one to three.</summary>
        public static UnitToken Warship(int level) =>
            Of(WarshipFaces[level - 1], WarshipRim, WarshipInk, level);

        /// <summary>
        /// One of the six. Struck true rather than hand-cut, which is the militia's mark and
        /// stays the militia's: a hull is not a thing anybody whittles.
        /// </summary>
        private static UnitToken Of(SKColor face, SKColor rim, SKColor ink, int level) =>
            new(new TokenStyle(
                Face: face,
                Rim: rim,
                RimShare: 0.09f,
                Ink: ink,
                Mark: TokenMark.Pips,
                Rough: false,
                Radius: Radii[level - 1],
                Count: level),
                seed: 0u);
    }

    /// <summary>A level-one troop transport: the smallest token, palest green, one pip.</summary>
    public static class RenderTransport1
    {
        private static readonly UnitToken Token = ShipTokens.Transport(1);

        public static Task Render(TileRenderContext context) => Token.Render(context);

        /// <inheritdoc cref="UnitToken.Prewarm"/>
        public static Task Prewarm(int tileSize) => Token.Prewarm(tileSize);

        /// <inheritdoc cref="UnitToken.ClearCache"/>
        public static void ClearCache() => Token.ClearCache();
    }

    /// <summary>A level-two troop transport: the middle of the three in every respect, two pips.</summary>
    public static class RenderTransport2
    {
        private static readonly UnitToken Token = ShipTokens.Transport(2);

        public static Task Render(TileRenderContext context) => Token.Render(context);

        /// <inheritdoc cref="UnitToken.Prewarm"/>
        public static Task Prewarm(int tileSize) => Token.Prewarm(tileSize);

        /// <inheritdoc cref="UnitToken.ClearCache"/>
        public static void ClearCache() => Token.ClearCache();
    }

    /// <summary>
    /// A level-three troop transport: the largest token, the deepest green, three pips.
    /// <para>
    /// Held short of the tile edge like the rest -- see <see cref="TokenStyle.Radius"/>. A fleet
    /// is drawn in a line far more often than forts are, and two tokens that touched would read
    /// as one long mark.
    /// </para>
    /// </summary>
    public static class RenderTransport3
    {
        private static readonly UnitToken Token = ShipTokens.Transport(3);

        public static Task Render(TileRenderContext context) => Token.Render(context);

        /// <inheritdoc cref="UnitToken.Prewarm"/>
        public static Task Prewarm(int tileSize) => Token.Prewarm(tileSize);

        /// <inheritdoc cref="UnitToken.ClearCache"/>
        public static void ClearCache() => Token.ClearCache();
    }

    /// <summary>A level-one warship: the smallest token, the brightest red, one pip.</summary>
    public static class RenderWarship1
    {
        private static readonly UnitToken Token = ShipTokens.Warship(1);

        public static Task Render(TileRenderContext context) => Token.Render(context);

        /// <inheritdoc cref="UnitToken.Prewarm"/>
        public static Task Prewarm(int tileSize) => Token.Prewarm(tileSize);

        /// <inheritdoc cref="UnitToken.ClearCache"/>
        public static void ClearCache() => Token.ClearCache();
    }

    /// <summary>A level-two warship: the middle of the three in every respect, two pips.</summary>
    public static class RenderWarship2
    {
        private static readonly UnitToken Token = ShipTokens.Warship(2);

        public static Task Render(TileRenderContext context) => Token.Render(context);

        /// <inheritdoc cref="UnitToken.Prewarm"/>
        public static Task Prewarm(int tileSize) => Token.Prewarm(tileSize);

        /// <inheritdoc cref="UnitToken.ClearCache"/>
        public static void ClearCache() => Token.ClearCache();
    }

    /// <summary>A level-three warship: the largest token, the deepest red, three pips.</summary>
    /// <inheritdoc cref="RenderTransport3" path="/summary/para"/>
    public static class RenderWarship3
    {
        private static readonly UnitToken Token = ShipTokens.Warship(3);

        public static Task Render(TileRenderContext context) => Token.Render(context);

        /// <inheritdoc cref="UnitToken.Prewarm"/>
        public static Task Prewarm(int tileSize) => Token.Prewarm(tileSize);

        /// <inheritdoc cref="UnitToken.ClearCache"/>
        public static void ClearCache() => Token.ClearCache();
    }
}
