# Avalonia2DWorld.Map

Reusable, app-agnostic building blocks for a 2D tile map in Avalonia: the `MapView` control that draws it, the tile/grid data model, and a base view model for the pan/zoom/mini-map gestures every map needs. It has no opinion about what a tile *means* (terrain, resources, units) — that lives in a renderer supplied by the app (see [Avalonia2DWorld.MapServices](../Avalonia2DWorld.MapServices)).

## Installing

Reference the project from your app:

```xml
<ProjectReference Include="..\Avalonia2DWorld.Map\Avalonia2DWorld.Map.csproj" />
```

It targets `net10.0` and depends on Avalonia, Avalonia.Skia (for `ISkiaSharpApiLeaseFeature`), SkiaSharp 2.88.8, and CommunityToolkit.Mvvm.

## The pieces

- **`Controls.MapView`** — an Avalonia `Control` that draws a screenful of tiles. It holds no map state itself: it just renders whatever `Tiles` points at through whatever `Renderer` you give it, and raises commands for hover/click/drag/pan/zoom/mini-map gestures. Deciding what those gestures *do* is the view model's job.
- **`Models.TileMap`** — a mutable, copy-on-write map you build and edit. Editing publishes a new `Models.TileGrid` snapshot (`TileMap.Tiles`) that is safe to hand to the render thread while the old one may still be mid-frame.
- **`Models.TileGrid`** — the immutable snapshot `MapView.Tiles` binds to.
- **`Interfaces.IMapRenderer`** — the contract an app implements (or gets from MapServices) to actually paint tiles and labels onto an `SKCanvas`.
- **`ViewModels.MapViewModelBase`** — an `ObservableObject` base class that implements zoom-about-pointer, drag-to-pan, and click-to-recentre-from-mini-map, and exposes `Tiles`, `Labels`, `HoverText`, `Options`, and `Renderer` as bindable properties.

## Basic usage

### 1. Build a map

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

### 2. Write a view model

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

### 3. Supply a renderer

Implement `IMapRenderer.RenderTiles` / `RenderLabels`, or use `Avalonia2DWorld.MapServices`' `StandardRenderer`, and pass it to the view model's constructor.

### 4. Put a `MapView` in XAML

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

## Things worth knowing

- **Never mutate a published `MapTile` or `TileGrid`.** Both are read on Avalonia's render thread while a frame may be drawing. Always go through `TileMap.Edit`, which clones-and-replaces rows/tiles under the hood.
- **`MapViewOptions` is immutable — replace it, don't mutate it** (`Options = Options with { TileSize = 16 }`). `MapView` repaints on reference change and reads `Options` off-thread.
- **Zoom levels are discrete** (`MapViewModelBase.ZoomLevels`, default `[4, 6, 8, 12, 16, 24, 32, 48, 64]`) because renderers cache per-tile-size atlases. Override the property in a derived view model to change the ladder.
- Turn `Options.ShowFrameRate` on while tuning render components — it measures actual drawn frames, not wall-clock time.
- The mini-map (`Options.MiniMap`) is opt-in per corner (`MiniMapLocation.UpperLeft`, etc.) and off by default.
