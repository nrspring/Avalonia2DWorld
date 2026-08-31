using System;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NWorld.Generation.App.Generation;
using NWorld.Map.Constants;
using NWorld.Map.Models;
using NWorld.Map.ViewModels;
using NWorld.MapServices.ExtensionMethods;
using NWorld.MapServices.MapRenderComponents;
using NWorld.MapServices.Renderers;

namespace NWorld.Generation.App.ViewModels;

/// <summary>
/// Owns the map and every change made to it: what a map here is made of, what a hover and a
/// selection look like on it, and where a new one comes from.
/// <para>
/// Moving around in a map -- zoom, pan, the mini-map -- is the same job in any app that draws
/// one, and lives in <see cref="MapViewModelBase"/>. What is left here is everything that has
/// to know about render components, which is to say everything about <em>this</em> map rather
/// than about looking at a map.
/// </para>
/// <para>
/// There is no map until something makes one -- see <see cref="CreateMap"/>. The app opens on
/// an empty view rather than on a sample, so that what is on screen is always a map somebody
/// asked for. Every other command is a no-op until then, which is what lets the control stay
/// bound and live with nothing behind it.
/// </para>
/// </summary>
public partial class MainWindowViewModel : MapViewModelBase
{
    /// <summary>The longest a single side may be. Generous; <see cref="MaxMapTiles"/> is
    /// the limit that usually bites, and it is what allows a long thin map.</summary>
    private const int MaxMapDimension = 4096;

    /// <summary>
    /// The most tiles a map may hold.
    /// <para>
    /// A limit rather than a promise: a map is one <see cref="MapTile"/> object per tile,
    /// each with its own component dictionary, and every edit republishes the rows it touches
    /// (see the remarks on <see cref="TileMap"/>). A megatile map is already hundreds of
    /// megabytes. Past that the answer should be a sentence under the fields, not a machine
    /// that stops responding.
    /// </para>
    /// </summary>
    private const int MaxMapTiles = 1_048_576;

    /// <summary>
    /// The map, or null before one has been made. Replaced rather than refilled when a new
    /// one arrives, so the map a frame may still be drawing from is left whole.
    /// </summary>
    private TileMap? _map;

    private TileCoordinate? _hovered;
    private TileCoordinate? _selected;

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
    private string _title = "NWorld Generation";

    /// <summary>
    /// Width for the next map, as typed. A string rather than an int because a half-typed
    /// box is a normal state to be in -- binding to an int leaves the empty box holding the
    /// last good value while the caret sits in it, and there is nowhere to put "not a number
    /// yet". Read back through <see cref="TryReadSize"/>.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateMapCommand))]
    [NotifyPropertyChangedFor(nameof(SizeSummary), nameof(HasSizeProblem))]
    private string _newMapWidth = "600";

    /// <inheritdoc cref="NewMapWidth"/>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateMapCommand))]
    [NotifyPropertyChangedFor(nameof(SizeSummary), nameof(HasSizeProblem))]
    private string _newMapHeight = "400";

    /// <summary>
    /// How many continents the next build makes. An int, and bounded by the slider rather
    /// than by validation: there is no way to type a wrong one.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LandSummary))]
    private int _continentCount = 4;

    /// <summary>How much of the map ends up as land, as a percentage.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LandSummary))]
    private double _landCoverage = 30;

    /// <summary>How ragged the coastlines come out, as a percentage.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LandSummary))]
    private double _coastRoughness = 45;

    /// <summary>
    /// What makes a build repeatable. A string for the same reason the sizes are: a box being
    /// typed into passes through states that are not numbers.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(BuildContinentsCommand))]
    [NotifyPropertyChangedFor(nameof(LandSummary), nameof(HasLandProblem))]
    private string _continentSeed = "1";

    /// <summary>What the View panel is for.</summary>
    public string ViewHelp =>
        "How the map is drawn, as opposed to what is on it.\n\n" +
        "Nothing here changes a single tile, so none of it can be got wrong.";

    /// <summary>The animation toggle's tooltip.</summary>
    public string AnimationHelp =>
        "Keep repainting the map so the water moves.\n\n" +
        "Water is the only ground that animates; everything else is drawn from a cached " +
        "atlas and looks the same either way.\n\n" +
        "Turning it off stops the repaint loop altogether, so the map is redrawn only when " +
        "something changes it. That costs nothing while the map sits idle, and it leaves " +
        "the frame rate with nothing to measure.";

