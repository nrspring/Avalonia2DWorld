using System;
using System.Collections.Generic;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.IO;
using System.Text.Json;
using NWorld.Generation.App.Generation;
using NWorld.Generation.App.Persistence;
using Avalonia.Media;
using NWorld.Map.Interfaces;
using NWorld.Map.Models;
using NWorld.Map.ViewModels;
using NWorld.MapServices.Constants;
using NWorld.MapServices.ExtensionMethods;
using NWorld.MapServices.MapRenderComponents.StandardRenderer;
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
    /// The lowest a tile may be edited to: flat land, one above the sea.
    /// <para>
    /// Hand editing shapes relief, and the floor is what keeps it to that. All the ground there
    /// is lies at or above this; the only thing below it is the sea, and where the sea meets the
    /// land is a coastline, which the Base Land panel draws from a mask and a seed -- see the
    /// remarks on <see cref="EditElevation"/>.
    /// </para>
    /// <para>
    /// Flat land and not the first hill, so that a tile can be brought all the way back down to
    /// the ground a land pass laid: a floor above the flats would make the first click on every
    /// flat tile a one-way trip, and leave a rim of hills wherever a ridge was walked back.
    /// </para>
    /// <para>
    /// Written as the band rather than as one, because that is what it means: the floor is the
    /// lowest ground there is, and it moves with the bands if the bands ever move.
    /// </para>
    /// </summary>
    private const int LowestEditableElevation = Elevations.Flat;

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
    /// Whether the entry on top of the history is one a tile edit filed, and so already stands
    /// for the run of edits in progress. See <see cref="RememberTileEdit"/>.
    /// </summary>
    private bool _editing;

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

    /// <summary>
    /// The world as it is made: ground, cover, deposits, and the marks on top of them.
    /// <para>
    /// Held rather than built on each switch, because it is the expensive one. Its components
    /// cache an atlas or a sprite sheet per zoom level, and throwing the instance away would
    /// not throw those away -- they are static, and shared by every instance -- but building a
    /// second renderer to hold nothing is still a second renderer.
    /// </para>
    /// </summary>
    private readonly IMapRenderer _terrainView;

    /// <summary>The same world as height alone. See <see cref="ElevationRenderer"/>.</summary>
    private readonly IMapRenderer _heightView = new ElevationRenderer();

    /// <summary>The same world as what it is worth. See <see cref="ResourceRenderer"/>.</summary>
    private readonly IMapRenderer _resourceView = new ResourceRenderer();

    /// <summary>Which of the three the map is being drawn with.</summary>
    private Picture _picture = Picture.Terrain;

    /// <summary>
    /// The pictures the View panel offers. Private, and never saved: it is which way the map
    /// is being looked at right now, and a file that opened in a picture the person who saved
    /// it happened to be checking something in would be a file that opened wrong.
    /// </summary>
    private enum Picture
    {
        /// <summary>The world as it is made.</summary>
        Terrain,

        /// <summary>Height alone.</summary>
        Height,

        /// <summary>What the land is worth.</summary>
        Resources,
    }

    public MainWindowViewModel()
        : base(
            // One renderer for this view model's one map view: StandardRenderer reuses its
            // batch buffers between frames and refuses to draw two at once, so it is not
            // shared.
            new StandardRenderer(),
            new MapViewOptions { TileSize = 16, MiniMap = MiniMapLocation.LowerRight })
    {
        // Taken back off the base rather than built again here, which is the only way to hold
        // on to the instance the base was handed: a field initialiser cannot be passed to a
        // base constructor.
        _terrainView = Renderer;
    }

    /// <summary>
    /// Whether the map is drawn as the world it is. What the Terrain radio button binds to.
    /// <para>
    /// A boolean each rather than the enum and a converter, because a boolean is what a radio
    /// button binds to, and three of them is the whole of the choice.
    /// </para>
    /// </summary>
    public bool IsTerrainView
    {
        get => _picture == Picture.Terrain;
        set
        {
            if (value)
                Show(Picture.Terrain);
        }
    }

    /// <summary>Whether the map is drawn as height alone.</summary>
    /// <inheritdoc cref="IsTerrainView" path="/summary/para"/>
    public bool IsHeightView
    {
        get => _picture == Picture.Height;
        set
        {
            if (value)
                Show(Picture.Height);
        }
    }

    /// <summary>
    /// Whether the map is drawn as what the land is worth. Also what shows the key: the
    /// colours mean nothing in the other two pictures, so the key is not worth the room there.
    /// </summary>
    /// <inheritdoc cref="IsTerrainView" path="/summary/para"/>
    public bool IsResourceView
    {
        get => _picture == Picture.Resources;
        set
        {
            if (value)
                Show(Picture.Resources);
        }
    }

    /// <summary>
    /// What the colours of the resource picture mean, ready to hang on the panel. Taken from
    /// the renderer that decides them, so the key cannot drift from the map.
    /// </summary>
    public IReadOnlyList<KeySwatch> ResourceKey { get; } =
        ResourceRenderer.Key
            .Select(entry => new KeySwatch(
                entry.Name,
                new SolidColorBrush(Color.FromRgb(entry.Colour.Red, entry.Colour.Green, entry.Colour.Blue))))
            .ToList();

    /// <summary>
    /// Swaps the renderer under the view. Nothing else changes: not a tile, not the zoom, not
    /// where the view is looking -- this is the same map read a different way, and coming back
    /// from it should land exactly where it left.
    /// </summary>
    private void Show(Picture picture)
    {
        if (_picture == picture)
            return;

        _picture = picture;

        Renderer = picture switch
        {
            Picture.Height => _heightView,
            Picture.Resources => _resourceView,
            _ => _terrainView,
        };

        OnPropertyChanged(nameof(IsTerrainView));
        OnPropertyChanged(nameof(IsHeightView));
        OnPropertyChanged(nameof(IsResourceView));
    }

    /// <summary>
    /// Whether the Edit Tiles panel is open, which is the whole of the editing mode: while it
    /// is, a click on the map changes the tile under it instead of selecting it.
    /// <para>
    /// Two-way against the panel's own expander rather than a separate switch inside it. The
    /// accordion already closes one panel when another opens, so an open panel is a mode
    /// nobody can leave running by accident -- opening Resources to scatter something puts the
    /// click back to selecting, which is what somebody who has stopped editing tiles meant to
    /// happen.
    /// </para>
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TileEditSummary))]
    private bool _isEditingTiles;

    /// <summary>Which of the tools a click on the map is currently holding.</summary>
    private TileTool _tool = TileTool.Elevation;

    /// <summary>
    /// What the Edit Tiles panel does to the tile that is clicked. One at a time by
    /// construction: a click has one outcome, and a panel that let you arm two would have to
    /// decide which of them won.
    /// </summary>
    private enum TileTool
    {
        /// <summary>Ground, up and down. Left click raises, right click lowers.</summary>
        Elevation,

        /// <summary>Sea, deep or shallow. One click, because there are only the two.</summary>
        Water,

        /// <summary>
        /// The deposit named in the drop-down, on or off. One click, because which of the five
        /// is a question the panel has already answered -- see <see cref="EditResource"/>.
        /// </summary>
        Resource,
    }

    /// <summary>
    /// What the last click on a tile did, or null if nothing has been clicked since the tool or
    /// the map last changed. What <see cref="TileEditSummary"/> says when there is something to
    /// report; before the first click it falls back to saying what the tool does.
    /// </summary>
    private string? _tileEditReport;

    /// <summary>Whether <see cref="_tileEditReport"/> is a refusal rather than a change.</summary>
    private bool _tileEditRefused;

    /// <summary>
    /// Whether a click changes the height of the ground. What the elevation radio binds to.
    /// <para>
    /// A boolean each rather than the enum and a converter, for the reason the choice of
    /// picture is one: a boolean is what a radio button binds to.
    /// </para>
    /// </summary>
    public bool IsElevationTool
    {
        get => _tool == TileTool.Elevation;
        set
        {
            if (value)
                Arm(TileTool.Elevation);
        }
    }

    /// <summary>Whether a click switches the sea between deep and shallow.</summary>
    /// <inheritdoc cref="IsElevationTool" path="/summary/para"/>
    public bool IsWaterTool
    {
        get => _tool == TileTool.Water;
        set
        {
            if (value)
                Arm(TileTool.Water);
        }
    }

    /// <summary>Whether a click puts the chosen deposit on a tile, or takes it off.</summary>
    /// <inheritdoc cref="IsElevationTool" path="/summary/para"/>
    public bool IsResourceTool
    {
        get => _tool == TileTool.Resource;
        set
        {
            if (value)
                Arm(TileTool.Resource);
        }
    }

    /// <summary>
    /// The five deposits, in the order the resource key reads them, for the drop-down to offer.
    /// <para>
    /// <see cref="TileResource.None"/> is not among them: nothing is not a deposit somebody
    /// chooses to lay, it is what a tile goes back to when the one it has is clicked off. A
    /// drop-down entry for it would be a second way of doing what the click already does.
    /// </para>
    /// <para>
    /// The names come off the enum, which is also what the resource key is labelled from, so
    /// the word in this list and the word beside the colour on the map are the same word.
    /// </para>
    /// </summary>
    public IReadOnlyList<TileResource> EditResources { get; } =
    [
        TileResource.Iron,
        TileResource.Wood,
        TileResource.Oil,
        TileResource.Sulphur,
        TileResource.Stone,
    ];

    /// <summary>
    /// Which deposit the resource tool lays. What the drop-down binds to.
    /// <para>
    /// Iron to begin with, for no better reason than that it is the first of them: something
    /// has to be chosen, and a drop-down opening on nothing would make the first click on the
    /// map a click that did nothing.
    /// </para>
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TileEditSummary))]
    private TileResource _editResource = TileResource.Iron;

    /// <summary>
    /// Picks up a tool. The line under them goes back to saying what the new one does, because
    /// what it says at the moment is what the old one did.
    /// </summary>
    private void Arm(TileTool tool)
    {
        if (_tool == tool)
            return;

        _tool = tool;
        _tileEditReport = null;
        _tileEditRefused = false;

        OnPropertyChanged(nameof(IsElevationTool));
        OnPropertyChanged(nameof(IsWaterTool));
        OnPropertyChanged(nameof(IsResourceTool));
        OnPropertyChanged(nameof(TileEditSummary));
        OnPropertyChanged(nameof(HasTileEditProblem));
    }

    /// <summary>
    /// What the last click did, or what the next one will do. The line under the tools.
    /// </summary>
    public string TileEditSummary =>
        _map is null
            ? "No map yet -- make one in the Start panel."
            : _tileEditReport ?? _tool switch
            {
                TileTool.Water => "Click a sea tile to switch it between deep and shallow.",
                TileTool.Resource => $"Click a tile to put {EditResource} on it, or take it off.",
                _ => "Left click raises a tile. Right click lowers it.",
            };

    /// <summary>Whether that line is a complaint, and so drawn in the warning colour.</summary>
    public bool HasTileEditProblem => _map is null || _tileEditRefused;

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

    /// <summary>How far the shallows reach out from the land, in tiles.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShallowsSummary))]
    private double _shallowsReach = 5;

    /// <summary>
    /// How much that reach wanders along the coast, as a percentage. Not zero by default: a
    /// shelf of one width the whole way round is the one thing that gives a drawn sea away.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShallowsSummary))]
    private double _shallowsVariation = 55;

    /// <summary>How much of the land ends up as mountain, as a percentage.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MountainSummary))]
    private double _mountainCoverage = 10;

    /// <summary>How much of the land ends up as hills, as a percentage.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HillSummary))]
    private double _hillCoverage = 25;

    /// <summary>
    /// How broken the high ground is: low is broad rounded upland, high is sharp ranges with
    /// the peaks strung along their crests.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MountainSummary))]
    private double _ruggedness = 75;

    /// <summary>Roughly how far a range runs before it breaks, in tiles.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MountainSummary))]
    private double _rangeSize = 30;

    /// <summary>
    /// How far the hills reach out from the high ground, in tiles. The hills' own dial, and
    /// the only one they have: it does nothing whatever to a mountain.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HillSummary))]
    private double _hillSpread = 10;

    /// <inheritdoc cref="ContinentSeed"/>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(BuildHillsCommand), nameof(BuildMountainsCommand))]
    [NotifyPropertyChangedFor(
        nameof(HillSummary), nameof(HasHillProblem),
        nameof(MountainSummary), nameof(HasMountainProblem))]
    private string _terrainSeed = "1";

    /// <summary>How much of the land ends up as swamp, as a percentage.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SwampSummary))]
    private double _swampCoverage = 8;

    /// <summary>How much of the land ends up as desert, as a percentage.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DesertSummary))]
    private double _desertCoverage = 10;

    /// <summary>Roughly how many tiles across one patch of swamp or desert runs.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SwampSummary), nameof(DesertSummary))]
    private double _patchSize = 18;

    /// <summary>
    /// How much the patches gather together, as a percentage: nothing spreads them over the
    /// whole map, full heaps them into a few districts. Shared by both covers, like the patch
    /// size, because it is a fact about the grain of the world rather than about either one.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SwampSummary), nameof(DesertSummary))]
    private double _coverClustering;

    /// <inheritdoc cref="ContinentSeed"/>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(BuildSwampsCommand), nameof(BuildDesertsCommand))]
    [NotifyPropertyChangedFor(
        nameof(SwampSummary), nameof(HasSwampProblem),
        nameof(DesertSummary), nameof(HasDesertProblem))]
    private string _coverSeed = "1";

    /// <summary>How many rivers to run down to the sea.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RiverSummary))]
    private double _riverCount = 6;

    /// <summary>
    /// How much the rivers wander on their way down, as a percentage. High by default: a
    /// river that takes the shortest way to the water is a canal.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RiverSummary))]
    private double _riverWinding = 70;

    /// <summary>The shortest river worth drawing, in tiles.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RiverSummary))]
    private double _riverLength = 40;

    /// <inheritdoc cref="ContinentSeed"/>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(BuildRiversCommand))]
    [NotifyPropertyChangedFor(nameof(RiverSummary), nameof(HasRiverProblem))]
    private string _riverSeed = "1";

    /// <summary>How many lakes to fill in the low ground.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LakeSummary))]
    private double _lakeCount = 6;

    /// <summary>Roughly how many tiles a lake covers.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LakeSummary))]
    private double _lakeSize = 60;

    /// <summary>
    /// How far a lake reaches out of round, as a percentage. High by default: a lake that takes
    /// the shape the ground alone gives it is a pond.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LakeSummary))]
    private double _lakeShape = 65;

    /// <inheritdoc cref="ContinentSeed"/>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(BuildLakesCommand))]
    [NotifyPropertyChangedFor(nameof(LakeSummary), nameof(HasLakeProblem))]
    private string _lakeSeed = "1";

    /// <summary>How much of the land ends up carrying iron, as a percentage.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IronSummary))]
    private double _ironCoverage = 4;

    /// <summary>How much of the land ends up carrying timber, as a percentage.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WoodSummary))]
    private double _woodCoverage = 10;

    /// <summary>How much of the land ends up carrying oil, as a percentage.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OilSummary))]
    private double _oilCoverage = 3;

    /// <summary>How much of the land ends up carrying sulphur, as a percentage.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SulphurSummary))]
    private double _sulphurCoverage = 2;

    /// <summary>How much of the land ends up carrying stone, as a percentage.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StoneSummary))]
    private double _stoneCoverage = 6;

    /// <summary>
    /// Roughly how many tiles across one field of a resource runs. Shared by all five, as the
    /// patch size is shared by the two covers: it is the grain of the world's wealth rather
    /// than a fact about any one resource.
    /// <para>
    /// Its own dial and not the cover panel's, because there is no reason a world of small
    /// marshes should also be a world of small ore fields.
    /// </para>
    /// </summary>
    [ObservableProperty]
    private double _resourcePatchSize = 10;

    /// <summary>
    /// How much the fields gather together, as a percentage: nothing sprinkles them over
    /// whatever ground suits them, full heaps them into a few districts.
    /// </summary>
    [ObservableProperty]
    private double _resourceClustering = 45;

    /// <inheritdoc cref="ContinentSeed"/>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(
        nameof(BuildIronCommand), nameof(BuildWoodCommand), nameof(BuildOilCommand),
        nameof(BuildSulphurCommand), nameof(BuildStoneCommand), nameof(BuildAllResourcesCommand))]
    [NotifyPropertyChangedFor(
        nameof(IronSummary), nameof(WoodSummary), nameof(OilSummary),
        nameof(SulphurSummary), nameof(StoneSummary), nameof(HasResourceProblem))]
    private string _resourceSeed = "1";

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

    /// <summary>What the Edit Tiles panel is for.</summary>
    public string TileEditHelp =>
        ("Change the map a tile at a time, by hand, where a pass has left something not quite " +
         "right -- a gap in a ridge, a saddle in the wrong place, a shelf that stops one tile " +
         "short of the headland.@@" +
         "While this panel is open the map is under whichever tool is held below. Closing the " +
         "panel, or opening any other, gives the map back: left click goes back to selecting a " +
         "tile, and right click to nothing at all. There is no separate switch to remember to " +
         "turn off.@@" +
         "Dragging with the right button still pans the map, panel open or shut. A drag moves " +
         "the map and changes nothing; only a right click that stays put is a click.@@" +
         "No tool here moves a coastline. Elevation works on land and never goes below " +
         $"{LowestEditableElevation}, which is flat land; water works on the sea and leaves it " +
         "sea; a deposit sits on top of ground and does not change it. Where the land and the " +
         "sea meet is drawn from a mask and a seed in the Base Land panel, and drawing it a " +
         "tile at a time would be a second and much worse way of doing the same " +
         "thing.").Replace("@@", "\n\n");

    /// <summary>The tool choice's tooltip.</summary>
    public string TileToolHelp =>
        ("What a click on the map does. One tool at a time, and one of them always held.@@" +
         "Change elevation works the ground up and down, one elevation a click, between " +
         $"{LowestEditableElevation} and {Elevations.MountainsTo}. Left click up, right click " +
         "down -- both directions on one tool, because shaping ground is going back and forth " +
         "over the same few tiles, and a step too far and a step back should not be a trip to " +
         "the panel and back between them.@@" +
         "Change water type switches a tile of sea between deep and shallow. One click does " +
         "it, since there are only the two; the right button does nothing here, having no " +
         "second direction to carry. It changes how the water is drawn and nothing else: the " +
         "sea is all at one level, and a shallow is water near enough to the shore that a map " +
         "would paint it paler. Mark Shallows in the Base Land panel lays the whole band at " +
         "once and will overwrite anything set here.@@" +
         "Change resource puts the deposit named in the drop-down on the tile, or takes it off " +
         "again if it is already there. A tile holding a different deposit is given this one " +
         "instead -- there is room for one at a time. Like the water tool it is one click, and " +
         "the right button does nothing.@@" +
         "Where a deposit may sit is the same rule the scatter passes obey, so a click can be " +
         "refused: nothing at sea or on running water, no timber above the treeline or in sand, " +
         "and no oil off the flats and hills. Ore, brimstone and stone go anywhere on dry land. " +
         "Taking one off is never refused. Scattering from the Resources panel replaces every " +
         "deposit of the kind it is dealing, so it will overwrite what is placed here.@@" +
         "A click that has nothing to do says so and changes nothing: past either end of the " +
         "heights, elevation on the sea, water on the land, or a deposit on ground that cannot " +
         "hold it.@@" +
         "A run of clicks is one undo, not one each, whichever tools it used. The history is " +
         $"only {UndoDepth} maps deep, and filing every click would mean five touch-ups threw " +
         "away the build they were touching up -- so Ctrl+Z puts the map back as it was when " +
         "this run of editing started. Running a pass, opening a file or making a map ends the " +
         "run, and the next click starts a new one.").Replace("@@", "\n\n");

    /// <summary>What the View panel is for.</summary>
    public string ViewHelp =>
        "How the map is drawn, as opposed to what is on it.\n\n" +
        "Nothing here changes a single tile, so none of it can be got wrong.";

    /// <summary>The renderer choice's tooltip.</summary>
    public string RendererHelp =>
        ("Which picture of the map to draw. The map itself is the same either way.@@" +
         "Terrain is the world as it is made: ground, cover, rivers, deposits, and the hover " +
         "and selection marks over them.@@" +
         "Elevation throws all of that away and draws height alone. The sea is black and the " +
         "land runs from a near-black grey at the flats to nearly white at the highest peak, " +
         "by an even step per elevation. It is the picture to check a range against, because " +
         "it is the only one with nothing else in it: a coastline, a pass and a saddle are " +
         "all far easier to read once the grass and the marshes are gone.@@" +
         "Rivers are the one thing it keeps, in blue, at the height they cross -- so the water " +
         "lightens as it climbs towards its source, and the valley it came down is still a " +
         "valley you can find. Nothing else on the tile shows.@@" +
         "Resources throws away the height as well. The world goes flat -- black sea, grey " +
         "land, paler grey rivers -- and every deposit is a solid square of its own colour, " +
         "with the key below to read them by. Flat on purpose: a deposit is a few tiles wide, " +
         "and ground drawn as grass and marsh and sand is competing colour at exactly that " +
         "size. Take the world down to a grey and the squares are the only colour left.@@" +
         "Nothing else moves with it. The zoom, where the view is looking, and every tile on " +
         "the map are exactly as they were, so switching back and forth costs nothing but the " +
         "repaint.@@" +
         "The elevation numbers below show in either picture. They are worth turning on here " +
         "above all: the greys say which of two slopes is the higher and only the number says " +
         "by how much.").Replace("@@", "\n\n");

    /// <summary>The animation toggle's tooltip.</summary>
    public string AnimationHelp =>
        "Keep repainting the map so the water moves.\n\n" +
        "Water is the only ground that animates; everything else is drawn from a cached " +
        "atlas and looks the same either way.\n\n" +
        "Turning it off stops the repaint loop altogether, so the map is redrawn only when " +
        "something changes it. That costs nothing while the map sits idle, and it leaves " +
        "the frame rate with nothing to measure.";

    /// <summary>The shallows-reach tooltip.</summary>
    public string ShallowsReachHelp =>
        ("How far the shallow water reaches out from the land, in tiles.@@" +
         "Measured from every shore at once, so an island gets its own shelf and a strait " +
         "narrower than twice this is shallow the whole way across -- which is what a strait " +
         "is.@@" +
         "An average rather than a rule: the variation dial moves the drop-off in and out " +
         "either side of it. At zero reach there are no shallows at all and the sea runs deep " +
         "up to the beach.").Replace("@@", "\n\n");

    /// <summary>The shallows-variation tooltip.</summary>
    public string ShallowsVariationHelp =>
        ("How much the reach wanders along the coast.@@" +
         "At nothing the shallows are a ribbon of one width the whole way round, which is the " +
         "one thing that gives a drawn sea away as a measurement. Turn it up and the shelf " +
         "runs from nothing at one headland to twice the reach across the next bay.@@" +
         "It moves the edge of the shelf over the length of a bay rather than tile by tile. " +
         "A finer wander would fray the drop-off into speckle, which reads as a fault in the " +
         "picture rather than as water.").Replace("@@", "\n\n");

    /// <summary>What the Shallows button does.</summary>
    public string BuildShallowsHelp =>
        ("Mark the sea near the land as shallow and draw it paler.@@" +
         "Depth here is drawn and not modelled. The sea is all at one level and stays there; " +
         "a shallow is a tile of water near enough to a shore that a map would show the " +
         "bottom through it. Nothing moves, nothing is raised, and no coastline changes.@@" +
         "Run it after the land is the shape you want. It reads the coast as it stands, so " +
         "building continents or islands afterwards leaves the old shelf behind -- press it " +
         "again and it will follow the new shore.").Replace("@@", "\n\n");

    /// <summary>What the Hills and Mountains panel is for.</summary>
    public string TerrainHelp =>
        ("Raise the flat land into hills and mountains. It shapes height only -- not one " +
         "tile of coast moves, and no land is added or taken away.@@" +
         "The two are raised separately, by their own buttons, and neither disturbs the " +
         "other: press either as often as you like without the second press piling onto the " +
         "first. They share no dial but the seed -- Rugged and Range shape the ranges, " +
         "Spread belongs to the hills -- and the seed is shared because it is what says " +
         "which world this is. Both are cut from the one relief, which is what keeps them " +
         "one landscape.@@" +
         "Run them after the land is the shape you want. Building continents or scattering " +
         "islands starts the world over from sea level, so anything raised here goes with " +
         "it and has to be raised again.@@" +
         "Turn on Elevation in the View panel to read the numbers off the tiles.").Replace("@@", "\n\n");

    /// <summary>The Raise Hills tooltip.</summary>
    public string RaiseHillsHelp =>
        ("Lay hills over the flat land, at elevation 2 to 10.@@" +
         "Mountains are left exactly where they are, and the hills take only ground the " +
         "mountains are not already standing on -- so the figure above is what is left of " +
         "the dial once a range has had its share.@@" +
         "Pressing this again replaces the hills rather than adding more.").Replace("@@", "\n\n");

    /// <summary>The Raise Mountains tooltip.</summary>
    public string RaiseMountainsHelp =>
        ("Raise mountain ranges, at elevation 11 to 30, and cut passes through them.@@" +
         "The hills are left where they are. A range that would wall off part of a " +
         "landmass has its lowest saddle cut down to hill height, so every part of a " +
         "continent can be walked to from every other part without climbing -- which costs " +
         "a fraction of a percent of the mountain figure above.@@" +
         "Pressing this again replaces the ranges rather than adding more.").Replace("@@", "\n\n");

    /// <summary>The mountain-coverage tooltip.</summary>
    public string MountainCoverageHelp =>
        ("How much of the land ends up as mountain -- elevation 11 to 30.@@" +
         "Of the land rather than of the map, so it means the same thing on a world that is " +
         "mostly ocean as on one that is mostly continent. Exact, like the land dial: the " +
         "generator scores every tile and takes the best of them, so this figure holds " +
         "however the other dials are set.@@" +
         "Mountains are raised first and hills fill in around them, so this dial is the one " +
         "that has its share whatever else is asked for.").Replace("@@", "\n\n");

    /// <summary>The hill-coverage tooltip.</summary>
    public string HillCoverageHelp =>
        ("How much of the land ends up as hills -- elevation 2 to 10.@@" +
         "Counted as a share of all the land, but the hills can only have ground the " +
         "mountains are not on. Ask for more than is left and you get what is left.@@" +
         "Hills are the next band down from the same field the mountains come out of, which " +
         "is why they gather around the ranges rather than being scattered at random.").Replace("@@", "\n\n");

    /// <summary>The ruggedness tooltip.</summary>
    public string RuggednessHelp =>
        ("Whether the high ground is rounded or sharp.@@" +
         "Low is broad swells of upland with the height spread over them. High strings the " +
         "peaks along creases, and a crease in a smooth field is a line -- which is what " +
         "makes a range read as a range rather than as a patch. Turn it up for a world of " +
         "chains and passes; turn it down for rolling country.@@" +
         "It changes where the mountains are, never how much mountain there is.").Replace("@@", "\n\n");

    /// <summary>The range-size tooltip.</summary>
    public string RangeSizeHelp =>
        ("Roughly how many tiles a range runs for before it breaks up.@@" +
         "Small values give scattered massifs; large ones give long chains that cross the " +
         "whole of a continent. It also sets how far inland the high ground is pushed, since " +
         "a range that runs down into the sea reads as a drowned world rather than a " +
         "continent.").Replace("@@", "\n\n");

    /// <summary>The hill-spread tooltip.</summary>
    public string HillSpreadHelp =>
        ("How far the hills reach out from the high ground, in tiles.@@" +
         "The hills are cut from the same relief as the mountains, blurred by this much. " +
         "Blurring moves high ground nowhere and only widens it, so however far you push " +
         "this the hills stay an apron around the ranges rather than wandering off on their " +
         "own -- they just make a broader one.@@" +
         "At zero they are the ring immediately below the ranges, which is as tight as the " +
         "two ever sit. It does nothing at all to a mountain.").Replace("@@", "\n\n");

    /// <inheritdoc cref="ContinentSeedHelp"/>
    public string TerrainSeedHelp =>
        ("Any whole number. The same seed with the same settings raises the same hills and " +
         "the same mountains, every time.@@" +
         "The relief is built on the land it is given, so changing the land seed changes " +
         "this too -- the same terrain seed on a different coastline is a different world.").Replace("@@", "\n\n");

    /// <summary>What the Swamps and Deserts panel is for.</summary>
    public string CoverHelp =>
        ("Spread swamp and desert over the ground, in place of grass.@@" +
         "Neither can go anywhere but flat land or hills. Mountains are rock and ice, the sea " +
         "is not ground at all, and raising a range over a marsh takes the marsh with it -- " +
         "it is eleven thousand feet up now.@@" +
         "Like the panel above, the two have a button each and neither disturbs the other. " +
         "Nothing here moves a coast or changes a height: a swamp is what a tile is made of, " +
         "not where it is.").Replace("@@", "\n\n");

    /// <summary>The swamp-coverage tooltip.</summary>
    public string SwampCoverageHelp =>
        ("How much of the land ends up as swamp.@@" +
         "Swamps are water that has nowhere to drain, so they look for the lowest ground they " +
         "can find and they lean towards the coast. A bog on a hilltop is wrong however near " +
         "the sea it is, and one in the middle of a continent has nothing feeding it.@@" +
         "Counted as a share of all the land, but only lowland can hold it. Ask for more than " +
         "there is lowland and you get the lowland there is.").Replace("@@", "\n\n");

    /// <summary>The desert-coverage tooltip.</summary>
    public string DesertCoverageHelp =>
        ("How much of the land ends up as desert.@@" +
         "Deserts are land the weather cannot reach, so they look for the deep interior. " +
         "Height does not come into it -- sand is as happy on a hill as on a plain, so long " +
         "as it is not a mountain.@@" +
         "Counted the same way as the swamps, and it will not take ground a swamp is already " +
         "on.@@" +
         "Neither writes over the other, so whichever you spread first gets first pick of the " +
         "ground they both want -- about one tile of cover in thirteen. Either way both dials " +
         "are met in full.").Replace("@@", "\n\n");

    /// <summary>The patch-size tooltip.</summary>
    public string PatchSizeHelp =>
        ("Roughly how many tiles across one patch runs. Shared: it sets the grain of both.@@" +
         "Small values give a lot of little marshes and sand pans; large ones give a few " +
         "broad ones. It does not change how much of either there is -- only how big one " +
         "piece of it runs.").Replace("@@", "\n\n");

    /// <summary>The clustering tooltip.</summary>
    public string ClusteringHelp =>
        ("How much the patches gather together. Shared, like the patch size.@@" +
         "At nothing they are sprinkled over whatever ground suits them, wherever on the map " +
         "that is -- marsh in every hollow, sand in every dry corner. Turn it up and they " +
         "heap into a few districts instead: one great fen country and one great erg, with " +
         "the rest of the lowland left clear.@@" +
         "It takes no tile off the coverage -- the same amount of swamp arrives either way, " +
         "and no patch is drawn any bigger. But patches heaped together run into their " +
         "neighbours, so the pieces you end up looking at are fewer and broader.").Replace("@@", "\n\n");

    /// <summary>What the Rivers panel is for.</summary>
    public string RiverHelp =>
        ("Run rivers down off the hills to the sea.@@" +
         "A river is what a tile is made of and not how high it stands: it takes the elevation " +
         "of the ground it replaces, so nothing here digs a channel, moves a coast or touches " +
         "a range. Press it over a finished world and the world is as it was, with water on " +
         "it.@@" +
         "They start in the hills rather than the mountains. A river takes the height of what " +
         "it crosses, and a range is not even ground -- one started on a crag comes out with " +
         "a twenty-unit step across its own width. The foot of a range is where the water " +
         "gathers anyway.@@" +
         "Rivers run into each other: one that reaches a river already drawn stops there and " +
         "becomes its tributary, so what you get is systems rather than parallel lines.@@" +
         "Like the islands, a press adds to what is there rather than replacing it. Press " +
         "again for another set and it will find country the last one left alone, joining " +
         "what it meets; keep pressing and it will eventually tell you there is no room " +
         "left.").Replace("@@", "\n\n");

    /// <summary>The river-count tooltip.</summary>
    public string RiverCountHelp =>
        ("How many rivers to add to whatever is already there.@@" +
         "A target rather than a promise. Sources are kept well apart -- from each other, so " +
         "they do not all start on the same hill, and from the rivers already on the map, so " +
         "a second press works into country the first one left alone. One that cannot make a " +
         "river as long as you have asked for is passed over too.@@" +
         "So a bare world gives you the number you ask for and a well-watered one gives you " +
         "fewer. The line below says which you got.@@" +
         "Count them on the map by their mouths and not by their lines: two rivers that meet " +
         "are still two.").Replace("@@", "\n\n");

    /// <summary>The winding tooltip.</summary>
    public string RiverWindingHelp =>
        ("How much a river wanders on its way down.@@" +
         "At nothing it takes the straightest way to the water that does not climb, which " +
         "reads as a canal. Turn it up and it follows the lie of the country instead, bending " +
         "well out of its way and coming back -- longer, slower, and the shape a river " +
         "actually has.@@" +
         "It never buys a bend by climbing. Going uphill is dear enough to outweigh the whole " +
         "of this dial, so a wandering river still falls the whole way down.").Replace("@@", "\n\n");

    /// <summary>The river-length tooltip.</summary>
    public string RiverLengthHelp =>
        ("The shortest river worth drawing, in tiles.@@" +
         "Anything shorter is a stream, and a source that can only manage one is passed over " +
         "for another further inland. Raise it for a few great rivers; lower it to let the " +
         "short coastal ones in.@@" +
         "Measured along the water rather than across the map, so a winding river clears the " +
         "bar from closer to the sea than a straight one does.").Replace("@@", "\n\n");

    /// <inheritdoc cref="ContinentSeedHelp"/>
    public string RiverSeedHelp =>
        ("Any whole number. The same seed with the same settings runs the same rivers, every " +
         "time.@@" +
         "It sets the lie of the country the rivers wind through, so changing it moves every " +
         "bend without moving a single source: the sources come off the relief, which this " +
         "does not touch.").Replace("@@", "\n\n");

    /// <summary>What the Lakes panel is for.</summary>
    public string LakeHelp =>
        ("Fill the hollows in the low ground with standing water.@@" +
         "A lake is what a tile is made of and not how high it stands, exactly as a river is: " +
         "it takes the elevation of the ground it lies over, so nothing here digs a basin, " +
         "moves a coast or touches a range. Press it over a finished world and the world is as " +
         "it was, with water in it.@@" +
         "A lake is drawn as the same water a river is, which is what lets a river run into one " +
         "as a single unbroken surface rather than two blues meeting at a line. It also means " +
         "the Rivers panel already knows what a lake is: a later set of rivers keeps its " +
         "sources clear of one and ends its routes at one, which is what a river reaching a " +
         "lake does.@@" +
         "They are not dropped on the map as shapes. Each one fills the hollow it starts in, " +
         "outwards from the lowest tile and never more than a step above it, so its outline is " +
         "the contour of the ground -- which is why it comes out an awkward shape rather than a " +
         "circle, and why it is never found lying up a hillside.@@" +
         "Like the rivers, a press adds to what is there rather than replacing it. Press again " +
         "for another set and it will find hollows the last one left alone, keeping well clear " +
         "of the sea and of water already down; keep pressing and it will eventually tell you " +
         "there is no room left.").Replace("@@", "\n\n");

    /// <summary>The lake-count tooltip.</summary>
    public string LakeCountHelp =>
        ("How many lakes to add to whatever is already there.@@" +
         "A target rather than a promise. Each one needs a hollow with room for the size you " +
         "have asked for, dry land between it and every other piece of water, and space from " +
         "the lakes this press has already filled -- so a rolling world with wide valleys gives " +
         "you the number you ask for and a flat or crowded one gives you fewer. The line below " +
         "says which you got.@@" +
         "Count them on the map by their shores and not by the blue: a lake that a river runs " +
         "into is one piece of water with a river attached, and still one lake.").Replace("@@", "\n\n");

    /// <summary>The lake-size tooltip.</summary>
    public string LakeSizeHelp =>
        ("About how many tiles of water a lake covers.@@" +
         "An average, not a ruling: each one varies either side of it, because lakes of one " +
         "size read as a spill of identical ponds. A hollow that can only hold half of what you " +
         "have asked for is passed over for a better one rather than filled with a puddle.@@" +
         "Asked in tiles of surface rather than in a width, since a lake has no width worth " +
         "quoting -- that is rather the point of it. Somewhere around eighty tiles is a lake " +
         "you can see at a glance on a map this size; a few hundred is an inland sea and will " +
         "only fit in the broadest valleys your world has.").Replace("@@", "\n\n");

    /// <summary>The lake-shape tooltip.</summary>
    public string LakeShapeHelp =>
        ("How far a lake reaches out of round.@@" +
         "At nothing it takes only the shape the ground gives it, which in gentle country is " +
         "close to a circle: on a plain the terrain has no opinion about which way the water " +
         "should spread, and a growth with no opinion of its own answers that with a disc.@@" +
         "Turn it up and a broad field of noise decides between the tiles the ground cannot, so " +
         "the water reaches well into one quarter and hardly at all into the next -- lobes, " +
         "bays and a headland, which is what a lake looks like.@@" +
         "It never buys its shape by climbing. Going uphill is dear enough to outweigh the " +
         "whole of this dial, so however far a lake reaches it stays in the hollow and stays " +
         "flat.").Replace("@@", "\n\n");

    /// <inheritdoc cref="ContinentSeedHelp"/>
    public string LakeSeedHelp =>
        ("Any whole number. The same seed with the same settings fills the same lakes, every " +
         "time.@@" +
         "It sets both the field that pulls their shapes out of round and how big each one comes " +
         "out within the size you have asked for. Which hollows they choose comes off the " +
         "relief, which this does not touch -- so changing it reshapes the lakes without moving " +
         "them.").Replace("@@", "\n\n");

    /// <inheritdoc cref="ContinentSeedHelp"/>
    public string CoverSeedHelp =>
        ("Any whole number. The same seed with the same settings spreads the same swamps and " +
         "the same deserts, every time.@@" +
         "The two are dealt off this one seed but not off one field, or a swamp and a desert " +
         "would want exactly the same ground and only the first one pressed would get any.").Replace("@@", "\n\n");

    /// <summary>What the Resources panel is for.</summary>
    public string ResourceHelp =>
        ("Scatter what the land is worth over it: ore, timber, oil, brimstone and stone.@@" +
         "A resource is what a tile has rather than what it is made of, so it is drawn over " +
         "the ground instead of in place of it -- iron in a marsh is an ordinary thing for a " +
         "map to say, and nothing here moves a coast, a height or a cover.@@" +
         "Each has a button of its own and none disturbs the others, but a tile carries one " +
         "deposit or none: whichever you spread first gets first pick of the ground two of " +
         "them both want. Spread All presses the five in turn.@@" +
         "Run them last. They are laid on the world as it stands, and anything that rebuilds " +
         "the land underneath -- a new coastline above all -- takes its deposits with it.").Replace("@@", "\n\n");

    /// <summary>The iron tooltip.</summary>
    public string IronCoverageHelp =>
        ("How much of the land ends up carrying iron.@@" +
         "Ore wants the high ground, where the rock is at the surface rather than buried " +
         "under everything that has settled on it since. It is barred from nowhere on land, " +
         "though: a seam in the lowlands is unusual and not impossible.@@" +
         "Counted as a share of all the land, like every other coverage dial.").Replace("@@", "\n\n");

    /// <summary>The timber tooltip.</summary>
    public string WoodCoverageHelp =>
        ("How much of the land ends up carrying timber.@@" +
         "Wood is the fussiest of the five: nothing grows in sand and nothing grows above the " +
         "treeline, so it takes only lowland that is not desert, and it leans towards the " +
         "weather coming off the sea.@@" +
         "The figure below counts the lowland, which is the ceiling it cannot pass. Sand and " +
         "running water come off that, so a world of great deserts will give you less than it " +
         "says.").Replace("@@", "\n\n");

    /// <summary>The oil tooltip.</summary>
    public string OilCoverageHelp =>
        ("How much of the land ends up carrying oil.@@" +
         "Crude lies where the ground has been low for a very long time, so it takes lowland " +
         "only -- never a mountain -- and prefers the interior basins to the coast.@@" +
         "Counted against the lowland, as the timber is.").Replace("@@", "\n\n");

    /// <summary>The sulphur tooltip.</summary>
    public string SulphurCoverageHelp =>
        ("How much of the land ends up carrying sulphur.@@" +
         "Brimstone is the most particular about where it sits: it gathers on the ranges " +
         "themselves rather than over the upland generally, which is why a world with no " +
         "mountains has very little of it and it turns up in the same few places when it " +
         "does.").Replace("@@", "\n\n");

    /// <summary>The stone tooltip.</summary>
    public string StoneCoverageHelp =>
        ("How much of the land ends up carrying building stone.@@" +
         "It wants the same high ground the ore does and is far less fussy about getting it: " +
         "half of what makes a quarry is the rock and half is somebody wanting to build with " +
         "it. Expect it in patches anywhere, and more of them in the hills.").Replace("@@", "\n\n");

    /// <summary>The resource patch-size tooltip.</summary>
    public string ResourcePatchSizeHelp =>
        ("Roughly how many tiles across one field of a resource runs. Shared: it sets the " +
         "grain of all five.@@" +
         "Small values scatter single tiles and pairs across the whole world; large ones give " +
         "a few broad fields worth going to war over. It does not change how much of anything " +
         "there is -- only how big one piece of it runs.@@" +
         "Its own dial rather than the cover panel's, because there is no reason a world of " +
         "small marshes should also be a world of small ore fields.").Replace("@@", "\n\n");

    /// <summary>The resource clustering tooltip.</summary>
    public string ResourceClusteringHelp =>
        ("How much the fields gather together. Shared, like the patch size.@@" +
         "At nothing they are sprinkled over whatever ground suits them, wherever on the map " +
         "that is. Turn it up and they heap into districts instead: an ore country and an oil " +
         "country, with the rest of the world left bare -- which is what makes one stretch of " +
         "coast worth holding and the next one not.@@" +
         "It takes no tile off the coverage. The same amount arrives either way, and no field " +
         "is drawn any bigger; but fields heaped together run into their neighbours, so the " +
         "pieces you end up looking at are fewer and broader.").Replace("@@", "\n\n");

    /// <inheritdoc cref="ContinentSeedHelp"/>
    public string ResourceSeedHelp =>
        ("Any whole number. The same seed with the same settings scatters the same deposits, " +
         "every time.@@" +
         "The five are dealt off this one seed but not off one field, or they would all want " +
         "exactly the same ground and only the first one pressed would get any.").Replace("@@", "\n\n");

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
            .Replace("@@", "\n\n");

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
        ContinentSeed = NewSeed();

    /// <summary>The seed as typed. Any whole number, negatives included.</summary>
    private static bool TryReadSeed(string? text, out int seed) => int.TryParse(text, out seed);

    /// <summary>
    /// A fresh seed for one of the New buttons. Six digits and never zero: long enough that
    /// two presses are unlikely to collide, short enough to read out to somebody.
    /// </summary>
    private static string NewSeed() =>
        Random.Shared.Next(1, 1_000_000).ToString(CultureInfo.InvariantCulture);

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
        IslandSeed = NewSeed();

    /// <summary>
    /// The line under the shallows controls: what the next pass will do, or what is stopping
    /// it.
    /// </summary>
    public string ShallowsSummary
    {
        get
        {
            if (Tiles is null)
                return "Make a map in Start first.";

            if (_land is null)
                return "Build some land first.";

            var reach = (int)ShallowsReach;

            if (reach <= 0)
                return "Deep water right up to the beach.";

            var edge = ShallowsVariation switch
            {
                >= 80 => "wandering far in and out",
                >= 40 => "wandering along the coast",
                > 0 => "barely wandering",
                _ => "at one width the whole way round",
            };

            return $"Shallows about {reach:N0} tiles out, {edge}.";
        }
    }

    /// <inheritdoc cref="HasSizeProblem"/>
    public bool HasShallowsProblem => !CanBuildShallows();

    /// <summary>
    /// Marks the sea near the land as shallow and puts the result on screen.
    /// <para>
    /// Depth only. The land mask, the relief and every cover on the land come through exactly
    /// as they were -- this changes how a tile of sea is drawn and nothing else at all.
    /// </para>
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanBuildShallows))]
    private void BuildShallows()
    {
        if (_map is not { } map || _land is not { } land)
            return;

        Remember();

        var relief = CurrentRelief(map);

        // The continents' seed rather than one of its own. The shelf is a property of this
        // coastline and the coastline is that seed's doing, so a world re-rolled from it gets
        // a new shore and new shallows together -- which is one dial fewer on a panel that
        // already has two seeds on it.
        var cover = ShallowsBuilder.Mark(
            map.Width, map.Height, land, CurrentCover(map),
            new ShallowSettings(ShallowsReach, ShallowsVariation / 100, ReadSeedOr(ContinentSeed, 1)));

        Rebuild(map, relief, cover, CurrentResources(map));
    }

    /// <summary>Whether there is a coast to lay a shelf against.</summary>
    private bool CanBuildShallows() => _map is not null && _land is not null;

    /// <summary>
    /// A seed box read for a pass that does not have a seed box of its own, falling back
    /// rather than refusing: the shallows are worth having on a map whose continent seed has
    /// been typed into and left half-finished.
    /// </summary>
    private static int ReadSeedOr(string text, int fallback) =>
        TryReadSeed(text, out var seed) ? seed : fallback;

    /// <summary>
    /// The line under the hill controls: what the next raise will do, or what is stopping it.
    /// </summary>
    public string HillSummary
    {
        get
        {
            if (TerrainTrouble() is { } trouble)
                return trouble;

            // What the pass will actually do rather than what the dial says: the hills can
            // only have ground the mountains are not already standing on.
            var acres = Acres();
            var standing = MountainTiles();
            var hills = Math.Min((long)(acres * HillCoverage / 100), acres - standing);

            var apron = HillSpread < 1
                ? "in a ring below the ranges"
                : $"spread {HillSpread:0} tiles out from the ranges";

            return $"{hills:N0} tiles of hills, {apron}.";
        }
    }

    /// <inheritdoc cref="HasSizeProblem"/>
    public bool HasHillProblem => !CanBuildTerrain();

    /// <summary>
    /// The line under the mountain controls.
    /// </summary>
    public string MountainSummary
    {
        get
        {
            if (TerrainTrouble() is { } trouble)
                return trouble;

            return $"{(long)(Acres() * MountainCoverage / 100):N0} tiles of mountain, with passes cut through.";
        }
    }

    /// <inheritdoc cref="HasSizeProblem"/>
    public bool HasMountainProblem => !CanBuildTerrain();

    /// <summary>
    /// What is stopping either terrain pass, or null if nothing is. The two summaries say the
    /// same things about the same conditions, so they say them from one place.
    /// </summary>
    private string? TerrainTrouble()
    {
        if (Tiles is null)
            return "Make a map in Start first.";

        if (_land is null)
            return "Build some land first.";

        return TryReadSeed(TerrainSeed, out _) ? null : "Seed: any whole number.";
    }

    /// <summary>How many tiles of land there are to work with.</summary>
    private long Acres() => CountTiles(static elevation => elevation > Elevations.Sea);

    /// <summary>How much of the map is already standing at mountain height.</summary>
    private long MountainTiles() =>
        CountTiles(static elevation => elevation >= Elevations.MountainsFrom);

    /// <summary>
    /// How many tiles stand at a height the summaries care about.
    /// <para>
    /// Off the tiles rather than off the land mask, so every one of these counts the map that
    /// is actually on screen. The mask says only what is land; the summaries also have to ask
    /// how high it is, and asking two different sources is how two lines under two dials end
    /// up disagreeing about the same map.
    /// </para>
    /// </summary>
    private long CountTiles(Func<int, bool> wanted)
    {
        if (Tiles is not { } tiles)
            return 0;

        var counted = 0L;

        for (var i = 0; i < tiles.Count; i++)
        {
            if (wanted(tiles[i].Elevation))
                counted++;
        }

        return counted;
    }

    /// <summary>
    /// Raises the hills, leaving any mountains where they are.
    /// <para>
    /// Height only. The coast does not move, so the land mask is left exactly as it was and
    /// every pass that drew it still holds -- these are the one kind of build that can be run
    /// over a finished world without starting it over.
    /// </para>
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanBuildTerrain))]
    private void BuildHills() => Raise(TerrainBuilder.RaiseHills);

    /// <summary>Raises the mountains, leaving any hills where they are.</summary>
    /// <inheritdoc cref="BuildHills" path="/summary/para"/>
    [RelayCommand(CanExecute = nameof(CanBuildTerrain))]
    private void BuildMountains() => Raise(TerrainBuilder.RaiseMountains);

    /// <summary>
    /// Runs one of the terrain passes over the map and puts the result on screen.
    /// <para>
    /// The heights the pass builds on are read back off the tiles rather than kept alongside
    /// them. The tiles are where the elevation actually lives -- it is what gets saved, and
    /// what undo restores -- so anything held beside them would be a second copy to keep in
    /// step, and the one that went stale would be this one.
    /// </para>
    /// </summary>
    private void Raise(
        Func<int, int, bool[], int[], TerrainSettings, int[]> pass)
    {
        if (_map is not { } map || _land is not { } land || !TryReadSeed(TerrainSeed, out var seed))
            return;

        Remember();

        var relief = pass(map.Width, map.Height, land, CurrentRelief(map), new TerrainSettings(
            MountainCoverage / 100, HillCoverage / 100, Ruggedness / 100, RangeSize,
            HillSpread, seed));

        // Carried through rather than cleared: raising a range is not a reason to drain the
        // marshes on the other side of the continent. What the range itself rises over does
        // lose its cover, which Rebuild sees to.
        Rebuild(map, relief, CurrentCover(map), CurrentResources(map));
    }

    /// <summary>The height of every tile, in reading order.</summary>
    private static int[] CurrentRelief(TileMap map)
    {
        var tiles = map.Tiles;
        var relief = new int[tiles.Count];

        for (var i = 0; i < relief.Length; i++)
            relief[i] = tiles[i].Elevation;

        return relief;
    }

    /// <summary>
    /// What every tile is made of, in reading order. Read back off the ground each tile is
    /// drawn with, for the same reason the heights are: the tiles are where this actually
    /// lives, and a copy kept beside them would be the one that went stale.
    /// </summary>
    private static GroundCover[] CurrentCover(TileMap map)
    {
        var tiles = map.Tiles;
        var cover = new GroundCover[tiles.Count];

        for (var i = 0; i < cover.Length; i++)
            cover[i] = CoverOf(tiles[i]);

        return cover;
    }

    /// <summary>
    /// What one tile is made of, read back off the ground it is drawn with. The single-tile
    /// half of <see cref="CurrentCover"/>, split out because editing one tile needs to know
    /// what it was before it is given a new ground -- and two copies of this mapping is two
    /// chances for the edit and the rebuild to disagree about what a marsh is.
    /// </summary>
    private static GroundCover CoverOf(MapTile tile)
    {
        if (!tile.MapRenderComponents.TryGetValue(RenderComponentLayers.BaseGround, out var ground))
            return GroundCover.Grass;

        return
            ground.ComponentType == MapRenderComponentConstants.Swamp ? GroundCover.Swamp :
            ground.ComponentType == MapRenderComponentConstants.Desert ? GroundCover.Desert :
            ground.ComponentType == MapRenderComponentConstants.Water ? GroundCover.River :
            ground.ComponentType == MapRenderComponentConstants.ShallowWater ? GroundCover.Shallow :
            GroundCover.Grass;
    }

    /// <summary>
    /// What every tile is worth, in reading order. Read back off the tiles for the reason the
    /// heights and the covers are: the tiles are where this lives, and a copy kept beside them
    /// would be the one that went stale.
    /// </summary>
    private static TileResource[] CurrentResources(TileMap map)
    {
        var tiles = map.Tiles;
        var deposits = new TileResource[tiles.Count];

        for (var i = 0; i < deposits.Length; i++)
            deposits[i] = ResourceOf(tiles[i]);

        return deposits;
    }

    /// <inheritdoc cref="CoverOf"/>
    private static TileResource ResourceOf(MapTile tile)
    {
        if (!tile.MapRenderComponents.TryGetValue(RenderComponentLayers.Resource, out var deposit))
            return TileResource.None;

        return
            deposit.ComponentType == MapRenderComponentConstants.Iron ? TileResource.Iron :
            deposit.ComponentType == MapRenderComponentConstants.Wood ? TileResource.Wood :
            deposit.ComponentType == MapRenderComponentConstants.Oil ? TileResource.Oil :
            deposit.ComponentType == MapRenderComponentConstants.Sulphur ? TileResource.Sulphur :
            deposit.ComponentType == MapRenderComponentConstants.Stone ? TileResource.Stone :
            TileResource.None;
    }

    /// <summary>
    /// Whether there is land to raise, and a seed to raise it with. Land and not merely a
    /// map: there is nothing to do to an empty ocean, and a button that would do nothing
    /// should say so by being grey.
    /// </summary>
    private bool CanBuildTerrain() =>
        _map is not null && _land is not null && TryReadSeed(TerrainSeed, out _);

    /// <summary>Fills the terrain seed box with a new one.</summary>
    [RelayCommand]
    private void NewTerrainSeed() =>
        TerrainSeed = NewSeed();

    /// <summary>
    /// The line under the swamp controls: what the next spread will do, or what is stopping it.
    /// </summary>
    public string SwampSummary => CoverSummary(SwampCoverage, "of swamp, in the low wet ground");

    /// <inheritdoc cref="HasSizeProblem"/>
    public bool HasSwampProblem => !CanSpreadCover();

    /// <summary>The line under the desert controls.</summary>
    public string DesertSummary => CoverSummary(DesertCoverage, "of desert, out in the interior");

    /// <inheritdoc cref="HasSizeProblem"/>
    public bool HasDesertProblem => !CanSpreadCover();

    /// <summary>
    /// What one of the cover passes will actually manage, which is not always what its dial
    /// says: only lowland can hold either, so a world that is mostly mountain has less to give
    /// than the dial asks for.
    /// </summary>
    private string CoverSummary(double coverage, string what)
    {
        if (Tiles is null)
            return "Make a map in Start first.";

        if (_land is null)
            return "Build some land first.";

        if (!TryReadSeed(CoverSeed, out _))
            return "Seed: any whole number.";

        var lowland = LowlandTiles();
        var tiles = Math.Min((long)(Acres() * coverage / 100), lowland);

        return lowland == 0
            ? "No lowland to spread over -- it is all mountain."
            : $"{tiles:N0} tiles {what}.";
    }

    /// <summary>
    /// How much of the map is lowland: land, and not up in the mountains. What a swamp or a
    /// desert may sit on, what timber and oil may sit on, and what a lake may lie in.
    /// </summary>
    private long LowlandTiles() => CountTiles(Elevations.IsLowland);

    /// <summary>
    /// Spreads swamp over the low wet ground, leaving any desert where it is.
    /// <para>
    /// Cover only. Nothing here moves a coast or changes a height -- a swamp is what a tile is
    /// made of, not where it is -- so the land and the relief both come through untouched.
    /// </para>
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanSpreadCover))]
    private void BuildSwamps() =>
        Spread(GroundCoverBuilder.SpreadSwamps, SwampCoverage);

    /// <summary>Spreads desert through the interior, leaving any swamp where it is.</summary>
    /// <inheritdoc cref="BuildSwamps" path="/summary/para"/>
    [RelayCommand(CanExecute = nameof(CanSpreadCover))]
    private void BuildDeserts() =>
        Spread(GroundCoverBuilder.SpreadDeserts, DesertCoverage);

    /// <summary>Runs one of the cover passes and puts the result on screen.</summary>
    /// <inheritdoc cref="Raise(Func{int, int, bool[], int[], TerrainSettings, int[]})" path="/summary/para"/>
    private void Spread(
        Func<int, int, int[], GroundCover[], CoverSettings, GroundCover[]> pass, double coverage)
    {
        if (_map is not { } map || _land is null || !TryReadSeed(CoverSeed, out var seed))
            return;

        Remember();

        var relief = CurrentRelief(map);

        var cover = pass(
            map.Width, map.Height, relief, CurrentCover(map),
            new CoverSettings(coverage / 100, PatchSize, CoverClustering / 100, seed));

        // Deposits come through: a marsh spreading over a wood takes the wood, and Rebuild
        // sees to that, but the ore two counties away is no business of this pass.
        Rebuild(map, relief, cover, CurrentResources(map));
    }

    /// <summary>Whether there is land to spread over, and a seed to spread it with.</summary>
    private bool CanSpreadCover() =>
        _map is not null && _land is not null && TryReadSeed(CoverSeed, out _);

    /// <summary>Fills the cover seed box with a new one.</summary>
    [RelayCommand]
    private void NewCoverSeed() =>
        CoverSeed = NewSeed();

    /// <summary>
    /// The line under the river controls: what the next run will do, or what is stopping it.
    /// </summary>
    public string RiverSummary
    {
        get
        {
            if (Tiles is null)
                return "Make a map in Start first.";

            if (_land is null)
                return "Build some land first.";

            if (!TryReadSeed(RiverSeed, out _))
                return "Seed: any whole number.";

            // Rivers rise in the hills, so a world with none has nowhere to start one. Worth
            // saying outright rather than letting the button do nothing quietly.
            if (HillTiles() == 0)
                return "No hills to rise in -- raise some first.";

            var rivers = (int)RiverCount;

            if (rivers <= 0)
                return "No rivers.";

            // What the last press managed, until a dial moves and it is a prediction again.
            // Worth saying because neither shortfall is obvious from the map: the length dial
            // may be asking for more than the country can carry, and a map that already has
            // rivers on it has less room left for another set.
            if (_riversMade is { } ran)
            {
                return ran == rivers
                    ? $"Added {ran:N0} rivers. Press again for another set."
                    : ran == 0
                        ? $"No room for another river {(int)RiverLength:N0} tiles long."
                        : $"Added {ran:N0} of {rivers:N0} -- no room for the rest.";
            }

            return $"Adds up to {rivers:N0} more rivers, {(int)RiverLength:N0} tiles or longer, down to the sea.";
        }
    }

    /// <summary>
    /// How many rivers the last run actually managed, or null if a dial has moved since -- at
    /// which point the line under the button goes back to saying what the next run will try.
    /// </summary>
    private int? _riversMade;

    // Any dial moving makes the last run's tally stale. Written out rather than folded into
    // one handler because the toolkit generates one of these per property.
    partial void OnRiverCountChanged(double value) => _riversMade = null;

    partial void OnRiverWindingChanged(double value) => _riversMade = null;

    partial void OnRiverLengthChanged(double value) => _riversMade = null;

    partial void OnRiverSeedChanged(string value) => _riversMade = null;

    /// <inheritdoc cref="HasSizeProblem"/>
    public bool HasRiverProblem => !CanBuildRivers() || HillTiles() == 0;

    /// <summary>How much of the map is hill, which is the only ground a river may rise on.</summary>
    private long HillTiles() =>
        CountTiles(static elevation => elevation is >= Elevations.HillsFrom and <= Elevations.HillsTo);

    /// <summary>
    /// Runs another set of rivers down off the hills and puts the result on screen.
    /// <para>
    /// Cover only, like the swamps and the deserts: the land and the relief both come through
    /// exactly as they were, and a river takes the height of the tile it runs over.
    /// </para>
    /// <para>
    /// Added rather than replacing, like the islands and unlike the other cover passes, so
    /// pressing it twice leaves two sets -- and the second sees the first's rivers, keeping
    /// its sources clear of them and running into them where it meets them.
    /// </para>
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanBuildRivers))]
    private void BuildRivers()
    {
        if (_map is not { } map || _land is null || !TryReadSeed(RiverSeed, out var seed))
            return;

        Remember();

        var relief = CurrentRelief(map);

        var cover = RiverBuilder.Add(
            map.Width, map.Height, relief, CurrentCover(map),
            new RiverSettings((int)RiverCount, RiverWinding / 100, (int)RiverLength, seed),
            out var made);

        Rebuild(map, relief, cover, CurrentResources(map));

        _riversMade = made;
        OnPropertyChanged(nameof(RiverSummary));
    }

    /// <summary>Whether there is land to run rivers over, and a seed to run them with.</summary>
    private bool CanBuildRivers() =>
        _map is not null && _land is not null && TryReadSeed(RiverSeed, out _);

    /// <summary>Fills the river seed box with a new one.</summary>
    [RelayCommand]
    private void NewRiverSeed() =>
        RiverSeed = NewSeed();

    /// <summary>
    /// The line under the lake controls: what the next run will do, or what is stopping it.
    /// </summary>
    public string LakeSummary
    {
        get
        {
            if (Tiles is null)
                return "Make a map in Start first.";

            if (_land is null)
                return "Build some land first.";

            if (!TryReadSeed(LakeSeed, out _))
                return "Seed: any whole number.";

            // Lakes sit in the low ground, so a world that is all sea and mountains has nowhere
            // to put one. Worth saying outright rather than letting the button do nothing.
            if (LowlandTiles() == 0)
                return "No low ground to fill -- build some land first.";

            var lakes = (int)LakeCount;

            if (lakes <= 0)
                return "No lakes.";

            // What the last press managed, until a dial moves and it is a prediction again.
            // Worth saying because neither shortfall is obvious from the map: the size dial may
            // be asking for more than any hollow can hold, and a map that already has water on
            // it has less room left for another set.
            if (_lakesMade is { } ran)
            {
                return ran == lakes
                    ? $"Added {ran:N0} lakes. Press again for another set."
                    : ran == 0
                        ? $"No hollow left with room for {(int)LakeSize:N0} tiles of water."
                        : $"Added {ran:N0} of {lakes:N0} -- no room for the rest.";
            }

            return $"Adds up to {lakes:N0} more lakes, about {(int)LakeSize:N0} tiles each, in the hollows.";
        }
    }

    /// <summary>
    /// How many lakes the last run actually managed, or null if a dial has moved since -- at
    /// which point the line under the button goes back to saying what the next run will try.
    /// </summary>
    private int? _lakesMade;

    // Any dial moving makes the last run's tally stale, as with the rivers above.
    partial void OnLakeCountChanged(double value) => _lakesMade = null;

    partial void OnLakeSizeChanged(double value) => _lakesMade = null;

    partial void OnLakeShapeChanged(double value) => _lakesMade = null;

    partial void OnLakeSeedChanged(string value) => _lakesMade = null;

    /// <inheritdoc cref="HasSizeProblem"/>
    public bool HasLakeProblem => !CanBuildLakes() || LowlandTiles() == 0;

    /// <summary>
    /// Fills another set of lakes in the low ground and puts the result on screen.
    /// <para>
    /// Cover only, like the rivers: the land and the relief both come through exactly as they
    /// were, and a lake takes the height of the tiles it lies over.
    /// </para>
    /// <para>
    /// Added rather than replacing, like the rivers and unlike the other cover passes, so
    /// pressing it twice leaves two sets -- and the second sees the first's water, keeping clear
    /// of it rather than laying a lake across it.
    /// </para>
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanBuildLakes))]
    private void BuildLakes()
    {
        if (_map is not { } map || _land is null || !TryReadSeed(LakeSeed, out var seed))
            return;

        Remember();

        var relief = CurrentRelief(map);

        var cover = LakeBuilder.Add(
            map.Width, map.Height, relief, CurrentCover(map),
            new LakeSettings((int)LakeCount, LakeSize, LakeShape / 100, seed),
            out var made);

        Rebuild(map, relief, cover, CurrentResources(map));

        _lakesMade = made;
        OnPropertyChanged(nameof(LakeSummary));
    }

    /// <summary>Whether there is land to fill lakes in, and a seed to fill them with.</summary>
    private bool CanBuildLakes() =>
        _map is not null && _land is not null && TryReadSeed(LakeSeed, out _);

    /// <summary>Fills the lake seed box with a new one.</summary>
    [RelayCommand]
    private void NewLakeSeed() =>
        LakeSeed = NewSeed();

    /// <summary>The line under the iron controls: what the next spread will do, or what is
    /// stopping it.</summary>
    public string IronSummary => ResourceSummary(IronCoverage, Acres, "of iron, up in the high ground");

    /// <summary>The line under the timber controls.</summary>
    public string WoodSummary => ResourceSummary(WoodCoverage, LowlandTiles, "of timber, below the treeline");

    /// <summary>The line under the oil controls.</summary>
    public string OilSummary => ResourceSummary(OilCoverage, LowlandTiles, "of oil, in the low basins");

    /// <summary>The line under the sulphur controls.</summary>
    public string SulphurSummary => ResourceSummary(SulphurCoverage, Acres, "of sulphur, on the ranges");

    /// <summary>The line under the stone controls.</summary>
    public string StoneSummary => ResourceSummary(StoneCoverage, Acres, "of stone, wherever the rock is");

    /// <inheritdoc cref="HasSizeProblem"/>
    public bool HasResourceProblem => !CanSpreadResources();

    /// <summary>
    /// What one of the resource passes will actually manage, which is not always what its dial
    /// says: each resource has its own idea of what ground it can sit on.
    /// </summary>
    /// <param name="room">
    /// How much of the map could hold this resource at all, counted off the heights alone --
    /// all the land for the three that go anywhere, the lowland for the two that do not. The
    /// finer exclusions are not counted here: sand will not grow timber and no resource sits
    /// on a river, so a world of great deserts delivers a little less than this says. Both are
    /// in the tooltip, and neither is worth a walk over every tile's ground on every keystroke.
    /// </param>
    private string ResourceSummary(double coverage, Func<long> room, string what)
    {
        if (Tiles is null)
            return "Make a map in Start first.";

        if (_land is null)
            return "Build some land first.";

        if (!TryReadSeed(ResourceSeed, out _))
            return "Seed: any whole number.";

        var ground = room();
        var tiles = Math.Min((long)(Acres() * coverage / 100), ground);

        return ground == 0
            ? "No ground of the right kind to put it on."
            : $"{tiles:N0} tiles {what}.";
    }

    /// <summary>Scatters iron over the high ground, leaving the other four where they are.</summary>
    /// <inheritdoc cref="Deposit" path="/summary/para"/>
    [RelayCommand(CanExecute = nameof(CanSpreadResources))]
    private void BuildIron() =>
        Deposit(ResourceBuilder.SpreadIron, IronCoverage);

    /// <summary>Scatters timber over the well-watered lowlands.</summary>
    /// <inheritdoc cref="Deposit" path="/summary/para"/>
    [RelayCommand(CanExecute = nameof(CanSpreadResources))]
    private void BuildWood() =>
        Deposit(ResourceBuilder.SpreadWood, WoodCoverage);

    /// <summary>Scatters oil through the low basins.</summary>
    /// <inheritdoc cref="Deposit" path="/summary/para"/>
    [RelayCommand(CanExecute = nameof(CanSpreadResources))]
    private void BuildOil() =>
        Deposit(ResourceBuilder.SpreadOil, OilCoverage);

    /// <summary>Scatters sulphur over the ranges.</summary>
    /// <inheritdoc cref="Deposit" path="/summary/para"/>
    [RelayCommand(CanExecute = nameof(CanSpreadResources))]
    private void BuildSulphur() =>
        Deposit(ResourceBuilder.SpreadSulphur, SulphurCoverage);

    /// <summary>Scatters building stone wherever the rock is.</summary>
    /// <inheritdoc cref="Deposit" path="/summary/para"/>
    [RelayCommand(CanExecute = nameof(CanSpreadResources))]
    private void BuildStone() =>
        Deposit(ResourceBuilder.SpreadStone, StoneCoverage);

    /// <summary>
    /// Spreads all five, in the order they appear on the panel.
    /// <para>
    /// One pass over the map and one undo step rather than five of each, which is the whole
    /// point of it: five presses put four maps into the undo history that nobody wanted to
    /// keep. The order still decides who gets the ground two of them want, and it is the
    /// order shown -- iron first, stone last.
    /// </para>
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanSpreadResources))]
    private void BuildAllResources()
    {
        if (_map is not { } map || _land is null || !TryReadSeed(ResourceSeed, out var seed))
            return;

        Remember();

        var relief = CurrentRelief(map);
        var cover = CurrentCover(map);
        var deposits = CurrentResources(map);

        foreach (var (pass, coverage) in Passes())
        {
            deposits = pass(
                map.Width, map.Height, relief, cover, deposits,
                new DepositSettings(coverage / 100, ResourcePatchSize, ResourceClustering / 100, seed));
        }

        Rebuild(map, relief, cover, deposits);

        IEnumerable<(Func<int, int, int[], GroundCover[], TileResource[], DepositSettings, TileResource[]>, double)> Passes()
        {
            yield return (ResourceBuilder.SpreadIron, IronCoverage);
            yield return (ResourceBuilder.SpreadWood, WoodCoverage);
            yield return (ResourceBuilder.SpreadOil, OilCoverage);
            yield return (ResourceBuilder.SpreadSulphur, SulphurCoverage);
            yield return (ResourceBuilder.SpreadStone, StoneCoverage);
        }
    }

    /// <summary>
    /// Runs one of the resource passes and puts the result on screen.
    /// <para>
    /// Deposits only. Nothing here moves a coast, a height or a cover -- a resource is what a
    /// tile has, not what it is -- so the land, the relief and the ground all come through
    /// untouched.
    /// </para>
    /// </summary>
    private void Deposit(
        Func<int, int, int[], GroundCover[], TileResource[], DepositSettings, TileResource[]> pass,
        double coverage)
    {
        if (_map is not { } map || _land is null || !TryReadSeed(ResourceSeed, out var seed))
            return;

        Remember();

        var relief = CurrentRelief(map);
        var cover = CurrentCover(map);

        var deposits = pass(
            map.Width, map.Height, relief, cover, CurrentResources(map),
            new DepositSettings(coverage / 100, ResourcePatchSize, ResourceClustering / 100, seed));

        Rebuild(map, relief, cover, deposits);
    }

    /// <summary>Whether there is land to scatter over, and a seed to scatter it with.</summary>
    private bool CanSpreadResources() =>
        _map is not null && _land is not null && TryReadSeed(ResourceSeed, out _);

    /// <summary>Fills the resource seed box with a new one.</summary>
    [RelayCommand]
    private void NewResourceSeed() =>
        ResourceSeed = NewSeed();

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
                ShallowsReach = ShallowsReach,
                ShallowsVariation = ShallowsVariation,
                MountainCoverage = MountainCoverage,
                HillCoverage = HillCoverage,
                Ruggedness = Ruggedness,
                RangeSize = RangeSize,
                HillSpread = HillSpread,
                TerrainSeed = TerrainSeed,
                SwampCoverage = SwampCoverage,
                DesertCoverage = DesertCoverage,
                PatchSize = PatchSize,
                CoverClustering = CoverClustering,
                CoverSeed = CoverSeed,
                RiverCount = RiverCount,
                RiverWinding = RiverWinding,
                RiverLength = RiverLength,
                RiverSeed = RiverSeed,
                LakeCount = LakeCount,
                LakeSize = LakeSize,
                LakeShape = LakeShape,
                LakeSeed = LakeSeed,
                IronCoverage = IronCoverage,
                WoodCoverage = WoodCoverage,
                OilCoverage = OilCoverage,
                SulphurCoverage = SulphurCoverage,
                StoneCoverage = StoneCoverage,
                ResourcePatchSize = ResourcePatchSize,
                ResourceClustering = ResourceClustering,
                ResourceSeed = ResourceSeed,
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
        ShallowsReach = settings.ShallowsReach ?? ShallowsReach;
        ShallowsVariation = settings.ShallowsVariation ?? ShallowsVariation;

        MountainCoverage = settings.MountainCoverage ?? MountainCoverage;
        HillCoverage = settings.HillCoverage ?? HillCoverage;
        Ruggedness = settings.Ruggedness ?? Ruggedness;
        RangeSize = settings.RangeSize ?? RangeSize;
        HillSpread = settings.HillSpread ?? HillSpread;
        TerrainSeed = settings.TerrainSeed ?? TerrainSeed;

        SwampCoverage = settings.SwampCoverage ?? SwampCoverage;
        DesertCoverage = settings.DesertCoverage ?? DesertCoverage;
        PatchSize = settings.PatchSize ?? PatchSize;
        CoverClustering = settings.CoverClustering ?? CoverClustering;
        RiverCount = settings.RiverCount ?? RiverCount;
        RiverWinding = settings.RiverWinding ?? RiverWinding;
        RiverLength = settings.RiverLength ?? RiverLength;
        RiverSeed = settings.RiverSeed ?? RiverSeed;
        LakeCount = settings.LakeCount ?? LakeCount;
        LakeSize = settings.LakeSize ?? LakeSize;
        LakeShape = settings.LakeShape ?? LakeShape;
        LakeSeed = settings.LakeSeed ?? LakeSeed;
        CoverSeed = settings.CoverSeed ?? CoverSeed;

        IronCoverage = settings.IronCoverage ?? IronCoverage;
        WoodCoverage = settings.WoodCoverage ?? WoodCoverage;
        OilCoverage = settings.OilCoverage ?? OilCoverage;
        SulphurCoverage = settings.SulphurCoverage ?? SulphurCoverage;
        StoneCoverage = settings.StoneCoverage ?? StoneCoverage;
        ResourcePatchSize = settings.ResourcePatchSize ?? ResourcePatchSize;
        ResourceClustering = settings.ResourceClustering ?? ResourceClustering;
        ResourceSeed = settings.ResourceSeed ?? ResourceSeed;

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

        // Whatever run of tile edits that entry stood for has been undone with it, so the next
        // click is the start of a fresh one and files an entry of its own -- and the line in
        // the Edit Tiles panel goes back to saying what the tool will do, since what it says
        // at the moment is what a tile that has just been put back was.
        _editing = false;
        _tileEditReport = null;
        _tileEditRefused = false;

        OnPropertyChanged(nameof(TileEditSummary));
        OnPropertyChanged(nameof(HasTileEditProblem));

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
    /// <param name="state">
    /// What to file, for the caller that has nothing to replace the map with. An edit to a
    /// few tiles changes the live map in place, so filing that map would file something that
    /// is about to change; it hands in a <see cref="TileMap.Snapshot"/> instead. Left null by
    /// every pass that builds a new map, which is most of them.
    /// </param>
    private void Remember(TileMap? state = null)
    {
        if ((state ?? _map) is not { } map)
            return;

        // A snapshot means a tile edit, and a tile edit opens a run that later clicks join
        // rather than file entries of their own; anything else closes it. Set here rather
        // than at the two call sites so that no future pass can file an entry and leave the
        // run looking as though it is still the one on top of the history.
        _editing = state is not null;

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
        var relief = new int[land.Length];

        for (var i = 0; i < land.Length; i++)
            relief[i] = land[i] ? Elevations.Flat : Elevations.Sea;

        // Grass everywhere, because this is where the world starts over: a land pass draws a
        // new coastline, and a swamp that survived it would be a swamp somewhere nobody put
        // one. The relief goes the same way, for the same reason.
        Rebuild(map, relief, new GroundCover[land.Length], new TileResource[land.Length]);
    }

    /// <summary>
    /// Rebuilds the map from an elevation, a ground cover and a resource per tile: deep water
    /// wherever the elevation says nothing, and the ground it is given wherever it says
    /// anything.
    /// <para>
    /// The one place a map is made, so that drawing a coastline, raising a mountain range and
    /// flooding a marsh are the same operation handed different numbers.
    /// </para>
    /// </summary>
    /// <param name="deposits">
    /// What each tile is worth. Passed in rather than carried over from <paramref name="map"/>
    /// so that every caller has to decide: a pass that only changes how the ground is drawn
    /// hands back what it was given, and a pass that draws a new coastline hands back nothing,
    /// because the world its deposits were placed in has gone.
    /// </param>
    private void Rebuild(TileMap map, int[] relief, GroundCover[] cover, TileResource[] deposits)
    {
        _map = new TileMap(map.Width, map.Height, map.OriginX, map.OriginY, coordinate =>
        {
            var index = ((coordinate.Y - map.OriginY) * map.Width) + (coordinate.X - map.OriginX);
            var elevation = relief[index];

            // Depth is the sea's whole cover, and the only one it can carry: everything else
            // in the enum is something the ground is made of, and there is no ground here.
            if (elevation <= Elevations.Sea)
                return BuildOceanTile(coordinate, cover[index] == GroundCover.Shallow);

            // The one place the rule is enforced, so nothing else has to remember it: swamp
            // and desert are lowland covers, and ground raised out of the lowlands loses
            // whatever was on it. A pass may hand in a cover for a tile that has since become
            // a mountain -- the terrain passes do exactly that -- and this is where it goes.
            //
            // Rivers need no exception here, which is deliberate and is why RiverBuilder keeps
            // them off the mountains: every tile of a river is lowland when it is drawn, so a
            // river only ever meets this rule after a range has been raised across one -- and
            // then the range should take it, exactly as it takes a marsh.
            var ground = Elevations.IsLowland(elevation) ? cover[index] : GroundCover.Grass;

            // And the same for what the tile is worth, against the ground it has just been
            // given rather than the one it had: a wood the sea has taken, or that a range has
            // risen through, is not a wood any more. ResourceBuilder owns the rule so that the
            // pass that lays a deposit and the rebuild that carries one over cannot disagree
            // about where it may sit.
            var deposit = deposits[index];

            if (!ResourceBuilder.CanHold(deposit, elevation, ground))
                deposit = TileResource.None;

            return BuildLandTile(coordinate, elevation, ground, deposit);
        });

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

        // A new map is all deep water: there is no land yet for a shelf to be near.
        _map = new TileMap(width, height, fill: coordinate => BuildOceanTile(coordinate, shallow: false));
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
        BuildHillsCommand.NotifyCanExecuteChanged();
        BuildMountainsCommand.NotifyCanExecuteChanged();
        BuildSwampsCommand.NotifyCanExecuteChanged();
        BuildDesertsCommand.NotifyCanExecuteChanged();
        BuildShallowsCommand.NotifyCanExecuteChanged();
        BuildLakesCommand.NotifyCanExecuteChanged();
        BuildIronCommand.NotifyCanExecuteChanged();
        BuildWoodCommand.NotifyCanExecuteChanged();
        BuildOilCommand.NotifyCanExecuteChanged();
        BuildSulphurCommand.NotifyCanExecuteChanged();
        BuildStoneCommand.NotifyCanExecuteChanged();
        BuildAllResourcesCommand.NotifyCanExecuteChanged();
        UndoCommand.NotifyCanExecuteChanged();

        OnPropertyChanged(nameof(LandSummary));
        OnPropertyChanged(nameof(HasLandProblem));
        OnPropertyChanged(nameof(IslandSummary));
        OnPropertyChanged(nameof(HasIslandProblem));
        OnPropertyChanged(nameof(HillSummary));
        OnPropertyChanged(nameof(HasHillProblem));
        OnPropertyChanged(nameof(MountainSummary));
        OnPropertyChanged(nameof(HasMountainProblem));
        OnPropertyChanged(nameof(ShallowsSummary));
        OnPropertyChanged(nameof(HasShallowsProblem));
        OnPropertyChanged(nameof(SwampSummary));
        OnPropertyChanged(nameof(HasSwampProblem));
        OnPropertyChanged(nameof(DesertSummary));
        OnPropertyChanged(nameof(HasDesertProblem));
        OnPropertyChanged(nameof(IronSummary));
        OnPropertyChanged(nameof(WoodSummary));
        OnPropertyChanged(nameof(OilSummary));
        OnPropertyChanged(nameof(SulphurSummary));
        OnPropertyChanged(nameof(StoneSummary));
        OnPropertyChanged(nameof(HasResourceProblem));
        OnPropertyChanged(nameof(LakeSummary));
        OnPropertyChanged(nameof(HasLakeProblem));
        OnPropertyChanged(nameof(TileEditSummary));
        OnPropertyChanged(nameof(HasTileEditProblem));
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

            // The same question this map asks anywhere: what does the topmost thing on the tile
            // have to say for itself.
            HoverText = coordinate is { } over ? ComponentParams.HoverTextOf(map[over]) : null;
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

        // The Edit Tiles panel takes the click while it is open -- see IsEditingTiles. The
        // selection is left exactly where it was rather than moved to the tile being edited:
        // raising a ridge is a run of clicks along it, and a selection mark following the
        // pointer through that is a mark nobody asked to move.
        if (IsEditingTiles)
        {
            switch (_tool)
            {
                case TileTool.Water:
                    SwitchWater(map, clicked);
                    break;

                case TileTool.Resource:
                    ToggleResource(map, clicked);
                    break;

                default:
                    EditElevation(map, clicked, 1);
                    break;
            }

            return;
        }

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
    /// Lowers the tile that was right-clicked, while the Edit Tiles panel is open.
    /// <para>
    /// The other direction, on the other button, rather than a second tool to go and pick in
    /// the panel first. Shaping ground is a matter of going back and forth over the same few
    /// tiles -- a step too far, a step back -- and a panel you have to return to between them
    /// makes the correction cost more than the mistake.
    /// </para>
    /// <para>
    /// The elevation tool's, and no other's. Height is a scale and has two directions to give
    /// the two buttons; the other tools are switches -- deep or shallow, the deposit or none --
    /// and a switch has only the one move, so a right click that did what a left one does would
    /// be a way of pressing the same button twice.
    /// </para>
    /// <para>
    /// Nothing at all with the panel shut. The right button is how the map is dragged around,
    /// and a right click that quietly changed a tile whenever a drag happened not to move
    /// would be a map that edits itself.
    /// </para>
    /// </summary>
    [RelayCommand]
    private void RightClick(TileCoordinate? coordinate)
    {
        if (!IsEditingTiles
            || _tool != TileTool.Elevation
            || _map is not { } map
            || coordinate is not { } clicked)
            return;

        EditElevation(map, clicked, -1);
    }

    /// <summary>
    /// Moves one tile's ground up or down a step and puts it right: the height, the ground it
    /// is drawn with, what it is worth, and its label.
    /// <para>
    /// Relief only. It will not take a tile below <see cref="LowestEditableElevation"/> and it
    /// will not touch the sea, so no click here moves a coastline: what is land stays land and
    /// what is sea stays sea, however far the ground between them is walked up and down.
    /// Drawing coastlines is the Base Land panel's job, and it does it from a mask and a seed
    /// -- a tool that could do it a tile at a time would be a second and much worse way of
    /// doing the same thing, and it is why the land mask the generators build from needs no
    /// telling here.
    /// </para>
    /// <para>
    /// An edit to the live map rather than a rebuild of it, which is what the generator passes
    /// do. A rebuild is the right shape for a pass that decides every tile and the wrong one
    /// for a click: it is a fresh tile object for each of a million, and paying that per click
    /// would make the tool unusable on exactly the large maps somebody would want to touch up
    /// by hand.
    /// </para>
    /// <para>
    /// Which is why undo is handed a <see cref="TileMap.Snapshot"/> and not the map. Every
    /// other caller of <see cref="Remember"/> replaces the map outright, so what it files stops
    /// changing; this one does not, and filing the live map would file the very map that is
    /// about to change under it.
    /// </para>
    /// </summary>
    private void EditElevation(TileMap map, TileCoordinate coordinate, int step)
    {
        if (map[coordinate] is not { } tile)
            return;

        // Asked of the height rather than of the ground drawn on it, so that a river -- water
        // to look at, and land at whatever height it runs over -- is ground this can shape,
        // the same as the bank beside it.
        if (tile.Elevation <= Elevations.Sea)
        {
            ReportEdit(coordinate, "is sea. Only land can be raised or lowered.", refused: true);
            return;
        }

        var elevation = tile.Elevation + step;

        // Both ends refuse rather than clamp, and refuse before anything is filed: a click that
        // changes nothing must not spend a history slot, and a Lower that quietly stopped at the
        // floor would look like a tool that had stopped working.
        if (elevation < LowestEditableElevation)
        {
            ReportEdit(coordinate, $"cannot go below {LowestEditableElevation}.", refused: true);
            return;
        }

        if (elevation > Elevations.MountainsTo)
        {
            ReportEdit(coordinate, $"is as high as ground goes, at {Elevations.MountainsTo}.", refused: true);
            return;
        }

        // Rebuild's rule rather than this method's: swamp and desert are lowland covers, and
        // ground raised past the hills loses whatever was on it.
        var cover = CoverOf(tile);

        if (!Elevations.IsLowland(elevation))
            cover = GroundCover.Grass;

        // And the same question Rebuild asks of a deposit, against the ground the tile is about
        // to have rather than the one it had: an oil field does not survive being made a
        // mountain, and a wood does not survive being made a desert.
        var kept = ResourceBuilder.CanHold(ResourceOf(tile), elevation, cover);

        RememberTileEdit(map);

        map.Edit(editor => editor.Update(coordinate, edited =>
        {
            edited.Elevation = elevation;

            Ground(edited, GroundOf(cover));

            if (!kept)
                edited.MapRenderComponents.Remove(RenderComponentLayers.Resource);

            // Last, and after the ground: the label is written from the elevation the tile has
            // just been given, and whether it is written at all is asked of the ground it has
            // just been drawn with.
            Label(edited);
        }));

        Tiles = map.Tiles;

        ReportEdit(coordinate, $"is at {elevation}.", refused: false);
    }

    /// <summary>
    /// Switches one tile of sea between deep and shallow.
    /// <para>
    /// A cycle of two, so one click is the whole of it -- see the remarks on
    /// <see cref="RightClick"/>.
    /// </para>
    /// <para>
    /// The sea only, and it stays sea: this changes how a tile of water is drawn and nothing
    /// else about it. Depth here is drawn and not modelled -- the sea is all at elevation zero
    /// and there is no seabed under this map -- so a shallow is a tile of water near enough to
    /// the shore that a map would paint it paler, and saying which tiles those are is all this
    /// does. See <see cref="Generation.ShallowsBuilder"/>, which says it for a whole coast at
    /// once from a distance and a noise field; this is for the tile that pass got wrong.
    /// </para>
    /// <para>
    /// Read back off the tiles by <see cref="CurrentCover"/> like any other cover, so a shelf
    /// edited here survives every later pass that rebuilds the map -- and is overwritten, like
    /// every other shallow, the next time Mark Shallows is pressed.
    /// </para>
    /// </summary>
    private void SwitchWater(TileMap map, TileCoordinate coordinate)
    {
        if (map[coordinate] is not { } tile)
            return;

        // Both asked, though on a map as it stands either would do: the sea is the water that
        // is at sea level, and a river is the water that is not. Asking only the height would
        // take a lake for the sea if a pass ever left one at zero, and asking only the ground
        // would take a river for it.
        if (tile.Elevation > Elevations.Sea || !IsWater(tile))
        {
            ReportEdit(coordinate, "is not sea. Only open water has a depth to switch.", refused: true);
            return;
        }

        var shallow = CoverOf(tile) is GroundCover.Shallow;

        RememberTileEdit(map);

        map.Edit(editor => editor.Update(coordinate, edited =>
            Ground(edited, shallow
                ? MapRenderComponentConstants.DeepWater
                : MapRenderComponentConstants.ShallowWater)));

        Tiles = map.Tiles;

        ReportEdit(coordinate, shallow ? "is deep." : "is shallow.", refused: false);
    }

    /// <summary>
    /// Puts <see cref="EditResource"/> on one tile, or takes it off again.
    /// <para>
    /// A toggle of the chosen deposit and not a cycle through all five: the drop-down has
    /// already said which one, so the only question a click has left is whether the tile has it.
    /// A tile holding a different deposit is given this one -- there is room for one at a time,
    /// and swapping is what somebody clicking iron onto a wood was asking for.
    /// </para>
    /// <para>
    /// Where a deposit may sit is <see cref="ResourceBuilder.CanHold"/>'s to say, the same rule
    /// the scatter passes are held to and the same one <see cref="EditElevation"/> re-asks when
    /// it moves the ground out from under one. Taking a deposit off is never refused: whatever
    /// the ground is, it can be ground with nothing on it.
    /// </para>
    /// </summary>
    private void ToggleResource(TileMap map, TileCoordinate coordinate)
    {
        if (map[coordinate] is not { } tile)
            return;

        var wanted = EditResource;
        var held = ResourceOf(tile);

        // Off, and nothing else to check. A tile that is losing what it had cannot be the wrong
        // ground for what it is left with.
        if (held == wanted)
        {
            RememberTileEdit(map);

            map.Edit(editor => editor.Update(coordinate, edited =>
                edited.MapRenderComponents.Remove(RenderComponentLayers.Resource)));

            Tiles = map.Tiles;

            ReportEdit(coordinate, $"has no {wanted} now.", refused: false);
            return;
        }

        var cover = CoverOf(tile);

        if (!ResourceBuilder.CanHold(wanted, tile.Elevation, cover))
        {
            // The two halves of the rule, said apart, because they are refusals of different
            // kinds: there is no ground here at all, or there is and it is the wrong sort.
            ReportEdit(
                coordinate,
                tile.Elevation <= Elevations.Sea || cover is GroundCover.River or GroundCover.Shallow
                    ? $"is water. {wanted} needs dry ground."
                    : $"is no ground for {wanted}.",
                refused: true);

            return;
        }

        RememberTileEdit(map);

        map.Edit(editor => editor.Update(
            coordinate,
            edited => edited.SetResourceType(MarkerOf(wanted))));

        Tiles = map.Tiles;

        // Named rather than merely confirmed, since the click may have replaced something: what
        // the tile has now is the useful half, and on the terrain picture one marker looks much
        // like another until it is zoomed right in.
        ReportEdit(
            coordinate,
            held == TileResource.None ? $"has {wanted}." : $"has {wanted} in place of {held}.",
            refused: false);
    }

    /// <summary>
    /// Files the map as it stands before a tile edit, unless the last thing filed was the last
    /// tile edit's map.
    /// <para>
    /// Because a click is small and the history is five deep. Filing one map per click means
    /// five clicks along a ridge throw away the continent build that raised it, and undo comes
    /// back to a coastline five steps of one tile from where it went in -- an undo that cannot
    /// reach past the last few seconds of touching up is not the undo the button describes.
    /// So a run of edits is one entry: the map as it was when the run started, which is where
    /// a single Ctrl+Z puts it back. A build, a load or a new map ends the run by filing its
    /// own entry, and the next click starts a fresh one.
    /// </para>
    /// </summary>
    private void RememberTileEdit(TileMap map)
    {
        if (_editing)
            return;

        Remember(map.Snapshot());
    }

    /// <summary>
    /// Says what just happened to a tile, in the line under the tools.
    /// </summary>
    private void ReportEdit(TileCoordinate coordinate, string outcome, bool refused)
    {
        _tileEditReport = $"({coordinate.X}, {coordinate.Y}) {outcome}";
        _tileEditRefused = refused;

        OnPropertyChanged(nameof(TileEditSummary));
        OnPropertyChanged(nameof(HasTileEditProblem));
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
                ComponentParams.ForElevation(tile.Elevation));
        else
            tile.MapRenderComponents.Remove(RenderComponentLayers.ElevationLabel);
    }

    /// <summary>
    /// Whether a tile is open sea, and so has no number worth writing on it: the ocean is all
    /// at one level by definition, and a map of it covered in zeroes hides the coastline that
    /// is the only thing the numbers are there to read against.
    /// <para>
    /// Asked of the ground drawn on the tile rather than of its elevation. Elevation is the
    /// thing being labelled, and a rule that read it would be deciding what to show from the
    /// very number in question -- any land a later pass leaves at zero would be taken for sea.
    /// </para>
    /// <para>
    /// Both depths of sea, and neither of them is what a river is drawn as. A river takes the
    /// height of the ground it runs over and is the one water on the map that is not all at
    /// one level -- so its number is worth reading, and is in fact the number somebody
    /// checking a river runs downhill would want most. A shelf is still sea, still at zero,
    /// and still nothing to write a number on.
    /// </para>
    /// </summary>
    private static bool IsWater(MapTile tile) =>
        tile.MapRenderComponents.TryGetValue(RenderComponentLayers.BaseGround, out var ground) &&
        (ground.ComponentType == MapRenderComponentConstants.DeepWater ||
         ground.ComponentType == MapRenderComponentConstants.ShallowWater);

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
    /// One tile of continent, at the height it stands. Elevation 1 is flat land -- one above
    /// the sea, and all a land pass raises on its own; the Hills and Mountains panel is what
    /// puts anything higher here.
    /// </summary>
    private MapTile BuildLandTile(
        TileCoordinate coordinate,
        int elevation = Elevations.Flat,
        GroundCover cover = GroundCover.Grass,
        TileResource resource = TileResource.None)
    {
        var tile = new MapTile { X = coordinate.X, Y = coordinate.Y, Elevation = elevation };

        Ground(tile, GroundOf(cover));

        // On its own layer over the ground, which is what lets it be drawn without the tile
        // having to stop being marsh or sand to carry it.
        if (resource != TileResource.None)
            tile.SetResourceType(MarkerOf(resource));

        Label(tile);

        return tile;
    }

    /// <summary>
    /// Which ground a land cover is drawn with. The other direction of <see cref="CoverOf"/>,
    /// and shared by the pass that builds a tile and the edit that changes one.
    /// </summary>
    private static Guid GroundOf(GroundCover cover) => cover switch
    {
        GroundCover.Swamp => MapRenderComponentConstants.Swamp,
        GroundCover.Desert => MapRenderComponentConstants.Desert,

        // Open, wadeable water, which is what the shallows were already drawn as and what
        // a river is: the same surface as the sea in a different colour, so a mouth is one
        // unbroken sheet across the coastline rather than two textures meeting at it.
        GroundCover.River => MapRenderComponentConstants.Water,

        _ => MapRenderComponentConstants.Grass,
    };

    /// <summary>
    /// Which marker a deposit is drawn with. The other direction of <see cref="ResourceOf"/>,
    /// and shared by the pass that builds a tile and the click that lays one.
    /// </summary>
    /// <remarks>
    /// <see cref="TileResource.None"/> has no marker and must not be asked for: nothing is
    /// drawn by leaving the layer empty, not by putting a component there that means nothing.
    /// Callers test for it first -- both of them have to anyway, since one is deciding whether
    /// to write the layer at all and the other is deciding whether to clear it.
    /// </remarks>
    private static Guid MarkerOf(TileResource resource) => resource switch
    {
        TileResource.Iron => MapRenderComponentConstants.Iron,
        TileResource.Wood => MapRenderComponentConstants.Wood,
        TileResource.Oil => MapRenderComponentConstants.Oil,
        TileResource.Sulphur => MapRenderComponentConstants.Sulphur,
        _ => MapRenderComponentConstants.Stone,
    };

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
        tile.SetBaseGroundType(groundType, ComponentParams.ForElevation(tile.Elevation));

    /// <summary>
    /// One tile of open ocean: deep water, elevation zero. Elevation is set explicitly
    /// rather than left at its default, because zero is a decision here -- sea level, the
    /// datum everything the generator raises will be measured from.
    /// </summary>
    private MapTile BuildOceanTile(TileCoordinate coordinate, bool shallow = false)
    {
        var tile = new MapTile { X = coordinate.X, Y = coordinate.Y, Elevation = 0 };

        // Both at elevation zero, and deliberately. The sea has one level whatever is under
        // it -- a shelf is a fact about the bottom, and this map has no bottom -- so the
        // shallows are a way of drawing the water and not a second height for it.
        Ground(tile, shallow
            ? MapRenderComponentConstants.ShallowWater
            : MapRenderComponentConstants.DeepWater);

        Label(tile);

        return tile;
    }
}
