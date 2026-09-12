namespace Avalonia2DWorld.Map.Models
{
    /// <summary>Where a drag has got to.</summary>
    public enum MapDragPhase
    {
        /// <summary>The button has just gone down. Nothing has moved yet.</summary>
        Started,

        /// <summary>The pointer has moved with the button still down.</summary>
        Moved,

        /// <summary>The button has come up, or the drag was cut short. The last step.</summary>
        Finished,
    }

    /// <summary>
    /// A left-button drag across the map, in map pixels, from where it began to where the
    /// pointer is now.
    /// <para>
    /// The control reports the gesture and nothing else. What is being dragged -- a label, a
    /// marker, a selection box, nothing at all -- is the caller's business, and the caller is
    /// the only thing that could know: <see cref="Controls.MapView"/> holds no map state and
    /// has no idea what is under the point the button went down on.
    /// </para>
    /// <para>
    /// Given as an absolute pair and not as a per-step delta, which is the opposite of
    /// <see cref="MapPanRequest"/> and right for the opposite reason. A pan is a change to the
    /// view and the view is what it is now, so a step is all a caller needs. A drag moves a
    /// thing to where the pointer is, and a thing moved by accumulated steps drifts: every
    /// rounding, every dropped move and every zoom mid-gesture is added to it for good. Held
    /// against the point the button went down on, the answer is the same however the gesture
    /// arrived.
    /// </para>
    /// <para>
    /// <see cref="From"/> is fixed for the whole gesture, so a caller that grabbed something on
    /// <see cref="MapDragPhase.Started"/> can hold the offset between the grab and the thing's
    /// own position and keep it: what was picked up an inch from its middle stays picked up an
    /// inch from its middle.
    /// </para>
    /// <para>
    /// <see cref="MapDragPhase.Finished"/> always arrives, once, for every drag that started --
    /// including one cut short by the pointer capture going elsewhere. A caller may put back
    /// whatever it was holding without keeping a timer on it.
    /// </para>
    /// </summary>
    /// <param name="From">Where the button went down, in map pixels.</param>
    /// <param name="At">Where the pointer is now, in map pixels.</param>
    /// <param name="Phase">Which step of the gesture this is.</param>
    public readonly record struct MapPixelDrag(MapPixel From, MapPixel At, MapDragPhase Phase)
    {
        /// <summary>How far the pointer has come since the button went down.</summary>
        public MapPixel Delta => new(At.X - From.X, At.Y - From.Y);
    }
}
