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
/// <para>
/// There is no map until something makes one -- see <see cref="CreateMap"/>. The app opens on an
/// empty view rather than on a sample, so that what is on screen is always a map somebody
/// asked for. Every other command is a no-op until then, which is what lets the control stay
/// bound and live with nothing behind it.
/// </para>
/// </summary>
public partial class MainWindowViewModel : ViewModelBase
{
    /// <summary>The longest a single side may be. Generous; <see cref="MaxMapTiles"/> is
    /// the limit that usually bites, and it is what allows a long thin map.</summary>
    private const int MaxMapDimension = 4096;

    /// <summary>
    /// The most tiles a map may hold.
    /// <para>
    /// A limit rather than a promise: a map is one <see cref="MapTile"/> object per tile,
    /// each with its own component dictionary, and every edit republishes an array of that
    /// many references (see the remarks on <see cref="TileMap"/>). A megatile map is already
    /// hundreds of megabytes and a hover you can feel. Past that the answer should be a
    /// sentence under the fields, not a machine that stops responding.
    /// </para>
    /// </summary>
    private const int MaxMapTiles = 1_048_576;

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

    /// <summary>
    /// One renderer for this view model's one map view. <see cref="StandardRenderer"/> reuses
    /// its batch buffers between frames and refuses to draw two at once, so it is not shared.
    /// </summary>
    private readonly IMapRenderer _renderer = new StandardRenderer();

    /// <summary>
    /// The map, or null before one has been made. Replaced rather than refilled when a new
    /// one arrives, so the map a frame may still be drawing from is left whole.
    /// </summary>
    private TileMap? _map;

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
    /// <para>
    /// Null until there is a map. <c>MapView</c> draws nothing and queues no animation frames
    /// while it is, which is the whole of the empty state.
    /// </para>
    /// </summary>
    [ObservableProperty]
    private IReadOnlyList<MapTile>? _tiles;

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

    /// <summary>
    /// Width for the next map, as typed. A string rather than an int because a half-typed
    /// box is a normal state to be in -- binding to an int leaves the empty box holding the
    /// last good value while the caret sits in it, and there is nowhere to put "not a number
    /// yet". Read back through <see cref="TryReadSize"/>.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateMapCommand))]
    [NotifyPropertyChangedFor(nameof(SizeSummary), nameof(HasSizeProblem))]
    private string _newMapWidth = "64";

    /// <inheritdoc cref="NewMapWidth"/>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateMapCommand))]
    [NotifyPropertyChangedFor(nameof(SizeSummary), nameof(HasSizeProblem))]
    private string _newMapHeight = "48";

    public IMapRenderer Renderer => _renderer;

    /// <summary>
    /// Whether the map view runs its repaint loop, which is what makes the water move.
    /// A view of the map rather than a fact about it, so it lives in <see cref="Options"/>.
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

    /// <summary>
    /// Whether the map view draws its frame-rate label. A view of the map rather than a fact
    /// about it, which is why it lives in <see cref="Options"/> and not on the map.
    /// </summary>
    public bool ShowFrameRate
    {
        get => Options.ShowFrameRate;
        set
        {
            if (Options.ShowFrameRate != value)
                Options = Options with { ShowFrameRate = value };
        }
    }

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
    /// Whether there is a map to draw. Bind to it for an empty-state prompt over the view.
    /// </summary>
    public bool HasMap => _map is not null;

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
        OnPropertyChanged(nameof(HasMap));
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
    /// Keeps the properties that read out of <see cref="Options"/> in step with it, however
    /// it was replaced -- the toggle's own setter is only one of the ways it moves.
    /// </summary>
    partial void OnOptionsChanged(MapViewOptions value)
    {
        OnPropertyChanged(nameof(IsAnimated));
        OnPropertyChanged(nameof(ShowFrameRate));
    }

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
    /// Zooms about the pointer, one step along <see cref="ZoomLevels"/> per wheel notch.
    /// </summary>
    [RelayCommand]
    private void Zoom(MapWheelRequest request)
    {
        // Nothing on screen to zoom about, and no extent for the clamp below to work from.
        // Before the residue rather than after, so a wheel turned over the empty view does
        // not bank a step that spends itself the instant a map appears.
        if (_map is not { } map)
            return;

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
            map,
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
    /// map knows how big it is, and the wheel request carries how much of it fits on screen.
    /// The control itself has no opinion and will draw whatever origin it is handed.
    /// </para>
    /// </summary>
    /// <param name="visibleX">Width of the map view in tiles, at the new tile size.</param>
    /// <param name="visibleY">Height of the map view in tiles, at the new tile size.</param>
    private static MapViewOptions Clamp(MapViewOptions options, TileMap map, double visibleX, double visibleY) =>
        options with
        {
            OriginX = ClampAxis(options.OriginX, map.OriginX, map.Width, visibleX),
            OriginY = ClampAxis(options.OriginY, map.OriginY, map.Height, visibleY),
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
