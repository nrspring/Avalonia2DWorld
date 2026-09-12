# Avalonia2DWorld

A hobby project exploring procedural world generation and tile-based 2D map rendering, built with [Avalonia UI](https://avaloniaui.net/) on .NET.

The repository is organized as a solution ([Avalonia2DWorld.slnx](Avalonia2DWorld.slnx)) of a few focused projects rather than one monolithic app:

- **Avalonia2DWorld.Generation.App** — The main Avalonia desktop application. Procedurally generates world maps (continents, islands, lakes, rivers, terrain, resources, ground cover, etc.) and lets you view/save the results.
- **Avalonia2DWorld.Generation.TestApp** — A secondary Avalonia app used for testing/inspecting generated map archives outside of the main generation workflow.
- **Avalonia2DWorld.Map** — Shared map/tile primitives and the reusable `MapView` control: tile grids, tile placement, map interaction models (pan, zoom, drag), and persistence formats for tile maps. See [Using Avalonia2DWorld.Map](#using-avalonia2dworldmap) below.
- **Avalonia2DWorld.MapServices** — Rendering services layered on top of `Avalonia2DWorld.Map`: the standard tile renderer, terrain/resource/unit render components (water, coastline, roads, cities, forts, units, etc.), and related caching and helper logic.

There's also a [Documents](Documents) folder for planning notes.

## Status

This is an actively evolving personal project. Expect things to move fast and documentation to lag behind the code — more details will be added here as the project stabilizes.

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
