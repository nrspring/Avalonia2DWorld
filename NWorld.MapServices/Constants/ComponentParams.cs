using System;
using System.Collections.Generic;
using System.Globalization;

namespace NWorld.MapServices.Constants
{
    /// <summary>
    /// What a render component's parameters are called, and how to read them.
    /// <para>
    /// Here rather than in <c>NWorld.Map</c> for the same reason <see cref="RenderComponentLayers"/>
    /// is: the map itself only knows that a component carries some named strings. What those
    /// names <em>mean</em> -- that a ground tile is labelled with its height, that a lone road
    /// is labelled with which way it lies -- is a fact about this world, and belongs with the
    /// rest of them.
    /// </para>
    /// <para>
    /// Named rather than numbered, which is the whole point of the change. A positional list
    /// says nothing about itself: <c>["40"]</c> is a number in a slot, and the only way to know
    /// it is a height is to find the code that reads slot zero and hope nothing else writes
    /// there. A name is checkable, survives a component growing a second parameter, and lets
    /// two unrelated things sit on one component without agreeing an order first.
    /// </para>
    /// </summary>
    public static class ComponentParams
    {
        /// <summary>
        /// How high the ground under a tile stands, as a whole number. Written onto the ground
        /// component and read back by the shading -- see <c>ElevationShade</c> -- and by the
        /// label that writes it over the tile.
        /// </summary>
        public const string Elevation = "elevation";

        /// <summary>
        /// How far a span with no neighbours is turned, in quarter turns. The one thing about a
        /// road or a bridge that is stored rather than worked out, because a tile with nothing
        /// beside it has nothing to take its shape from -- see <c>StoneSpan</c>.
        /// </summary>
        public const string Turns = "turns";

        /// <summary>
        /// Where a parameter written before they had names ended up: under the index it was
        /// stored at.
        /// <para>
        /// Maps saved by an earlier build hold a plain list, and the readers below fall back to
        /// this when a name is missing. That is not a guess -- every component that ever carried
        /// a parameter carried exactly one, and this is it. It costs one dictionary lookup on a
        /// miss and it means nine months of saved worlds still open.
        /// </para>
        /// <para>
        /// Harmless on anything written since: a file with names in it has no key called this,
        /// so the fallback finds nothing and the answer is the same as having not tried.
        /// </para>
        /// </summary>
        private const string FirstPositional = "0";

        /// <summary>One component's worth of parameters, holding a single named value.</summary>
        public static Dictionary<string, string> Of(string name, string value) =>
            new() { [name] = value };

        /// <summary>The elevation parameter for a tile standing this high.</summary>
        public static Dictionary<string, string> ForElevation(int elevation) =>
            Of(Elevation, elevation.ToString(CultureInfo.InvariantCulture));

        /// <summary>The rotation parameter for a span lying this way.</summary>
        public static Dictionary<string, string> ForTurns(int quarters) =>
            Of(Turns, quarters.ToString(CultureInfo.InvariantCulture));

        /// <summary>
        /// One named parameter, or null where the component does not carry it.
        /// <see cref="FirstPositional"/> explains the fallback.
        /// </summary>
        public static string? Value(IReadOnlyDictionary<string, string>? parameters, string name)
        {
            if (parameters is null || parameters.Count == 0)
                return null;

            return parameters.TryGetValue(name, out var value)
                || parameters.TryGetValue(FirstPositional, out value)
                    ? value
                    : null;
        }

        /// <summary>
        /// One named parameter as a whole number, or null where it is missing or is not one.
        /// <para>
        /// Answers with null rather than throwing, because everything reading these is drawing a
        /// frame: a parameter somebody has edited by hand into nonsense should cost that tile
        /// its shading, not the map its next frame.
        /// </para>
        /// </summary>
        public static int? Int(IReadOnlyDictionary<string, string>? parameters, string name) =>
            Value(parameters, name) is { } text
            && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
                ? number
                : null;
    }
}
