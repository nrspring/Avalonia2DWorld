using SkiaSharp;

namespace Avalonia2DWorld.Map.Models
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
    /// <param name="World">
    /// The whole map, for a component that has to look at a tile other than the one it is
    /// drawing. Null where the caller has none to give -- a still render or a test.
    /// <para>
    /// The <b>whole</b> map, deliberately, and not the screenful being drawn. A component that
    /// decides its shape from its neighbours -- a road that has to know whether to draw a
    /// straight, a corner or a junction -- would otherwise get a different answer for a tile at
    /// the edge of the window than for the same tile once it had been scrolled inland, and the
    /// map would rearrange itself as it was panned.
    /// </para>
    /// <para>
    /// Safe to read from the render thread on exactly the same terms as the tiles themselves: a
    /// published <see cref="TileGrid"/>, and every row in it, is never written to again -- an
    /// edit replaces rows and publishes a new grid. So this is a snapshot, and it is the very
    /// snapshot the tiles in this frame came out of.
    /// </para>
    /// <para>
    /// Read through <see cref="TileGrid.At"/>, which answers with null off the edge of the map
    /// so a component drawing a border tile needs no bounds check of its own. Do not hold on to
    /// it past the frame: it is safe to keep, being immutable, but keeping it pins every tile of
    /// a map that has otherwise been replaced.
    /// </para>
    /// </param>
    /// <param name="Gpu">
    /// The context the canvas is drawing through, for a component that wants an offscreen
    /// surface of its own. Null where there is none -- a still render, a test, or a pass into
    /// a raster surface -- and a component handed null must draw the ordinary way rather than
    /// refusing.
    /// <para>
    /// Here because it is a fact about the frame and cannot be known any earlier: the context
    /// belongs to the render thread and is handed over with the canvas, while everything else
    /// on this record is settled on the UI thread a moment before. Whoever leases the canvas
    /// fills it in.
    /// </para>
    /// <para>
    /// A surface built from it lives only as long as the context does. Hold the context that
    /// built one alongside it and compare before reuse: a lost device gives a new one, and
    /// anything made from the old is so much rubbish.
    /// </para>
    /// </param>
    public readonly record struct RenderFrame(
        int TileSize, float TimeSeconds, TileGrid? World = null, GRContext? Gpu = null)
    {
        /// <summary>A frame with time stopped, for still renders and for tests.</summary>
        public static RenderFrame Still(int tileSize) => new(tileSize, 0f);
    }
}