    /// <summary>The frame-rate toggle's tooltip.</summary>
    public string FrameRateHelp =>
        "Draw the frames per second in the top-right corner of the map.\n\n" +
        "It counts frames the map view actually draws, averaged over half a second. The " +
        "water animates continuously, so on a still map this is the cost of drawing one " +
        "screenful: watch it while zooming out, where the tile count on screen climbs " +
        "fastest.\n\n" +
        "A dash means nothing was measured, which is what an empty view reads as.";

    /// <summary>What the Base Land panel is for.</summary>
    public string BaseLandHelp =>
        ("Raise the land the world is built on: grass at elevation 1, everywhere the sea is " +
         "not.@@" +
         "Continents come first and islands after, built separately because they are " +
         "different things -- a continent is where the map is going to happen, an island is " +
         "detail around the edges of it.@@" +
         "Each build starts from open ocean again, so a build is never laid on top of the " +
         "last one. The same settings and the same seed always give the same land.").Replace("@@", "\n\n");

    /// <summary>The continent-count tooltip.</summary>
    public string ContinentCountHelp =>
        ("How many separate landmasses to grow.@@" +
         "They are spread as far apart as the map allows, and sized around the land budget " +
         "below: more continents on the same budget means smaller ones, not more land.").Replace("@@", "\n\n");

    /// <summary>The land-coverage tooltip.</summary>
    public string LandCoverageHelp =>
        ("How much of the map ends up as land.@@" +
         "Exact rather than approximate: the generator scores every tile and takes the best " +
         "of them, so this figure holds however ragged the coast is.@@" +
         "The sea keeps the outer tenth of the map whatever this says. A continent cut off " +
         "by the border reads as a mistake.").Replace("@@", "\n\n");

    /// <summary>The coast-roughness tooltip.</summary>
    public string CoastRoughnessHelp =>
        ("How far the coastline wanders from the shape underneath it.@@" +
         "At zero the continents are near-circular. High up, the coast is bitten deep enough " +
         "to strand pieces offshore as islands of their own -- which is a fine way to get " +
         "an archipelago, and not what the islands panel will be for.@@" +
         "It does not change how much land there is, only where the edge of it falls.").Replace("@@", "\n\n");

    /// <summary>The seed tooltip.</summary>
    public string ContinentSeedHelp =>
        ("Any whole number. The same seed with the same settings builds the same continents, " +
         "every time.@@" +
         "Change it to get a different world of the same description; the button beside it " +
         "picks one at random.").Replace("@@", "\n\n");

    /// <summary>The build-button tooltip.</summary>
    public string BuildContinentsHelp =>
        ("Rebuild the map as sea, and raise the continents into it: grass at elevation 1, " +
         "deep water at 0.@@" +
         "This replaces everything on the map, including a previous build and anything " +
         "selected. There is no undo yet -- the seed is what gets a world back.@@" +
         "Greyed out until there is a map to build on.").Replace("@@", "\n\n");

    /// <summary>
    /// What the Start panel is for, in a sentence. The tooltips on the controls carry the
    /// rules; this one carries the point.
    /// </summary>
    public string StartHelp =>
        "Make a new map to work on.\n\n" +
        "It arrives as open ocean: every tile deep water at elevation 0. That is the datum " +
        "the generator will raise land out of.";

    /// <summary>
    /// The width field's tooltip. Built from the same constants the validation uses, so the
    /// numbers quoted to the reader cannot drift from the numbers enforced.
    /// </summary>
    public string WidthHelp =>
        "How many tiles the map runs west to east.\n\n" +
        $"A whole number from 1 to {MaxMapDimension:N0}. Width times height must also come " +
        $"to no more than {MaxMapTiles:N0} tiles, which is the limit that usually bites.\n\n" +
        "Tiles are drawn at a fixed size, so a wider map is not a smaller one on screen: it " +
        "runs past the edge of the window, and you zoom out to see the rest.";

    /// <inheritdoc cref="WidthHelp"/>
    public string HeightHelp =>
        "How many tiles the map runs north to south.\n\n" +
        $"A whole number from 1 to {MaxMapDimension:N0}. Width times height must also come " +
        $"to no more than {MaxMapTiles:N0} tiles, which is the limit that usually bites.\n\n" +
        "Tiles are drawn at a fixed size, so a taller map is not a smaller one on screen: it " +
        "runs past the edge of the window, and you zoom out to see the rest.";

