using System;

namespace Avalonia2DWorld.Map.Models
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
        private readonly int _miniMapSize = 160;
        private readonly int _miniMapMargin = 12;

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
        /// The map coordinate at the control's top-left corner, in tiles and fractional
        /// tiles. A tile at (x, y) is drawn at ((x - OriginX) * TileSize, (y - OriginY) *
        /// TileSize) from the control's top-left.
        /// <para>
        /// In tile units rather than pixels so that it survives a <see cref="TileSize"/>
        /// change unaltered. That is what makes a cursor-anchored zoom one subtraction in
        /// the view model rather than a rescale the control would have to be told about.
        /// </para>
        /// <para>
        /// Unclamped: the control will happily draw a map scrolled off its own edge. Where
        /// the origin is allowed to go is the view model's call, since only it knows how big
        /// the map is.
        /// </para>
        /// </summary>
        public double OriginX { get; init; }

        /// <inheritdoc cref="OriginX"/>
        public double OriginY { get; init; }

        /// <summary>
        /// Whether to repaint continuously. Animated components -- water above all -- move
        /// with <see cref="RenderFrame.TimeSeconds"/> and are frozen without it. Turn it off
        /// for a static map and the control repaints only when something it binds to changes.
        /// </summary>
        public bool IsAnimated { get; init; } = true;

        /// <summary>
        /// Which corner the mini-map sits in, or <see cref="MiniMapLocation.Off"/> for none.
        /// <para>
        /// The mini-map is the whole tile list again at whatever scale fits the inset, so it
        /// costs a second pass over the tiles and a second zoom level in the render caches.
        /// At inset scale that level is small, and the caches evict by least-recently-drawn,
        /// so the two sizes coexist rather than thrash.
        /// </para>
        /// </summary>
        public MiniMapLocation MiniMap { get; init; } = MiniMapLocation.Off;

        /// <summary>
        /// Side of the mini-map inset in pixels. Square whatever the map's shape: the map is
        /// letterboxed inside it, which keeps the inset's position independent of the tiles
        /// and so cheap enough to work out on every pointer move.
        /// </summary>
        public int MiniMapSize
        {
            get => _miniMapSize;
            init
            {
                ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
                _miniMapSize = value;
            }
        }

        /// <summary>
        /// Whether to draw the frame rate in the top-right corner.
        /// <para>
        /// Off by default: it is an instrument, not part of the map. Worth turning on while
        /// changing anything a render component does per tile, since the cost of that shows
        /// up here long before it is visible as a stutter.
        /// </para>
        /// <para>
        /// Frames the control actually draws, which is what makes it honest in both
        /// directions: with <see cref="IsAnimated"/> off there is no repaint loop to measure
        /// and the label reads a dash rather than inventing a number.
        /// </para>
        /// </summary>
        public bool ShowFrameRate { get; init; }

        /// <summary>Gap between the mini-map inset and the two edges it is tucked against.</summary>
        public int MiniMapMargin
        {
            get => _miniMapMargin;
            init
            {
                ArgumentOutOfRangeException.ThrowIfNegative(value);
                _miniMapMargin = value;
            }
        }
    }
}
