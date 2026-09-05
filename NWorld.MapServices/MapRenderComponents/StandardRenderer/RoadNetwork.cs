using NWorld.Map.Models;
using NWorld.MapServices.Constants;

namespace NWorld.MapServices.MapRenderComponents.StandardRenderer
{
    /// <summary>
    /// What joins to what on the enhancement layer: roads, bridges, and the four sides they can
    /// meet a neighbour on.
    /// <para>
    /// One place rather than one per render function, because a road and a bridge have to agree
    /// about this or a road would run up to a bridge and stop at a stub while the bridge drew an
    /// arm reaching out to meet it. They are one network with two surfaces -- a bridge is the
    /// stretch of road that happens to be over water -- and this is where that is said.
    /// </para>
    /// <para>
    /// Read off the map at draw time rather than stored on the tiles. That is what makes laying
    /// a road a one-tile edit: its neighbours re-read their own surroundings on the next frame
    /// and fit themselves to it, and lifting one leaves the survivors correct without anything
    /// having to go round and tell them.
    /// </para>
    /// </summary>
    internal static class RoadNetwork
    {
        /// <summary>Which side a bit of <see cref="Mask"/> stands for.</summary>
        public const int North = 1;

        /// <inheritdoc cref="North"/>
        public const int East = 2;

        /// <inheritdoc cref="North"/>
        public const int South = 4;

        /// <inheritdoc cref="North"/>
        public const int West = 8;

        /// <summary>
        /// Which sides of a tile have something of the network on them, as four bits.
        /// <para>
        /// Four-way and not eight, like everything else that decides what joins to what on this
        /// map. A road meeting another only across a corner is two roads, the same way a coast
        /// touching only at a corner is two islands.
        /// </para>
        /// </summary>
        public static int Mask(TileGrid world, int x, int y) =>
            (Carries(world, x, y - 1) ? North : 0)
            | (Carries(world, x + 1, y) ? East : 0)
            | (Carries(world, x, y + 1) ? South : 0)
            | (Carries(world, x - 1, y) ? West : 0);

        /// <summary>
        /// Whether the tile at a map coordinate is part of the network: a road, a bridge, or a
        /// city or fort for them to arrive at. False off the edge of the map, which is what makes
        /// a road run up to the border and stop rather than reach past it.
        /// <para>
        /// A city, a fort, a factory, a shipyard and a works all count even though they are places
        /// rather than ways. A road that ran up to a town and stopped at a stub a tile short of it would be a
        /// road to nowhere; counting the place here is what carries the road all the way to its
        /// edge.
        /// </para>
        /// <para>
        /// A shipyard is in that list even though it takes its own shape from the water and not
        /// from this. The two questions are different and both get asked: the yard decides where
        /// its slips go by looking at the sea, and the road decides whether to keep going by
        /// looking at the yard.
        /// </para>
        /// <para>
        /// A fort reads this back the other way round as well -- see <c>RenderFort</c>, and
        /// <c>RenderFactory</c> after it. What they take from their neighbours is not how far to
        /// build but which sides to open a gate on, so the same four bits that bring the road up
        /// to the wall are the ones that put a door in it.
        /// </para>
        /// </summary>
        public static bool Carries(TileGrid world, int x, int y) =>
            world.At(x, y) is { } tile
            && tile.MapRenderComponents.TryGetValue(RenderComponentLayers.Enhancement, out var built)
            && (built.ComponentType == MapRenderComponentConstants.Road
                || built.ComponentType == MapRenderComponentConstants.Bridge
                || built.ComponentType == MapRenderComponentConstants.City
                || built.ComponentType == MapRenderComponentConstants.Fort
                || built.ComponentType == MapRenderComponentConstants.Factory
                || built.ComponentType == MapRenderComponentConstants.Shipyard
                || built.ComponentType == MapRenderComponentConstants.Works);

        /// <summary>
        /// Which sides of a tile have more city on them, as four bits.
        /// <para>
        /// A city is built a tile at a time and each tile is a piece of one, so what a piece
        /// needs to know is not where the roads are but where the rest of the town is: the
        /// streets and the blocks have to run on across that edge rather than stop at it. A road
        /// arriving is a road arriving, and the town does nothing about it.
        /// </para>
        /// </summary>
        public static int CityMask(TileGrid world, int x, int y) =>
            (IsCity(world, x, y - 1) ? North : 0)
            | (IsCity(world, x + 1, y) ? East : 0)
            | (IsCity(world, x, y + 1) ? South : 0)
            | (IsCity(world, x - 1, y) ? West : 0);

        /// <inheritdoc cref="CityMask"/>
        private static bool IsCity(TileGrid world, int x, int y) =>
            world.At(x, y) is { } tile
            && tile.MapRenderComponents.TryGetValue(RenderComponentLayers.Enhancement, out var built)
            && built.ComponentType == MapRenderComponentConstants.City;
    }
}
