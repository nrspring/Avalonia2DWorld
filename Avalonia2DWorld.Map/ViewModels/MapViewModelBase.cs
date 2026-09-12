using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia2DWorld.Map.Interfaces;
using Avalonia2DWorld.Map.Models;

namespace Avalonia2DWorld.Map.ViewModels
{
    /// <summary>
    /// The half of a map view model that is the same in every app: what is on screen, and the
    /// gestures that move it.
    /// <para>
    /// <c>MapView</c> deliberately holds no view of its own. It reports a wheel turn, a drag,
    /// a press in the inset, and something else decides what those mean -- but "zoom about
    /// the pointer", "drag the map along with the pointer" and "centre where the inset was
    /// clicked" are the same decisions almost every time, and re-deciding them per app is how
    /// two maps end up scrolling differently for no reason.
    /// </para>
    /// <para>
    /// What is <b>not</b> here is anything about tiles: what a map is made of, what a hover or
    /// a selection looks like, and where a map comes from in the first place. Those need the
    /// render components, which live above this library, and they are the parts that really do
    /// differ from one app to the next. A derived view model owns the map, publishes
    /// <see cref="Tiles"/>, and inherits the rest.
    /// </para>
    /// </summary>
    public abstract partial class MapViewModelBase : ObservableObject
    {
        /// <summary>
        /// The tile sizes the wheel steps between, smallest first.
        /// <para>
        /// Discrete, and few. The renderer takes an int tile size and every render component
        /// caches an atlas or a sprite sheet per size, so a zoom that followed the wheel
        /// continuously would build a fresh set for every pixel it passed through and evict
        /// the set it had just left. Nine levels spans 16x and the caches hold them all at
        /// once.
        /// </para>
        /// <para>
        /// Roughly geometric rather than evenly spaced: 4 to 5 is a far bigger change to the
        /// eye than 48 to 49, because what the eye reads is the ratio.
        /// </para>
        /// </summary>
        private static readonly int[] DefaultZoomLevels = [4, 6, 8, 12, 16, 24, 32, 48, 64];

        /// <summary>
        /// Wheel movement seen but not yet spent on a zoom step.
        /// <para>
        /// A mouse reports one notch as one whole unit; a precision touchpad reports the same
        /// swipe as a stream of small fractions. Accumulating and spending only whole units is
        /// what makes a step mean the same thing on both.
        /// </para>
        /// </summary>
        private double _wheelResidue;

        /// <summary>
        /// The current tiles, or null before there is a map. A derived view model publishes a
        /// new grid here after every edit; the control repaints on the reference changing.
        /// </summary>
        [ObservableProperty]
        private TileGrid? _tiles;

        /// <summary>
        /// The writing placed on the map, or null before there is any.
        /// <para>
        /// Beside <see cref="Tiles"/> rather than in it, because a label is placed by
        /// <see cref="MapPixel"/> and belongs to no tile -- see <see cref="MapLabel"/>. Held
        /// here in the base for the same reason the tiles are: every app that can write on a
        /// map hands the list to the control the same way, and there is nothing about it that
        /// differs from one to the next.
        /// </para>
        /// <para>
        /// Replaced rather than edited, on the same terms as everything else the render thread
        /// reads. A derived view model that keeps a working list publishes a copy of it here
        /// after each change; a fresh array of a few dozen labels is not worth the row-sharing
        /// <see cref="TileMap"/> goes to for a million tiles.
        /// </para>
        /// </summary>
        [ObservableProperty]
        private IReadOnlyList<MapLabel>? _labels;

        /// <summary>
        /// What to say beside the pointer, or null for nothing.
        /// <para>
        /// Held here because every map view shows it the same way, and set by whichever view
        /// model owns the hover: what is worth saying about a tile is a question about the world,
        /// and this base knows nothing about tiles.
        /// </para>
        /// </summary>
        [ObservableProperty]
        private string? _hoverText;

        /// <summary>
        /// How the map is drawn. Replaced rather than edited -- <c>Options = Options with
        /// { TileSize = 8 }</c> -- which is both what makes it safe to read while a frame is
        /// in flight and what tells the control to repaint.
        /// </summary>
        [ObservableProperty]
        private MapViewOptions _options;

