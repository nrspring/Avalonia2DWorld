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
using Avalonia2DWorld.Map.Interfaces;
using Avalonia2DWorld.Map.Models;
using SkiaSharp;

namespace Avalonia2DWorld.Map.Controls
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
        /// The writing placed on the map, drawn over the tiles. Null or empty for a map with
        /// nothing written on it, which is what one opens as.
        /// <para>
        /// A list of its own beside <see cref="TilesProperty"/> and not part of it, because a
        /// label is positioned in <see cref="MapPixel"/> and belongs to no tile -- see
        /// <see cref="MapLabel"/>. There is nowhere in the grid to put one, and a map may
        /// carry a hundred labels where it carries a million tiles, so the two want different
        /// treatment in every respect.
        /// </para>
        /// <para>
        /// Handed to the render thread by reference and read there, on the same terms as the
        /// tiles: publish a new list rather than editing the one that was assigned. Labels are
        /// counted in dozens, so a fresh array per edit costs nothing worth the machinery
        /// <see cref="TileMap"/> needs.
        /// </para>
        /// </summary>
        public static readonly StyledProperty<IReadOnlyList<MapLabel>?> LabelsProperty =
            AvaloniaProperty.Register<MapView, IReadOnlyList<MapLabel>?>(nameof(Labels));

        /// <summary>
        /// A line to show beside the pointer, or null for none.
        /// <para>
        /// The control draws whatever string it is handed and has no opinion about where it came
        /// from -- which is the only arrangement that could work, since what is worth saying
        /// about a tile is a fact about the world and this library has none. Deciding it is the
        /// caller's; see <c>ComponentParams.HoverTextOf</c>, which is where this map answers it.
        /// </para>
        /// <para>
        /// Beside the pointer rather than pinned to a corner, because it is about the thing under
        /// the pointer and the eye is already there. It keeps clear of the cursor and folds back
        /// inside the control at the edges, so it is readable in the corner of the map as well as
        /// the middle of it.
        /// </para>
        /// </summary>
        public static readonly StyledProperty<string?> HoverTextProperty =
            AvaloniaProperty.Register<MapView, string?>(nameof(HoverText));

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
        /// Invoked with the <see cref="MapPixel"/> that was clicked: the same left click
        /// <see cref="ClickCommandProperty"/> reports, said to the pixel instead of to the
        /// tile. The parameter is never null.
        /// <para>
        /// As well as that command and not instead of it. The two answer different questions
        /// about one gesture -- which square was clicked, and where exactly -- and a caller
        /// that wants both wants them about the same click. Bind whichever say what the tool
        /// in hand needs; a caller with tile tools and pixel tools binds both and each of its
        /// handlers ignores the clicks that are not its own.
        /// </para>
        /// <para>
        /// Fired on release rather than on press, and only when the pointer did not move --
        /// see <see cref="PixelDragCommandProperty"/>, which is the same button. The tile
        /// click above still comes on the way down: a tile is either clicked or it is not,
        /// and there is no gesture it has to be told apart from.
        /// </para>
        /// <para>
        /// Not fired over the mini-map inset, for the reason the tile is not: the inset draws
        /// the whole map at its own scale, so the arithmetic would name a point far from the
        /// one being pointed at.
        /// </para>
        /// </summary>
        public static readonly StyledProperty<ICommand?> PixelClickCommandProperty =
            AvaloniaProperty.Register<MapView, ICommand?>(nameof(PixelClickCommand));

        /// <summary>
        /// Invoked with a <see cref="MapPixelDrag"/> at each step of a left-button drag across
        /// the map: once as it starts, once per move, and once as it ends. The parameter is
        /// never null.
        /// <para>
        /// The left button, because this is for dragging something that is <em>on</em> the map
        /// rather than the map itself -- which is the right button's job, and has to stay one
        /// gesture away from it.
        /// </para>
        /// <para>
        /// The control decides nothing about what is being dragged and does not ask. It reports
        /// where the button went down and where the pointer has got to, both in map pixels, and
        /// whether the caller found anything at the first of those is the caller's own affair.
        /// A drag that grabbed nothing is a drag that does nothing, and it costs a command
        /// execution per move to find that out.
        /// </para>
        /// <para>
        /// Bind it and the left button carries a drag as well as a click; leave it unbound and
        /// the button behaves exactly as it did -- nothing is captured and no gesture is
        /// tracked.
        /// </para>
        /// </summary>
        public static readonly StyledProperty<ICommand?> PixelDragCommandProperty =
            AvaloniaProperty.Register<MapView, ICommand?>(nameof(PixelDragCommand));

        /// <summary>
        /// Invoked with the <see cref="TileCoordinate"/> that was right-clicked. The parameter
        /// is never null.
        /// <para>
        /// The other thing a tile can be asked to do, for the caller that has two of them and
        /// would rather not make somebody pick between them in a panel first. What that is, is
        /// none of the control's business.
        /// </para>
        /// <para>
        /// Fired on release rather than on press, and only when the map did not move under the
        /// button -- see <see cref="PanCommandProperty"/>, which is the same button.
        /// </para>
        /// </summary>
        public static readonly StyledProperty<ICommand?> RightClickCommandProperty =
            AvaloniaProperty.Register<MapView, ICommand?>(nameof(RightClickCommand));

        /// <summary>
        /// Invoked with a <see cref="MapPanRequest"/> for each step of a right-button drag
        /// across the map. The parameter is never null.
        /// <para>
        /// The right button, because the left one already means something on a map made of
        /// tiles: a left drag has to be distinguishable from a left click, and asking the
        /// control to tell those apart by how far the pointer moved before it came up would
        /// make every click wait to find out what it was.
        /// </para>
        /// <para>
        /// The right button now carries both, so it does pay that price -- but only on the
        /// button that has nothing to do on the way down. A drag pans from the first move; a
        /// press and release within <see cref="ClickSlack"/> of each other never panned
        /// anywhere and is handed to <see cref="RightClickCommandProperty"/> instead. Nothing
        /// waits: the pan still starts on the first move, and it is the click that is decided
        /// last, which is the one that can afford to be.
        /// </para>
        /// <para>
        /// Bind it and the map can be dragged around; leave it unbound and a right drag does
        /// nothing, and the event is left for whatever else may want it.
        /// </para>
        /// </summary>
        public static readonly StyledProperty<ICommand?> PanCommandProperty =
            AvaloniaProperty.Register<MapView, ICommand?>(nameof(PanCommand));

        /// <summary>
        /// Invoked with a <see cref="MiniMapRequest"/> when the left button goes down inside
        /// the mini-map inset. The parameter is never null.
        /// <para>
        /// Separate from <see cref="ClickCommand"/> because the two mean different things: a
        /// click on the map is about a tile, and a click on the inset is about the map. The
        /// inset sits over the tiles it is showing, so a press inside it goes here and
        /// nowhere else -- the tile underneath is hidden, and nobody aiming at the inset
        /// meant to pick it.
        /// </para>
        /// <para>
        /// Bind it and the inset becomes a way to move the view; leave it unbound and the
        /// inset stays what it was, a picture you cannot click.
        /// </para>
        /// </summary>
        public static readonly StyledProperty<ICommand?> MiniMapCommandProperty =
            AvaloniaProperty.Register<MapView, ICommand?>(nameof(MiniMapCommand));

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

        /// <summary>
        /// How far the pointer may wander between a press and its release and still count as a
        /// click rather than a drag.
        /// <para>
        /// Not zero, because a hand on a mouse moves a pixel or two on the way to letting go,
        /// and a tool that ignored every second click would read as broken. Small, because the
        /// gesture it has to stay clear of is a deliberate drag across the map.
        /// </para>
        /// <para>
        /// One number for both buttons. They carry different pairs of gestures -- the right a
        /// pan and a click, the left a drag and a click -- but the thing being measured is the
        /// same hand on the same mouse, and two figures for it would mean the two buttons
        /// disagreed about what holding still is.
        /// </para>
        /// </summary>
        private const double ClickSlack = 4;

        // Held rather than made per pointer move: the pointer moves a great many times.
        private static readonly Cursor PickCursor = new(StandardCursorType.Hand);
        private static readonly Cursor PanCursor = new(StandardCursorType.SizeAll);

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

        // Whether the button that went down inside the inset is still down. While it is,
        // the pointer is captured and every move it makes is a move of the view.
        private bool _draggingMiniMap;

        // Where the pointer was on the previous step of a right-button drag, or null when
        // there is no drag. Kept as a position rather than a total, because what goes to the
        // command is the step and not the gesture.
        private Point? _panFrom;

        /// <summary>
        /// Where the right button went down, kept still while <see cref="_panFrom"/> walks with
        /// the pointer. What the release is measured against to tell a click from a drag.
        /// </summary>
        private Point? _rightPressedAt;

        /// <summary>
        /// Where the left button went down, in map pixels, or null when it is not down over the
        /// map. Fixed for the whole gesture: it is what every step of a drag is measured from,
        /// and what tells a click from a drag when the button comes up.
        /// <para>
        /// Kept in map pixels rather than in control pixels so that it survives the view moving
        /// under it -- a wheel turn mid-drag rescales every control pixel and leaves this one
        /// naming the same piece of ground.
        /// </para>
        /// </summary>
        private MapPixel? _pixelFrom;

        /// <inheritdoc cref="_pixelFrom"/>
        private Point? _leftPressedAt;

        /// <summary>
        /// Whether a <see cref="PixelDragCommandProperty"/> gesture is in flight, so that the
        /// finish is sent once and only for a drag that was started.
        /// </summary>
        private bool _dragging;

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
            AffectsRender<MapView>(
                TilesProperty, LabelsProperty, HoverTextProperty, RendererProperty, OptionsProperty);
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

        /// <inheritdoc cref="LabelsProperty"/>
        public IReadOnlyList<MapLabel>? Labels
        {
            get => GetValue(LabelsProperty);
            set => SetValue(LabelsProperty, value);
        }

        /// <inheritdoc cref="HoverTextProperty"/>
        public string? HoverText
        {
            get => GetValue(HoverTextProperty);
            set => SetValue(HoverTextProperty, value);
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

        /// <inheritdoc cref="PixelClickCommandProperty"/>
        public ICommand? PixelClickCommand
        {
            get => GetValue(PixelClickCommandProperty);
            set => SetValue(PixelClickCommandProperty, value);
        }

        /// <inheritdoc cref="PixelDragCommandProperty"/>
        public ICommand? PixelDragCommand
        {
            get => GetValue(PixelDragCommandProperty);
            set => SetValue(PixelDragCommandProperty, value);
        }

        /// <inheritdoc cref="RightClickCommandProperty"/>
        public ICommand? RightClickCommand
        {
            get => GetValue(RightClickCommandProperty);
            set => SetValue(RightClickCommandProperty, value);
        }

        /// <inheritdoc cref="PanCommandProperty"/>
        public ICommand? PanCommand
        {
            get => GetValue(PanCommandProperty);
            set => SetValue(PanCommandProperty, value);
        }

        /// <inheritdoc cref="MiniMapCommandProperty"/>
        public ICommand? MiniMapCommand
        {
            get => GetValue(MiniMapCommandProperty);
            set => SetValue(MiniMapCommandProperty, value);
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
                // The whole grid rides along with the frame, not just the screenful below it:
                // a component that reads its neighbours has to get the same answer for a tile
                // whether it is in the middle of the window or at its edge. See RenderFrame.
                new RenderFrame(options.TileSize, (float)_animationSeconds, tiles),
                tiles,
                Labels,
                VisibleWindow(tiles, options),
                OriginPixels(options),
                miniMap,
                new Rect(
                    options.OriginX,
                    options.OriginY,
                    Bounds.Width / options.TileSize,
                    Bounds.Height / options.TileSize),
                _miniMapCache,
                now,
                options.ShowFrameRate ? _framesPerSecond : null,
                HoverText,
                _pointer));

            RequestNextFrame(options);
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);

            _pointer = e.GetPosition(this);

            if (_draggingMiniMap)
            {
                // The view follows the pointer until the button comes up, wherever it goes:
                // dragging to the edge of the inset and then past it is how anyone asks to
                // keep going, and letting go of the drag because the pointer slipped off a
                // 160 pixel square would be its own kind of wrong.
                if (DraggedMiniMapRequest(_pointer.Value) is { } request)
                    Execute(MiniMapCommand, request);

                // No hover while dragging. The map is sliding under a pointer that is not
                // pointing at it, and a highlight skidding across the tiles is noise.
                SetHovered(null);
                e.Handled = true;
                return;
            }

            if (_panFrom is { } previous)
            {
                var options = Options ?? MapViewOptions.Default;

                Execute(PanCommand, new MapPanRequest(
                    (_pointer.Value.X - previous.X) / options.TileSize,
                    (_pointer.Value.Y - previous.Y) / options.TileSize,
                    Bounds.Width / options.TileSize,
                    Bounds.Height / options.TileSize));

                _panFrom = _pointer;
                e.Handled = true;

                // The hover is left alone rather than suppressed: the pointer really is over
                // the map, and the tile under it changes as the map slides past. Moving the
                // options re-resolves it, so the highlight stays where the cursor is.
                return;
            }

            if (_dragging && _pixelFrom is { } grabbed && ToPixel(_pointer.Value) is { } at)
            {
                Execute(PixelDragCommand, new MapPixelDrag(grabbed, at, MapDragPhase.Moved));
                e.Handled = true;

                // The hover is left to follow the pointer as usual. Whatever is being dragged
                // is over the map, not instead of it, and the tile under the cursor is still
                // the tile under the cursor.
            }

            SetHovered(ToTile(_pointer.Value));
            UpdateCursor();

            // Only while there is something to show. A label beside the pointer has to keep up
            // with it, and the tile under it does not change on most moves -- so without this the
            // text would stay where the pointer entered the tile until it left again. Nothing is
            // asked for when nothing is being said, which is nearly always.
            if (HoverText is { Length: > 0 })
                InvalidateVisual();
        }

        protected override void OnPointerExited(PointerEventArgs e)
        {
            base.OnPointerExited(e);

            // Not while dragging: the pointer leaving the control during a capture is
            // normal, and the drag is still going on.
            if (_draggingMiniMap || _panFrom is not null || _dragging)
                return;

            _pointer = null;
            SetHovered(null);
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);

            var point = e.GetCurrentPoint(this);
            var position = e.GetPosition(this);

            if (point.Properties.IsRightButtonPressed)
            {
                // Not over the inset, which is a picture of the whole map: it has nothing to
                // pan and no tile of its own to change. And not without somewhere to send
                // either half of the gesture -- bind neither command and the button is left
                // alone for whatever else may want it.
                if ((PanCommand is null && RightClickCommand is null)
                    || MiniMapRequestAt(position) is not null)
                    return;

                _panFrom = position;
                _rightPressedAt = position;
                e.Pointer.Capture(this);

                // Only where a drag would actually go somewhere. With just the click bound
                // there is nothing to drag, and a cursor promising otherwise would be a lie
                // held for as long as the button is down.
                if (PanCommand is not null)
                    Cursor = PanCursor;

                e.Handled = true;
                return;
            }

            if (!point.Properties.IsLeftButtonPressed)
                return;

            // The inset first, since it is drawn over the map: a press inside it is about the
            // inset, and the tile behind it is not what anyone was pointing at. Handled only
            // if something took it, so an unbound inset still behaves like the picture it is.
            if (MiniMapRequestAt(position) is { } request)
            {
                if (Execute(MiniMapCommand, request))
                {
                    // Captured, so the drag that may follow keeps arriving here even once the
                    // pointer has left the inset -- or the control.
                    e.Pointer.Capture(this);
                    _draggingMiniMap = true;
                    e.Handled = true;
                }

                return;
            }

            if (ToTile(position) is not { } tile)
                return;

            // The pointer is over a tile whether or not a move was seen first -- a touch or a
            // pen never sends one -- so the hover is brought up to date before the click
            // rather than left stale.
            SetHovered(tile);
            Execute(ClickCommand, tile);

            // The pixel half of the same press opens a gesture rather than reporting a click.
            // What it turns out to have been -- a click, or a drag -- is settled on release;
            // see PixelClickCommandProperty and PixelDragCommandProperty, which are the two
            // ways it can end.
            if (ToPixel(position) is { } pixel)
            {
                _pixelFrom = pixel;
                _leftPressedAt = position;

                if (PixelDragCommand is not null)
                {
                    // Captured so the drag keeps arriving here once the pointer has left the
                    // control, which is how anything gets dragged to the edge of the map.
                    e.Pointer.Capture(this);
                    _dragging = true;
                    Execute(PixelDragCommand, new MapPixelDrag(pixel, pixel, MapDragPhase.Started));
                }
            }

            e.Handled = true;
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);

            if (_panFrom is not null)
            {
                var releasedAt = e.GetPosition(this);

                // Where the button went down against where it came up. A drag has moved the
                // map out from under the pointer by now and is finished; anything that stayed
                // put was aimed at the tile it is still over.
                var clicked = _rightPressedAt is { } from
                    && Math.Abs(releasedAt.X - from.X) <= ClickSlack
                    && Math.Abs(releasedAt.Y - from.Y) <= ClickSlack;

                _panFrom = null;
                _rightPressedAt = null;
                e.Pointer.Capture(null);
                e.Handled = true;
                UpdateCursor();

                if (clicked && ToTile(releasedAt) is { } tile)
                {
                    // Brought up to date first, for the reason the left button does it: a
                    // touch or a pen reaches here having never sent a move, and the highlight
                    // should be on the tile that is about to change.
                    SetHovered(tile);
                    Execute(RightClickCommand, tile);
                }

                return;
            }

            // The button that started it, not merely a button coming up: a right click taken
            // while the left one is held would otherwise end the left gesture for it.
            if (_pixelFrom is not null && e.InitialPressMouseButton == MouseButton.Left)
            {
                EndPixelGesture(e.GetPosition(this), clicked: true);
                e.Pointer.Capture(null);
                e.Handled = true;
                UpdateCursor();
                return;
            }

            if (!_draggingMiniMap)
                return;

            _draggingMiniMap = false;
            e.Pointer.Capture(null);
            e.Handled = true;

            // Back to whatever the pointer is now over: it may have been let go anywhere,
            // including over a tile that should light up.
            if (_pointer is { } position)
            {
                SetHovered(ToTile(position));
                UpdateCursor();
            }
        }

        /// <summary>
        /// Ends the drag when the capture goes elsewhere -- another control taking it, the
        /// window losing focus mid-drag. Without this the control would think the button was
        /// still down and pan on the next stray move.
        /// </summary>
        protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
        {
            base.OnPointerCaptureLost(e);

            _draggingMiniMap = false;
            _panFrom = null;
            _rightPressedAt = null;

            // Finished rather than dropped: a caller that picked something up on the way down
            // is holding it, and losing the capture is not a reason to make it wait for a
            // release that is never coming. Never a click -- nobody clicked anything.
            EndPixelGesture(_pointer, clicked: false);

            UpdateCursor();
        }

        /// <summary>
        /// Closes the left-button gesture: the drag if one was started, then the click if the
        /// pointer never left where it went down.
        /// <para>
        /// One place, because both ways out of the gesture -- the button coming up, the capture
        /// going elsewhere -- have to leave exactly the same state behind, and the one that
        /// does not is the one that leaves the next stray move dragging something.
        /// </para>
        /// </summary>
        /// <param name="releasedAt">
        /// Where the pointer ended up, or null if that is not known -- a capture lost while the
        /// pointer was off the control. The drag then finishes where it last was.
        /// </param>
        /// <param name="clicked">
        /// Whether a click is still possible. False where the gesture was cut short rather than
        /// let go of.
        /// </param>
        private void EndPixelGesture(Point? releasedAt, bool clicked)
        {
            if (_pixelFrom is not { } from)
                return;

            var at = releasedAt is { } position ? ToPixel(position) ?? from : from;
            var dragged = _dragging;

            // Cleared before the commands run: a handler is free to do anything, including
            // something that comes back through this control, and it should not find a gesture
            // still standing that has already ended.
            _pixelFrom = null;
            _dragging = false;

            var stayedPut = clicked
                && _leftPressedAt is { } pressed
                && releasedAt is { } up
                && Math.Abs(up.X - pressed.X) <= ClickSlack
                && Math.Abs(up.Y - pressed.Y) <= ClickSlack;

            _leftPressedAt = null;

            if (dragged)
                Execute(PixelDragCommand, new MapPixelDrag(from, at, MapDragPhase.Finished));

            // After the drag has been closed out, so a handler that puts down what it was
            // holding has already done so by the time it is told the same spot was clicked.
            if (stayedPut)
                Execute(PixelClickCommand, at);
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
            // Not while dragging the inset, where the options change on every move and the
            // tile under the pointer is the inset itself.
            if (change.Property == OptionsProperty && !_draggingMiniMap && _pointer is { } position)
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
            _draggingMiniMap = false;
            _panFrom = null;
            _rightPressedAt = null;

            // Told it is over, rather than forgotten: a caller holding something picked up on
            // the way down gets its finish here as it would anywhere else.
            EndPixelGesture(_pointer, clicked: false);

            _pointer = null;
            Cursor = null;
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
        /// A press at <paramref name="position"/> as a <see cref="MiniMapRequest"/>, or null
        /// when it is not inside the mini-map inset.
        /// </summary>
        private MiniMapRequest? MiniMapRequestAt(Point position)
        {
            var options = Options ?? MapViewOptions.Default;

            if (Tiles is not { } grid ||
                MiniMapRect(Bounds.Size, options) is not { } inset ||
                !inset.Contains(position))
            {
                return null;
            }

            return MiniMapRequestFrom(inset, grid, options, position);
        }

        /// <summary>
        /// The same, for a pointer that is mid-drag and so allowed to be anywhere. The point
        /// it names is pinned to the edge of the inset rather than running off the map, which
        /// is what makes dragging past the corner stop at the corner instead of flinging the
        /// view into empty space.
        /// </summary>
        private MiniMapRequest? DraggedMiniMapRequest(Point position)
        {
            var options = Options ?? MapViewOptions.Default;

            if (Tiles is not { } grid || MiniMapRect(Bounds.Size, options) is not { } inset)
                return null;

            return MiniMapRequestFrom(inset, grid, options, new Point(
                Math.Clamp(position.X, inset.X, inset.Right),
                Math.Clamp(position.Y, inset.Y, inset.Bottom)));
        }

        /// <summary>
        /// A point inside the inset as the map coordinate it stands for. The inset shows the
        /// whole map across its own width, so the coordinate is that fraction of the way
        /// across it -- fractional on purpose, since a caller centring the view wants the
        /// point and not the tile it happened to fall in.
        /// </summary>
        private MiniMapRequest MiniMapRequestFrom(
            Rect inset, TileGrid grid, MapViewOptions options, Point position) => new(
                grid.OriginX + ((position.X - inset.X) / inset.Width * grid.Width),
                grid.OriginY + ((position.Y - inset.Y) / inset.Height * grid.Height),
                Bounds.Width / options.TileSize,
                Bounds.Height / options.TileSize);

        /// <summary>
        /// The hand over the inset, and while a drag from it is still going. The inset is the
        /// one part of the control that answers a press with something other than a tile, and
        /// nothing else about it says so.
        /// </summary>
        private void UpdateCursor()
        {
            if (_panFrom is not null)
            {
                Cursor = PanCursor;
                return;
            }

            var overInset = _pointer is { } position && MiniMapRequestAt(position) is not null;

            Cursor = MiniMapCommand is not null && (overInset || _draggingMiniMap)
                ? PickCursor
                : null;
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
        /// Pixel position within the control to the point on the map it lands on, or null over
        /// the mini-map inset.
        /// <para>
        /// <see cref="ToTile"/> without the rounding down, in the space
        /// <see cref="MapPixel"/> defines: the same arithmetic, kept fractional and then scaled
        /// off the zoom, so that a point clicked at one tile size names the same place at every
        /// other one.
        /// </para>
        /// </summary>
        private MapPixel? ToPixel(Point position)
        {
            var options = Options ?? MapViewOptions.Default;

            if (MiniMapRect(Bounds.Size, options) is { } inset && inset.Contains(position))
                return null;

            return MapPixel.FromTiles(
                options.OriginX + (position.X / options.TileSize),
                options.OriginY + (position.Y / options.TileSize));
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
        /// Where the mini-map inset sits, or null when it is off, there is no map, or the
        /// control is too small to give it room.
        /// <para>
        /// The inset is the shape of the map: <see cref="MapViewOptions.MiniMapSize"/> is the
        /// longer edge and the other follows the map's proportions. A fixed square would have
        /// to either letterbox a map that is not square or crop it, and cropping is the worse
        /// of the two -- an overview that leaves out the part you are not looking at is not
        /// an overview.
        /// </para>
        /// <para>
        /// Depends on the map only through its width and height, which the grid carries, so
        /// this is still cheap enough for <see cref="ToTile"/> to consult on every move.
        /// </para>
        /// </summary>
        private Rect? MiniMapRect(Size bounds, MapViewOptions options)
        {
            if (options.MiniMap == MiniMapLocation.Off || Tiles is not { } grid)
                return null;

            double budget = options.MiniMapSize;
            double margin = options.MiniMapMargin;

            var size = grid.Width >= grid.Height
                ? new Size(budget, budget * grid.Height / grid.Width)
                : new Size(budget * grid.Width / grid.Height, budget);

            // Dropped rather than shrunk when it will not fit: an inset scaled down to suit a
            // narrow window stops being readable long before it stops fitting.
            if (bounds.Width < size.Width + (2 * margin) || bounds.Height < size.Height + (2 * margin))
                return null;

            var left = options.MiniMap is MiniMapLocation.UpperLeft or MiniMapLocation.LowerLeft
                ? margin
                : bounds.Width - size.Width - margin;

            var top = options.MiniMap is MiniMapLocation.UpperLeft or MiniMapLocation.UpperRight
                ? margin
                : bounds.Height - size.Height - margin;

            return new Rect(left, top, size.Width, size.Height);
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

            /// <summary>
            /// Who drew the picture. Part of what makes it stale: the view model may offer a
            /// choice of renderers, and a cached inset drawn by the one that has just been
            /// switched away from would stand there until the tiles themselves changed.
            /// </summary>
            private IMapRenderer? _renderer;

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

                var sameShape = _image is not null && _width == width && _height == height &&
                    ReferenceEquals(_renderer, renderer);

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
                _renderer = renderer;
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
            IReadOnlyList<MapLabel>? labels,
            IReadOnlyList<MapTile> visible,
            SKPoint originPixels,
            Rect? miniMap,
            Rect viewportTiles,
            MiniMapCache miniMapCache,
            double nowSeconds,
            double? framesPerSecond,
            string? hoverText,
            Point? pointer) : ICustomDrawOperation
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
                        // The context goes only to the map pass. The mini-map below draws
                        // into a raster surface of its own, where an offscreen built on this
                        // would be an image from the wrong device.
                        renderer.RenderTiles(canvas, frame with { Gpu = lease.GrContext }, visible)
                            .GetAwaiter().GetResult();

                        // After the tiles and inside the same translate: writing on a map is
                        // over the map and scrolls with it. Deliberately not in the mini-map
                        // pass below -- the inset is a picture of where things are, and a name
                        // written across it at that scale would hide the very thing it names.
                        if (labels is { Count: > 0 })
                            renderer.RenderLabels(canvas, frame, labels).GetAwaiter().GetResult();
                    }
                    finally
                    {
                        canvas.RestoreToCount(scrolled);
                    }

                    // After the map, so the inset sits over it rather than under. The inset
                    // shows the whole map, so it gets the grid and not the screenful.
                    if (miniMap is { } inset)
                        DrawMiniMap(canvas, inset);

                    // Over the map and the inset both: it is about whatever the pointer is
                    // on, and the pointer is over all of it.
                    if (hoverText is { Length: > 0 } && pointer is { } at)
                        DrawHoverText(canvas, hoverText, at);

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
                // one, since a map with more tiles than the inset has pixels still has to
                // show all of them. The picture is scaled to the inset when it is drawn, so
                // this only decides how much detail is in it, not how much of the map.
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

                    if (snapshot is not null)
                    {
                        // The inset already has the map's proportions, so the whole picture
                        // goes in the whole box: no crop, no bars, and the scale is the same
                        // on both axes.
                        //
                        // Mip sampling once the picture is being reduced by more than a
                        // little, which it is on any map with more tiles than the inset has
                        // pixels; bilinear alone turns that into a stipple of stray tiles.
                        var reduction = snapshot.Width / Math.Max(1f, rect.Width * deviceScale);

                        using var sampling = new SKPaint
                        {
                            FilterQuality = reduction > 1.2f ? SKFilterQuality.Medium : SKFilterQuality.Low,
                            IsAntialias = true,
                        };

                        canvas.DrawImage(snapshot, rect, sampling);
                    }
                    else
                    {
                        // Too big to hold as a picture. Drawn straight through instead, which
                        // is what this always used to do: slower per frame, but a mini-map
                        // that costs a frame is better than one that costs the memory.
                        //
                        // Inside its own save: what follows is drawn in the inset's
                        // coordinates, not in the scaled map ones this needs.
                        var scaled = canvas.Save();
                        try
                        {
                            canvas.Translate(rect.Left, rect.Top);
                            canvas.Scale(
                                rect.Width / (tiles.Width * tileSize),
                                rect.Height / (tiles.Height * tileSize));
                            canvas.Translate(-tiles.OriginX * tileSize, -tiles.OriginY * tileSize);

                            renderer.RenderTiles(canvas, frame with { TileSize = tileSize }, tiles)
                                .GetAwaiter().GetResult();
                        }
                        finally
                        {
                            canvas.RestoreToCount(scaled);
                        }
                    }

                    DrawViewport(canvas, rect);
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
            /// Outlines the part of the map that is on screen, inside the inset.
            /// <para>
            /// Clipped to the inset rather than drawn wherever the arithmetic lands: the view
            /// is free to sit past the edge of the map -- a map smaller than the window is
            /// centred in it, with the view hanging off both sides -- and an outline drawn
            /// out there would be a white box floating over the map itself.
            /// </para>
            /// </summary>
            private void DrawViewport(SKCanvas canvas, SKRect rect)
            {
                var scaleX = rect.Width / tiles.Width;
                var scaleY = rect.Height / tiles.Height;

                var box = SKRect.Intersect(
                    new SKRect(
                        rect.Left + ((float)(viewportTiles.X - tiles.OriginX) * scaleX),
                        rect.Top + ((float)(viewportTiles.Y - tiles.OriginY) * scaleY),
                        rect.Left + ((float)(viewportTiles.Right - tiles.OriginX) * scaleX),
                        rect.Top + ((float)(viewportTiles.Bottom - tiles.OriginY) * scaleY)),
                    rect);

                // Nothing of the map on screen, or so little that a stroke would be the whole
                // of it. A box with no inside is worse than no box.
                if (box.Width < 2f || box.Height < 2f)
                    return;

                // Half a pixel in, so the stroke sits on whole pixels, and half a pixel off
                // each edge of the inset so a view that reaches the border still shows a line
                // rather than half of one under the border's own.
                box = new SKRect(
                    Math.Max(box.Left + 0.5f, rect.Left + 0.5f),
                    Math.Max(box.Top + 0.5f, rect.Top + 0.5f),
                    Math.Min(box.Right - 0.5f, rect.Right - 0.5f),
                    Math.Min(box.Bottom - 0.5f, rect.Bottom - 0.5f));

                // A dark line under the white one. The water is dark enough that white reads
                // on its own most of the time, and pale enough where a crest catches the
                // light that sometimes it does not.
                using (var shadow = new SKPaint
                {
                    Color = new SKColor(0, 0, 0, 110),
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = 3f,
                    IsAntialias = true,
                })
                {
                    canvas.DrawRect(box, shadow);
                }

                using var outline = new SKPaint
                {
                    Color = new SKColor(255, 255, 255, 235),
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = 1.25f,
                    IsAntialias = true,
                };

                canvas.DrawRect(box, outline);
            }

            /// <summary>
            /// The frame rate, in a pill in the top-right corner.
            /// <para>
            /// Pushed below the mini-map when the inset is in that corner too. The inset was
            /// there first and is the bigger thing to hide behind, and a number sitting on
            /// top of a map of the map is unreadable anyway.
            /// </para>
            /// </summary>
            /// <summary>
            /// Writes a line beside the pointer, on a plate dark enough to read over any ground
            /// the map can put under it.
            /// <para>
            /// Below and to the right by default, which is where a hand holding a mouse is not.
            /// Where that would run off the control it flips to the other side instead of being
            /// clamped: a label sliding along the edge would sit under the very cursor it belongs
            /// to, and the corner is exactly where somebody is most likely to be pointing at
            /// something they want named.
            /// </para>
            /// </summary>
            private void DrawHoverText(SKCanvas canvas, string text, Point at)
            {
                using var pen = new SKPaint
                {
                    TextSize = 12.5f,
                    IsAntialias = true,
                    Color = new SKColor(0xEE, 0xF3, 0xFA),
                };

                var metrics = pen.FontMetrics;
                var width = pen.MeasureText(text);

                const float padX = 6f;
                const float padY = 4f;
                const float gap = 14f;

                var high = metrics.Descent - metrics.Ascent;
                var plate = SKRect.Create(
                    (float)at.X + gap,
                    (float)at.Y + gap,
                    width + (padX * 2f),
                    high + (padY * 2f));

                // Flipped rather than slid, so the plate never ends up under the cursor.
                if (plate.Right > Bounds.Width)
                    plate.Offset(-(plate.Width + (gap * 2f)), 0f);

                if (plate.Bottom > Bounds.Height)
                    plate.Offset(0f, -(plate.Height + (gap * 2f)));

                using var backing = new SKPaint
                {
                    Color = new SKColor(0x12, 0x1A, 0x26, 0xE8),
                    IsAntialias = true,
                };

                canvas.DrawRoundRect(plate, 4f, 4f, backing);

                backing.Color = new SKColor(0xFF, 0xFF, 0xFF, 0x2A);
                backing.Style = SKPaintStyle.Stroke;
                backing.StrokeWidth = 1f;

                canvas.DrawRoundRect(
                    new SKRect(plate.Left + 0.5f, plate.Top + 0.5f, plate.Right - 0.5f, plate.Bottom - 0.5f),
                    4f, 4f, backing);

                canvas.DrawText(text, plate.Left + padX, plate.Top + padY - metrics.Ascent, pen);
            }

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
