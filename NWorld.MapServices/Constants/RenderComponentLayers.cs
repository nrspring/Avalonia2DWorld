namespace NWorld.MapServices.Constants
{
    /// <summary>
    /// Which layer each kind of component is drawn on. Low numbers first, so a higher layer
    /// covers a lower one.
    /// <para>
    /// Here rather than in <c>NWorld.Map</c> for the same reason <see cref="Elevations"/> is:
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
    /// </summary>
    public static class RenderComponentLayers
    {
        /// <summary>The ground itself: grass, sand, bog, water.</summary>
        public const int BaseGround = 0;

        /// <summary>The tile under the pointer.</summary>
        public const int Hover = 1;

        /// <summary>Whatever is standing on the tile.</summary>
        public const int Unit = 2;

        /// <summary>The elevation written over the tile. Above everything, being an instrument.</summary>
        public const int ElevationLabel = 10;
    }
}
