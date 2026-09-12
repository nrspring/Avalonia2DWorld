# Avalonia2DWorld

A hobby project exploring procedural world generation and tile-based 2D map rendering, built with [Avalonia UI](https://avaloniaui.net/) on .NET.

The repository is organized as a solution ([Avalonia2DWorld.slnx](Avalonia2DWorld.slnx)) of a few focused projects rather than one monolithic app:

- **Avalonia2DWorld.Generation.App** — The main Avalonia desktop application. Procedurally generates world maps (continents, islands, lakes, rivers, terrain, resources, ground cover, etc.) and lets you view/save the results. See [Avalonia2DWorld.Generation.App](#avalonia2dworldgenerationapp) below.
- **Avalonia2DWorld.Generation.TestApp** — A secondary Avalonia app for testing units and enhancements (roads, towns, forts, ships, etc.) against a finished map, without touching the generator. See [Avalonia2DWorld.Generation.TestApp](#avalonia2dworldgenerationtestapp) below.
- **Avalonia2DWorld.Map** — Shared map/tile primitives and the reusable `MapView` control: tile grids, tile placement, map interaction models (pan, zoom, drag), and persistence formats for tile maps. See [Using Avalonia2DWorld.Map](#using-avalonia2dworldmap) below.
- **Avalonia2DWorld.MapServices** — Rendering services layered on top of `Avalonia2DWorld.Map`: the standard tile renderer, terrain/resource/unit render components (water, coastline, roads, cities, forts, units, etc.), and related caching and helper logic.

There's also a [Documents](Documents) folder for planning notes.

## Status

This is an actively evolving personal project. Expect things to move fast and documentation to lag behind the code — more details will be added here as the project stabilizes.

## Avalonia2DWorld.Generation.App

The purpose of this app is to make new maps: it's a hand-driven, panel-based world builder. You dial in some settings, press a button, and a procedural pass writes the result onto the map you're looking at — then you dial in the next thing (mountains, rivers, resources...) and press the next button, layering passes until the world looks right. The finished map is what everything else in the repo (`Avalonia2DWorld.Map`, `Avalonia2DWorld.MapServices`) exists to draw and to consume.

### Running it

```bash
dotnet run --project Avalonia2DWorld.Generation.App
```

The window opens with an empty map view and a floating stack of collapsible panels ("cards") down the left side, over the map. Nothing is generated until you tell it to.

### Workflow

The panels are meant to be worked top to bottom, since later passes generally build on what earlier ones left:

1. **File** — Open (`.nworld` map files, via `Avalonia2DWorld.Generation.App.Persistence.MapFile`) or Save the current map. A save writes the tiles *and* the panel settings that produced them, so reopening a file puts every dial back where it was — the point being that a map is something you keep working on, not just a picture you export.
2. **Start** — Type a width and height (in tiles; capped at 4096 per side and ~1,048,576 tiles total) and press **Create**. This throws away whatever map is open and makes a new blank one — open ocean, nothing on it — for the rest of the panels to build on.
3. **Base Land** — Grows the land the rest of the world sits on:
   - **Continents**: how many, how much of the map ends up as land, and how ragged the coastline is. Rebuilds the map as sea and raises fresh continents into it every time it's pressed — it replaces rather than adds.
   - **Islands**: scatters island count/size/coastal-preference on top of the existing continents (adds rather than replaces).
   - **Shallows**: marks a paler band of shallow water near every coast, purely a rendering distinction (no elevation change). Re-run after any change to the coastline.
4. **Hills and Mountains** — Raises the flat land into relief: hill coverage/spread, and mountain coverage/ruggedness/range size, sharing one seed. Each button (**Raise Hills**, **Raise Mountains**) replaces its own layer without disturbing the other. Mountains get first claim on the terrain field; hills fill in what's left.
5. **Swamps and Deserts** — Spreads ground cover (swamp favors low, coastal-leaning land; desert favors dry interior) in place of grass, sharing a patch size and clustering amount. Neither touches height or coastline; whichever is pressed first gets first pick of contested ground.
6. **Rivers** — Runs rivers from the hills down to the sea, merging into tributaries where they meet. **Add Rivers** adds to what's already there rather than replacing it — press again to fill in more of the map.
7. **Lakes** — Fills hollows in low ground with standing water, shaped from the terrain rather than dropped as a preset shape. Also additive.
8. **Resources** — Scatters deposits (iron, wood, oil, sulphur, stone) onto the finished land, each favoring different terrain (ore and stone want high ground, wood and oil want lowland, sulphur wants the ranges themselves). A tile holds one deposit at a time. Run this last — anything that rebuilds the land underneath (a new coastline, new terrain) takes the deposits on it along for the ride.
9. **Edit Tiles** — A hand-editing mode: while this panel is open, clicking the map raises/lowers elevation, flips a sea tile between deep/shallow, or lays/lifts the selected resource, depending on which tool is armed. For touch-ups a generation pass didn't quite get right, not for building a map from scratch.
10. **View** — How the map is drawn, not what's on it: switch between the Terrain / Elevation / Resources pictures (the same map, three different renderers — see `_terrainView`/`_heightView`/`_resourceView` in `MainWindowViewModel`), toggle water animation, the frame-rate readout, and elevation numbers drawn over each tile.

**Undo** (button, or Ctrl+Z) steps back through up to 5 whole-map snapshots — one per pass, so a run of several Edit Tiles clicks counts as a single step. Every seed field has its own **New** button for a random reroll; typing the same seed with the same settings always reproduces the same result, since generation is deterministic.

### Architecture notes

- `ViewModels/MainWindowViewModel.cs` is the app: it owns the live `TileMap`, every dial's bindable property, the undo history, and the commands each button invokes. It derives from `Avalonia2DWorld.Map.ViewModels.MapViewModelBase` (pan/zoom/mini-map) and adds everything specific to *this* map.
- `Generation/` holds the procedural passes themselves (`ContinentBuilder`, `IslandBuilder`, `TerrainBuilder`, `RiverBuilder`, `LakeBuilder`, `ResourceBuilder`, `GroundCoverBuilder`, `ShallowsBuilder`, plus shared helpers like `SimplexNoise` and `LandTopology`) — each one takes the current tiles/land mask and settings, and returns what the next state should be. `MainWindowViewModel` calls these and publishes the result through `TileMap.Edit`.
- `Persistence/MapFile.cs` defines the save format (`MapSettings` + the tile data) and how it's written/read.
- The renderer bound to the map view is `Avalonia2DWorld.MapServices`' `StandardRenderer` (see [Writing a custom renderer](#writing-a-custom-renderer) above) plus two purpose-built alternates, `ElevationRenderer` and `ResourceRenderer`, swapped in by the View panel.

## Avalonia2DWorld.Generation.TestApp

The purpose of this app is to test units and enhancements — the things built or posted *on* a finished map — against a real world, without dragging in everything the generator does. It opens a `.nworld` map produced by `Avalonia2DWorld.Generation.App`, treats that world as read-only, and lets you click around laying down roads, buildings, and units to see how they actually render and behave (rotation, adjacency shapes, placement rules) — the same render components `Avalonia2DWorld.MapServices` ships, exercised interactively instead of through code.

### Running it

```bash
dotnet run --project Avalonia2DWorld.Generation.TestApp
```

The window opens empty; press **Open** to load a `.nworld` map file (via `MapArchive`, in `Avalonia2DWorld.Generation.TestApp.Persistence`).

### What it's for

The world itself is never edited — it's loaded, drawn, and left exactly as it was generated. Everything this app changes lives on top of it, on two layers `MapTile` already reserves for exactly this (`RenderComponentLayers.Enhancement` and `RenderComponentLayers.Unit`), plus free-floating map labels:

- **Enhancements** (built on a tile): **Road**, **Bridge** (roads need dry land; bridges need water), **City** (adjacent city tiles grow into one town), **Fort**, **Factory**, **Shipyard** (needs water adjacent to launch into), **Works** (goes anywhere; draws itself based on whatever resource deposit is under it, if any).
- **Units** (standing on a tile, a separate layer so a tile can hold a building *and* a unit): **Militia**, **Soldiers**, **Cavalry** — all need dry land — and **Boat**/**Cog**/**Carrack** (small/medium/large ships), which need water.
- **Labels**: free-text map writing, placed at a pixel rather than a tile, with configurable background/foreground colors (typed as `#RRGGBB` / `#AARRGGBB` hex). Click open ground to write, click existing writing to edit it, drag to move it, or use **Rub out** to delete what's currently picked.

One radio button is armed at a time (**Off** included, so "do nothing" is as easy to reach as any tool) and a click on the map does whatever that tool does: build/post if the tile is clear, or lift/stand-down if it already holds something. Right-click turns a road/bridge a quarter turn where it has no neighbour to take its shape from. The line under the tool list reports what the last click did or why it was refused (e.g. "is water. A road needs dry ground -- build a bridge.").

**Save built** / **Load built** write and read a separate `.nworldx` file (via `EnhancementFile`, in `Avalonia2DWorld.MapServices.Persistence`) holding just the enhancement/unit/label layer — never the world itself — so the same base map can carry different sets of test content, and a saved enhancement file is named after the map it was built on (`<mapname>.nworldx`).

### Architecture notes

- `ViewModels/MainWindowViewModel.cs` owns the loaded map and the "what tool is armed / what did the last click do" state; it derives from `Avalonia2DWorld.Map.ViewModels.MapViewModelBase` for pan/zoom/mini-map, same as the generation app.
- Placement rules (dry land vs. water, adjacency for shipyards) are enforced here at the point of the click, using the same helpers (`Shoreline`, `ComponentParams`) the renderers themselves use to decide what to draw — so what this app allows you to build is what the renderer can actually make sense of.
- The frame-rate readout is on by default in this app (off in the generation app): this is where a render component gets worked on tile by tile, so any per-tile cost shows up here first.
- `Persistence/MapArchive.cs` reads the `.nworld` map format the generation app writes; `Avalonia2DWorld.MapServices.Persistence.EnhancementFile` reads/writes the `.nworldx` overlay this app owns.

## Using Avalonia2DWorld.Map

Reusable, app-agnostic building blocks for a 2D tile map in Avalonia: the `MapView` control that draws it, the tile/grid data model, and a base view model for the pan/zoom/mini-map gestures every map needs. It has no opinion about what a tile *means* (terrain, resources, units) — that lives in a renderer supplied by the app (see `Avalonia2DWorld.MapServices`).

### Installing

Reference the project from your app:

```xml
<ProjectReference Include="..\Avalonia2DWorld.Map\Avalonia2DWorld.Map.csproj" />
```

It targets `net10.0` and depends on Avalonia, Avalonia.Skia (for `ISkiaSharpApiLeaseFeature`), SkiaSharp 2.88.8, and CommunityToolkit.Mvvm.

### The pieces

- **`Controls.MapView`** — an Avalonia `Control` that draws a screenful of tiles. It holds no map state itself: it just renders whatever `Tiles` points at through whatever `Renderer` you give it, and raises commands for hover/click/drag/pan/zoom/mini-map gestures. Deciding what those gestures *do* is the view model's job.
- **`Models.TileMap`** — a mutable, copy-on-write map you build and edit. Editing publishes a new `Models.TileGrid` snapshot (`TileMap.Tiles`) that is safe to hand to the render thread while the old one may still be mid-frame.
- **`Models.TileGrid`** — the immutable snapshot `MapView.Tiles` binds to.
- **`Interfaces.IMapRenderer`** — the contract an app implements (or gets from MapServices) to actually paint tiles and labels onto an `SKCanvas`.
- **`ViewModels.MapViewModelBase`** — an `ObservableObject` base class that implements zoom-about-pointer, drag-to-pan, and click-to-recentre-from-mini-map, and exposes `Tiles`, `Labels`, `HoverText`, `Options`, and `Renderer` as bindable properties.

### Basic usage

#### 1. Build a map

```csharp
using Avalonia2DWorld.Map.Models;

var map = new TileMap(width: 200, height: 150, fill: coordinate => new MapTile
{
    X = coordinate.X,
    Y = coordinate.Y,
});
```

`map.Tiles` is the `TileGrid` to publish; `map.Edit(editor => ...)` batches changes and republishes once:

```csharp
map.Edit(editor => editor.Update(new TileCoordinate(5, 5), tile => tile.Elevation = 3));
```

#### 2. Write a view model

Derive from `MapViewModelBase`, own the `TileMap`, and republish `Tiles` after every edit:

```csharp
using Avalonia2DWorld.Map.Interfaces;
using Avalonia2DWorld.Map.ViewModels;

public partial class WorldMapViewModel : MapViewModelBase
{
    private readonly TileMap _map = new(200, 150);

    public WorldMapViewModel(IMapRenderer renderer) : base(renderer)
    {
        Tiles = _map.Tiles;
    }

    public void RaiseElevation(TileCoordinate coordinate)
    {
        _map.Edit(editor => editor.Update(coordinate, tile => tile.Elevation++));
        Tiles = _map.Tiles;
    }
}
```

`MapViewModelBase` already supplies `ZoomCommand`, `PanCommand`, and `MiniMapCommand` (wired up below) and keeps `Options.OriginX/OriginY/TileSize` clamped to the map bounds.

#### 3. Supply a renderer

Implement `IMapRenderer.RenderTiles` / `RenderLabels`, or use `Avalonia2DWorld.MapServices`' `StandardRenderer`, and pass it to the view model's constructor.

#### 4. Put a `MapView` in XAML

```xml
<Window xmlns:map="using:Avalonia2DWorld.Map.Controls">
    <map:MapView
        Tiles="{Binding Tiles}"
        Labels="{Binding Labels}"
        Renderer="{Binding Renderer}"
        Options="{Binding Options}"
        HoverText="{Binding HoverText}"
        ClickCommand="{Binding ClickCommand}"
        HoverCommand="{Binding HoverCommand}"
        PanCommand="{Binding PanCommand}"
        ZoomCommand="{Binding ZoomCommand}"
        MiniMapCommand="{Binding MiniMapCommand}" />
</Window>
```

Bind whichever of the other commands your app needs (`RightClickCommand`, `PixelClickCommand`, `PixelDragCommand`) — leaving one unbound just means that gesture does nothing.

### Things worth knowing

- **Never mutate a published `MapTile` or `TileGrid`.** Both are read on Avalonia's render thread while a frame may be drawing. Always go through `TileMap.Edit`, which clones-and-replaces rows/tiles under the hood.
- **`MapViewOptions` is immutable — replace it, don't mutate it** (`Options = Options with { TileSize = 16 }`). `MapView` repaints on reference change and reads `Options` off-thread.
- **Zoom levels are discrete** (`MapViewModelBase.ZoomLevels`, default `[4, 6, 8, 12, 16, 24, 32, 48, 64]`) because renderers cache per-tile-size atlases. Override the property in a derived view model to change the ladder.
- Turn `Options.ShowFrameRate` on while tuning render components — it measures actual drawn frames, not wall-clock time.
- The mini-map (`Options.MiniMap`) is opt-in per corner (`MiniMapLocation.UpperLeft`, etc.) and off by default.

## Writing a custom renderer

`MapView` draws nothing on its own — everything visible comes from whatever `IMapRenderer` it is bound to. There are two ways to get one: implement the interface from scratch, or add a component to `Avalonia2DWorld.MapServices`'s `StandardRenderer`. Reach for the second unless you're replacing the whole visual style of the map.

### The contract

```csharp
public interface IMapRenderer
{
    Task RenderTiles(SKCanvas canvas, RenderFrame frame, IReadOnlyList<MapTile> tiles);
    Task RenderLabels(SKCanvas canvas, RenderFrame frame, IReadOnlyList<MapLabel> labels);
}
```

- **`RenderTiles`** is called once per frame with every tile `MapView` has decided is on screen (plus a tile of margin). Draw tile `(x, y)` at pixel `(x * frame.TileSize, y * frame.TileSize)` — the canvas is already translated so the control's top-left is the map origin.
- **`RenderLabels`** draws `MapLabel`s over the finished tiles, in the same coordinate space (see `Models.MapLabel` / `Models.MapPixel` for how a label is positioned).
- **`RenderFrame`** carries what's fixed for the whole frame: `TileSize` (the current zoom), `TimeSeconds` (a single sampled clock — never call `DateTime.Now` or similar yourself, or animated content will tear between tiles drawn at slightly different instants), `World` (the whole `TileGrid`, for a component that needs to look at a neighbouring tile — may be `null`), and `Gpu` (the `GRContext` behind the canvas, for a component that wants an offscreen surface — may also be `null`).
- A `MapView` renderer instance is **not thread-safe by contract** — it's reused frame to frame so it can cache batch buffers, atlases, etc. Give each rendering thread (typically one, the UI thread's compositor) its own instance; don't share one across multiple `MapView`s that might draw concurrently.

### Option A: implement `IMapRenderer` directly

The minimum that works — no batching, one draw call per tile:

```csharp
public sealed class FlatColorRenderer : IMapRenderer
{
    private readonly SKPaint _paint = new() { Color = SKColors.SeaGreen };

    public Task RenderTiles(SKCanvas canvas, RenderFrame frame, IReadOnlyList<MapTile> tiles)
    {
        foreach (var tile in tiles)
        {
            canvas.DrawRect(
                tile.X * frame.TileSize, tile.Y * frame.TileSize,
                frame.TileSize, frame.TileSize,
                _paint);
        }
        return Task.CompletedTask;
    }

    public Task RenderLabels(SKCanvas canvas, RenderFrame frame, IReadOnlyList<MapLabel> labels) =>
        Task.CompletedTask; // or delegate to RenderMapLabel.Render from MapServices
}
```

This is fine for a prototype or a tiny map. It stops being fine once the map is large: at a low zoom level thousands of tiles are visible, and issuing a `DrawRect`/`DrawImage` per tile is the single biggest cost in a frame. If `tiles` implements `Interfaces.ITileRows` (which the grid `MapView` hands over does), walk it a row (`ReadOnlySpan<MapTile>`) at a time instead of through the `IReadOnlyList` indexer — it avoids an interface call per tile.

### Option B: add a component to `StandardRenderer` (recommended)

`StandardRenderer` (in `Avalonia2DWorld.MapServices`) already does the batching, layering, and visibility-culling work described above. What it doesn't know is what a new *kind* of thing looks like — that's what a "render component" adds. This is how every existing tile type (grass, water, roads, cities, units...) is implemented, and how you should add your own.

A tile doesn't draw itself directly; it carries a `Dictionary<int, MapRenderComponent>` keyed by **layer** (`Models.MapTile.MapRenderComponents`), and each `MapRenderComponent` has a `ComponentType` (a `Guid`) plus optional named `Params`. `StandardRenderer` groups every visible tile by `(layer, componentType)` into one batch and hands each batch to a render function in a single call — so your function draws *every* tile carrying your component in one pass, not one tile at a time.

1. **Add a component type constant** in `Avalonia2DWorld.MapServices.Constants.MapRenderComponentConstants`:

   ```csharp
   public static Guid Volcano { get; } = new Guid("...");
   ```

2. **Pick a layer** in `Avalonia2DWorld.MapServices.Constants.RenderComponentLayers` (or reuse an existing one, e.g. `BaseGround` for terrain, `Resource`, `Enhancement`, `Unit`). Layer numbers are part of the saved file format — never renumber an existing one; use one of the gaps left between them if you need a new slot.

3. **Write the render function**, following the shape every other one uses:

   ```csharp
   public static class RenderVolcano
   {
       public static Task Render(TileRenderContext context)
       {
           var canvas = context.Canvas;
           var tileSize = context.TileSize;

           foreach (var tile in context.Tiles) // ReadOnlySpan<TilePlacement>, only tiles with this component, already visible
           {
               using var paint = new SKPaint { Color = SKColors.DarkRed };
               canvas.DrawCircle(
                   tile.X * tileSize + tileSize / 2f, tile.Y * tileSize + tileSize / 2f,
                   tileSize * 0.4f, paint);
           }

           return Task.CompletedTask;
       }

       // Optional: build per-zoom-level caches (atlases, sprite sheets) ahead of a zoom step.
       public static Task Prewarm(int tileSize) => Task.CompletedTask;

       // Optional: drop cached textures, e.g. after a palette change.
       public static void ClearCache() { }
   }
   ```

   `TileRenderContext.Tiles` gives you `TilePlacement`s (`X`, `Y`, `Params`) — map coordinates, not pixels — for only the tiles carrying this component that are on screen this frame. Use `context.World` (a `TileGrid?`) if your component needs to inspect neighbouring tiles (see `RenderRoad`/`RoadNetwork` for how road shapes are decided from what's next door), and `context.Frame.TimeSeconds` for animation — never sample a clock yourself.

4. **Register it** in `Avalonia2DWorld.MapServices.MapRenderComponents.StandardRenderer.RenderHelperFunctions.Renderers`:

   ```csharp
   { MapRenderComponentConstants.Volcano, new(RenderVolcano.Render, RenderVolcano.Prewarm, RenderVolcano.ClearCache) },
   ```

   Pass `null` for `Prewarm`/`ClearCache` if the component holds nothing between frames (see `RenderEmpty` or `RenderWorks` for examples).

5. **Put it on a tile**:

   ```csharp
   map.Edit(editor => editor.Update(coordinate, tile =>
       tile.SetMapRenderComponent(RenderComponentLayers.BaseGround, new MapRenderComponent
       {
           ComponentType = MapRenderComponentConstants.Volcano,
       })));
   ```

### Things worth knowing

- **Determinism matters.** Existing components derive all their per-tile variation (which texture variant, noise phase, etc.) from a hash of the tile's own coordinates plus a fixed seed, not from randomness sampled at draw time. That's what keeps a tile looking the same across repaints, zoom changes, and app restarts.
- **Coalesce draws, don't loop per pixel or per tile where you can avoid it.** `RenderGrass` is the fullest example: it blits every tile in a batch with one `DrawAtlas` call and paints broad tone with one rect per run of horizontally-adjacent tiles, rather than two draws per tile. On a GPU-backed canvas the difference between one batched call and thousands of individual ones is measured in tens of milliseconds per frame.
- **Cache per zoom level, not per tile.** `TileSize` changes discretely (see `MapViewModelBase.ZoomLevels`), so a texture/atlas built for one size is reused across every frame at that size. `Avalonia2DWorld.MapServices.MapRenderComponents.StandardRenderer.ZoomLevelCache<T>` is the helper the existing components use for this.
- **A component that draws across tile boundaries** (a coastline, a road, a shore) doesn't fit the per-component-batch model and is instead drawn as an "edge" pass between the ground layer and everything above it — see `StandardRenderer.Edges` and `Coastline`/`GroundEdge`/`WaterDepth` for the pattern if you need one.
