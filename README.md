# Avalonia2DWorld

A hobby project exploring procedural world generation and tile-based 2D map rendering, built with [Avalonia UI](https://avaloniaui.net/) on .NET.

The repository is organized as a solution ([Avalonia2DWorld.slnx](Avalonia2DWorld.slnx)) of a few focused projects rather than one monolithic app:

- **Avalonia2DWorld.Generation.App** — The main Avalonia desktop application. Procedurally generates world maps (continents, islands, lakes, rivers, terrain, resources, ground cover, etc.) and lets you view/save the results.
- **Avalonia2DWorld.Generation.TestApp** — A secondary Avalonia app used for testing/inspecting generated map archives outside of the main generation workflow.
- **Avalonia2DWorld.Map** — Shared map/tile primitives and the reusable `MapView` control: tile grids, tile placement, map interaction models (pan, zoom, drag), and persistence formats for tile maps. See its own [README](Avalonia2DWorld.Map/README.md) for usage.
- **Avalonia2DWorld.MapServices** — Rendering services layered on top of `Avalonia2DWorld.Map`: the standard tile renderer, terrain/resource/unit render components (water, coastline, roads, cities, forts, units, etc.), and related caching and helper logic.

There's also a [Documents](Documents) folder for planning notes.

## Status

This is an actively evolving personal project. Expect things to move fast and documentation to lag behind the code — more details will be added here as the project stabilizes.
