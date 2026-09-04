using System;
using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NWorld.Generation.TestApp.Persistence;
using NWorld.Map.Models;
using NWorld.Map.ViewModels;
using NWorld.MapServices.Constants;
using NWorld.MapServices.ExtensionMethods;
using NWorld.MapServices.MapRenderComponents.StandardRenderer;
using NWorld.MapServices.Persistence;
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
/// The world itself is never edited. It arrives finished and stays as it was saved; what this
/// app changes is the enhancement layer over it, and that is saved to a file of its own -- see
/// <see cref="EnhancementFile"/>. Two files, one drawn over the other, and only the second one
/// is ever written.
/// </para>
/// </summary>
public partial class MainWindowViewModel : MapViewModelBase
{
    /// <summary>
    /// The map as it was loaded, or null before one has been opened.
    /// <para>
    /// Kept whole rather than reduced to its <see cref="TileGrid"/>, which is the only part
    /// drawn: what is built on it is laid a tile at a time, and that goes through the map's own
    /// editor rather than through a snapshot of what it published.
    /// </para>
    /// </summary>
    private TileMap? _map;

    /// <summary>
    /// What the open world's file was called, without its extension, for suggesting a name to
    /// save the enhancements under. A world and what was built on it want the same name and
    /// different extensions, so that a folder of them stays legible.
    /// </summary>
    private string? _mapName;

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

    /// <summary>What a click builds, or nothing.</summary>
    private Enhancement _building = Enhancement.None;

    /// <summary>
    /// What the Enhancements panel can put on a tile.
    /// <para>
    /// <see cref="None"/> is one of them rather than a separate switch beside them: the map is
    /// the thing on screen and most of what anyone does with it is look at it, so not building
    /// is the state this window spends most of its time in and deserves to be as easy to reach
    /// as the two that build.
    /// </para>
    /// </summary>
    private enum Enhancement
    {
        /// <summary>Clicks do nothing. What the window opens in.</summary>
        None,

        /// <summary>Stone road, on dry land.</summary>
        Road,

        /// <summary>The same road carried over water.</summary>
        Bridge,

        /// <summary>A piece of a town. Lay them beside each other and the town grows.</summary>
        City,
    }

    /// <summary>Whether clicks do nothing. What the first radio binds to.</summary>
    /// <inheritdoc cref="IsBuildingRoad" path="/summary/para"/>
    public bool IsBuildingNothing
    {
        get => _building == Enhancement.None;
        set
        {
            if (value)
                Arm(Enhancement.None);
        }
    }

    /// <summary>
    /// Whether clicks lay and lift roads.
    /// <para>
    /// A boolean each rather than the enum and a converter, for the reason every other choice
    /// in these apps is one: a boolean is what a radio button binds to.
    /// </para>
    /// </summary>
    public bool IsBuildingRoad
    {
        get => _building == Enhancement.Road;
        set
        {
            if (value)
                Arm(Enhancement.Road);
        }
    }

    /// <summary>Whether clicks lay and lift bridges.</summary>
    /// <inheritdoc cref="IsBuildingRoad" path="/summary/para"/>
    public bool IsBuildingBridge
    {
        get => _building == Enhancement.Bridge;
        set
        {
            if (value)
                Arm(Enhancement.Bridge);
        }
    }

    /// <summary>Whether clicks lay and lift pieces of a town.</summary>
    /// <inheritdoc cref="IsBuildingRoad" path="/summary/para"/>
    public bool IsBuildingCity
    {
        get => _building == Enhancement.City;
        set
        {
            if (value)
                Arm(Enhancement.City);
        }
    }

    /// <summary>
    /// Picks up a tool. The line under them goes back to saying what the new one does, because
    /// what it says at the moment is what the last click did with the old one.
    /// </summary>
    private void Arm(Enhancement building)
    {
        if (_building == building)
            return;

        _building = building;
        _buildReport = null;

        OnPropertyChanged(nameof(IsBuildingNothing));
        OnPropertyChanged(nameof(IsBuildingRoad));
        OnPropertyChanged(nameof(IsBuildingBridge));
        OnPropertyChanged(nameof(IsBuildingCity));
        OnPropertyChanged(nameof(BuildSummary));
    }

    /// <summary>Where the pointer is, so the tile under it can be lit.</summary>
    private TileCoordinate? _hovered;

    /// <summary>
    /// How far the last road laid was turned, in quarter turns, carried to the next one.
    /// <para>
    /// Remembered rather than started from nothing each time, so laying a line of upright
    /// roads is not a right click per tile. It only ever shows on a road with no neighbours --
    /// see the note in <c>RenderRoad</c> -- but it is what that road opens as.
    /// </para>
    /// </summary>
    private int _quarters;

