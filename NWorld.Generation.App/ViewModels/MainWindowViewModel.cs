using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NWorld.Map.Constants;
using NWorld.Map.Interfaces;
using NWorld.Map.Models;
using NWorld.MapServices.ExtensionMethods;
using NWorld.MapServices.MapRenderComponents;
using NWorld.MapServices.Renderers;

namespace NWorld.Generation.App.ViewModels;

/// <summary>
/// Owns the map and every change made to it. <c>MapView</c> renders <see cref="Tiles"/> and
/// reports where the pointer is; what that means happens here.
/// </summary>
public partial class MainWindowViewModel : ViewModelBase
{
    private const int MapWidth = 64;
    private const int MapHeight = 48;

    /// <summary>
    /// The tile sizes the wheel steps between, smallest first.
    /// <para>
    /// Discrete, and few. The renderer takes an int tile size and every render component
    /// caches an atlas or a sprite sheet per size, so a zoom that followed the wheel
    /// continuously would build a fresh set for every pixel it passed through and evict the
    /// set it had just left. Nine levels spans 16x and the caches hold them all at once.
    /// </para>
    /// <para>
    /// Roughly geometric rather than evenly spaced: 4 to 5 is a far bigger change to the eye
    /// than 48 to 49, because what the eye reads is the ratio.
    /// </para>
    /// </summary>
    private static readonly int[] ZoomLevels = [4, 6, 8, 12, 16, 24, 32, 48, 64];

    private readonly TileMap _map;

    /// <summary>
    /// One renderer for this view model's one map view. <see cref="StandardRenderer"/> reuses
    /// its batch buffers between frames and refuses to draw two at once, so it is not shared.
    /// </summary>
    private readonly IMapRenderer _renderer = new StandardRenderer();

    private TileCoordinate? _hovered;
    private TileCoordinate? _selected;

    /// <summary>
    /// Wheel movement seen but not yet spent on a zoom step.
    /// <para>
    /// A mouse reports one notch as one whole unit; a precision touchpad reports the same
    /// swipe as a stream of small fractions. Accumulating and spending only whole units is
    /// what makes a step mean the same thing on both.
    /// </para>
    /// </summary>
    private double _wheelResidue;

    [ObservableProperty]
    private string _title = "NWorld Generation";

    /// <summary>
    /// A new list after every edit -- <see cref="TileMap"/> never writes to a list it has
    /// already handed out, so the binding changing is what tells the control to repaint.
    /// </summary>
    [ObservableProperty]
    private IReadOnlyList<MapTile> _tiles;

    /// <summary>
    /// How the map is drawn. Replaced rather than edited -- <c>Options = Options with
    /// { TileSize = 8 }</c> -- which is both what makes it safe to read while a frame is in
    /// flight and what tells the control to repaint.
    /// </summary>
    [ObservableProperty]
    private MapViewOptions _options = new()
    {
        TileSize = 16,
        MiniMap = MiniMapLocation.LowerRight,
    };

    public MainWindowViewModel()
    {
        _map = new TileMap(MapWidth, MapHeight, fill: BuildTile);
        _tiles = _map.Tiles;
    }

    public IMapRenderer Renderer => _renderer;

    /// <summary>
    /// Moves the hover highlight. Called with null when the pointer leaves the control.
    /// </summary>
    [RelayCommand]
    private void Hover(TileCoordinate? coordinate)
    {
        if (_hovered == coordinate)
            return;

        // Both tiles in one edit: publishing between the clear and the set would put a frame
        // on screen with the highlight on neither, which reads as a flicker under the cursor.
        _map.Edit(editor =>
        {
            if (_hovered is { } previous)
                editor.Update(previous, tile => tile.MapRenderComponents.Remove(RenderComponentLayers.Hover));

            if (coordinate is { } current)
                editor.Update(current, tile => tile.SetHoverType(MapRenderComponentConstants.Hover));
        });

        _hovered = coordinate;
        Tiles = _map.Tiles;
    }

    /// <summary>
    /// Selects a tile, or clears the selection when the selected tile is clicked again.
    /// </summary>
    [RelayCommand]
    private void Click(TileCoordinate? coordinate)
    {
        if (coordinate is not { } clicked)
            return;

        var next = _selected == clicked ? (TileCoordinate?)null : clicked;

        _map.Edit(editor =>
        {
            if (_selected is { } previous)
                editor.Update(previous, tile => tile.MapRenderComponents.Remove(RenderComponentLayers.Unit));

            if (next is { } current)
                editor.Update(current, tile => tile.SetUnitType(MapRenderComponentConstants.Selected));
        });

        _selected = next;
        Tiles = _map.Tiles;
    }