        /// <param name="renderer">
        /// The renderer for this view model's one map view. Implementations reuse their batch
        /// buffers between frames and refuse to draw two at once, so one is not shared between
        /// views.
        /// </param>
        /// <param name="options">How to draw, to begin with. The defaults if left out.</param>
        protected MapViewModelBase(IMapRenderer renderer, MapViewOptions? options = null)
        {
            ArgumentNullException.ThrowIfNull(renderer);

            _renderer = renderer;
            _options = options ?? MapViewOptions.Default;
        }

        /// <summary>
        /// How the map is drawn, as opposed to what is drawn: the renderer the view hands its
        /// tiles to.
        /// <para>
        /// Settable, so a view model can offer a choice of them -- the same map read as
        /// terrain or as height. The control repaints on the change, and the old renderer is
        /// left whole rather than reset, so switching back is free.
        /// </para>
        /// </summary>
        /// <inheritdoc cref="MapViewModelBase(IMapRenderer, MapViewOptions?)" path="/param[@name='renderer']"/>
        [ObservableProperty]
        private IMapRenderer _renderer;

        /// <summary>Whether there is a map to draw.</summary>
        public bool HasMap => Tiles is not null;

        /// <summary>
        /// Whether the map view runs its repaint loop, which is what makes animated ground
        /// move. A view of the map rather than a fact about it, so it lives in
        /// <see cref="Options"/> and is surfaced here only to give a toggle something to bind.
        /// </summary>
        public bool IsAnimated
        {
            get => Options.IsAnimated;
            set
            {
                if (Options.IsAnimated != value)
                    Options = Options with { IsAnimated = value };
            }
        }

        /// <inheritdoc cref="IsAnimated"/>
        public bool ShowFrameRate
        {
            get => Options.ShowFrameRate;
            set
            {
                if (Options.ShowFrameRate != value)
                    Options = Options with { ShowFrameRate = value };
            }
        }

        /// <summary>
        /// The ladder the wheel steps along. Override to zoom over a different range; the
        /// values must be ascending, and every one of them is a tile size some render
        /// component will build a cache for.
        /// </summary>
        protected virtual ReadOnlySpan<int> ZoomLevels => DefaultZoomLevels;

        /// <summary>
        /// Zooms about the pointer, one step along <see cref="ZoomLevels"/> per wheel notch.
        /// </summary>
        [RelayCommand]
        private void Zoom(MapWheelRequest request)
        {
            if (Tiles is null)
                return;

            // The inset draws the whole map at its own scale, so the anchor names the tile
            // hidden behind it rather than the one under the pointer -- zooming about it would
            // send the view somewhere nobody pointed at.
            if (request.OverMiniMap)
                return;

            _wheelResidue += request.Delta;

            // Truncate rather than round, so the residue keeps its sign: rounding would let a
            // slow scroll one way spend a step in the other.
            var steps = (int)Math.Truncate(_wheelResidue);
            if (steps == 0)
                return;

            _wheelResidue -= steps;

            var levels = ZoomLevels;

            // Found rather than tracked, so a tile size set from anywhere else -- the initial
            // options, a zoom-to-fit later on -- still steps from where the view actually is.
            // A size that is not on the ladder gives -1, which steps from the smallest level;
            // recoverable in a way that throwing on a mouse wheel is not.
            var index = Math.Clamp(levels.IndexOf(Options.TileSize) + steps, 0, levels.Length - 1);

            var next = levels[index];
            if (next == Options.TileSize)
                return;

            // Hold the anchor still: its distance from the origin, measured in tiles, scales
            // by the inverse of the change in tile size. Anything else and the map creeps away
            // from the point being pointed at, a little further with every notch.
            var scale = (double)Options.TileSize / next;

            Options = Clamp(
                Options with
                {
                    TileSize = next,
                    OriginX = request.AnchorX - ((request.AnchorX - Options.OriginX) * scale),
                    OriginY = request.AnchorY - ((request.AnchorY - Options.OriginY) * scale),
                },
                request.ViewportX * scale,
                request.ViewportY * scale);

            Prewarm(levels[Math.Clamp(index + Math.Sign(steps), 0, levels.Length - 1)]);
        }

