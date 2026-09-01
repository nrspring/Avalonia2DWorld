namespace NWorld.MapServices.Constants
{
    /// <summary>
    /// What the heights on a map mean. The one place the bands are written down.
    /// <para>
    /// These are a fact about the world rather than about any one part of the program, and
    /// everything has to agree on them or the map contradicts itself: the generator decides
    /// which tiles are mountain, the shading decides how bright a mountain is drawn, and the
    /// panels decide what to say about one. Three copies of the number eleven is three chances
    /// to move two of them.
    /// </para>
    /// <para>
    /// Here rather than in <c>NWorld.Map</c>, which is a control and a grid of tiles and knows
    /// nothing about what a world is made of -- a tile there has an elevation the way it has an
    /// X, as a number nobody has interpreted yet. Deciding that eleven is a mountain is the same
    /// kind of decision as deciding what grass looks like, so it lives with
    /// <see cref="MapRenderComponents.MapRenderComponentConstants"/>: the lowest layer that both
    /// the renderers below and the generators above can see.
    /// </para>
    /// </summary>
    public static class Elevations
    {
        /// <summary>Sea level. The datum every other height is measured from.</summary>
        public const int Sea = 0;

        /// <summary>Flat land: one above the sea, and all a land pass raises on its own.</summary>
        public const int Flat = 1;

        /// <summary>The first and last elevations that count as hills.</summary>
        public const int HillsFrom = 2;

        /// <inheritdoc cref="HillsFrom"/>
        public const int HillsTo = 10;

        /// <summary>The first and last elevations that count as mountains.</summary>
        public const int MountainsFrom = 11;

        /// <inheritdoc cref="MountainsFrom"/>
        public const int MountainsTo = 30;

        /// <summary>
        /// Whether a height is ground somebody could stand a settlement, a swamp or a desert
        /// on: land, and not up in the mountains.
        /// </summary>
        public static bool IsLowland(int elevation) =>
            elevation is >= Flat and <= HillsTo;
    }
}
