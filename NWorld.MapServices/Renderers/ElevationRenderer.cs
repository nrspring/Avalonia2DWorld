using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NWorld.Map.Interfaces;
using NWorld.Map.Models;
using NWorld.MapServices.Constants;
using NWorld.MapServices.MapRenderComponents.StandardRenderer;
using NWorld.MapServices.MapRenderComponents.StandardRenderer.RenderingFunctions;
using SkiaSharp;

namespace NWorld.MapServices.Renderers
{
    /// <summary>
    /// Draws the map as height: the sea black, the land a grey that lightens all the way from
    /// the flats to the highest peak, and the rivers running through it in blue at the height
    /// they cross.
    /// <para>
    /// Height decides how bright a tile is and the one question asked about the ground decides
    /// which ramp that brightness is taken from. No cover, no deposits, no hover or selection
    /// mark, which is the whole point of it -- <see cref="StandardRenderer"/> draws what the
    /// world is made of, and there is no reading the shape of the land off a picture that also
    /// has marshes, ore and a coastline in it. Two renderers rather than a switch inside one,
    /// because they disagree about what a tile <em>is</em> and not merely about how to colour
    /// it.
    /// </para>
    /// <para>
    /// The one component it does draw is the elevation label, when the map is carrying them.
    /// That is not an inconsistency. A label is not something the world is made of, it is the
    /// height written down, and the picture whose whole subject is height is the last place it
    /// would be out of place -- it also answers the one question the ramp cannot, since a grey
    /// says which of two slopes is higher and only a number says by how much.
    /// </para>
    /// <para>
    /// The rivers are the single exception, and they earn it. A river takes the elevation of
    /// the ground it crosses, so on height alone it is indistinguishable from the valley it
    /// runs down -- and a river is the one feature that explains the relief around it rather
    /// than sitting on top of it. So water gets a ramp of its own: blue, and climbing with
    /// height step for step alongside the grey. The colour says river and the brightness still
    /// says how far up it is, so both can be read off the same tile.
    /// </para>
    /// <para>
    /// It follows that this holds no render components and builds no caches. There is nothing
    /// to cache: a tile is one flat rect in one of thirty-one colours from one of two ramps,
    /// the ramps are the same at every zoom level, and both are built once for the life of the
    /// process.
    /// </para>
    /// <para>
    /// The labels are the one thing here that needs a batch, and so the one thing that makes
    /// this hold state: the buffer they are gathered into is reused frame to frame, so a
    /// steady view settles into allocating nothing for them. That makes an instance
    /// single-threaded by construction, exactly as <see cref="StandardRenderer"/> is -- give
    /// each rendering thread its own.
    /// </para>
    /// </summary>
    public sealed class ElevationRenderer : IMapRenderer
    {
        /// <summary>
        /// The grey the lowest land is drawn in. Well clear of the black beneath it: the
        /// coastline is the one line this picture must never lose, and a flat plain at the
        /// water's edge is where it is thinnest.
        /// </summary>
        private const byte Lowest = 46;

        /// <summary>
        /// The grey the highest ground is drawn in. Short of white, so that the peaks have
        /// somewhere to go and the picture does not read as blown out.
        /// </summary>
        private const byte Highest = 250;

        /// <summary>
        /// One paint per elevation, black at sea level and lightening by an even step from
        /// there. Even rather than banded on purpose: the hills and mountains have their own
        /// bands elsewhere, and a picture whose only job is height should show height and not
        /// somebody's opinion of where a mountain begins.
        /// </summary>
        private static readonly SKPaint[] Land = BuildShades(water: false);

        /// <summary>
        /// The same climb for water, carried on blue instead of grey and running a little
        /// darker than the land at the same height -- see the colours below for why it cannot
        /// run level with it. Sea level is black on both ramps, so the sea comes out black
        /// without this having to know that it is the sea.
        /// </summary>
        private static readonly SKPaint[] Water = BuildShades(water: true);

        /// <summary>
        /// The labels on screen this frame, gathered while the fills are found and handed over
        /// once they are drawn. Grown to the largest screenful seen, and then reused.
        /// </summary>
        private TilePlacement[] _labels = [];

        private int _labelCount;

        private int _drawing;