    /// <summary>The Create button's tooltip, including why it may be greyed out.</summary>
    public string CreateHelp =>
        "Build the map at the size above and put it on screen.\n\n" +
        "Every tile is deep water at elevation 0. This replaces the current map and anything " +
        "selected on it, and there is no undo yet.\n\n" +
        "Greyed out whenever a size is unusable. The line above it says which, and why.";

    /// <summary>
    /// The line under the size fields: what Create is about to make, or -- when it is greyed
    /// out -- which box is the reason. A disabled button that says nothing is a puzzle, and
    /// the caps here are arbitrary enough that nobody could guess them.
    /// </summary>
    public string SizeSummary
    {
        get
        {
            if (!TryReadSize(NewMapWidth, out var width))
                return $"Width: a whole number from 1 to {MaxMapDimension:N0}.";

            if (!TryReadSize(NewMapHeight, out var height))
                return $"Height: a whole number from 1 to {MaxMapDimension:N0}.";

            var tiles = (long)width * height;

            return tiles > MaxMapTiles
                ? $"{tiles:N0} tiles is over the {MaxMapTiles:N0} limit."
                : $"{tiles:N0} tiles of open ocean at elevation 0.";
        }
    }

    /// <summary>
    /// Whether <see cref="SizeSummary"/> is currently a complaint rather than a description.
    /// Bound to a class on the text, which is what colours it.
    /// </summary>
    public bool HasSizeProblem => !CanCreateMap();

    /// <summary>
    /// The line under the continent controls: what the next build will do, or what is
    /// stopping it.
    /// </summary>
    public string LandSummary
    {
        get
        {
            if (Tiles is not { } grid)
                return "Make a map in Start first.";

            if (!TryReadSeed(ContinentSeed, out _))
                return "Seed: any whole number.";

            var tiles = (long)grid.Width * grid.Height * LandCoverage / 100;

            return $"{ContinentCount} continent{(ContinentCount == 1 ? string.Empty : "s")}, " +
                   $"about {tiles:N0} tiles of grass.";
        }
    }

    /// <inheritdoc cref="HasSizeProblem"/>
    public bool HasLandProblem => !CanBuildContinents();

