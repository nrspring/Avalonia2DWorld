using System;
using SkiaSharp;

namespace NWorld.Map.Models
{
    /// <summary>
    /// Everything one render function needs to draw one component across <b>every tile that
    /// carries it</b> in this frame: where to draw, how big, when, and with what arguments.
    /// <para>
    /// A batch and not a single tile, because that is the difference between a draw call per
    /// tile and a draw call per component. A zoomed-out screen is tens of thousands of tiles;
    /// issued one at a time they cannot be merged, whereas handed over together they collapse
    /// into a <c>DrawAtlas</c> and a handful of covering rects. Measured on the grass renderer
    /// at an 8px zoom, that is 74ms a frame against 35ms.
    /// </para>
    /// <para>
    /// <see cref="Tiles"/> is only valid for the duration of the call: the buffer behind it is
    /// owned by the renderer and refilled on the next frame. A function that needs to keep any
    /// of it must copy it out.
    /// </para>
    /// </summary>
    /// <param name="Canvas">Target canvas. A tile at (x, y) occupies (x * TileSize, y * TileSize).</param>
    /// <param name="Frame">Values shared by every tile in this frame — see <see cref="RenderFrame"/>.</param>
    /// <param name="Buffer">Backing array for <see cref="Tiles"/>; may be longer than <paramref name="Count"/>.</param>
    /// <param name="Count">How much of <paramref name="Buffer"/> is live.</param>
    public readonly record struct TileRenderContext(
        SKCanvas Canvas,
        RenderFrame Frame,
        TilePlacement[] Buffer,
        int Count)
    {
        /// <summary>Context for a single tile, which is mostly useful to tests.</summary>
        public TileRenderContext(SKCanvas canvas, RenderFrame frame, int x, int y, string[]? parameters = null)
            : this(canvas, frame, [new TilePlacement(x, y, parameters ?? [])], 1)
        {
        }

        /// <summary>
        /// The tiles to draw, in whatever order the renderer collected them. Nothing may be
        /// assumed about that order, but a renderer that hands them over row by row lets a
        /// component coalesce them — see how the grass tone finds its runs.
        /// </summary>
        public ReadOnlySpan<TilePlacement> Tiles => Buffer.AsSpan(0, Count);

        /// <inheritdoc cref="RenderFrame.TileSize"/>
        public int TileSize => Frame.TileSize;

        /// <inheritdoc cref="RenderFrame.TimeSeconds"/>
        public float TimeSeconds => Frame.TimeSeconds;

        /// <summary>
        /// The whole map, for a component whose shape depends on its neighbours. Null where the
        /// caller had none to give, so a component that uses it must have something sensible to
        /// draw without it.
        /// <para>
        /// This is the one thing here that reaches outside the batch. <see cref="Tiles"/> is
        /// only the tiles carrying this component, and only the ones in view; a road needs to
        /// know about the tile next door whether or not it has a road on it and whether or not
        /// it is on screen.
        /// </para>
        /// </summary>
        /// <inheritdoc cref="RenderFrame.World" path="/para"/>
        public TileGrid? World => Frame.World;
    }
}
