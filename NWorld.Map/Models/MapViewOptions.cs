using System;

namespace NWorld.Map.Models
{
    /// <summary>
    /// How <see cref="Controls.MapView"/> draws, as opposed to what it draws. One object so
    /// that adding a knob later is a property here rather than another bindable property on
    /// the control.
    /// <para>
    /// Immutable, and replaced rather than edited -- <c>options with { TileSize = 8 }</c>.
    /// That is not style: <see cref="TileSize"/> is read on Avalonia's render thread while
    /// the frame is being drawn, so a settable property here would be the same race that
    /// <see cref="TileMap"/> exists to avoid. Replacing the whole object also gives the
    /// control a reference change to notice, which is what repaints it.
    /// </para>
    /// </summary>
    public sealed record MapViewOptions
    {
        private readonly int _tileSize = 32;

        /// <summary>The options a control gets when nothing is bound to it.</summary>
        public static MapViewOptions Default { get; } = new();

        /// <summary>
        /// Tile width and height in pixels, i.e. the zoom level. A tile at (x, y) is drawn at
        /// (x * TileSize, y * TileSize) from the control's top-left.
        /// </summary>
        public int TileSize
        {
            get => _tileSize;
            init
            {
                // Rejected here rather than shrugged off at draw time: a zero tile size makes
                // every tile land on the same pixel and every pointer position divide by
                // zero, and the mistake is in the caller that set it.
                ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
                _tileSize = value;
            }
        }

        /// <summary>
        /// Whether to repaint continuously. Animated components -- water above all -- move
        /// with <see cref="RenderFrame.TimeSeconds"/> and are frozen without it. Turn it off
        /// for a static map and the control repaints only when something it binds to changes.
        /// </summary>
        public bool IsAnimated { get; init; } = true;
    }
}
