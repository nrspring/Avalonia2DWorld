namespace Avalonia2DWorld.Map.Models
{
    /// <summary>
    /// A tile's position on the map, in tiles and not in pixels.
    /// <para>
    /// What <see cref="Controls.MapView"/> hands to its hover and click commands. A coordinate
    /// rather than the <see cref="MapTile"/> itself, because the control is given a flat list
    /// with no spatial index: finding the tile would be a scan of the whole screenful on every
    /// pointer move, and the view model that supplied the list can already index it.
    /// </para>
    /// </summary>
    public readonly record struct TileCoordinate(int X, int Y);
}
