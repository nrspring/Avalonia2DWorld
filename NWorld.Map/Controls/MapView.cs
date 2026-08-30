using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using NWorld.Map.Interfaces;
using NWorld.Map.Models;
using SkiaSharp;

namespace NWorld.Map.Controls
{
    /// <summary>
    /// Draws a screenful of <see cref="MapTile"/> through an <see cref="IMapRenderer"/> and
    /// reports the tile under the pointer.
    /// <para>
    /// The tiles go onto Avalonia's own <see cref="SKCanvas"/> by way of a custom draw
    /// operation, not through a <c>WriteableBitmap</c>. That keeps the renderer's batched
    /// draws on the surface Avalonia is already compositing into; blitting a bitmap would
    /// undo the batching the renderer exists to do.
    /// </para>
    /// <para>
    /// The control holds no map state of its own. It renders what <see cref="Tiles"/> points
    /// at and raises <see cref="HoverCommand"/> and <see cref="ClickCommand"/>; deciding what
    /// a hover or a click means is the view model's job.
    /// </para>
    /// </summary>
    public class MapView : Control
    {
        /// <summary>
        /// The tiles to draw. Passed to the renderer as-is, in the order given -- components
        /// may coalesce runs of adjacent tiles, so reading order is worth preserving.
        /// <para>
        /// The list is handed to the render thread by reference and read there, so it must
        /// not be mutated once assigned. Publish a new list instead: that swap is atomic
        /// where an in-place edit races the frame being drawn.
        /// </para>
        /// </summary>
        public static readonly StyledProperty<IReadOnlyList<MapTile>?> TilesProperty =
            AvaloniaProperty.Register<MapView, IReadOnlyList<MapTile>?>(nameof(Tiles));

        /// <summary>
        /// The renderer that draws the tiles. Nothing is drawn while this is null.
        /// <para>
        /// One renderer per control: <see cref="IMapRenderer"/> implementations reuse their
        /// batch buffers between frames and are not shareable across concurrent draws.
        /// </para>
        /// </summary>
        public static readonly StyledProperty<IMapRenderer?> RendererProperty =
            AvaloniaProperty.Register<MapView, IMapRenderer?>(nameof(Renderer));

        /// <summary>
        /// Tile width and height in pixels, i.e. the zoom level. A tile at (x, y) is drawn at
        /// (x * TileSize, y * TileSize) from the control's top-left.
        /// </summary>
        public static readonly StyledProperty<int> TileSizeProperty =
            AvaloniaProperty.Register<MapView, int>(nameof(TileSize), defaultValue: 32);

        /// <summary>
        /// Invoked with the <see cref="TileCoordinate"/> under the pointer when that tile
        /// changes, and with null when the pointer leaves the control.
        /// <para>
        /// On the change and not on every pointer move: a move within one tile means nothing
        /// to a caller that highlights tiles, and firing per pixel would put a command
        /// execution and a repaint on every mouse event.
        /// </para>
        /// </summary>
        public static readonly StyledProperty<ICommand?> HoverCommandProperty =
            AvaloniaProperty.Register<MapView, ICommand?>(nameof(HoverCommand));

        /// <summary>
        /// Invoked with the <see cref="TileCoordinate"/> that was clicked. Left button only;
        /// the parameter is never null.
        /// </summary>
        public static readonly StyledProperty<ICommand?> ClickCommandProperty =
            AvaloniaProperty.Register<MapView, ICommand?>(nameof(ClickCommand));

        /// <summary>
        /// Whether to repaint continuously. Animated components -- water above all -- move
        /// with <see cref="RenderFrame.TimeSeconds"/> and are frozen without it. Turn it off
        /// for a static map and the control repaints only when something it binds to changes.
        /// </summary>
        public static readonly StyledProperty<bool> IsAnimatedProperty =
            AvaloniaProperty.Register<MapView, bool>(nameof(IsAnimated), defaultValue: true);

        // One clock for the control's whole life, sampled once per frame and handed to every
        // tile in it. Never a frame counter: that would tie animation speed to frame rate.
        private readonly Stopwatch _clock = Stopwatch.StartNew();

        private TileCoordinate? _hovered;
        private bool _framePending;

        static MapView()
        {
            AffectsRender<MapView>(TilesProperty, RendererProperty, TileSizeProperty, IsAnimatedProperty);
        }

        public MapView()
        {
            // Tiles are drawn at absolute map coordinates and a screenful rarely lands flush
            // against the control edge, so the last row and column need cutting off.
            ClipToBounds = true;
        }

        /// <inheritdoc cref="TilesProperty"/>
        public IReadOnlyList<MapTile>? Tiles
        {
            get => GetValue(TilesProperty);
            set => SetValue(TilesProperty, value);
        }

        /// <inheritdoc cref="RendererProperty"/>
        public IMapRenderer? Renderer
        {
            get => GetValue(RendererProperty);
            set => SetValue(RendererProperty, value);
        }

        /// <inheritdoc cref="TileSizeProperty"/>
        public int TileSize
        {
            get => GetValue(TileSizeProperty);
            set => SetValue(TileSizeProperty, value);
        }

        /// <inheritdoc cref="HoverCommandProperty"/>
        public ICommand? HoverCommand
        {
            get => GetValue(HoverCommandProperty);
            set => SetValue(HoverCommandProperty, value);
        }

