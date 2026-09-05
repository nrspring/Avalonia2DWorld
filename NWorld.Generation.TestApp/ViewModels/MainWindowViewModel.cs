using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NWorld.Generation.TestApp.Persistence;
using NWorld.Map.Models;
using NWorld.Map.ViewModels;
using NWorld.MapServices.Constants;
using NWorld.MapServices.Editing;
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
    /// The writing on the open map, and everything done to it.
    /// <para>
    /// A service and not a list here, because none of what a label tool does is about this
    /// window: writing one, picking one up, dragging it, changing what it says and rubbing it
    /// out are the same five things in any app that can write on a map, and the geometry they
    /// all rest on belongs with the code that draws them. What is left here is which gesture
    /// means which of the five -- see <see cref="MapLabelEditor"/>.
    /// </para>
    /// </summary>
    private readonly MapLabelEditor _writing = new();

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

        /// <summary>A walled fort, whole on its one tile.</summary>
        Fort,

        /// <summary>
        /// Writing on the map, placed where it was clicked rather than on the tile clicked.
        /// <para>
        /// The one tool here that is not an enhancement on a tile, and it sits in the same
        /// list anyway: to whoever is using this window they are all the same thing -- pick
        /// something, click the map, get it -- and splitting the list to reflect a difference
        /// in where the thing is stored would be the storage arranging the panel.
        /// </para>
        /// </summary>
        Label,
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

    /// <summary>Whether clicks build and pull down forts.</summary>
    /// <inheritdoc cref="IsBuildingRoad" path="/summary/para"/>
    public bool IsBuildingFort
    {
        get => _building == Enhancement.Fort;
        set
        {
            if (value)
                Arm(Enhancement.Fort);
        }
    }

    /// <summary>Whether clicks write on the map and rub the writing out.</summary>
    /// <inheritdoc cref="IsBuildingRoad" path="/summary/para"/>
    public bool IsBuildingLabel
    {
        get => _building == Enhancement.Label;
        set
        {
            if (value)
                Arm(Enhancement.Label);
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

        // Putting the label tool down puts down whatever it was holding. What is picked is a
        // state of that tool, and a label still shown as being edited by a panel that has gone
        // back to laying roads is a promise the window cannot keep.
        if (building != Enhancement.Label)
        {
            _writing.Unpick();
            _writing.Drop();
            OnPropertyChanged(nameof(HasPickedLabel));
        }

        OnPropertyChanged(nameof(IsBuildingNothing));
        OnPropertyChanged(nameof(IsBuildingRoad));
        OnPropertyChanged(nameof(IsBuildingBridge));
        OnPropertyChanged(nameof(IsBuildingCity));
        OnPropertyChanged(nameof(IsBuildingFort));
        OnPropertyChanged(nameof(IsBuildingLabel));
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

    /// <summary>
    /// What the box says: the words of the label being edited, or of the next one to be
    /// written where none is being edited.
    /// <para>
    /// One box for both, and not two, because they are the same question asked a moment apart.
    /// A label is written by typing and clicking; it is changed by clicking it and typing. Two
    /// boxes would mean typing the name of a place into the wrong one, which is the sort of
    /// mistake a window earns by having a box that is only sometimes the one that matters.
    /// </para>
    /// </summary>
    [ObservableProperty]
    private string _labelText = "";

    /// <summary>
    /// True while the boxes are being filled in from a label that has just been picked, so that
    /// filling them in is not mistaken for somebody editing it.
    /// <para>
    /// Without it, picking a label would write the label straight back over itself. Harmless as
    /// it happens, since what is written is what was read -- but it would put an edit through
    /// on every click, and the first real bug in that arrangement would be very hard to see.
    /// </para>
    /// </summary>
    private bool _filling;

    /// <summary>
    /// The colours, as the hex in the two boxes and as the colours those last parsed to.
    /// <para>
    /// Both kept, and that is the point: the text is what was typed and stays exactly as typed
    /// while it is being typed, and the colour is the last thing typed that was a colour. A box
    /// that snapped back to the last good value on every keystroke could not be edited at all
    /// -- "#E6121A26" passes through "#E" on its way to being written.
    /// </para>
    /// </summary>
    private uint _background = MapColour.DefaultBackground;

    /// <inheritdoc cref="_background"/>
    private uint _foreground = MapColour.DefaultForeground;

    /// <inheritdoc cref="_background"/>
    private string _backgroundHex = MapColour.ToHex(MapColour.DefaultBackground);

    /// <inheritdoc cref="_background"/>
    private string _foregroundHex = MapColour.ToHex(MapColour.DefaultForeground);

    /// <summary>
    /// The plate colour, as <c>#RRGGBB</c> or <c>#AARRGGBB</c>. Hex rather than a colour
    /// picker: a picker is a package this app does not otherwise need, and the hex is what the
    /// saved file holds anyway, so what is typed here is what can be read back out of it.
    /// </summary>
    public string LabelBackgroundHex
    {
        get => _backgroundHex;
        set => SetColour(value, ref _backgroundHex, ref _background, nameof(LabelBackgroundHex), nameof(LabelBackgroundBrush));
    }

    /// <summary>The colour of the writing itself, in the same hex.</summary>
    public string LabelForegroundHex
    {
        get => _foregroundHex;
        set => SetColour(value, ref _foregroundHex, ref _foreground, nameof(LabelForegroundHex), nameof(LabelForegroundBrush));
    }

    /// <summary>
    /// Carries a change in the words through to the label being edited. Nothing happens where
    /// none is: the box is then describing the label that has not been written yet.
    /// </summary>
    partial void OnLabelTextChanged(string value) => ApplyEdit();

    /// <summary>
    /// Writes the boxes onto the picked label, if there is one and this is not the fill that
    /// put them there in the first place.
    /// </summary>
    private void ApplyEdit()
    {
        if (_filling || _writing.Picked is null)
            return;

        if (_writing.Rewrite(LabelText, _background, _foreground))
        {
            Publish();
            Report($"\"{_writing.Picked!.Text}\" changed.");
        }
    }

    /// <summary>
    /// Fills the boxes in from a label that has just been picked, so that what is on the panel
    /// is what is on the map.
    /// </summary>
    private void Fill(MapLabel label)
    {
        _filling = true;

        try
        {
            LabelText = label.Text;
            LabelBackgroundHex = MapColour.ToHex(label.Background);
            LabelForegroundHex = MapColour.ToHex(label.Foreground);
        }
        finally
        {
            _filling = false;
        }
    }

    /// <summary>The plate colour as something a swatch can be painted with.</summary>
    public IBrush LabelBackgroundBrush => Swatch(_background);

    /// <inheritdoc cref="LabelBackgroundBrush"/>
    public IBrush LabelForegroundBrush => Swatch(_foreground);

    /// <summary>Whether both boxes currently hold something that is a colour.</summary>
    public bool LabelColoursRead =>
        MapColour.FromHex(_backgroundHex) is not null && MapColour.FromHex(_foregroundHex) is not null;

    /// <summary>
    /// Takes a typed colour: the text always, the colour only if the text is one. Says so
    /// either way, since the swatch beside the box is how anyone can tell which happened.
    /// </summary>
    private void SetColour(string? typed, ref string text, ref uint colour, string textProperty, string brushProperty)
    {
        text = typed ?? "";

        if (MapColour.FromHex(text) is { } parsed)
            colour = parsed;

        OnPropertyChanged(textProperty);
        OnPropertyChanged(brushProperty);
        OnPropertyChanged(nameof(LabelColoursRead));

        ApplyEdit();
    }

    /// <summary>A packed colour as a brush, for the swatch beside its box.</summary>
    private static IBrush Swatch(uint colour)
    {
        var (alpha, red, green, blue) = MapColour.Channels(colour);
        return new SolidColorBrush(Color.FromArgb(alpha, red, green, blue));
    }

    /// <summary>What the last click did, or what the next one will do.</summary>
    public string BuildSummary =>
        Tiles is null
            ? "Open a map first."
            : _buildReport ?? _building switch
            {
                Enhancement.Road => "Click dry land to lay a road, or an existing one to lift it.",
                Enhancement.Bridge => "Click water to lay a bridge, or an existing one to lift it.",
                Enhancement.City => "Click dry land to build. Tiles beside each other grow into one town.",
                Enhancement.Fort => "Click dry land to build. A gate opens on whichever side a road reaches it.",
                Enhancement.Label => _writing.Picked is null
                    ? "Click open ground to write. Click writing to edit it, or drag it somewhere else."
                    : $"Editing \"{_writing.Picked.Text}\". Drag it to move it, or type to change it.",
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

        // The same click reaches PixelClick below, which is the half of it the label tool
        // wants. Both commands see every click and each ignores the ones that are not its own.
        if (_building == Enhancement.Label)
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
                  || standing == MapRenderComponentConstants.Fort
                    ? "is pulled down."
                    : "is clear again.");

            return;
        }

        // A road and a town want ground under them; a bridge wants none. The one rule this app
        // has about the world it opened, and it is the rule that makes a bridge mean anything: a
        // road that could be laid over a river would be a road that never needed one.
        var wet = Shoreline.IsWater(tile);

        if (_building == Enhancement.Bridge && !wet)
        {
            Report(clicked, "is dry. A bridge needs water under it -- build a road.");
            return;
        }

        if (_building != Enhancement.Bridge && wet)
        {
            Report(clicked, _building switch
            {
                Enhancement.City => "is water. Nobody builds a town on it.",
                Enhancement.Fort => "is water. A fort wants ground to stand on.",
                _ => "is water. A road needs dry ground -- build a bridge.",
            });

            return;
        }

        var laying = _building switch
        {
            Enhancement.Bridge => MapRenderComponentConstants.Bridge,
            Enhancement.City => MapRenderComponentConstants.City,
            Enhancement.Fort => MapRenderComponentConstants.Fort,
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
            Enhancement.Fort => "holds a fort.",
            _ => "has a road.",
        });
    }

    /// <summary>
    /// Writes on the map where it was clicked, or rubs out the writing that is already there.
    /// <para>
    /// The pixel and not the tile, which is the whole of what makes a label different from
    /// everything else this window builds: a name goes over the thing it names, and the things
    /// worth naming -- a bay, a range, the far side of a river -- do not begin and end on tile
    /// boundaries.
    /// </para>
    /// <para>
    /// Rubbing out is a click on the writing, which is the only gesture that could mean it: a
    /// label covers a patch of map rather than a square, so there is nothing to select it by
    /// except what it covers.
    /// </para>
    /// </summary>
    [RelayCommand]
    private void PixelClick(MapPixel? point)
    {
        if (_building != Enhancement.Label || _map is null || point is not { } clicked)
            return;

        // Picked first, because a click that lands on writing is about that writing. Only open
        // ground is an invitation to write something new.
        if (_writing.Pick(clicked) is { } picked)
        {
            Fill(picked);
            Publish();
            Report($"Editing \"{picked.Text}\".");
            return;
        }

        if (_writing.Write(clicked, LabelText, _background, _foreground) is { } written)
        {
            Publish();
            Report($"\"{written.Text}\" written at ({clicked.X:F0}, {clicked.Y:F0}).");
            return;
        }

        Report("Nothing to write. Put some words in the box first.");
    }

    /// <summary>
    /// Drags a label to somewhere else on the map.
    /// <para>
    /// The grab decides the whole gesture: a press that finds writing is a move, and one that
    /// finds open ground is not a gesture at all -- every move after it does nothing and the
    /// release, if the pointer stayed put, is a click. That is what lets one button both write
    /// and rearrange without a mode to switch between them.
    /// </para>
    /// </summary>
    [RelayCommand]
    private void PixelDrag(MapPixelDrag drag)
    {
        if (_building != Enhancement.Label || _map is null)
            return;

        switch (drag.Phase)
        {
            case MapDragPhase.Started:
                if (_writing.Grab(drag.From) && _writing.Picked is { } grabbed)
                {
                    Fill(grabbed);
                    Publish();
                }

                break;

            case MapDragPhase.Moved:
                if (_writing.DragTo(drag.At))
                    Publish();
                break;

            case MapDragPhase.Finished:
                // Only where the pointer actually went somewhere. A press and release on the
                // same spot is a click -- it arrives here first, because every drag gets a
                // finish whether or not it moved -- and saying a label was moved to where it
                // already was would be a report of nothing having happened.
                if (_writing.IsDragging && drag.At != drag.From && _writing.Picked is { } moved)
                {
                    Report($"\"{moved.Text}\" moved to ({moved.Anchor.X:F0}, {moved.Anchor.Y:F0}).");
                }

                // Called whatever happened: the control promises one finish per drag it
                // started, and answering them all the same way is the point of that promise.
                _writing.Drop();
                break;
        }
    }

    /// <summary>
    /// Rubs out the label being edited. A button rather than a click on the map, because every
    /// click there already means something -- picking the label up is what a click on writing
    /// has to mean if it is to be draggable -- and a gesture that deleted what it touched would
    /// be a poor thing to discover by accident.
    /// </summary>
    [RelayCommand]
    private void RubOut()
    {
        if (_writing.Remove() is not { } gone)
            return;

        Publish();
        Report($"\"{gone.Text}\" is rubbed out.");
    }

    /// <summary>Whether there is a label being edited, for the buttons that need one.</summary>
    public bool HasPickedLabel => _writing.Picked is not null;

    /// <summary>
    /// Hands the writing to the map view, and tells the panel what is picked.
    /// <para>
    /// The editor publishes a fresh array after every change rather than handing out its own
    /// list: what the render thread has been given must not move under it.
    /// </para>
    /// </summary>
    private void Publish()
    {
        Labels = _writing.Labels;
        OnPropertyChanged(nameof(HasPickedLabel));
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
        if (_building == Enhancement.None || _building == Enhancement.Label
            || _map is not { } map || coordinate is not { } clicked)
        {
            return;
        }

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
            || built.ComponentType == MapRenderComponentConstants.City
            || built.ComponentType == MapRenderComponentConstants.Fort)
            ? built.ComponentType
            : null;

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
            EnhancementFile.Save(stream, tiles, _writing.Labels);
            Report(Standing(tiles, _writing.Count));
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
            var document = EnhancementFile.Load(stream);
            var laid = EnhancementFile.Apply(map, document);

            // The writing goes on beside the built tiles, not through the editor: it belongs
            // to no tile, so there is nothing on the map to lay it on. Replaced rather than
            // merged, for the reason the tiles are -- the file is the layer, not a set of
            // additions to it.
            _writing.Load(document.Labels);

            Tiles = map.Tiles;
            Publish();

            Report(laid == 0 && _writing.Count == 0
                ? $"{name} has nothing built in it. The map is clear."
                : $"Loaded {Count(laid, "built tile")} and {Count(_writing.Count, "label")} from {name}.");
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
    /// <para>
    /// The labels are counted separately and not folded in, because they are not tiles and
    /// there is no number that would be true of both.
    /// </para>
    /// </summary>
    private static string Standing(TileGrid tiles, int labels)
    {
        var count = 0;

        foreach (var tile in tiles)
        {
            if (tile.MapRenderComponents.ContainsKey(RenderComponentLayers.Enhancement))
                count++;
        }

        return count == 0 && labels == 0
            ? "Saved. Nothing is built on this world yet, and nothing is written on it."
            : $"Saved {Count(count, "built tile")} and {Count(labels, "label")}.";
    }

    /// <summary>
    /// A count and what it counts, with the plural where there is one. Said the same way
    /// everywhere so that "1 labels" never appears in this window.
    /// </summary>
    private static string Count(int howMany, string thing) =>
        howMany == 1 ? $"1 {thing}" : $"{howMany} {thing}s";


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

            // The writing was about the last world. Cleared rather than carried over, since a
            // name for a bay means nothing on a map with a different coastline.
            _writing.Clear();
            Publish();

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