        /// <summary>
        /// Drags the map along with the pointer, one step of a drag at a time.
        /// </summary>
        [RelayCommand]
        private void Pan(MapPanRequest request)
        {
            if (Tiles is null)
                return;

            // Subtracted, because the map moving right is the view moving left: what is under
            // the pointer when the drag starts should still be under it as the map arrives.
            Options = Clamp(
                Options with
                {
                    OriginX = Options.OriginX - request.DeltaX,
                    OriginY = Options.OriginY - request.DeltaY,
                },
                request.ViewportX,
                request.ViewportY);
        }

        /// <summary>
        /// Centres the view on a point picked in the mini-map inset.
        /// <para>
        /// The zoom level is left alone. Someone pointing at a corner of the overview is
        /// saying where to look, not how closely, and changing both at once loses them their
        /// bearings.
        /// </para>
        /// </summary>
        [RelayCommand]
        private void MiniMap(MiniMapRequest request)
        {
            if (Tiles is null)
                return;

            // Half a screen back from the point picked, so the point ends up in the middle
            // rather than in the top-left corner.
            Options = Clamp(
                Options with
                {
                    OriginX = request.MapX - (request.ViewportX / 2),
                    OriginY = request.MapY - (request.ViewportY / 2),
                },
                request.ViewportX,
                request.ViewportY);
        }

        /// <summary>
        /// Builds whatever a tile size needs before it is drawn at, off the calling thread.
        /// <para>
        /// Called with the level a zoom is about to arrive at next, so that getting there does
        /// not pay for an atlas build in the frame that asks for it. Nothing here knows what a
        /// render component caches -- that is above this library -- so the default does
        /// nothing and a level that was never prewarmed still draws, only slower the first
        /// time.
        /// </para>
        /// </summary>
        protected virtual void Prewarm(int tileSize)
        {
        }

        /// <summary>
        /// Pulls the origin back so the map cannot be scrolled off into empty space, and
        /// centres it on whichever axis it no longer fills.
        /// <para>
        /// Here rather than in the control, because it takes both halves of the knowledge: the
        /// grid knows how big the map is, and the request carries how much of it fits on
        /// screen. The control itself has no opinion and will draw whatever origin it is
        /// handed.
        /// </para>
        /// </summary>
        /// <param name="visibleX">Width of the map view in tiles, at the new tile size.</param>
        /// <param name="visibleY">Height of the map view in tiles, at the new tile size.</param>
        protected MapViewOptions Clamp(MapViewOptions options, double visibleX, double visibleY) =>
            Tiles is not { } grid
                ? options
                : options with
                {
                    OriginX = ClampAxis(options.OriginX, grid.OriginX, grid.Width, visibleX),
                    OriginY = ClampAxis(options.OriginY, grid.OriginY, grid.Height, visibleY),
                };

        /// <summary>
        /// One axis of the origin, held within the map.
        /// <para>
        /// Centred rather than pinned to the near edge when the map is smaller than the view:
        /// a map zoomed out past the window looks wrong tucked into a corner with all the
        /// slack gathered on one side.
        /// </para>
        /// </summary>
        protected static double ClampAxis(double origin, int mapOrigin, int extent, double visible) =>
            extent <= visible
                ? mapOrigin - ((visible - extent) / 2)
                : Math.Clamp(origin, mapOrigin, mapOrigin + extent - visible);

        /// <summary>
        /// Keeps the properties that read out of <see cref="Options"/> in step with it,
        /// however it was replaced -- a toggle's own setter is only one of the ways it moves.
        /// </summary>
        partial void OnOptionsChanged(MapViewOptions value)
        {
            OnPropertyChanged(nameof(IsAnimated));
            OnPropertyChanged(nameof(ShowFrameRate));
        }

        /// <summary>
        /// Whether there is a map is a fact about <see cref="Tiles"/>, and every derived view
        /// model would otherwise have to remember to say so itself.
        /// </summary>
        partial void OnTilesChanged(TileGrid? value)
        {
            OnPropertyChanged(nameof(HasMap));
            OnMapChanged();
        }

        /// <summary>
        /// Called after <see cref="Tiles"/> is replaced, for whatever a derived view model
        /// keeps in step with the map: a command that can only run when there is one, a
        /// summary of what is on it. Every edit publishes, so this runs often -- a hover is
        /// an edit -- and anything expensive belongs somewhere else.
        /// </summary>
        protected virtual void OnMapChanged()
        {
        }
    }
}
