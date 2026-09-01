using System;
using System.Globalization;
using NWorld.Map.Models;
using SkiaSharp;

namespace NWorld.MapServices.MapRenderComponents
{
    /// <summary>
    /// How bright a ground tile is drawn for the elevation it sits at: flat land as it always
    /// was, hills lifted out of it, mountains lifted further still.
    /// <para>
    /// Height is the one thing a map drawn from directly overhead cannot show by shape, so it
    /// has to be shown by tone -- the same bargain a shaded relief map makes. This is the
    /// whole of that: a lift per elevation, applied to the ground the tile already has, so
    /// grass at altitude still reads as grass.
    /// </para>
    /// <para>
    /// A lift rather than a scale, and <see cref="SKBlendMode.Screen"/> rather than a multiply,
    /// because the ground at elevation 1 has to come out exactly as it did before there was
    /// any of this: screening with black is the identity, so the flat land every map is made
    /// of is untouched and only what rises above it changes. Multiplying could only darken,
    /// which would mean dimming the whole world to make room at the top.
    /// </para>
    /// <para>
    /// A pass of its own, run last by every ground render function, rather than something each
    /// of them works into how it draws. The grounds draw in three different ways -- grass
    /// blits a pool of per-tile variants, sand and bog repeat one tiling texture, water
    /// animates -- and there is no single place inside that to put this. Afterwards there is:
    /// the ground is finished, whatever made it, and the lift goes over the top. So the scale
    /// is defined here once and every ground on the map answers to the same one.
    /// </para>
    /// <para>
    /// It costs one rect per run of neighbouring tiles standing at the same height, and
    /// nothing whatever for ground at or below elevation 1 -- which is every tile of every map
    /// until something raises one, and all of the sea for good.
    /// </para>
    /// </summary>
    internal static class ElevationShade
    {
        /// <summary>Sea level. The datum, and not something that can be lifted.</summary>
        public const int SeaLevel = 0;

        /// <summary>Flat land: the elevation that is drawn exactly as it would be without this.</summary>
        public const int Ground = 1;

        /// <summary>The first and last elevations that count as hills.</summary>
        public const int HillsFrom = 2;

        /// <inheritdoc cref="HillsFrom"/>
        public const int HillsTo = 10;

        /// <summary>The first and last elevations that count as mountains.</summary>
        public const int MountainsFrom = 11;

        /// <inheritdoc cref="MountainsFrom"/>
        public const int MountainsTo = 30;

        // The lift at each end of each band, before the screen rolls it off. Banded rather
        // than one straight line from 1 to 30 so that the bands are legible on the map: spread
        // evenly, a hill would differ from flat ground by three percent and the whole of the
        // hill country would look like a rounding error. The gap between the top of the hills
        // and the foot of the mountains is deliberate -- it is what makes a treeline you can
        // see.
        private const float HillsFloor = 10f;
        private const float HillsCeiling = 48f;
        private const float MountainsFloor = 56f;
        private const float MountainsCeiling = 112f;

        /// <summary>
        /// How much of the lift each channel gets.
        /// <para>
        /// Not neutral, and this is the difference between a mountain and a cloud. Screening
        /// ground with a grey walks it towards white: blue starts lowest in every ground colour
        /// on the map, so blue has the furthest to travel and gains the most, and by the top of
        /// the scale the colour has drained out of the land altogether. Holding blue back keeps
        /// the ground the colour it was and lets the lift do only the one job it is for.
        /// </para>
        /// </summary>
        private static readonly (float Red, float Green, float Blue) Tint = (0.85f, 1f, 0.5f);

        /// <summary>
        /// One paint per elevation, worked out once, null wherever the lift comes to nothing.
        /// Read-only afterwards, and so shared across draws and across threads, as every other
        /// paint held for the life of the program here is.
        /// </summary>
        private static readonly SKPaint?[] Paints = BuildPaints();

        /// <summary>
        /// The paint for one tile. Static, so the delegate behind it is allocated once rather
        /// than per draw.
        /// </summary>
        private static readonly Func<TilePlacement, SKPaint?> PaintFor =
            static tile => Paints[ElevationOf(tile.Params)];

        /// <summary>
        /// Screens the lift over ground that has already been drawn. The last thing a ground
        /// render function does.
        /// </summary>
        public static void Apply(TileRenderContext context)
        {
            var canvas = context.Canvas;
            var tileSize = context.TileSize;
            var tiles = context.Tiles;

            if (canvas is null || tileSize <= 0 || tiles.Length == 0)
                return;

            TileRuns.Fill(canvas, tiles, tileSize, PaintFor);
        }

        /// <summary>
        /// The elevation a ground component's parameters carry as their first argument, held
        /// inside the range there are lifts for.
        /// <para>
        /// Above the mountain tops it stays at the mountain tops: a peak higher than the scale
        /// allows for is still a peak, and brightening past this washes the ground out
        /// altogether.
        /// </para>
        /// <para>
        /// Anything that is not a number -- no parameters at all, most of all -- reads as flat
        /// ground rather than as an error. A render function is handed whatever a tile happens
        /// to carry, tiles come out of files as readily as out of a generator, and a map that
        /// predates elevation being passed down here should draw as the map it is rather than
        /// not at all.
        /// </para>
        /// </summary>
        private static int ElevationOf(string[] parameters) =>
            parameters.Length > 0 &&
            int.TryParse(parameters[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var elevation)
                ? Math.Clamp(elevation, SeaLevel, MountainsTo)
                : Ground;

        /// <summary>
        /// Fills the table: nothing at or below flat ground, then a straight line across the
        /// hills and another across the mountains.
        /// </summary>
        private static SKPaint?[] BuildPaints()
        {
            var paints = new SKPaint?[MountainsTo + 1];

            for (var elevation = 0; elevation < paints.Length; elevation++)
            {
                var lift = elevation switch
                {
                    <= Ground => 0f,
                    <= HillsTo => Across(elevation, HillsFrom, HillsTo, HillsFloor, HillsCeiling),
                    _ => Across(elevation, MountainsFrom, MountainsTo, MountainsFloor, MountainsCeiling),
                };

                // Left null rather than made into a paint that screens black, which is the
                // identity and so a draw that does nothing. This is what makes the pass free on
                // flat land and on the sea.
                if (lift <= 0f)
                    continue;

                paints[elevation] = new SKPaint
                {
                    // Opaque, which is what keeps the lift whole: any less alpha would be
                    // unpremultiplied on the way in and take the lift down with it.
                    Color = new SKColor(
                        Level(lift * Tint.Red), Level(lift * Tint.Green), Level(lift * Tint.Blue)),
                    BlendMode = SKBlendMode.Screen,

                    // Must stay off. Runs share their edges exactly; antialiased, both sides of
                    // a shared edge take partial coverage and the screen lands twice along it --
                    // a bright grid over the high ground, which is the one thing the renderers
                    // underneath work hardest to avoid.
                    IsAntialias = false,
                };
            }

            return paints;
        }

        /// <summary>One channel of a lift, as a byte.</summary>
        private static byte Level(float lift) => (byte)Math.Clamp((int)MathF.Round(lift), 0, 255);

        /// <summary>Where <paramref name="value"/> falls between two lifts, linearly.</summary>
        private static float Across(int value, int from, int to, float low, float high) =>
            low + ((high - low) * (value - from) / (float)(to - from));
    }
}