        /// <inheritdoc cref="ClickCommandProperty"/>
        public ICommand? ClickCommand
        {
            get => GetValue(ClickCommandProperty);
            set => SetValue(ClickCommandProperty, value);
        }

        /// <inheritdoc cref="IsAnimatedProperty"/>
        public bool IsAnimated
        {
            get => GetValue(IsAnimatedProperty);
            set => SetValue(IsAnimatedProperty, value);
        }

        /// <summary>The tile the pointer is over, or null when it is outside the control.</summary>
        public TileCoordinate? HoveredTile => _hovered;

        public override void Render(DrawingContext context)
        {
            base.Render(context);

            var renderer = Renderer;
            var tiles = Tiles;
            var tileSize = TileSize;

            if (renderer is null || tiles is not { Count: > 0 } || tileSize <= 0 ||
                Bounds is not { Width: > 0, Height: > 0 })
            {
                // Nothing to draw, so no frame to queue either -- an idle control should not
                // hold the render loop open. Everything that could give it something to draw
                // affects render, so the loop restarts on its own.
                return;
            }

            context.Custom(new MapDrawOperation(
                new Rect(Bounds.Size),
                renderer,
                new RenderFrame(tileSize, (float)_clock.Elapsed.TotalSeconds),
                tiles));

            RequestNextFrame();
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            SetHovered(ToTile(e.GetPosition(this)));
        }

        protected override void OnPointerExited(PointerEventArgs e)
        {
            base.OnPointerExited(e);
            SetHovered(null);
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);

            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                return;

            if (ToTile(e.GetPosition(this)) is not { } tile)
                return;

            // The pointer is over a tile whether or not a move was seen first -- a touch or a
            // pen never sends one -- so the hover is brought up to date before the click
            // rather than left stale.
            SetHovered(tile);
            Execute(ClickCommand, tile);
            e.Handled = true;
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);

            // No top level left to schedule against, and the pointer is by definition no
            // longer over anything.
            _framePending = false;
            SetHovered(null);
        }

        /// <summary>Pixel position within the control to the tile that covers it.</summary>
        private TileCoordinate? ToTile(Point position)
        {
            var tileSize = TileSize;
            if (tileSize <= 0)
                return null;

            // Floor rather than a cast: a cast truncates towards zero, which would fold the
            // whole strip from -tileSize to +tileSize into column 0.
            return new TileCoordinate(
                (int)Math.Floor(position.X / tileSize),
                (int)Math.Floor(position.Y / tileSize));
        }

        private void SetHovered(TileCoordinate? tile)
        {
            if (_hovered == tile)
                return;

            _hovered = tile;
            Execute(HoverCommand, tile);
        }

        private static void Execute(ICommand? command, TileCoordinate? parameter)
        {
            if (command is not null && command.CanExecute(parameter))
                command.Execute(parameter);
        }

        /// <summary>
        /// Queues the next animation frame. Guarded by <see cref="_framePending"/> so that an
        /// invalidation arriving between frames -- a property change, say -- does not stack a
        /// second request on top of the one already in flight.
        /// </summary>
        private void RequestNextFrame()
        {
            if (!IsAnimated || _framePending)
                return;

            if (TopLevel.GetTopLevel(this) is not { } topLevel)
                return;

            _framePending = true;
            topLevel.RequestAnimationFrame(_ =>
            {
                _framePending = false;
                InvalidateVisual();
            });
        }

        /// <summary>
        /// Carries one frame's worth of arguments over to the render thread, where Avalonia
        /// hands back the <see cref="SKCanvas"/> it is compositing into.
        /// </summary>
        private sealed class MapDrawOperation(
            Rect bounds,
            IMapRenderer renderer,
            RenderFrame frame,
            IReadOnlyList<MapTile> tiles) : ICustomDrawOperation
        {
            public Rect Bounds { get; } = bounds;

            public bool HitTest(Point p) => Bounds.Contains(p);

            // Never equal to another operation: the tiles or the clock have moved on by the
            // time a second one is built, so there is no frame worth reusing.
            public bool Equals(ICustomDrawOperation? other) => false;

            public void Render(ImmediateDrawingContext context)
            {
                // Absent on a non-Skia backend. Nothing to draw onto, so draw nothing rather
                // than throw out of the render thread.
                if (context.TryGetFeature(typeof(ISkiaSharpApiLeaseFeature)) is not ISkiaSharpApiLeaseFeature feature)
                    return;

                using var lease = feature.Lease();
                var canvas = lease.SkCanvas;

                // Avalonia has already applied this control's transform, so the canvas is in
                // the control's own coordinates and the clip belongs at the origin.
                var checkpoint = canvas.Save();
                try
                {
                    canvas.ClipRect(SKRect.Create((float)Bounds.Width, (float)Bounds.Height));

                    // Every render function completes synchronously -- the Task on
                    // IMapRenderer is there for the ones that may not always. Unwrapped
                    // rather than left dangling so a failed draw surfaces here and not on
                    // the finalizer thread.
                    renderer.RenderTiles(canvas, frame, tiles).GetAwaiter().GetResult();
                }
                finally
                {
                    canvas.RestoreToCount(checkpoint);
                }
            }

            public void Dispose()
            {
            }
        }
    }
}
