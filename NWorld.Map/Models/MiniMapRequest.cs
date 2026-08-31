namespace NWorld.Map.Models
{
    /// <summary>
    /// A press inside the mini-map inset, in the terms a view model needs to answer it.
    /// <para>
    /// The point is given in map coordinates rather than in pixels of the inset, because
    /// where the inset is and how far it shrinks the map are the control's business and
    /// nobody else's. What the caller gets is the place on the map that was pointed at.
    /// </para>
    /// <para>
    /// Named for the gesture and not for what it should do about it. Centring the view is
    /// the obvious reading and what the app does, but the control takes no view: this says
    /// where the press landed, and a caller that would rather drop a marker there is free
    /// to.
    /// </para>
    /// </summary>
    /// <param name="MapX">
    /// Where the press landed, in tiles and fractions of a tile from the map's origin.
    /// </param>
    /// <param name="MapY"><inheritdoc cref="MapX" path="/summary"/></param>
    /// <param name="ViewportX">
    /// Width of the map view in tiles, at the current tile size: how much of the map fits on
    /// screen. Carried so that a caller centring the view has the half-width to subtract
    /// without knowing how big the control is.
    /// </param>
    /// <param name="ViewportY"><inheritdoc cref="ViewportX" path="/summary"/></param>
    public readonly record struct MiniMapRequest(
        double MapX,
        double MapY,
        double ViewportX,
        double ViewportY);
}
