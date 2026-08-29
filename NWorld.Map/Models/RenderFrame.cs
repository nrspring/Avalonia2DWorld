namespace NWorld.Map.Models
{
    /// <summary>
    /// The values that are fixed for the whole of one frame.
    /// <para>
    /// <see cref="TimeSeconds"/> is sampled <b>once per frame</b> by whoever drives the repaint
    /// and handed unchanged to every tile in that frame. It must not be a frame counter, which
    /// would tie animation speed to frame rate, and render functions must never read the clock
    /// themselves: tiles would then each sample a slightly different instant, and anything
    /// moving across the map -- waves above all -- would tear at the tile boundaries.
    /// </para>
    /// </summary>
    /// <param name="TileSize">Width and height of a tile in pixels, i.e. the zoom level.</param>
    /// <param name="TimeSeconds">
    /// Seconds elapsed since the renderer started. Animated components should use it modulo
    /// their own loop length, which keeps the animation seamless and sidesteps the precision
    /// a float loses over a long session.
    /// </param>
    public readonly record struct RenderFrame(int TileSize, float TimeSeconds)
    {
        /// <summary>A frame with time stopped, for still renders and for tests.</summary>
        public static RenderFrame Still(int tileSize) => new(tileSize, 0f);
    }
}