    /// <summary>What the last click did, or what the next one will do.</summary>
    public string BuildSummary =>
        Tiles is null
            ? "Open a map first."
            : _buildReport ?? _building switch
            {
                Enhancement.Road => "Click dry land to lay a road, or an existing one to lift it.",
                Enhancement.Bridge => "Click water to lay a bridge, or an existing one to lift it.",
                Enhancement.City => "Click dry land to build. Tiles beside each other grow into one town.",
                _ => "Clicks do nothing. Pick something to build.",
            };

    /// <inheritdoc cref="BuildSummary"/>
    private string? _buildReport;

    /// <summary>
    /// Moves the hover highlight. Called with null when the pointer leaves the control.
    /// </summary>
    [RelayCommand]
    private void Hover(TileCoordinate? coordinate)
    {
        if (_map is not { } map || _hovered == coordinate)
            return;

        // Both tiles in one edit: publishing between the clear and the set would put a frame on
        // screen with the highlight on neither, which reads as a flicker under the cursor.
        map.Edit(editor =>
        {
            if (_hovered is { } previous)
                editor.Update(previous, tile => tile.MapRenderComponents.Remove(RenderComponentLayers.Hover));

            if (coordinate is { } current)
                editor.Update(current, tile => tile.SetHoverType(MapRenderComponentConstants.Hover));
        });

        _hovered = coordinate;
        Tiles = map.Tiles;
    }

    /// <summary>
    /// Lays a road on the tile that was clicked, or lifts the one that is already there.
    /// <para>
    /// One tile is edited and no more. What a road looks like is read off its neighbours when
    /// it is drawn rather than stored on it, so the four tiles around this one come out right
    /// on the next frame without being touched -- see <c>RenderRoad</c>.
    /// </para>
    /// </summary>
    [RelayCommand]
    private void Click(TileCoordinate? coordinate)
    {
        if (_building == Enhancement.None || _map is not { } map || coordinate is not { } clicked)
            return;

        if (map[clicked] is not { } tile)
            return;

        // Lifting is always allowed, whatever is underneath and whichever tool is held: a clear
        // tile is a state any ground can be in, and being made to switch tools to undo your own
        // click would be a rule with nothing behind it.
        if (Built(tile) is { } standing)
        {
            map.Edit(editor => editor.Update(
                clicked,
                edited => edited.MapRenderComponents.Remove(RenderComponentLayers.Enhancement)));

            Tiles = map.Tiles;

            Report(clicked, standing == MapRenderComponentConstants.Bridge
                ? "is open water again."
                : standing == MapRenderComponentConstants.City
                    ? "is pulled down."
                    : "is clear again.");

            return;
        }

        // A road and a town want ground under them; a bridge wants none. The one rule this app
        // has about the world it opened, and it is the rule that makes a bridge mean anything: a
        // road that could be laid over a river would be a road that never needed one.
        var wet = IsWater(tile);

        if (_building == Enhancement.Bridge && !wet)
        {
            Report(clicked, "is dry. A bridge needs water under it -- build a road.");
            return;
        }

        if (_building != Enhancement.Bridge && wet)
        {
            Report(clicked, _building == Enhancement.City
                ? "is water. Nobody builds a town on it."
                : "is water. A road needs dry ground -- build a bridge.");

            return;
        }

        var laying = _building switch
        {
            Enhancement.Bridge => MapRenderComponentConstants.Bridge,
            Enhancement.City => MapRenderComponentConstants.City,
            _ => MapRenderComponentConstants.Road,
        };

        map.Edit(editor => editor.Update(
            clicked,
            edited => edited.SetEnhancementType(laying, [Turns(_quarters)])));

        Tiles = map.Tiles;

        Report(clicked, _building switch
        {
            Enhancement.Bridge => "carries a bridge.",
            Enhancement.City => "is built on.",
            _ => "has a road.",
        });
    }

    /// <summary>
    /// Turns the road under the pointer a quarter, and every road laid after it.
    /// <para>
    /// Which only shows on a road with nothing beside it. A road that touches another takes its
    /// shape from what it touches, because a junction that ignored its own neighbours would be
    /// a road drawn pointing at nothing -- so the turn is remembered and waits for a tile where
    /// it means something.
    /// </para>
    /// </summary>
    [RelayCommand]
    private void RightClick(TileCoordinate? coordinate)
    {
        if (_building == Enhancement.None || _map is not { } map || coordinate is not { } clicked)
            return;

        _quarters = (_quarters + 1) % 4;

        if (map[clicked] is not { } tile || Built(tile) is not { } standing)
        {
            Report(clicked, $"nothing to turn. New work lies {Lie()}.");
            return;
        }

        map.Edit(editor => editor.Update(
            clicked,
            edited => edited.SetEnhancementType(standing, [Turns(_quarters)])));

        Tiles = map.Tiles;

        Report(clicked, $"turned. It lies {Lie()} where nothing joins it.");
    }

