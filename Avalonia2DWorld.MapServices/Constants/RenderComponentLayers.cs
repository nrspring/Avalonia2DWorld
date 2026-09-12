namespace Avalonia2DWorld.MapServices.Constants
{
    /// <summary>
    /// Which layer each kind of component is drawn on. Low numbers first, so a higher layer
    /// covers a lower one.
    /// <para>
    /// Here rather than in <c>Avalonia2DWorld.Map</c> for the same reason <see cref="Elevations"/> is:
    /// the grid itself only knows that a tile's components are keyed by an int and drawn in
    /// order. What those ints <em>mean</em> -- that ground goes under a hover, that a unit goes
    /// over both -- is a fact about this world, and belongs with the rest of them.
    /// </para>
    /// <para>
    /// The gaps are deliberate. A layer number is written into every saved tile, so these are
    /// part of the file format and must never be renumbered; leaving room between them is what
    /// lets something new slide in above the units without disturbing anything already on
    /// disk.
    /// </para>
    /// <para>
    /// Units did move once, from 2 to 4, to open the two layers below them for resources and
    /// for what gets built on them -- there being no room at all between the ground and the
    /// hover, which is where those belong. It cost the maps saved before the move: anything
    /// they hold on layer 2 now reads as a resource. That was affordable only because the sole
    /// thing ever written there was the selection mark, which is transient and renders the same
    /// wherever it lands. It will not be affordable again, and the gaps left above are what
    /// mean it should not have to be.
    /// </para>
    /// </summary>
    public static class RenderComponentLayers
    {
        /// <summary>The ground itself: grass, sand, bog, water.</summary>
        public const int BaseGround = 0;

        /// <summary>The tile under the pointer.</summary>
        public const int Hover = 9;

        /// <summary>
        /// What the tile is worth: ore, timber, oil. Part of the ground rather than something
        /// placed on it, so it goes under everything that can be built or stood there.
        /// </summary>
        public const int Resource = 2;

        /// <summary>What has been built on the tile: works on the ground rather than on it.</summary>
        public const int Enhancement = 3;

        /// <summary>Whatever is standing on the tile.</summary>
        public const int Unit = 4;

        /// <summary>The elevation written over the tile. Above everything, being an instrument.</summary>
        public const int ElevationLabel = 10;
    }
}