    /// <summary>
    /// Zooms about the pointer, one step along <see cref="ZoomLevels"/> per wheel notch.
    /// </summary>
    [RelayCommand]
    private void Zoom(MapWheelRequest request)
    {
        // The inset draws the whole map at its own scale, so the anchor names the tile
        // hidden behind it rather than the one under the pointer -- zooming about it would
        // send the view somewhere nobody pointed at. Left alone until the inset has a
        // behaviour of its own.
        if (request.OverMiniMap)
            return;

        _wheelResidue += request.Delta;

        // Truncate rather than round, so the residue keeps its sign: rounding would let a
        // slow scroll one way spend a step in the other.
        var steps = (int)Math.Truncate(_wheelResidue);
        if (steps == 0)
            return;

        _wheelResidue -= steps;

        // Found rather than tracked, so a tile size set from anywhere else -- the initial
        // options, a zoom-to-fit later on -- still steps from where the view actually is.
        // A size that is not on the ladder gives -1, which steps from the smallest level;
        // recoverable in a way that throwing on a mouse wheel is not.
        var index = Math.Clamp(
            Array.IndexOf(ZoomLevels, Options.TileSize) + steps, 0, ZoomLevels.Length - 1);

        var next = ZoomLevels[index];
        if (next == Options.TileSize)
            return;

        // Hold the anchor still: its distance from the origin, measured in tiles, scales by
        // the inverse of the change in tile size. Anything else and the map creeps away
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

        // The level past this one, off the UI thread, so arriving there on the next notch
        // does not pay for the atlas build in the frame that asks for it. Fire and forget:
        // a level that was never prewarmed still draws, only slower the first time.
        _ = RenderHelperFunctions.PrewarmAll(
            ZoomLevels[Math.Clamp(index + Math.Sign(steps), 0, ZoomLevels.Length - 1)]);
    }

    /// <summary>
    /// Pulls the origin back so the map cannot be scrolled off into empty space, and centres
    /// it on whichever axis it no longer fills.
    /// <para>
    /// Here rather than in the control, because it takes both halves of the knowledge: the
    /// view model knows how big the map is, and the wheel request carries how much of it
    /// fits on screen. The control itself has no opinion and will draw whatever origin it
    /// is handed.
    /// </para>
    /// </summary>
    /// <param name="visibleX">Width of the map view in tiles, at the new tile size.</param>
    /// <param name="visibleY">Height of the map view in tiles, at the new tile size.</param>
    private MapViewOptions Clamp(MapViewOptions options, double visibleX, double visibleY) =>
        options with
        {
            OriginX = ClampAxis(options.OriginX, _map.OriginX, _map.Width, visibleX),
            OriginY = ClampAxis(options.OriginY, _map.OriginY, _map.Height, visibleY),
        };

    /// <summary>
    /// One axis of the origin, held within the map.
    /// <para>
    /// Centred rather than pinned to the near edge when the map is smaller than the view: a
    /// map zoomed out past the window looks wrong tucked into a corner with all the slack
    /// gathered on one side.
    /// </para>
    /// </summary>
    private static double ClampAxis(double origin, int mapOrigin, int extent, double visible) =>
        extent <= visible
            ? mapOrigin - ((visible - extent) / 2)
            : Math.Clamp(origin, mapOrigin, mapOrigin + extent - visible);

    /// <summary>
    /// Stand-in terrain, enough to tell the ground types apart while the real generator is
    /// being written. Deterministic, so a run looks the same twice.
    /// </summary>
    private static MapTile BuildTile(TileCoordinate coordinate)
    {
        var tile = new MapTile { X = coordinate.X, Y = coordinate.Y };

        var height =
            Math.Sin(coordinate.X * 0.12) +
            Math.Cos(coordinate.Y * 0.15) +
            Math.Sin((coordinate.X + coordinate.Y) * 0.05);

        tile.SetBaseGroundType(height switch
        {
            < -1.4 => MapRenderComponentConstants.DeepWater,
            < -0.7 => MapRenderComponentConstants.Water,
            < 0.6 => MapRenderComponentConstants.Grass,
            < 1.4 => MapRenderComponentConstants.Swamp,
            _ => MapRenderComponentConstants.Desert,
        });

        return tile;
    }
}
