using System;
using System.Collections.Generic;
using System.Globalization;
using NWorld.Map.Models;

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
    /// Named rather than numbered, which is the whole point of them being a dictionary. A
    /// positional list says nothing about itself: <c>["40"]</c> is a number in a slot, and the
    /// only way to know it is a height is to find the code that reads slot zero and hope nothing
    /// else writes there. A name is checkable, survives a component growing a second parameter,
    /// and lets two unrelated things sit on one component without agreeing an order first.
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
        /// What to say about the tile when the pointer is over it. Any component on any layer may
        /// carry one; <see cref="HoverTextOf"/> decides which one is heard.
        /// </summary>
        public const string Hover = "hover";

        /// <summary>
        /// Where a parameter written before they had names ended up: under the index it was
        /// stored at.
        /// <para>
        /// Maps saved by an earlier build hold a plain list, so a value that would now be called
        /// something is sitting under this instead. Only <see cref="Older"/> looks here, and only
        /// for the two parameters that existed back then -- see the warning on it.
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

        /// <summary>One named parameter, or null where the component does not carry it.</summary>
        public static string? Value(IReadOnlyDictionary<string, string>? parameters, string name) =>
            parameters is not null && parameters.TryGetValue(name, out var value) ? value : null;

        /// <summary>
        /// One named parameter as a whole number, or null where it is missing or is not one.
        /// <para>
        /// Answers with null rather than throwing, because everything reading these is drawing a
        /// frame: a parameter somebody has edited by hand into nonsense should cost that tile its
        /// shading, not the map its next frame.
        /// </para>
        /// </summary>
        public static int? Int(IReadOnlyDictionary<string, string>? parameters, string name) =>
            Value(parameters, name) is { } text
            && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
                ? number
                : null;

        /// <summary>
        /// One named parameter, falling back to the slot it would have sat in before parameters
        /// had names.
        /// <para>
        /// <b>Only for the two that predate names</b> -- <see cref="Elevation"/> and
        /// <see cref="Turns"/> -- and it matters that it is only those. Every component in an old
        /// file that carried anything carried it at slot zero, so a fallback offered to <em>any</em>
        /// name would hand that one value out under every name ever asked for: ask an old ground
        /// tile for its hover text and it would answer with its height. Anything added since names
        /// existed has never been positional and must use <see cref="Value"/>.
        /// </para>
        /// </summary>
        public static string? Older(IReadOnlyDictionary<string, string>? parameters, string name)
        {
            if (name != Elevation && name != Turns)
                throw new ArgumentException($"{name} never had a position to fall back to.", nameof(name));

            return Value(parameters, name) ?? Value(parameters, FirstPositional);
        }

        /// <inheritdoc cref="Older"/>
        public static int? OlderInt(IReadOnlyDictionary<string, string>? parameters, string name) =>
            Older(parameters, name) is { } text
            && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
                ? number
                : null;

        /// <summary>
        /// What a tile has to say for itself: the <see cref="Hover"/> parameter of the topmost
        /// component carrying one, or null where none does.
        /// <para>
        /// Topmost means the highest layer, which is the one drawn last and so the one on top --
        /// see <see cref="RenderComponentLayers"/>. That is the only rule that needs no
        /// explaining to whoever is looking at the map: what it tells you about is whatever you
        /// can actually see there. A works built over an iron deposit says works, because the
        /// works is what is in front of you; pull it down and the same tile says iron again,
        /// with nothing having been told.
        /// </para>
        /// <para>
        /// Any component may carry one. Nothing here knows or cares which kinds do, so a thing
        /// that starts labelling itself later needs no change on this side.
        /// </para>
        /// </summary>
        public static string? HoverTextOf(MapTile? tile)
        {
            if (tile is null)
                return null;

            string? found = null;
            var topmost = int.MinValue;

            // Walked rather than sorted: a tile holds a handful of components, and this is asked
            // on every pointer move.
            foreach (var (layer, component) in tile.MapRenderComponents)
            {
                if (layer <= topmost || component is null)
                    continue;

                if (Value(component.Params, Hover) is not { Length: > 0 } text)
                    continue;

                found = text;
                topmost = layer;
            }

            return found;
        }
    }
}
