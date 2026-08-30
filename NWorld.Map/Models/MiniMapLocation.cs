namespace NWorld.Map.Models
{
    /// <summary>
    /// Which corner of <see cref="Controls.MapView"/> the mini-map sits in, if it is shown
    /// at all.
    /// </summary>
    public enum MiniMapLocation
    {
        /// <summary>No mini-map. The default, and free -- nothing extra is drawn.</summary>
        Off = 0,

        UpperLeft,
        UpperRight,
        LowerLeft,
        LowerRight,
    }
}
