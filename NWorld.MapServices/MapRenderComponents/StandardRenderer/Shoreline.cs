using System;
using NWorld.Map.Models;
using NWorld.MapServices.Constants;

namespace NWorld.MapServices.MapRenderComponents.StandardRenderer
{
    /// <summary>
    /// Where the land stops: which tiles are water, which are ground, and which sides of a
    /// piece of ground have water against them.
    /// <para>
    /// The same shape as <see cref="RoadNetwork"/> and for the same reasons. It is read off the
    /// map at draw time rather than stored on the tiles, so raising a piece of land or flooding
    /// it needs no second pass to go round and tell the neighbours; and it lives in one place
    /// rather than in each thing that wants it, because everything that asks whether a tile is
    /// water has to get the same answer. A coast drawn round a river that a bridge does not
    /// think is a river is two bugs, and they are the same bug.
    /// </para>
    /// <para>
    /// Four-way and not eight, again as the road network is. Two land masses meeting only across
    /// a corner are two land masses, and a coast drawn as though they touched would join them.
    /// </para>
    /// </summary>
    public static class Shoreline
    {
        /// <summary>Which side a bit of <see cref="Mask"/> stands for.</summary>
        public const int North = 1;

        /// <inheritdoc cref="North"/>
        public const int East = 2;

        /// <inheritdoc cref="North"/>
        public const int South = 4;

        /// <inheritdoc cref="North"/>
        public const int West = 8;

        /// <summary>A tile with water on all four sides.</summary>
        public const int All = North | East | South | West;

        /// <summary>
        /// Which sides of the tile at a map coordinate have water against them, as four bits.
        /// Zero for a tile that is not land, so a caller can ask about any tile and get nothing
        /// back for the ones with no coast to draw.
        /// </summary>
        public static int Mask(TileGrid world, int x, int y)
        {
            ArgumentNullException.ThrowIfNull(world);

            if (!IsLand(world.At(x, y)))
                return 0;

            return (IsWater(world.At(x, y - 1)) ? North : 0)
                | (IsWater(world.At(x + 1, y)) ? East : 0)
                | (IsWater(world.At(x, y + 1)) ? South : 0)
                | (IsWater(world.At(x - 1, y)) ? West : 0);
        }

        /// <summary>
        /// Whether a tile is water: the sea at either depth, or a river or a lake.
        /// <para>
        /// Asked of the ground the tile is drawn with and not of its height, because the two
        /// disagree exactly where it matters. A river takes the elevation of the land it runs
        /// over, so by the numbers it is high ground, and to anyone looking at the map it is
        /// plainly a river -- and a river with a bank drawn along it is most of what this is
        /// for.
        /// </para>
        /// <para>
        /// False for a null tile, which is what a coordinate off the edge of the map comes back
        /// as. A world does not end in water merely because it ends, and a coast drawn all the
        /// way round the border would put one there.
        /// </para>
        /// </summary>
        public static bool IsWater(MapTile? tile) =>
            Ground(tile) is { } ground
            && (ground == MapRenderComponentConstants.Water
                || ground == MapRenderComponentConstants.ShallowWater
                || ground == MapRenderComponentConstants.DeepWater);

        /// <inheritdoc cref="IsWater(MapTile?)"/>
        public static bool IsWater(TileGrid world, int x, int y) => IsWater(world?.At(x, y));

        /// <summary>
        /// Whether a tile is ground somebody could stand on.
        /// <para>
        /// Named rather than taken as "not water", so that a tile off the map and a tile with
        /// nothing on its ground layer are neither, and get no coast. The alternative reads a
        /// hole in the map as a beach.
        /// </para>
        /// </summary>
        public static bool IsLand(MapTile? tile) =>
            Ground(tile) is { } ground
            && (ground == MapRenderComponentConstants.Grass
                || ground == MapRenderComponentConstants.Desert
                || ground == MapRenderComponentConstants.Swamp);

        /// <inheritdoc cref="IsLand(MapTile?)"/>
        public static bool IsLand(TileGrid world, int x, int y) => IsLand(world?.At(x, y));

        /// <summary>
        /// Whether any of the eight tiles around this one is land.
        /// <para>
        /// Eight and not four, which is the one place here that counts corners -- and it is not
        /// an exception to the rule above, because it is not asking what joins to what. It is
        /// asking which water is near enough to the shore to be worth drawing something in, and
        /// water off the corner of a headland is as near as water off its side.
        /// </para>
        /// </summary>
        public static bool TouchesLand(TileGrid world, int x, int y)
        {
            ArgumentNullException.ThrowIfNull(world);

            for (var dy = -1; dy <= 1; dy++)
            {
                for (var dx = -1; dx <= 1; dx++)
                {
                    if ((dx != 0 || dy != 0) && IsLand(world.At(x + dx, y + dy)))
                        return true;
                }
            }

            return false;
        }

        /// <summary>
        /// What a tile is made of, or null where there is no tile or nothing on its ground
        /// layer.
        /// </summary>
        private static Guid? Ground(MapTile? tile) =>
            tile is not null
            && tile.MapRenderComponents.TryGetValue(RenderComponentLayers.BaseGround, out var ground)
                ? ground.ComponentType
                : null;
    }
}
