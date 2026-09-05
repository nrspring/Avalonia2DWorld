using System;
using System.Collections.Generic;
using System.Globalization;
using NWorld.Map.Models;
using NWorld.MapServices.Constants;
using SkiaSharp;

namespace NWorld.MapServices.MapRenderComponents.StandardRenderer
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
        // The bands themselves live in Elevations, where the generators can see them too.
        // Named here only to keep the table below readable.
        private const int SeaLevel = Elevations.Sea;
        private const int Ground = Elevations.Flat;
        private const int HillsTo = Elevations.HillsTo;
        private const int MountainsFrom = Elevations.MountainsFrom;
        private const int MountainsTo = Elevations.MountainsTo;

        // The lift at each end of each band, before the screen rolls it off. Banded rather
        // than one straight line from 1 to 30 so that the bands are legible on the map: spread
        // evenly, a hill would differ from flat ground by three percent and the whole of the
        // hill country would look like a rounding error. The gap between the top of the hills
        // and the foot of the mountains is deliberate -- it is what makes a treeline you can
        // see.
        //
        // The hills start well clear of nothing for the same reason. A band that fades in from
        // zero spends its first few heights indistinguishable from the flat land it is meant to
        // stand out of, and most hill country on a map is its first few heights -- so the floor
        // is a step up onto the band rather than the bottom of a ramp into it, and the climb
        // across the band is what is left over.
        private const float HillsFloor = 32f;
        private const float HillsCeiling = 60f;
        private const float MountainsFloor = 78f;
        private const float MountainsCeiling = 205f;

        /// <summary>
        /// How much of the lift each channel gets at the foot of the mountains.
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
        /// How much of the lift each channel gets on the hills.
        /// <para>
        /// Warmer than the mountains, and this is what tells hill country from the flat at a
        /// glance. Lifted by the same near-green as a mountain foot, a hill is only a paler
        /// version of the field beside it -- and pale enough to read on its own is most of the
        /// way to snow before the land is out of the lowlands. Leaning the lift towards red
        /// instead dries the ground it lands on, upland turf and bracken rather than pasture,
        /// so the hills separate by colour as well as by tone and need far less tone to do it.
        /// </para>
        /// </summary>
        private static readonly (float Red, float Green, float Blue) HillsTint = (1f, 0.82f, 0.42f);

        /// <summary>
        /// What the tint becomes at the very top of the scale.
        /// <para>
        /// Neutral, which is snow. Holding blue back is what keeps a hillside green, and it is
        /// exactly the wrong thing at thirty: a peak is bare rock and ice, and it has no
        /// business being the same colour as the meadow it stands over. So the tint is walked
        /// from one to the other across the mountain band -- green at the foot of a range,
        /// white at the summit, and everything between shading through.
        /// </para>
        /// </summary>
        private static readonly (float Red, float Green, float Blue) Snow = (1f, 1f, 1f);

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
        private static int ElevationOf(IReadOnlyDictionary<string, string> parameters) =>
            ComponentParams.Int(parameters, ComponentParams.Elevation) is { } elevation
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
                    <= HillsTo => Across(
                        elevation, Elevations.HillsFrom, HillsTo, HillsFloor, HillsCeiling),
                    _ => Across(elevation, MountainsFrom, MountainsTo, MountainsFloor, MountainsCeiling),
                };

                // Left null rather than made into a paint that screens black, which is the
                // identity and so a draw that does nothing. This is what makes the pass free on
                // flat land and on the sea.
                if (lift <= 0f)
                    continue;

                // Nothing below the mountains, then all the way over across the band.
                var snow = elevation < MountainsFrom
                    ? 0f
                    : (float)(elevation - MountainsFrom) / (MountainsTo - MountainsFrom);

                // The hills keep their own colour the whole way up; the mountains start from
                // theirs and walk to snow.
                var tint = elevation <= HillsTo ? HillsTint : Tint;

                var red = tint.Red + ((Snow.Red - tint.Red) * snow);
                var green = tint.Green + ((Snow.Green - tint.Green) * snow);
                var blue = tint.Blue + ((Snow.Blue - tint.Blue) * snow);

                paints[elevation] = new SKPaint
                {
                    // Opaque, which is what keeps the lift whole: any less alpha would be
                    // unpremultiplied on the way in and take the lift down with it.
                    Color = new SKColor(Level(lift * red), Level(lift * green), Level(lift * blue)),
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
