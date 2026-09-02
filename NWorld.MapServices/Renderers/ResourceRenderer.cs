using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NWorld.Map.Interfaces;
using NWorld.Map.Models;
using NWorld.MapServices.Constants;
using NWorld.MapServices.MapRenderComponents.StandardRenderer;
using SkiaSharp;

namespace NWorld.MapServices.Renderers
{
    /// <summary>
    /// One entry in the key to a picture: what a colour on the map means.
    /// </summary>
    /// <param name="Name">What to call it.</param>
    /// <param name="Colour">The colour the map draws it in.</param>
    public readonly record struct KeyEntry(string Name, SKColor Colour);

    /// <summary>
    /// Draws what the land is worth: the world flattened to three greys, with every deposit on
    /// it as a solid square of its own colour.
    /// <para>
    /// Height is thrown away deliberately, and it is the whole reason this is a picture of its
    /// own rather than a layer over one of the others. A deposit is a few tiles wide; ground
    /// shaded by elevation, or drawn as grass and marsh and sand, is a field of competing
    /// colour at exactly the scale the deposits are, and the eye cannot pick five scattered
    /// squares out of it. Flatten the world to a grey and the squares are the only colour
    /// left, which is what makes "where is the iron" a question the map answers at a glance.
    /// </para>
    /// <para>
    /// The land keeps only what is needed to know where you are looking: the sea black, the
    /// land one grey, and the rivers a lighter grey through it. Rivers, because a river is the
    /// landmark people navigate a map by and because it carries no deposits itself, so it can
    /// be told apart without taking a colour that a resource wants.
    /// </para>
    /// <para>
    /// Stateless and cacheless, as <see cref="ElevationRenderer"/> mostly is: a tile is one
    /// flat rect from a fixed palette, at every zoom level. It draws no labels -- a number
    /// saying how high a tile stands has nothing to say about what is buried in it.
    /// </para>
    /// </summary>
    public sealed class ResourceRenderer : IMapRenderer
    {
        // The world, flattened. Dark enough that every resource colour reads as colour against
        // it, and separated enough from each other to keep a coastline and a river legible.
        private static readonly SKPaint Sea = Fill(SKColors.Black);
        private static readonly SKPaint Ground = Fill(new SKColor(0x4A, 0x4A, 0x4E));
        private static readonly SKPaint River = Fill(new SKColor(0x93, 0x99, 0xA3));

        // Five hues, as far apart as five can be got: warm, green, violet, yellow and cyan.
        // Chosen against the grey rather than against each other alone, since every one of
        // them is seen as a handful of tiles surrounded by it.
        private static readonly SKPaint Iron = Fill(new SKColor(0xE0, 0x6C, 0x4B));
        private static readonly SKPaint Wood = Fill(new SKColor(0x4C, 0xB0, 0x50));
        private static readonly SKPaint Oil = Fill(new SKColor(0xA6, 0x6B, 0xE8));
        private static readonly SKPaint Sulphur = Fill(new SKColor(0xF2, 0xD4, 0x42));
        private static readonly SKPaint Stone = Fill(new SKColor(0x46, 0xB8, 0xD8));

        /// <summary>
        /// What the colours on this picture mean, in the order they are worth reading: the
        /// deposits first, since they are what the picture is for, and the ground under them
        /// after.
        /// <para>
        /// Published from here because this is where the colours are decided. A key drawn
        /// somewhere else off its own copy of them is a key that goes quietly wrong the first
        /// time one is adjusted, and a wrong key is worse than none.
        /// </para>
        /// </summary>
        public static IReadOnlyList<KeyEntry> Key { get; } =
        [
            new("Iron", Iron.Color),
            new("Wood", Wood.Color),
            new("Oil", Oil.Color),
            new("Sulphur", Sulphur.Color),
            new("Stone", Stone.Color),
            new("Land", Ground.Color),
            new("River", River.Color),
            new("Water", Sea.Color),
        ];

