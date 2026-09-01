using System;
using System.Collections.Generic;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.IO;
using System.Text.Json;
using NWorld.Generation.App.Generation;
using NWorld.Generation.App.Persistence;
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

    /// <summary>Blank line between paragraphs of tooltip text.</summary>
    private const string DoubleBreak = "\n\n";

    /// <summary>
    /// How many maps back undo can go.
    /// <para>
    /// Whole maps, tiles and all, rather than the settings that produced them or the land
    /// mask behind them. Both of those are smaller, and both would stop being the map the
    /// moment a pass writes something a mask cannot hold -- a moisture value, a road. A map
    /// is the one thing that will always be able to describe a map.
    /// </para>
    /// <para>
    /// Which costs what a map costs, times this: tens of megabytes on a large one. The kept
    /// maps share nothing with the live one, since every pass builds fresh tiles.
    /// </para>
    /// </summary>
    private const int UndoDepth = 5;

    /// <summary>
    /// The map, or null before one has been made. Replaced rather than refilled when a new
    /// one arrives, so the map a frame may still be drawing from is left whole.
    /// </summary>
    private TileMap? _map;

    /// <summary>
    /// The land as it stands: continents, plus every island scattered since.
    /// <para>
    /// Kept rather than read back off the tiles, because the generators work in masks and a
    /// mask is what the next pass needs. Islands add to this, so scattering twice leaves two
    /// scatters; building continents replaces it, which is what clears the islands.
    /// </para>
    /// </summary>
    private bool[]? _land;

    /// <summary>
    /// Maps as they were before the last few things that changed them, oldest first.
    /// </summary>
    private readonly List<MapState> _history = [];

    private TileCoordinate? _hovered;
    private TileCoordinate? _selected;

    /// <summary>
    /// Whether the tiles of <see cref="_map"/> carry elevation labels, which is not the same
    /// question as <see cref="ShowElevation"/>: a map arrives from a file or from undo with
    /// whatever it was built or saved with, and the two are reconciled by
    /// <see cref="SyncElevationLabels"/>.
    /// <para>
    /// Tracked rather than asked of the map every time, because the answer only ever changes
    /// where the map does and the pass that would change it touches every tile.
    /// </para>
    /// </summary>
    private bool _labelled;

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

    /// <summary>How many islands the next scatter drops.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IslandSummary))]
    private int _islandCount = 40;

    /// <summary>Average island radius, in tiles.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IslandSummary))]
    private double _islandSize = 7;

    /// <summary>
    /// Where in the sea islands prefer to be: 0 is open water, 50 is no preference at all,
    /// 100 is crowding the coast.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IslandSummary))]
    private double _islandCoastHug = 50;

    /// <inheritdoc cref="ContinentSeed"/>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(BuildIslandsCommand))]
    [NotifyPropertyChangedFor(nameof(IslandSummary), nameof(HasIslandProblem))]
    private string _islandSeed = "1";

    /// <summary>
    /// Whether the land carries its elevation as a number drawn over each tile. The sea does
    /// not -- see <see cref="IsWater"/>.
    /// <para>
    /// A view of the map that is nonetheless made of tiles: the number is a render component
    /// on <see cref="RenderComponentLayers.ElevationLabel"/> like anything else drawn, so
    /// turning this on and off is an edit to the map rather than a flag the renderer reads.
    /// That is what lets the label be drawn by the same batching pass as the ground, and it is
    /// why this lives here rather than in <see cref="MapViewOptions"/> beside the animation
    /// and frame-rate toggles.
    /// </para>
    /// </summary>
    [ObservableProperty]
    private bool _showElevation;

    /// <summary>
    /// What the last save or load did, or why it did not. Cleared by nothing: the last thing
    /// that happened to a file is worth still being able to read a minute later.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFileProblem))]
    private string _fileStatus = "No file open.";

    /// <summary>Whether <see cref="FileStatus"/> is a complaint rather than a description.</summary>
    public bool HasFileProblem { get; private set; }

    /// <summary>The save tooltip.</summary>
    public string SaveHelp =>
        "Write the map to a file, with the panel settings alongside it.\n\n" +
        "The settings are saved because they are how a map is worked on: opening one with " +
        "the dials back at their defaults means guessing what the coastline came from before " +
        "another pass can be run over it.\n\n" +
        "Tiles are stored compressed, so a map is a fraction of what it takes in memory.";

    /// <summary>The load tooltip.</summary>
    public string LoadHelp =>
        "Open a map from a file, settings and all.\n\n" +
        "This replaces the map on screen. It is undoable like any other change, so opening " +
        "the wrong file costs one Ctrl+Z.";

    /// <summary>The undo tooltip.</summary>
    public string UndoHelp =>
        "Put the map back as it was before the last thing that changed it.\n\n" +
        $"Up to {UndoDepth} maps back: making a map, building continents and scattering " +
        "islands each file the one they replace. Ctrl+Z does the same.\n\n" +
        "Whole maps are kept, so what comes back is exactly what was there -- not a rebuild " +
        "from the settings, which have most likely moved on since.";

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

    /// <summary>The elevation toggle's tooltip.</summary>
    public string ElevationHelp =>
        ("Write each land tile's elevation over it. The sea is left clear: it is all at one " +
         "level anyway, and a field of zeroes would bury the coastline the numbers are read " +
         "against.@@" +
         "Unlike the rest of this panel it does change the tiles -- the number is drawn from " +
         "a component on the tile, the same as the ground under it -- so turning it on for a " +
         "large map takes a moment. It is not undoable, and it survives a build: raise new " +
         "continents with this on and the new land comes up labelled.@@" +
         "Nothing is drawn below a tile size of 12 pixels, where a number would be a smudge. " +
         "Zoom in if the map goes quiet.").Replace("@@", "\n\n");

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

    /// <summary>The island-count tooltip.</summary>
    public string IslandCountHelp =>
        ("How many islands to scatter.@@" +
         "A target rather than a promise: an island that can find nowhere to sit -- no water " +
         "far enough from a coast to hold it -- is dropped rather than forced somewhere " +
         "silly. Ask for two hundred on a crowded map and you will get fewer.").Replace("@@", "\n\n");

    /// <summary>The island-size tooltip.</summary>
    public string IslandSizeHelp =>
        ("Average radius, in tiles. Each island varies either side of it, and none of them " +
         "are circles.@@" +
         "Size is what separates an archipelago from a scattering of rocks, and it is also " +
         "what decides how much room an island needs: a big one has fewer places it can " +
         "go.").Replace("@@", "\n\n");

    /// <summary>The coast-hug tooltip.</summary>
    public string IslandCoastHugHelp =>
        ("Where in the sea the islands prefer to be.@@" +
         "Halfway is no preference: anywhere there is room. Turn it up and they crowd " +
         "the shores as archipelagos and offshore chains; turn it down and they are " +
         "pushed out into open water as lone landfalls.@@" +
         "Halfway is not the same as either end, because most of the sea on a map with " +
         "land in it is fairly near that land. Left alone, this gives you the sea as it " +
         "comes, which reads as more islands near the coasts than far from them.@@" +
         "They never touch the mainland whatever this says, and they keep the same " +
         "distance from each other: a channel of clear water is always left.")
            .Replace("@@", DoubleBreak);

    /// <summary>The island-seed tooltip.</summary>
    public string IslandSeedHelp =>
        ("Any whole number, and separate from the continents' seed: re-roll the islands as " +
         "often as you like and the mainland does not move.@@" +
         "The same seed with the same settings scatters the same islands, every time.").Replace("@@", "\n\n");

    /// <summary>The island build-button tooltip.</summary>
    public string BuildIslandsHelp =>
        ("Scatter islands through the sea: grass at elevation 1, the same as a continent.@@" +
         "They are added to whatever is already there. Press it again for another handful: " +
         "the islands already placed count as coast for the next lot, which is how a chain " +
         "grows outwards. Change the seed first, or the same settings will keep finding much " +
         "the same water.@@" +
         "Building continents starts the land over and takes the islands with it, since the " +
         "sea they were placed in has changed.@@" +
         "Greyed out until there is a map. Without continents it still works, and drops " +
         "islands into open ocean.").Replace("@@", "\n\n");

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

        Remember();

        // Assigned, never merged: a continent build is where the world starts over. Islands
        // today and whatever layers on later are all just marks in this one mask, so
        // replacing it is what clears them -- and any pass added later gets that for free,
        // as long as it keeps writing here.
        _land = ContinentBuilder.Build(map.Width, map.Height, new ContinentSettings(
            ContinentCount, LandCoverage / 100, CoastRoughness / 100, seed));

        Raise(map, _land);
    }

    /// <summary>Whether there is a map to build on and a seed to build with.</summary>
    private bool CanBuildContinents() => _map is not null && TryReadSeed(ContinentSeed, out _);

    /// <summary>Fills the continent seed box with a new one, for the button beside it.</summary>
    [RelayCommand]
    private void NewContinentSeed() =>
        ContinentSeed = Random.Shared.Next(1, 1_000_000).ToString(CultureInfo.InvariantCulture);

    /// <summary>The seed as typed. Any whole number, negatives included.</summary>
    private static bool TryReadSeed(string? text, out int seed) => int.TryParse(text, out seed);

    /// <summary>
    /// The line under the island controls: what the next scatter will do, or what is
    /// stopping it.
    /// </summary>
    public string IslandSummary
    {
        get
        {
            if (Tiles is null)
                return "Make a map in Start first.";

            if (!TryReadSeed(IslandSeed, out _))
                return "Seed: any whole number.";

            var where = IslandCoastHug switch
            {
                >= 75 => "crowding the coasts",
                >= 55 => "leaning towards the coasts",
                > 45 => "wherever there is room",
                >= 25 => "leaning out to sea",
                _ => "far out in open water",
            };

            return $"Adds up to {IslandCount} more islands, {where}.";
        }
    }

    /// <inheritdoc cref="HasSizeProblem"/>
    public bool HasIslandProblem => !CanBuildIslands();

    /// <summary>
    /// Scatters islands through the sea around whatever land is already there, as more grass
    /// at elevation 1.
    /// <para>
    /// Added to the map rather than replacing what the last scatter did, so pressing it twice
    /// leaves twice the islands -- and the second pass sees the first one's islands as coast,
    /// which is what lets a chain grow outwards a handful at a time.
    /// </para>
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanBuildIslands))]
    private void BuildIslands()
    {
        if (_map is not { } map || !TryReadSeed(IslandSeed, out var seed))
            return;

        Remember();

        // No land yet is a map of open ocean, which is a fine thing to drop islands into --
        // and is what the mask of nothing means.
        var existing = _land ?? new bool[map.Width * map.Height];

        _land = IslandBuilder.Add(map.Width, map.Height, existing, new IslandSettings(
            IslandCount, IslandSize, IslandCoastHug / 100, seed));

        Raise(map, _land);
    }

    /// <summary>Whether there is a map to scatter islands over, and a seed to do it with.</summary>
    private bool CanBuildIslands() => _map is not null && TryReadSeed(IslandSeed, out _);

    /// <summary>Fills the island seed box with a new one.</summary>
    [RelayCommand]
    private void NewIslandSeed() =>
        IslandSeed = Random.Shared.Next(1, 1_000_000).ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Writes the map and the current settings to <paramref name="stream"/>.
    /// <para>
    /// Takes a stream rather than a path because choosing the file is the window's job: a
    /// view model that opened dialogs would be a view model that could not be run without
    /// one.
    /// </para>
    /// </summary>
    /// <param name="name">What to call the file in the status line.</param>
    public void Save(Stream stream, string name)
    {
        if (Tiles is not { } tiles)
            return;

        try
        {
            MapFile.Save(stream, tiles, new MapSettings
            {
                ContinentCount = ContinentCount,
                LandCoverage = LandCoverage,
                CoastRoughness = CoastRoughness,
                ContinentSeed = ContinentSeed,
                IslandCount = IslandCount,
                IslandSize = IslandSize,
                IslandCoastHug = IslandCoastHug,
                IslandSeed = IslandSeed,
                TileSize = Options.TileSize,
                OriginX = Options.OriginX,
                OriginY = Options.OriginY,
            });

            Report($"Saved {name}: {tiles.Width} x {tiles.Height} tiles.", problem: false);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            Report($"Could not save {name}: {error.Message}", problem: true);
        }
    }

    /// <summary>
    /// Reads a map back from <paramref name="stream"/> and puts it on screen, with the
    /// settings it was saved with.
    /// </summary>
    /// <inheritdoc cref="Save" path="/param[@name='name']"/>
    public void Load(Stream stream, string name)
    {
        MapDocument document;

        try
        {
            document = MapFile.Load(stream);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or JsonException)
        {
            Report($"Could not open {name}: {error.Message}", problem: true);
            return;
        }

        Remember();

        _map = document.Map;

        // Rebuilt from the tiles rather than saved: the mask is a summary of the map, and a
        // summary that can be recomputed is a summary that cannot go stale.
        _land = LandMask(document.Map.Tiles);

        _hovered = null;
        _selected = null;

        Apply(document.Settings);

        _labelled = IsLabelled(_map.Tiles);

        Tiles = _map.Tiles;

        // The file carries whatever labels it was saved with, which need not be what the
        // panel is asking for now.
        SyncElevationLabels();

        Report($"Opened {name}: {_map.Width} x {_map.Height} tiles.", problem: false);
    }

    /// <summary>
    /// Puts the panels back where they were when the map was saved. Anything the file did not
    /// carry is left as it is, which is what lets an older file open in a newer build.
    /// </summary>
    private void Apply(MapSettings settings)
    {
        ContinentCount = settings.ContinentCount ?? ContinentCount;
        LandCoverage = settings.LandCoverage ?? LandCoverage;
        CoastRoughness = settings.CoastRoughness ?? CoastRoughness;
        ContinentSeed = settings.ContinentSeed ?? ContinentSeed;

        IslandCount = settings.IslandCount ?? IslandCount;
        IslandSize = settings.IslandSize ?? IslandSize;
        IslandCoastHug = settings.IslandCoastHug ?? IslandCoastHug;
        IslandSeed = settings.IslandSeed ?? IslandSeed;

        // The size boxes describe the next map to be made, and the one just opened is the
        // best guess at what that should be.
        NewMapWidth = _map is { } map ? map.Width.ToString(CultureInfo.InvariantCulture) : NewMapWidth;
        NewMapHeight = _map is { } opened ? opened.Height.ToString(CultureInfo.InvariantCulture) : NewMapHeight;

        Options = Options with
        {
            TileSize = settings.TileSize ?? Options.TileSize,
            OriginX = settings.OriginX ?? 0,
            OriginY = settings.OriginY ?? 0,
        };
    }

    /// <summary>Which tiles are land, for the passes that build on what is already there.</summary>
    private static bool[] LandMask(TileGrid tiles)
    {
        var mask = new bool[tiles.Count];

        for (var i = 0; i < mask.Length; i++)
            mask[i] = tiles[i].Elevation > 0;

        return mask;
    }

    /// <summary>Sets the file line, and whether it is bad news.</summary>
    private void Report(string status, bool problem)
    {
        HasFileProblem = problem;
        FileStatus = status;
        OnPropertyChanged(nameof(HasFileProblem));
    }

    /// <summary>
    /// Puts the map back as it was before the last build.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo()
    {
        if (_history.Count == 0)
            return;

        var state = _history[^1];
        _history.RemoveAt(_history.Count - 1);

        // A map of a different size is a different world; the view has nowhere sensible to
        // stay, so it goes back to the corner. Same size, and it stays where it was, which
        // is what makes undoing a build you were watching worth anything.
        var resized = _map is not { } current
            || current.Width != state.Map.Width
            || current.Height != state.Map.Height;

        _map = state.Map;
        _land = state.Land;

        // Restored rather than cleared: the map still carries the highlights it had when it
        // was put away, and these are what say where they are.
        _hovered = state.Hovered;
        _selected = state.Selected;

        if (resized)
            Options = Options with { OriginX = 0, OriginY = 0 };

        // Read off the map rather than filed with it. A history entry can be the very map
        // that is live -- a hover edits in place -- so what it was labelled with when it was
        // filed is not necessarily what it is labelled with now.
        _labelled = IsLabelled(_map.Tiles);

        Tiles = _map.Tiles;

        SyncElevationLabels();
    }

    /// <summary>Whether there is anything to go back to.</summary>
    private bool CanUndo() => _history.Count > 0;

    /// <summary>
    /// Files the map as it stands, for undo to come back to. Called by everything that
    /// replaces it.
    /// <para>
    /// The map itself, not a copy: a pass builds a new <see cref="TileMap"/> rather than
    /// editing this one, so what is filed here stops changing the moment it is filed.
    /// </para>
    /// </summary>
    private void Remember()
    {
        if (_map is not { } map)
            return;

        _history.Add(new MapState(map, _land, _hovered, _selected));

        // Oldest first out. A deeper history is a memory decision rather than a design one.
        if (_history.Count > UndoDepth)
            _history.RemoveAt(0);

        UndoCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Rebuilds the map from a land mask: grass at elevation 1 where it says land, deep water
    /// at 0 everywhere else.
    /// <para>
    /// A rebuild rather than an edit, for both passes. Every tile is decided by the same
    /// mask, which is the fastest way to touch all of them and what makes a build repeatable:
    /// the map that comes out depends on the settings and the seed, never on what was there
    /// before.
    /// </para>
    /// </summary>
    private void Raise(TileMap map, bool[] land)
    {
        _map = new TileMap(map.Width, map.Height, map.OriginX, map.OriginY, coordinate =>
            land[((coordinate.Y - map.OriginY) * map.Width) + (coordinate.X - map.OriginX)]
                ? BuildLandTile(coordinate)
                : BuildOceanTile(coordinate));

        // Built labelled or not as the panel asks, which is a pass over the tiles this one
        // has just made anyway -- cheaper than raising the land and then walking all of it
        // again to write numbers on it.
        _labelled = ShowElevation;

        // The tiles the pointer was over are gone, and the view is left where it is: the map
        // is the same size and in the same place, and someone watching a coastline appear
        // should not have to find their way back to it.
        _hovered = null;
        _selected = null;

        Tiles = _map.Tiles;
    }

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

        Remember();

        _map = new TileMap(width, height, fill: BuildOceanTile);
        _land = null;
        _labelled = ShowElevation;

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
        BuildIslandsCommand.NotifyCanExecuteChanged();
        UndoCommand.NotifyCanExecuteChanged();

        OnPropertyChanged(nameof(LandSummary));
        OnPropertyChanged(nameof(HasLandProblem));
        OnPropertyChanged(nameof(IslandSummary));
        OnPropertyChanged(nameof(HasIslandProblem));
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
    /// Puts the labels on or takes them off, so that the map matches what the panel asks for.
    /// </summary>
    partial void OnShowElevationChanged(bool value) => SyncElevationLabels();

    /// <summary>
    /// Writes an elevation label onto every tile, or clears one off every tile, whichever
    /// <see cref="ShowElevation"/> is asking for.
    /// <para>
    /// Every tile, and so a clone of every tile: the map is copy-on-write, which is what makes
    /// it safe to edit while the renderer walks it. That is the price of the labels being real
    /// tile data rather than something the renderer decides, and it is why nothing calls this
    /// unless the map is actually on the wrong side of the toggle. On the largest map allowed
    /// it is a visible pause; on any map somebody is reading numbers off, it is not.
    /// </para>
    /// <para>
    /// Not undoable. It changes no tile's elevation, ground or highlight, so putting it on the
    /// history would mean spending an undo slot on something a second click already reverses.
    /// </para>
    /// </summary>
    private void SyncElevationLabels()
    {
        if (_map is not { } map || _labelled == ShowElevation)
            return;

        map.Edit(editor =>
        {
            for (var y = 0; y < map.Height; y++)
            {
                for (var x = 0; x < map.Width; x++)
                    editor.Update(new TileCoordinate(map.OriginX + x, map.OriginY + y), Label);
            }
        });

        _labelled = ShowElevation;

        Tiles = map.Tiles;
    }

    /// <summary>
    /// Gives a tile its elevation label, or takes it away, according to
    /// <see cref="ShowElevation"/>.
    /// <para>
    /// The number is baked into the component's parameters rather than read off the tile at
    /// draw time, because a render function is handed placements and never the map. A
    /// component is replaced and never edited, so a tile whose elevation changes gets a fresh
    /// label along with it -- see the remarks on <see cref="MapTile.Clone"/>.
    /// </para>
    /// </summary>
    private void Label(MapTile tile)
    {
        if (ShowElevation && !IsWater(tile))
            tile.SetElevationLabelType(
                MapRenderComponentConstants.ElevationLabel,
                [tile.Elevation.ToString(CultureInfo.InvariantCulture)]);
        else
            tile.MapRenderComponents.Remove(RenderComponentLayers.ElevationLabel);
    }

    /// <summary>
    /// Whether a tile is sea, and so has no number worth writing on it: the water is all at
    /// one level by definition, and a map of open ocean covered in zeroes hides the coastline
    /// that is the only thing the numbers are there to read against.
    /// <para>
    /// Asked of the ground drawn on the tile rather than of its elevation. Elevation is the
    /// thing being labelled, and a rule that read it would be deciding what to show from the
    /// very number in question -- a lake held above sea level would go unlabelled, and any
    /// land a later pass leaves at zero would be taken for sea.
    /// </para>
    /// </summary>
    private static bool IsWater(MapTile tile) =>
        tile.MapRenderComponents.TryGetValue(RenderComponentLayers.BaseGround, out var ground) &&
        (ground.ComponentType == MapRenderComponentConstants.Water ||
         ground.ComponentType == MapRenderComponentConstants.DeepWater);

    /// <summary>
    /// Whether a map carries elevation labels at all.
    /// <para>
    /// Every tile, rather than the first one: the sea goes unlabelled, and the top-left corner
    /// of a generated map is nearly always sea. It stops at the first label it finds, so the
    /// walk only runs to the end on a map that has none -- which on the largest map allowed is
    /// a pass over an array, and happens only where a map arrives from a file or from undo.
    /// </para>
    /// </summary>
    private static bool IsLabelled(TileGrid tiles)
    {
        for (var i = 0; i < tiles.Count; i++)
        {
            if (tiles[i].MapRenderComponents.ContainsKey(RenderComponentLayers.ElevationLabel))
                return true;
        }

        return false;
    }

    /// <summary>
    /// A map as it was, and the two coordinates that say what is highlighted on it.
    /// </summary>
    private readonly record struct MapState(
        TileMap Map, bool[]? Land, TileCoordinate? Hovered, TileCoordinate? Selected);

    /// <summary>
    /// One tile of continent: grass at elevation 1. One above the sea, which is all
    /// "above water" needs to mean until there is anything to put on it.
    /// </summary>
    private MapTile BuildLandTile(TileCoordinate coordinate)
    {
        var tile = new MapTile { X = coordinate.X, Y = coordinate.Y, Elevation = 1 };

        Ground(tile, MapRenderComponentConstants.Grass);
        Label(tile);

        return tile;
    }

    /// <summary>
    /// Puts a ground on a tile, with the tile's elevation alongside it -- which is what draws
    /// hills and mountains lighter than the flat land around them.
    /// <para>
    /// Passed down as a component parameter rather than read off the tile, for the reason
    /// every other number here is: a render function is handed placements and never the map.
    /// Anything that changes a tile's elevation has to set its ground again afterwards, or
    /// the tile will be drawn at the height it used to be.
    /// </para>
    /// </summary>
    private static void Ground(MapTile tile, Guid groundType) =>
        tile.SetBaseGroundType(
            groundType,
            [tile.Elevation.ToString(CultureInfo.InvariantCulture)]);

    /// <summary>
    /// One tile of open ocean: deep water, elevation zero. Elevation is set explicitly
    /// rather than left at its default, because zero is a decision here -- sea level, the
    /// datum everything the generator raises will be measured from.
    /// </summary>
    private MapTile BuildOceanTile(TileCoordinate coordinate)
    {
        var tile = new MapTile { X = coordinate.X, Y = coordinate.Y, Elevation = 0 };

        Ground(tile, MapRenderComponentConstants.DeepWater);
        Label(tile);

        return tile;
    }
}
