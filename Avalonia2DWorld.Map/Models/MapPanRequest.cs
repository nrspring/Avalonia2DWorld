namespace Avalonia2DWorld.Map.Models
{
    /// <summary>
    /// One step of a drag across the map, in the terms a view model needs to answer it.
    /// <para>
    /// Reported as the distance the pointer moved and not as an origin to move to, because
    /// what a drag should do to the view is not the control's decision. Dragging the map
    /// along with the pointer is the obvious reading and what the app does -- which means
    /// subtracting this from the origin, since pulling the map right is looking further
    /// left -- but a caller that would rather scrub a timeline with it is free to.
    /// </para>
    /// <para>
    /// Sent per move rather than as a total from where the drag began, so a caller has no
    /// gesture to remember: each step is a small change to whatever the origin is now.
    /// </para>
    /// </summary>
    /// <param name="DeltaX">
    /// How far the pointer moved since the last step, in tiles at the current tile size.
    /// Positive is rightwards.
    /// </param>
    /// <param name="DeltaY"><inheritdoc cref="DeltaX" path="/summary"/></param>
    /// <param name="ViewportX">
    /// Width of the map view in tiles, at the current tile size: how much of the map fits on
    /// screen. Carried so that a caller holding the view inside the map has the width to
    /// work from without knowing how big the control is.
    /// </param>
    /// <param name="ViewportY"><inheritdoc cref="ViewportX" path="/summary"/></param>
    public readonly record struct MapPanRequest(
        double DeltaX,
        double DeltaY,
        double ViewportX,
        double ViewportY);
}
