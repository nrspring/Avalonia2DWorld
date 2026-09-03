using System;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using NWorld.Generation.TestApp.Persistence;
using NWorld.Map.Models;
using NWorld.Map.ViewModels;
using NWorld.MapServices.MapRenderComponents.StandardRenderer;
using NWorld.MapServices.Renderers;

namespace NWorld.Generation.TestApp.ViewModels;

/// <summary>
/// Owns the map this app is looking at, which is all it owns.
/// <para>
/// Moving around in a map -- zoom, pan, the mini-map -- comes from
/// <see cref="MapViewModelBase"/>, the same as in the generation app. What is left here is
/// opening a file and saying how it went.
/// </para>
/// <para>
/// Nothing edits. A world arrives finished and stays as it was saved: this app exists to look
/// at one and, in time, to draw what gets built on top of it from somewhere else. There is no
/// undo here because there is nothing to undo.
/// </para>
/// </summary>
public partial class MainWindowViewModel : MapViewModelBase
{
    /// <summary>
    /// The map as it was loaded, or null before one has been opened.
    /// <para>
    /// Kept whole rather than reduced to its <see cref="TileGrid"/>, which is the only part
    /// drawn today. What comes next here is a second file of improvements laid over these
    /// tiles, and that needs the map rather than a snapshot of it.
    /// </para>
    /// </summary>
    private TileMap? _map;

    public MainWindowViewModel()
        : base(
            // One renderer for this view model's one map view: StandardRenderer reuses its
            // batch buffers between frames and refuses to draw two at once, so it is not
            // shared.
            new StandardRenderer(),
            new MapViewOptions { TileSize = 16, MiniMap = MiniMapLocation.LowerRight })
    {
    }

    [ObservableProperty]
    private string _title = "NWorld Test";

    /// <summary>
    /// What happened to the last file opened, for the line under the button.
    /// </summary>
    [ObservableProperty]
    private string _status = "No map open. Press Open.";

    /// <summary>Whether <see cref="Status"/> is a complaint rather than a report.</summary>
    [ObservableProperty]
    private bool _hasProblem;

    /// <summary>
    /// Reads a map from <paramref name="stream"/> and puts it on screen.
    /// <para>
    /// Takes a stream rather than a path because choosing the file is the window's job: a view
    /// model that opened dialogs would be a view model that could not be run without one.
    /// </para>
    /// </summary>
    /// <param name="name">What to call the file in the status line.</param>
    public void Load(Stream stream, string name)
    {
        try
        {
            var map = MapArchive.Load(stream);

            _map = map;

            // Back to the map's own corner. Whatever the view was looking at belonged to the
            // last map, and a new one scrolled halfway off screen looks like nothing happened.
            Options = Options with { OriginX = map.OriginX, OriginY = map.OriginY };

            Tiles = map.Tiles;

            Report($"{name}: {map.Width} x {map.Height} tiles.", problem: false);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            // The three ways a file can fail to be a map: unreadable, not one of these, or not
            // ours to read. Anything else is a bug here and should not be swallowed.
            Report($"Could not open {name}: {error.Message}", problem: true);
        }
    }

    /// <summary>
    /// Builds the render components' caches for a tile size the zoom is heading towards, off
    /// the UI thread. Fire and forget: a level that was never prewarmed still draws, only
    /// slower the first time it is asked for.
    /// </summary>
    protected override void Prewarm(int tileSize) => _ = RenderHelperFunctions.PrewarmAll(tileSize);

    /// <summary>Says how the last open went, and whether it went wrong.</summary>
    private void Report(string status, bool problem)
    {
        Status = status;
        HasProblem = problem;
    }
}
