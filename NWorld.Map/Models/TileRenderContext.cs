using SkiaSharp;

namespace NWorld.Map.Models
{
    /// <summary>
    /// Everything one render function needs to draw one component on one tile: where to draw,
    /// how big, when, and with what arguments.
    /// </summary>
    /// <param name="Canvas">Target canvas. The tile occupies (X * TileSize, Y * TileSize).</param>
    /// <param name="Frame">Values shared by every tile in this frame — see <see cref="RenderFrame"/>.</param>
    /// <param name="X">Map coordinate, not a pixel offset.</param>
    /// <param name="Y">Map coordinate, not a pixel offset.</param>
    /// <param name="Params">
    /// The component's own arguments, from <see cref="MapRenderComponent.Params"/>. Usually empty.
    /// </param>
    public readonly record struct TileRenderContext(
        SKCanvas Canvas,
        RenderFrame Frame,
        int X,
        int Y,
        string[] Params)
    {
        /// <summary>Context for a component that takes no arguments, which is most of them.</summary>
        public TileRenderContext(SKCanvas canvas, RenderFrame frame, int x, int y)
            : this(canvas, frame, x, y, [])
        {
        }

        /// <inheritdoc cref="RenderFrame.TileSize"/>
        public int TileSize => Frame.TileSize;

        /// <inheritdoc cref="RenderFrame.TimeSeconds"/>
        public float TimeSeconds => Frame.TimeSeconds;
    }
}