        public async Task RenderTiles(SKCanvas canvas, RenderFrame frame, IReadOnlyList<MapTile> tiles)
        {
            ArgumentNullException.ThrowIfNull(canvas);
            ArgumentNullException.ThrowIfNull(tiles);

            // Sharing an instance across threads corrupts the label buffer, and does it far
            // from here. Cheaper to refuse, for the reasons StandardRenderer refuses.
            if (Interlocked.Exchange(ref _drawing, 1) == 1)
            {
                throw new InvalidOperationException(
                    $"{nameof(ElevationRenderer)} reuses its label buffer between frames and cannot " +
                    "draw two frames at once. Give each rendering thread its own instance.");
            }

            try
            {
                await Draw(canvas, frame, tiles);
            }
            finally
            {
                Volatile.Write(ref _drawing, 0);
            }
        }

        private Task Draw(SKCanvas canvas, RenderFrame frame, IReadOnlyList<MapTile> tiles)
        {
            var tileSize = frame.TileSize;

            if (tileSize <= 0 || tiles.Count == 0)
                return Task.CompletedTask;

            // Asked once for the whole frame rather than once per tile: below this the labels
            // draw as nothing at all, so there is no sense gathering any. It is what keeps the
            // mini-map pass, which draws every tile on the map at a few pixels each, from
            // walking a million component dictionaries to write nothing down.
            var labelled = tileSize >= RenderElevationLabel.MinTileSize;

            _labelCount = 0;

            var (minX, minY, maxX, maxY) = VisibleTiles.For(canvas, tileSize);
            var run = new Run(canvas, tileSize);

            // Walked as a span where the list allows it, for the reason StandardRenderer does
            // the same: on a large map most tiles are off screen, and fetching each one
            // through the interface to throw it away is several milliseconds a frame.
            switch (tiles)
            {
                case ITileRows grid:
                    for (var row = 0; row < grid.RowCount; row++)
                        Fill(ref run, grid.Row(row), labelled, minX, minY, maxX, maxY);
                    break;

                case MapTile[] array:
                    Fill(ref run, array.AsSpan(), labelled, minX, minY, maxX, maxY);
                    break;

                default:
                    foreach (var tile in tiles)
                        Add(ref run, tile, labelled, minX, minY, maxX, maxY);
                    break;
            }

            run.Flush();

            // After every fill, and not tile by tile: a label sits in the middle of its tile
            // but the halo behind it does not stop at the edge, and a neighbour filled
            // afterwards would paint over whatever crossed.
            return _labelCount == 0
                ? Task.CompletedTask
                : RenderHelperFunctions.RenderComponent(
                    new TileRenderContext(canvas, frame, _labels, _labelCount),
                    MapRenderComponentConstants.ElevationLabel);
        }

        private void Fill(
            ref Run run,
            ReadOnlySpan<MapTile> tiles,
            bool labelled,
            int minX,
            int minY,
            int maxX,
            int maxY)
        {
            foreach (var tile in tiles)
                Add(ref run, tile, labelled, minX, minY, maxX, maxY);
        }

        private void Add(
            ref Run run, MapTile? tile, bool labelled, int minX, int minY, int maxX, int maxY)
        {
            if (tile is null || tile.X < minX || tile.X > maxX || tile.Y < minY || tile.Y > maxY)
                return;

            if (labelled)
                Collect(tile);

            // Clamped at both ends, so a tile raised past the top of the scale comes out as
            // the highest ground rather than out of bounds.
            var elevation = Math.Clamp(tile.Elevation, Elevations.Sea, Elevations.MountainsTo);

            // Only land is asked what it is made of. Everything at sea level is black on both
            // ramps, so the question could only cost a dictionary lookup to arrive at the
            // answer it already had -- and on most maps the sea is most of the tiles.
            var shades = elevation > Elevations.Sea && IsWater(tile) ? Water : Land;

            run.Add(tile.X, tile.Y, shades[elevation]);
        }

