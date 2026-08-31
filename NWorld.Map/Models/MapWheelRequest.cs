namespace NWorld.Map.Models
{
    /// <summary>
    /// A wheel turn over the map.
    /// <para>
    /// Carries what happened and where, never what it means. Whether a turn is a zoom, a
    /// pan, or nothing at all is the view model's call, in the same way that what a click
    /// means is -- <see cref="Controls.MapView"/> only knows that the wheel moved.
    /// </para>
    /// <para>
    /// Everything here is a fact about the control at the moment of the turn, which is why
    /// the viewport size is in it: the view model owns the map and so the limits on where
    /// the map may be scrolled to, but only the control knows how much of it fits on screen.
    /// </para>
    /// </summary>
    /// <param name="Delta">
    /// Vertical wheel movement in notches, positive away from the user. Fractional on
    /// precision touchpads, and passed through unrounded: deciding how much of a turn counts
    /// as one step is a decision, and so belongs with the rest of them.
    /// </param>
    /// <param name="DeltaX">
    /// Horizontal wheel movement, from a tilt wheel or a sideways touchpad swipe. Zero on
    /// most mice. Carried because the gesture that pans a map sideways is the same event as
    /// the one that zooms it.
    /// </param>
    /// <param name="AnchorX">
    /// Where the pointer was, in fractional tiles and in map coordinates -- so the whole
    /// part names the tile and the fraction says where within it.
    /// <para>
    /// Fractional rather than a <see cref="TileCoordinate"/> because rounding to a whole
    /// tile here would throw away exactly the precision that keeps an anchored zoom from
    /// drifting a little further off-target with every notch.
    /// </para>
    /// </param>
    /// <param name="AnchorY"><inheritdoc cref="AnchorX" path="/node()"/></param>
    /// <param name="ViewportX">
    /// How many tiles wide the control is, at the tile size in force when the wheel turned.
    /// In tiles rather than pixels to match the anchor; multiply by the ratio of old tile
    /// size to new to get the width at another zoom level.
    /// </param>
    /// <param name="ViewportY"><inheritdoc cref="ViewportX" path="/node()"/></param>
    /// <param name="OverMiniMap">
    /// Whether the pointer was over the mini-map inset, where the anchor names the tile
    /// hidden behind the inset rather than the one being pointed at.
    /// </param>
    public readonly record struct MapWheelRequest(
        double Delta,
        double DeltaX,
        double AnchorX,
        double AnchorY,
        double ViewportX,
        double ViewportY,
        bool OverMiniMap);
}