    /// <summary>
    /// Raises continents out of the sea: grass at elevation 1 inside the coastlines, deep
    /// water at 0 outside them.
    /// <para>
    /// A rebuild rather than an edit. Every tile is decided by the same pass, which is both
    /// the fastest way to touch all of them and what makes a build repeatable: the map that
    /// comes out depends on the settings and the seed, never on what was there before.
    /// </para>
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanBuildContinents))]
    private void BuildContinents()
    {
        if (_map is not { } map || !TryReadSeed(ContinentSeed, out var seed))
            return;

        var land = ContinentBuilder.Build(map.Width, map.Height, new ContinentSettings(
            ContinentCount, LandCoverage / 100, CoastRoughness / 100, seed));

        _map = new TileMap(map.Width, map.Height, map.OriginX, map.OriginY, coordinate =>
            land[((coordinate.Y - map.OriginY) * map.Width) + (coordinate.X - map.OriginX)]
                ? BuildLandTile(coordinate)
                : BuildOceanTile(coordinate));

        // The tiles the pointer was over are gone, and the view is left where it is: the map
        // is the same size and the same place, and someone watching a coastline appear should
        // not have to find their way back to it.
        _hovered = null;
        _selected = null;

        Tiles = _map.Tiles;
    }

    /// <summary>Whether there is a map to build on and a seed to build with.</summary>
    private bool CanBuildContinents() => _map is not null && TryReadSeed(ContinentSeed, out _);

    /// <summary>Fills the seed box with a new one, for the button beside it.</summary>
    [RelayCommand]
    private void NewSeed() =>
        ContinentSeed = Random.Shared.Next(1, 1_000_000).ToString(CultureInfo.InvariantCulture);

    /// <summary>The seed as typed. Any whole number, negatives included.</summary>
    private static bool TryReadSeed(string? text, out int seed) => int.TryParse(text, out seed);

    /// <summary>
    /// Builds a map at the size typed into the Start panel and puts it on screen, replacing
    /// whatever was there. Open ocean: every tile deep water at elevation zero, which is the
    /// blank canvas the generator will raise land out of.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanCreateMap))]
    private void CreateMap()
    {
        // Re-parsed rather than trusted from CanExecute: the button is not the only way in,
        // and a command that reads its own inputs cannot be fired into an invalid state.
        if (!TryReadSize(NewMapWidth, out var width) || !TryReadSize(NewMapHeight, out var height))
            return;

        _map = new TileMap(width, height, fill: BuildOceanTile);

        // The pointer may well be over the control already, but it is over a different map
        // now, and the highlights it left behind belong to tiles that no longer exist. The
        // next pointer move re-establishes the hover against the map that is actually there.
        _hovered = null;
        _selected = null;

        // Back to the map's own corner. The origin left over from the last map means
        // nothing on this one, and a new map scrolled halfway off screen looks like nothing
        // happened.
        Options = Options with { OriginX = 0, OriginY = 0 };

        Tiles = _map.Tiles;
    }

    /// <summary>
    /// Whether both boxes currently hold a usable size. What greys the Create button out.
    /// </summary>
    private bool CanCreateMap() =>
        TryReadSize(NewMapWidth, out var width) &&
        TryReadSize(NewMapHeight, out var height) &&
        (long)width * height <= MaxMapTiles;

    /// <summary>
    /// One dimension as typed, if it is a whole number of tiles between 1 and
    /// <see cref="MaxMapDimension"/>.
    /// </summary>
    private static bool TryReadSize(string? text, out int size) =>
        int.TryParse(text, out size) && size > 0 && size <= MaxMapDimension;

    /// <summary>
    /// The land panel can only build on a map, and whether there is one changes when the
    /// tiles do.
    /// </summary>
    protected override void OnMapChanged()
    {
        BuildContinentsCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(LandSummary));
        OnPropertyChanged(nameof(HasLandProblem));
    }

    /// <summary>
    /// Builds the render components' caches for a tile size the zoom is heading towards, off
    /// the UI thread. Fire and forget: a level that was never prewarmed still draws, only
    /// slower the first time it is asked for.
    /// </summary>
    protected override void Prewarm(int tileSize) => _ = RenderHelperFunctions.PrewarmAll(tileSize);

    /// <summary>
    /// Moves the hover highlight. Called with null when the pointer leaves the control.
    /// </summary>
    [RelayCommand]
    private void Hover(TileCoordinate? coordinate)
    {
        if (_map is not { } map || _hovered == coordinate)
            return;

        // Both tiles in one edit: publishing between the clear and the set would put a frame
        // on screen with the highlight on neither, which reads as a flicker under the cursor.
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
    /// Selects a tile, or clears the selection when the selected tile is clicked again.
    /// </summary>
    [RelayCommand]
    private void Click(TileCoordinate? coordinate)
    {
        if (_map is not { } map || coordinate is not { } clicked)
            return;

        var next = _selected == clicked ? (TileCoordinate?)null : clicked;

        map.Edit(editor =>
        {
            if (_selected is { } previous)
                editor.Update(previous, tile => tile.MapRenderComponents.Remove(RenderComponentLayers.Unit));

            if (next is { } current)
                editor.Update(current, tile => tile.SetUnitType(MapRenderComponentConstants.Selected));
        });

        _selected = next;
        Tiles = map.Tiles;
    }

    /// <summary>
    /// One tile of continent: grass at elevation 1. One above the sea, which is all
    /// "above water" needs to mean until there is anything to put on it.
    /// </summary>
    private static MapTile BuildLandTile(TileCoordinate coordinate)
    {
        var tile = new MapTile { X = coordinate.X, Y = coordinate.Y, Elevation = 1 };

        tile.SetBaseGroundType(MapRenderComponentConstants.Grass);

        return tile;
    }

    /// <summary>
    /// One tile of open ocean: deep water, elevation zero. Elevation is set explicitly
    /// rather than left at its default, because zero is a decision here -- sea level, the
    /// datum everything the generator raises will be measured from.
    /// </summary>
    private static MapTile BuildOceanTile(TileCoordinate coordinate)
    {
        var tile = new MapTile { X = coordinate.X, Y = coordinate.Y, Elevation = 0 };

        tile.SetBaseGroundType(MapRenderComponentConstants.DeepWater);

        return tile;
    }
}