        /// <summary>
        /// Keeps the tile elevation label, where there is one, for the pass after the fills.
        /// </summary>
        private void Collect(MapTile tile)
        {
            if (!tile.MapRenderComponents.TryGetValue(RenderComponentLayers.ElevationLabel, out var label) ||
                label is null ||
                label.ComponentType != MapRenderComponentConstants.ElevationLabel)
            {
                return;
            }

            if (_labelCount == _labels.Length)
                Array.Resize(ref _labels, Math.Max(64, _labels.Length * 2));

            _labels[_labelCount++] = new TilePlacement(tile.X, tile.Y, label.Params ?? []);
        }

        /// <summary>
        /// Whether the tile is drawn as water: a river, or the sea. One of the two things this
        /// renderer asks about a tile besides how high it stands.
        /// <para>
        /// Off the ground component rather than off a flag, because that is where the answer
        /// lives -- a tile is a river because it is drawn with running water on it, and the
        /// map carries no second opinion for this to consult.
        /// </para>
        /// </summary>
        private static bool IsWater(MapTile tile) =>
            tile.MapRenderComponents.TryGetValue(RenderComponentLayers.BaseGround, out var ground) &&
            ground is not null &&
            (ground.ComponentType == MapRenderComponentConstants.Water ||
             ground.ComponentType == MapRenderComponentConstants.ShallowWater ||
             ground.ComponentType == MapRenderComponentConstants.DeepWater);

        private static SKPaint[] BuildShades(bool water)
        {
            var shades = new SKPaint[Elevations.MountainsTo + 1];

            // The sea, and everything at or below its level, on both ramps. Black rather than
            // the bottom of either scale, because the one thing this picture has to say before
            // it says anything about height is where the water stops.
            shades[Elevations.Sea] = Fill(SKColors.Black);

            var climb = Elevations.MountainsTo - Elevations.Flat;

            for (var elevation = Elevations.Flat; elevation <= Elevations.MountainsTo; elevation++)
            {
                var height = (float)(elevation - Elevations.Flat) / climb;
                var value = Lowest + ((Highest - Lowest) * height);

                // A real blue rather than a grey with a wash over it. A tint faint enough to
                // keep the exact grey is a tint too faint to find a river in a valley, and
                // finding it is the point.
                //
                // Blue carries very little of what the eye reads as brightness, so a blue of
                // the same lightness as the grey beside it would have to be so pale it stopped
                // being blue. The channels are lifted instead until a river sits a little
                // under its own banks and no further: enough that the height still climbs
                // visibly along the water from mouth to source, which is the reading this ramp
                // exists for, and never enough for a river low down to pass for a valley.
                shades[elevation] = Fill(water
                    ? new SKColor(
                        Channel(value * 0.30f),
                        Channel(value * 0.62f),
                        Channel(80f + (value * 0.85f)))
                    : new SKColor(Channel(value), Channel(value), Channel(value)));
            }

            return shades;
        }

        private static byte Channel(float value) => (byte)Math.Clamp(MathF.Round(value), 0, 255);

        /// <summary>
        /// A flat fill. Antialiasing stays off: neighbouring tiles share their edges exactly,
        /// and antialiased edges give both sides partial coverage, which shows up as a grid
        /// drawn over the picture in a lighter grey than either tile.
        /// </summary>
        private static SKPaint Fill(SKColor color) => new()
        {
            Color = color,
            IsAntialias = false,
            Style = SKPaintStyle.Fill,
        };

        /// <summary>
        /// A row of neighbouring tiles being drawn in one grey, so a plain costs a rect per
        /// row rather than a rect per tile. The same saving <c>TileRuns</c> makes for the
        /// standard renderer, which cannot be borrowed here: that one works from the
        /// placements a batch carries, and this renderer has no batches to put them in.
        /// </summary>
        private struct Run(SKCanvas canvas, int tileSize)
        {
            private SKPaint? _paint;
            private int _x;
            private int _y;
            private int _length;

            public void Add(int x, int y, SKPaint paint)
            {
                if (ReferenceEquals(paint, _paint) && y == _y && x == _x + _length)
                {
                    _length++;
                    return;
                }

                Flush();

                _paint = paint;
                _x = x;
                _y = y;
                _length = 1;
            }

            public void Flush()
            {
                if (_paint is null || _length == 0)
                    return;

                canvas.DrawRect(
                    SKRect.Create(_x * tileSize, _y * tileSize, _length * tileSize, tileSize),
                    _paint);

                _length = 0;
                _paint = null;
            }
        }
    }
}
