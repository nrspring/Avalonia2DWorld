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

    private readonly TileMap _map;

    /// <summary>
    /// One renderer for this view model's one map view. <see cref="StandardRenderer"/> reuses
    /// its batch buffers between frames and refuses to draw two at once, so it is not shared.
    /// </summary>
    private readonly IMapRenderer _renderer = new StandardRenderer();

    private TileCoordinate? _hovered;
    private TileCoordinate? _selected;

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
    private MapViewOptions _options = new() { TileSize = 16 };

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