    /// <summary>What is built on a tile, or null where nothing is.</summary>
    private static Guid? Built(MapTile tile) =>
        tile.MapRenderComponents.TryGetValue(RenderComponentLayers.Enhancement, out var built)
        && (built.ComponentType == MapRenderComponentConstants.Road
            || built.ComponentType == MapRenderComponentConstants.Bridge
            || built.ComponentType == MapRenderComponentConstants.City)
            ? built.ComponentType
            : null;

    /// <summary>
    /// Whether a tile is water: the sea at either depth, or a river or lake.
    /// <para>
    /// Asked of the ground the tile is drawn with rather than of its height, because the two
    /// disagree exactly where it matters. A river takes the elevation of the land it runs over,
    /// so a river tile is high ground by the numbers and very much water to anyone trying to
    /// cross it -- which is the whole case for building a bridge.
    /// </para>
    /// </summary>
    private static bool IsWater(MapTile tile) =>
        tile.MapRenderComponents.TryGetValue(RenderComponentLayers.BaseGround, out var ground)
        && (ground.ComponentType == MapRenderComponentConstants.Water
            || ground.ComponentType == MapRenderComponentConstants.ShallowWater
            || ground.ComponentType == MapRenderComponentConstants.DeepWater);

    /// <summary>The rotation as a road's one parameter.</summary>
    private static string Turns(int quarters) => quarters.ToString(CultureInfo.InvariantCulture);

    /// <summary>Which way the turn now standing would lay a lone road.</summary>
    private string Lie() => _quarters % 2 == 0 ? "across" : "up and down";

    /// <summary>Says what the last click did, in the line under the toggle.</summary>
    private void Report(TileCoordinate coordinate, string outcome) =>
        Report($"({coordinate.X}, {coordinate.Y}) {outcome}");

    /// <inheritdoc cref="Report(TileCoordinate, string)"/>
    private void Report(string outcome)
    {
        _buildReport = outcome;
        OnPropertyChanged(nameof(BuildSummary));
    }

    /// <summary>
    /// What to call a file of these, from the world they were built on: the same name, a
    /// different extension.
    /// </summary>
    public string SuggestedEnhancementName =>
        $"{_mapName ?? "enhancements"}.{EnhancementFile.Extension}";

    /// <summary>
    /// Writes everything built on the open map to <paramref name="stream"/>.
    /// <para>
    /// The world is not written with it and never is. It was finished when it was generated,
    /// and this app has no business changing it -- see <see cref="EnhancementFile"/> for why
    /// the two are kept in separate files.
    /// </para>
    /// </summary>
    public void SaveEnhancements(Stream stream)
    {
        if (Tiles is not { } tiles)
            return;

        try
        {
            EnhancementFile.Save(stream, tiles);
            Report(Standing(tiles));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Report($"Could not save: {error.Message}");
        }
    }

    /// <summary>
    /// Reads a file of enhancements and lays it over the open map, replacing whatever was built
    /// there. A file for a differently shaped world is refused rather than half applied.
    /// </summary>
    /// <param name="name">What to call the file when saying how it went.</param>
    public void LoadEnhancements(Stream stream, string name)
    {
        if (_map is not { } map)
            return;

        try
        {
            var laid = EnhancementFile.Apply(map, EnhancementFile.Load(stream));

            Tiles = map.Tiles;

            Report(laid == 0
                ? $"{name} has nothing built in it. The map is clear."
                : $"Loaded {laid} from {name}.");
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            // The map is untouched: everything that can go wrong here goes wrong while reading
            // or while checking the shape, both of which happen before a single tile is edited.
            Report($"Could not load {name}: {error.Message}");
        }
    }

    /// <summary>
    /// How a save went, counted off the map that was just written. Said in tiles because that
    /// is what was saved: three tiles of town are three entries in the file and one town on
    /// screen, and a line that claimed either number alone would be wrong half the time.
    /// </summary>
    private static string Standing(TileGrid tiles)
    {
        var count = 0;

        foreach (var tile in tiles)
        {
            if (tile.MapRenderComponents.ContainsKey(RenderComponentLayers.Enhancement))
                count++;
        }

        return count switch
        {
            0 => "Saved. Nothing is built on this world yet.",
            1 => "Saved the one built tile.",
            _ => $"Saved {count} built tiles.",
        };
    }


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
            _mapName = Path.GetFileNameWithoutExtension(name);

            // The pointer may well be over the control already, but it is over a different map
            // now, and the highlight it left behind belongs to a tile that no longer exists.
            _hovered = null;
            _buildReport = null;

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

    /// <summary>
    /// The build line follows the map appearing and going, since what it can say depends on
    /// there being one. Every edit publishes, a hover included, so this runs often.
    /// </summary>
    protected override void OnMapChanged() => OnPropertyChanged(nameof(BuildSummary));

    /// <summary>Says how the last open went, and whether it went wrong.</summary>
    private void Report(string status, bool problem)
    {
        Status = status;
        HasProblem = problem;
    }
}
