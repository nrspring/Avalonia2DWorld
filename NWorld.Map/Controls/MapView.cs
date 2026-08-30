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
        /// How to draw: tile size, whether to animate. See <see cref="MapViewOptions"/>.
        /// <para>
        /// Replaced, never edited -- the options are read while a frame is being drawn, and a
        /// reference change is also what tells the control to repaint.
        /// </para>
        /// </summary>
        public static readonly StyledProperty<MapViewOptions?> OptionsProperty =
            AvaloniaProperty.Register<MapView, MapViewOptions?>(
                nameof(Options), defaultValue: MapViewOptions.Default);

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

        // One clock for the control's whole life, sampled once per frame and handed to every
        // tile in it. Never a frame counter: that would tie animation speed to frame rate.
        private readonly Stopwatch _clock = Stopwatch.StartNew();

        private TileCoordinate? _hovered;
        private bool _framePending;

        static MapView()
        {
            AffectsRender<MapView>(TilesProperty, RendererProperty, OptionsProperty);
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

        /// <inheritdoc cref="OptionsProperty"/>
        public MapViewOptions? Options
        {
            get => GetValue(OptionsProperty);
            set => SetValue(OptionsProperty, value);
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

        /// <summary>The tile the pointer is over, or null when it is outside the control.</summary>
        public TileCoordinate? HoveredTile => _hovered;

        public override void Render(DrawingContext context)
        {
            base.Render(context);

            var renderer = Renderer;
            var tiles = Tiles;
            var options = Options ?? MapViewOptions.Default;

            if (renderer is null || tiles is not { Count: > 0 } ||
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
                new RenderFrame(options.TileSize, (float)_clock.Elapsed.TotalSeconds),
                tiles,
                MiniMapRect(Bounds.Size, options)));

            RequestNextFrame(options);
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

            // Null over the mini-map inset, where a click means nothing yet.
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

        /// <summary>
        /// Pixel position within the control to the tile that covers it, or null over the
        /// mini-map inset.
        /// </summary>
        private TileCoordinate? ToTile(Point position)
        {
            var options = Options ?? MapViewOptions.Default;

            // The inset draws the whole map at its own scale, so this arithmetic would name
            // the tile hidden *behind* it rather than the one being pointed at. Reporting
            // nothing is honest; the inset becomes a way to navigate once there is a viewport
            // to move.
            if (MiniMapRect(Bounds.Size, options) is { } inset && inset.Contains(position))
                return null;

            // Floor rather than a cast: a cast truncates towards zero, which would fold the
            // whole strip from -tileSize to +tileSize into column 0.
            return new TileCoordinate(
                (int)Math.Floor(position.X / options.TileSize),
                (int)Math.Floor(position.Y / options.TileSize));
        }

        /// <summary>
        /// Where the mini-map inset sits, or null when it is off or the control is too small
        /// to give it room.
        /// <para>
        /// Depends only on the control size and the options, never on the tiles -- which is
        /// what makes it cheap enough for <see cref="ToTile"/> to consult on every move.
        /// </para>
        /// </summary>
        private static Rect? MiniMapRect(Size bounds, MapViewOptions options)
        {
            if (options.MiniMap == MiniMapLocation.Off)
                return null;

            double size = options.MiniMapSize;
            double margin = options.MiniMapMargin;

            // Dropped rather than shrunk when it will not fit: an inset scaled down to suit a
            // narrow window stops being readable long before it stops fitting.
            if (bounds.Width < size + (2 * margin) || bounds.Height < size + (2 * margin))
                return null;

            var left = options.MiniMap is MiniMapLocation.UpperLeft or MiniMapLocation.LowerLeft
                ? margin
                : bounds.Width - size - margin;

            var top = options.MiniMap is MiniMapLocation.UpperLeft or MiniMapLocation.UpperRight
                ? margin
                : bounds.Height - size - margin;

            return new Rect(left, top, size, size);
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
        private void RequestNextFrame(MapViewOptions options)
        {
            if (!options.IsAnimated || _framePending)
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
            IReadOnlyList<MapTile> tiles,
            Rect? miniMap) : ICustomDrawOperation
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

                    // After the map, so the inset sits over it rather than under.
                    if (miniMap is { } inset)
                        DrawMiniMap(canvas, inset);
                }
                finally
                {
                    canvas.RestoreToCount(checkpoint);
                }
            }

            /// <summary>
            /// The same tiles again, scaled to fit <paramref name="inset"/>.
            /// <para>
            /// The renderer is reused rather than given its own instance: the two passes are
            /// sequential, and the guard an <see cref="IMapRenderer"/> keeps is against
            /// concurrent frames, not successive draws.
            /// </para>
            /// </summary>
            private void DrawMiniMap(SKCanvas canvas, Rect inset)
            {
                if (!TryGetExtent(out var minX, out var minY, out var columns, out var rows))
                    return;

                var rect = SKRect.Create(
                    (float)inset.X, (float)inset.Y, (float)inset.Width, (float)inset.Height);

                // Whole pixels, because the renderer takes an int tile size -- and at least
                // one, since a map wider than the inset still has to show something.
                var tileSize = Math.Max(1, Math.Min((int)(rect.Width / columns), (int)(rect.Height / rows)));

                var checkpoint = canvas.Save();
                try
                {
                    canvas.ClipRect(rect);

                    // A scrim behind the tiles. The map still reads through the letterbox
                    // bars of a map that is not square, but dimmed enough that the inset
                    // holds together as one panel.
                    using (var backdrop = new SKPaint { Color = new SKColor(0, 0, 0, 170) })
                        canvas.DrawRect(rect, backdrop);

                    canvas.Translate(
                        rect.Left + ((rect.Width - (columns * tileSize)) / 2f) - (minX * tileSize),
                        rect.Top + ((rect.Height - (rows * tileSize)) / 2f) - (minY * tileSize));

                    renderer.RenderTiles(canvas, frame with { TileSize = tileSize }, tiles)
                        .GetAwaiter().GetResult();
                }
                finally
                {
                    canvas.RestoreToCount(checkpoint);
                }

                // Outside the clip and inset by half a pixel, so a 1px stroke lands on whole
                // pixels instead of straddling the edge and coming out grey.
                using var border = new SKPaint
                {
                    Color = new SKColor(255, 255, 255, 90),
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = 1,
                    IsAntialias = true,
                };

                canvas.DrawRect(
                    new SKRect(rect.Left + 0.5f, rect.Top + 0.5f, rect.Right - 0.5f, rect.Bottom - 0.5f),
                    border);
            }

            /// <summary>
            /// The tile extent, as an origin and a size in tiles. False when there is nothing
            /// to measure.
            /// </summary>
            private bool TryGetExtent(out int minX, out int minY, out int columns, out int rows)
            {
                minX = minY = int.MaxValue;
                var maxX = int.MinValue;
                var maxY = int.MinValue;

                // Scanned rather than taken from the caller: the control is handed a flat
                // list and told nothing about its shape. One pass, and only when the inset
                // is actually on.
                foreach (var tile in tiles)
                {
                    if (tile is null)
                        continue;

                    if (tile.X < minX) minX = tile.X;
                    if (tile.X > maxX) maxX = tile.X;
                    if (tile.Y < minY) minY = tile.Y;
                    if (tile.Y > maxY) maxY = tile.Y;
                }

                columns = maxX - minX + 1;
                rows = maxY - minY + 1;
                return maxX >= minX && maxY >= minY;
            }

            public void Dispose()
            {
            }
        }
    }
}