        public Task RenderTiles(SKCanvas canvas, RenderFrame frame, IReadOnlyList<MapTile> tiles)
        {
            ArgumentNullException.ThrowIfNull(canvas);
            ArgumentNullException.ThrowIfNull(tiles);

            var tileSize = frame.TileSize;

            if (tileSize <= 0 || tiles.Count == 0)
                return Task.CompletedTask;

            var (minX, minY, maxX, maxY) = VisibleTiles.For(canvas, tileSize);
            var run = new FillRun(canvas, tileSize);

            // Walked as a span where the list allows it, for the reason the other renderers do
            // the same: on a large map most tiles are off screen, and fetching each one through
            // the interface to throw it away is several milliseconds a frame.
            switch (tiles)
            {
                case ITileRows grid:
                    for (var row = 0; row < grid.RowCount; row++)
                        Draw(ref run, grid.Row(row), minX, minY, maxX, maxY);
                    break;

                case MapTile[] array:
                    Draw(ref run, array.AsSpan(), minX, minY, maxX, maxY);
                    break;

                default:
                    foreach (var tile in tiles)
                        Add(ref run, tile, minX, minY, maxX, maxY);
                    break;
            }

            run.Flush();

            return Task.CompletedTask;
        }

        private static void Draw(
            ref FillRun run, ReadOnlySpan<MapTile> tiles, int minX, int minY, int maxX, int maxY)
        {
            foreach (var tile in tiles)
                Add(ref run, tile, minX, minY, maxX, maxY);
        }

        private static void Add(ref FillRun run, MapTile? tile, int minX, int minY, int maxX, int maxY)
        {
            if (tile is null || tile.X < minX || tile.X > maxX || tile.Y < minY || tile.Y > maxY)
                return;

            run.Add(tile.X, tile.Y, Shade(tile));
        }

        /// <summary>
        /// What one tile is drawn in: its deposit if it carries one, and otherwise the ground
        /// it stands on.
        /// <para>
        /// The deposit first, because this is the picture that exists to show them. Nothing
        /// currently puts one on a river, but if something ever does, a resource the map is
        /// hiding under a landmark would be the worse of the two mistakes.
        /// </para>
        /// </summary>
        private static SKPaint Shade(MapTile tile)
        {
            if (tile.MapRenderComponents.TryGetValue(RenderComponentLayers.Resource, out var deposit) &&
                deposit is not null)
            {
                if (deposit.ComponentType == MapRenderComponentConstants.Iron)
                    return Iron;

                if (deposit.ComponentType == MapRenderComponentConstants.Wood)
                    return Wood;

                if (deposit.ComponentType == MapRenderComponentConstants.Oil)
                    return Oil;

                if (deposit.ComponentType == MapRenderComponentConstants.Sulphur)
                    return Sulphur;

                if (deposit.ComponentType == MapRenderComponentConstants.Stone)
                    return Stone;
            }

            if (!tile.MapRenderComponents.TryGetValue(RenderComponentLayers.BaseGround, out var ground) ||
                ground is null)
            {
                // No ground to ask. Fall back on the one thing every tile carries, which is
                // how high it stands.
                return tile.Elevation > Elevations.Sea ? Ground : Sea;
            }

            if (ground.ComponentType == MapRenderComponentConstants.Water)
                return River;

            if (ground.ComponentType == MapRenderComponentConstants.DeepWater ||
                ground.ComponentType == MapRenderComponentConstants.ShallowWater)
            {
                return Sea;
            }

            return Ground;
        }

        /// <summary>
        /// A flat fill. Antialiasing stays off: neighbouring tiles share their edges exactly,
        /// and antialiased edges give both sides partial coverage, which shows up as a grid
        /// drawn over the picture.
        /// </summary>
        private static SKPaint Fill(SKColor color) => new()
        {
            Color = color,
            IsAntialias = false,
            Style = SKPaintStyle.Fill,
        };
    }
}
