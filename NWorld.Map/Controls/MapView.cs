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
        /// The tiles to draw, with the shape that says where each one is.
        /// <para>
        /// A grid rather than a flat list because the control only ever draws the part of it
        /// that fits on screen, and it should not have to look through a map to find that
        /// part: with the shape in hand the screenful is a slice, and the work of a frame
        /// follows the size of the window instead of the size of the map.
        /// </para>
        /// <para>
        /// Handed to the render thread by reference and read there, so it must not be
        /// mutated once assigned. Publish a new grid instead -- which is what
        /// <see cref="TileMap"/> does, sharing every row the edit did not touch.
        /// </para>
        /// </summary>
        public static readonly StyledProperty<TileGrid?> TilesProperty =
            AvaloniaProperty.Register<MapView, TileGrid?>(nameof(Tiles));

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

        /// <summary>
        /// Invoked with a <see cref="MapWheelRequest"/> when the wheel turns over the
        /// control. The parameter is never null.
        /// <para>
        /// Named for what callers use it for rather than for the event, but the control
        /// takes no view on that: it reports the turn and where it happened, and a view
        /// model that would rather pan than zoom is free to.
        /// </para>
        /// </summary>
        public static readonly StyledProperty<ICommand?> ZoomCommandProperty =
            AvaloniaProperty.Register<MapView, ICommand?>(nameof(ZoomCommand));

        // How long the frame rate is averaged over. Long enough to be steady, short enough
        // that a change made while watching it shows up as an answer rather than a drift.
        private const double RateWindowSeconds = 0.5;

        // The most animation time one frame may cover. A longer gap than this is a stall or a
        // resume rather than a slow frame -- a minimised window, a breakpoint, animation
        // switched back on -- and swallowing it costs a hitch, where spending it would jump
        // every wave on the map to a phase nobody watched it reach.
        private const double MaxAnimationStep = 0.25;

        // One clock for the control's whole life, sampled once per frame and handed to every
        // tile in it. Never a frame counter: that would tie animation speed to frame rate.
        private readonly Stopwatch _clock = Stopwatch.StartNew();

        // The animation clock, and the wall clock reading it was last advanced from. Not
        // the wall clock itself: this one stops when the repaint loop does. Without that,
        // any frame the control was asked for while paused -- a hover, a zoom -- would draw
        // the water at the time it arrived, and the mouse would work as a scrub bar over an
        // animation that is supposed to be still.
        private double _animationSeconds;
        private double _lastFrameSeconds;

        // The mini-map, kept as a picture between frames. The inset is the one thing left
        // whose cost follows the size of the map, because all of it is on screen and none of
        // it can be left out.
        private readonly MiniMapCache _miniMapCache = new();

        private TileCoordinate? _hovered;

        // Kept so that a zoom can re-resolve the hover without the pointer having moved.
        // Null exactly when the pointer is not over the control.
        private Point? _pointer;

        private bool _framePending;

        // Frames drawn since the current measuring window opened, and when it opened.
        // Counted over a window rather than taken from the gap between two frames: one
        // interval is noise, a single late frame reads as a collapse, and a number that
        // flickers every frame cannot be read at all.
        private double _rateWindowStart;
        private int _rateFrames;
        private double _framesPerSecond;

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
        public TileGrid? Tiles
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

        /// <inheritdoc cref="ZoomCommandProperty"/>
        public ICommand? ZoomCommand
        {
            get => GetValue(ZoomCommandProperty);
            set => SetValue(ZoomCommandProperty, value);
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

            var miniMap = MiniMapRect(Bounds.Size, options);

            // Counted here rather than at the top of the method, so what is measured is
            // frames that drew a map: the early return above is not a frame at 0ms.
            var now = _clock.Elapsed.TotalSeconds;
            MeasureFrameRate(now, options.IsAnimated);
            AdvanceAnimation(now, options.IsAnimated);

            context.Custom(new MapDrawOperation(
                new Rect(Bounds.Size),
                renderer,
                new RenderFrame(options.TileSize, (float)_animationSeconds),
                tiles,
                VisibleWindow(tiles, options),
                OriginPixels(options),
                miniMap,
                _miniMapCache,
                now,
                options.ShowFrameRate ? _framesPerSecond : null));

            RequestNextFrame(options);
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);

            _pointer = e.GetPosition(this);
            SetHovered(ToTile(_pointer.Value));
        }

        protected override void OnPointerExited(PointerEventArgs e)
        {
            base.OnPointerExited(e);

            _pointer = null;
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

        protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
        {
            base.OnPointerWheelChanged(e);

            var options = Options ?? MapViewOptions.Default;
            var position = e.GetPosition(this);

            // Taken from the event rather than from _pointer: the wheel arrives with no move
            // in front of it when it is turned the instant the pointer enters. Recorded too,
            // so the zoom that follows can re-resolve the hover against it.
            _pointer = position;

            var request = new MapWheelRequest(
                e.Delta.Y,
                e.Delta.X,
                options.OriginX + (position.X / options.TileSize),
                options.OriginY + (position.Y / options.TileSize),
                Bounds.Width / options.TileSize,
                Bounds.Height / options.TileSize,
                MiniMapRect(Bounds.Size, options) is { } inset && inset.Contains(position));

            // Marked handled only when something actually took it, so a control with no
            // ZoomCommand bound still lets a ScrollViewer it happens to sit in scroll.
            if (Execute(ZoomCommand, request))
                e.Handled = true;
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);

            // A zoom or a pan moves the map out from under a stationary pointer, and a
            // stationary pointer sends no move event to notice it with. Without this the
            // highlight stays on the tile that *was* under the cursor until the mouse is
            // jiggled.
            if (change.Property == OptionsProperty && _pointer is { } position)
                SetHovered(ToTile(position));
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);

            // No top level left to schedule against, and the pointer is by definition no
            // longer over anything. The rate window is dropped too: the gap until the control
            // is attached again is not a slow frame.
            _framePending = false;
            _rateFrames = 0;
            _rateWindowStart = _clock.Elapsed.TotalSeconds;
            _pointer = null;
            SetHovered(null);
        }

        /// <summary>
        /// Moves the animation clock on by the time this frame covers, or holds it where it
        /// is while the repaint loop is off.
        /// <para>
        /// Held rather than reset, so switching animation back on carries on from the water
        /// that is on screen instead of snapping to whatever phase the wall clock had
        /// reached in the meantime.
        /// </para>
        /// </summary>
        private void AdvanceAnimation(double now, bool animated)
        {
            if (animated)
                _animationSeconds += Math.Min(now - _lastFrameSeconds, MaxAnimationStep);

            // Recorded either way: the gap that matters is the one since the last frame that
            // was drawn, not since the last one that moved the animation on.
            _lastFrameSeconds = now;
        }

        /// <summary>
        /// The part of <paramref name="tiles"/> that lands inside the control, as a view over
        /// the grid rather than a copy of it.
        /// <para>
        /// Worked out from the origin and the tile size, which is the whole point of being
        /// given a grid: a map of a million tiles costs the same to draw as one of a
        /// thousand, because neither the control nor the renderer ever touches a tile that is
        /// not on screen.
        /// </para>
        /// <para>
        /// A tile of margin on every side. The tile under the top-left corner is usually only
        /// part way on, and a component is free to draw a little past its own square.
        /// </para>
        /// </summary>
        private TileWindow VisibleWindow(TileGrid tiles, MapViewOptions options)
        {
            var minX = (int)Math.Floor(options.OriginX) - 1;
            var minY = (int)Math.Floor(options.OriginY) - 1;
            var maxX = (int)Math.Ceiling(options.OriginX + (Bounds.Width / options.TileSize)) + 1;
            var maxY = (int)Math.Ceiling(options.OriginY + (Bounds.Height / options.TileSize)) + 1;

            return tiles.Window(minX, minY, maxX, maxY);
        }

        /// <summary>
        /// Folds this frame into the rate, and republishes the average when the window is up.
        /// </summary>
        /// <param name="animated">
        /// Whether the repaint loop is running. When it is not, the rate is dropped rather
        /// than measured: the frames still arriving are the ones a hover or a zoom asked for,
        /// and averaging those would report how fast the mouse is moving. The label reads a
        /// dash instead, which is the truth -- there is no frame rate to have.
        /// </param>
        private void MeasureFrameRate(double now, bool animated)
        {
            if (!animated)
            {
                _rateFrames = 0;
                _rateWindowStart = now;
                _framesPerSecond = 0;
                return;
            }

            _rateFrames++;

            var elapsed = now - _rateWindowStart;
            if (elapsed < RateWindowSeconds)
                return;

            _framesPerSecond = _rateFrames / elapsed;
            _rateFrames = 0;
            _rateWindowStart = now;
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

            // The inverse of what the draw translates by, and deliberately not the rounded
            // form: snapping the origin here the way OriginPixels does would put the hit
            // test up to half a tile away from the draw at the smallest zoom levels.
            //
            // Floor rather than a cast: a cast truncates towards zero, which would fold the
            // whole strip from -tileSize to +tileSize into column 0.
            return new TileCoordinate(
                (int)Math.Floor(options.OriginX + (position.X / options.TileSize)),
                (int)Math.Floor(options.OriginY + (position.Y / options.TileSize)));
        }

        /// <summary>
        /// The origin in whole pixels, which is what the tiles are translated by.
        /// <para>
        /// Rounded, because the render components build their sprites and atlases at whole
        /// pixel sizes and a fractional translate makes Skia resample every one of them --
        /// a blurred map, and the sampling cost paid on every tile of every frame.
        /// </para>
        /// </summary>
        private static SKPoint OriginPixels(MapViewOptions options) => new(
            (float)Math.Round(options.OriginX * options.TileSize),
            (float)Math.Round(options.OriginY * options.TileSize));

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

        /// <summary>
        /// Runs <paramref name="command"/> if it will have it, and reports whether it did --
        /// which is what lets the wheel handler leave an event it could not use unhandled.
        /// </summary>
        private static bool Execute<T>(ICommand? command, T parameter)
        {
            if (command is null || !command.CanExecute(parameter))
                return false;

            command.Execute(parameter);
            return true;
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
        /// The mini-map, held as a picture between frames.
        /// <para>
        /// The inset is the whole map at inset scale, so drawing it costs a pass over every
        /// tile there is -- and unlike the map pass, none of it can be culled, because all of
        /// it is on screen. On a large map that is the single most expensive thing in the
        /// frame, and it is spent redrawing a picture that is a few hundred pixels across and
        /// has usually not changed.
        /// </para>
        /// <para>
        /// So it is drawn once and kept. The cost of a rebuild is unchanged; what changes is
        /// how often one happens: only when the tiles are replaced, and then no more than
        /// once every <see cref="RefreshSeconds"/>. The inset therefore does not animate, and
        /// lags an edit by up to that long. Both are the right trade for an overview: at one
        /// pixel a tile there is nothing in the animation left to see, and a hover highlight
        /// is smaller than a pixel.
        /// </para>
        /// </summary>
        private sealed class MiniMapCache
        {
            /// <summary>How long a picture may stand while the tiles under it change.</summary>
            private const double RefreshSeconds = 1.0;

            /// <summary>
            /// Largest picture worth holding, in pixels. Past this the caller draws straight
            /// through instead: a mini-map that costs a frame is better than one that costs
            /// a hundred megabytes.
            /// </summary>
            private const long MaxSnapshotPixels = 8L * 1024 * 1024;

            private SKImage? _image;

            // The one before it, dropped only when a third arrives. A frame that has already
            // drawn from an image may not have reached the GPU yet, and disposing it the
            // moment its replacement exists is how that turns into a use after free.
            private SKImage? _previous;

            private TileGrid? _tiles;
            private int _width;
            private int _height;
            private double _builtAt;

            /// <summary>
            /// The current picture of the mini-map, rebuilding it first if it is stale. Null
            /// when the map is too large to hold as one, which the caller answers by drawing
            /// the tiles itself.
            /// </summary>
            public SKImage? ImageFor(
                IMapRenderer renderer,
                RenderFrame frame,
                TileGrid tiles,
                int tileSize,
                int deviceScale,
                double nowSeconds)
            {
                // Built at the resolution it will be drawn at, not at the control's, so the
                // picture is exactly as sharp as the draw it stands in for.
                var pixelTileSize = tileSize * deviceScale;
                var width = tiles.Width * pixelTileSize;
                var height = tiles.Height * pixelTileSize;

                if ((long)width * height > MaxSnapshotPixels)
                    return null;

                var sameShape = _image is not null && _width == width && _height == height;
                if (sameShape && (ReferenceEquals(_tiles, tiles) || nowSeconds - _builtAt < RefreshSeconds))
                    return _image;

                return Build(renderer, frame, tiles, pixelTileSize, width, height, nowSeconds);
            }

            private SKImage? Build(
                IMapRenderer renderer,
                RenderFrame frame,
                TileGrid tiles,
                int pixelTileSize,
                int width,
                int height,
                double nowSeconds)
            {
                using var surface = SKSurface.Create(
                    new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));

                // Out of memory, or a size Skia would not take. Whatever is already cached is
                // a better answer than a blank corner.
                if (surface is null)
                    return _image;

                var canvas = surface.Canvas;
                canvas.Clear(SKColors.Transparent);

                // The map's own origin to the picture's, so a map that does not start at
                // (0, 0) still lands inside it.
                canvas.Translate(-tiles.OriginX * pixelTileSize, -tiles.OriginY * pixelTileSize);

                renderer.RenderTiles(canvas, frame with { TileSize = pixelTileSize }, tiles)
                    .GetAwaiter().GetResult();

                _previous?.Dispose();
                _previous = _image;
                _image = surface.Snapshot();

                _tiles = tiles;
                _width = width;
                _height = height;
                _builtAt = nowSeconds;

                return _image;
            }
        }

        /// <summary>
        /// Carries one frame's worth of arguments over to the render thread, where Avalonia
        /// hands back the <see cref="SKCanvas"/> it is compositing into.
        /// </summary>
        private sealed class MapDrawOperation(
            Rect bounds,
            IMapRenderer renderer,
            RenderFrame frame,
            TileGrid tiles,
            IReadOnlyList<MapTile> visible,
            SKPoint originPixels,
            Rect? miniMap,
            MiniMapCache miniMapCache,
            double nowSeconds,
            double? framesPerSecond) : ICustomDrawOperation
        {
            // Monospaced, so the box does not twitch as the digits change under it. Null when
            // the machine has no such face, which Skia reads as "use the default".
            private static readonly SKTypeface? LabelTypeface =
                SKTypeface.FromFamilyName("Consolas");

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

                    // The origin scrolls the tiles and nothing else. The inset below is
                    // screen furniture pinned to its corner, so the translate is unwound
                    // before it rather than left standing.
                    var scrolled = canvas.Save();
                    try
                    {
                        canvas.Translate(-originPixels.X, -originPixels.Y);

                        // Every render function completes synchronously -- the Task on
                        // IMapRenderer is there for the ones that may not always. Unwrapped
                        // rather than left dangling so a failed draw surfaces here and not
                        // on the finalizer thread.
                        renderer.RenderTiles(canvas, frame, visible).GetAwaiter().GetResult();
                    }
                    finally
                    {
                        canvas.RestoreToCount(scrolled);
                    }

                    // After the map, so the inset sits over it rather than under. The inset
                    // shows the whole map, so it gets the grid and not the screenful.
                    if (miniMap is { } inset)
                        DrawMiniMap(canvas, inset);

                    // Last of all: an instrument reading the frame it is drawn in should be
                    // on top of everything that frame cost.
                    if (framesPerSecond is { } rate)
                        DrawFrameRate(canvas, rate);
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
                var rect = SKRect.Create(
                    (float)inset.X, (float)inset.Y, (float)inset.Width, (float)inset.Height);

                // Whole pixels, because the renderer takes an int tile size -- and at least
                // one, since a map wider than the inset still has to show something.
                var tileSize = Math.Max(
                    1, Math.Min((int)(rect.Width / tiles.Width), (int)(rect.Height / tiles.Height)));

                // The inset is drawn in the control's coordinates but lands on a surface that
                // may be scaled up for the display. Building the snapshot at that scale is
                // what keeps it as sharp as the direct draw it replaces.
                var deviceScale = Math.Max(1, (int)MathF.Ceiling(canvas.TotalMatrix.ScaleX));

                var snapshot = miniMapCache.ImageFor(
                    renderer, frame, tiles, tileSize, deviceScale, nowSeconds);

                var checkpoint = canvas.Save();
                try
                {
                    canvas.ClipRect(rect);

                    // A scrim behind the tiles. The map still reads through the letterbox
                    // bars of a map that is not square, but dimmed enough that the inset
                    // holds together as one panel.
                    using (var backdrop = new SKPaint { Color = new SKColor(0, 0, 0, 170) })
                        canvas.DrawRect(rect, backdrop);

                    var width = tiles.Width * tileSize;
                    var height = tiles.Height * tileSize;

                    var left = rect.Left + ((rect.Width - width) / 2f);
                    var top = rect.Top + ((rect.Height - height) / 2f);

                    if (snapshot is not null)
                    {
                        // The picture is built at the display resolution and drawn at the
                        // control's, so it is shrunk by the display scale and no further --
                        // which bilinear covers. Nearest would alias that down to a stipple,
                        // and mip sampling would pay for a reduction that never happens.
                        using var sampling = new SKPaint
                        {
                            FilterQuality = SKFilterQuality.Low,
                            IsAntialias = true,
                        };

                        canvas.DrawImage(snapshot, SKRect.Create(left, top, width, height), sampling);
                    }
                    else
                    {
                        // Too big to hold as a picture. Drawn straight through instead, which
                        // is what this always used to do: slower per frame, but a mini-map
                        // that costs a frame is better than one that costs the memory.
                        canvas.Translate(left - (tiles.OriginX * tileSize), top - (tiles.OriginY * tileSize));
                        renderer.RenderTiles(canvas, frame with { TileSize = tileSize }, tiles)
                            .GetAwaiter().GetResult();
                    }
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
            /// The frame rate, in a pill in the top-right corner.
            /// <para>
            /// Pushed below the mini-map when the inset is in that corner too. The inset was
            /// there first and is the bigger thing to hide behind, and a number sitting on
            /// top of a map of the map is unreadable anyway.
            /// </para>
            /// </summary>
            private void DrawFrameRate(SKCanvas canvas, double rate)
            {
                const float margin = 12f;
                const float padX = 8f;
                const float padY = 4f;

                // A dash rather than a zero before the first window closes, and whenever the
                // repaint loop is off: no measurement is a different thing from no frames.
                var text = rate > 0 ? $"{rate:0} FPS" : "-- FPS";

                using var label = new SKPaint
                {
                    Color = new SKColor(0xE6, 0xEE, 0xFA),
                    IsAntialias = true,
                    TextSize = 12f,
                    TextAlign = SKTextAlign.Right,
                    Typeface = LabelTypeface,
                };

                var metrics = label.FontMetrics;
                var width = label.MeasureText(text) + (padX * 2);
                var height = metrics.Descent - metrics.Ascent + (padY * 2);

                var box = SKRect.Create((float)Bounds.Width - margin - width, margin, width, height);

                if (miniMap is { } inset)
                {
                    var insetRect = new SKRect(
                        (float)inset.X, (float)inset.Y, (float)inset.Right, (float)inset.Bottom);

                    if (box.IntersectsWith(insetRect))
                        box.Offset(0, insetRect.Bottom + margin - box.Top);
                }

                using var backdrop = new SKPaint
                {
                    Color = new SKColor(0, 0, 0, 140),
                    IsAntialias = true,
                };

                canvas.DrawRoundRect(box, 6f, 6f, backdrop);
                canvas.DrawText(text, box.Right - padX, box.Top + padY - metrics.Ascent, label);
            }

            public void Dispose()
            {
            }
        }
    }
}
