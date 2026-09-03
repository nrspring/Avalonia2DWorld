using System;
using System.Collections.Generic;
using System.Text;

namespace NWorld.Map.Models
{
    public class MapRenderComponent
    {
        public required Guid ComponentType { get;set;}
        public string[]? Params { get; set;}

        /// <summary>
        /// How far the component is turned when it is drawn, in degrees clockwise. Zero, the
        /// default, is however the component draws itself with no rotation asked for.
        /// <para>
        /// Nothing reads this yet. It is here so that callers can start setting it; a render
        /// function that means to honour it has to apply it itself, and none of them does so
        /// far, so a tile with a rotation on it draws exactly as it did before.
        /// </para>
        /// <para>
        /// Degrees rather than radians because it is set by hand more often than it is
        /// computed, and a double rather than a float to match every other number that comes
        /// down from the panels -- the cast to what Skia wants belongs at the draw site.
        /// </para>
        /// <para>
        /// Not saved. <see cref="Persistence.TileMapFormat"/> writes a component's type and its
        /// parameters and nothing else, so a rotation does not survive a save and reload yet;
        /// carrying it would mean a new version of the tile format.
        /// </para>
        /// <para>
        /// A component is replaced rather than edited once a tile has been published -- see the
        /// remarks on <see cref="MapTile.Clone"/> -- and this field is no exception to that. It
        /// is settable so a component can be built with a rotation, not so one already on the
        /// map can be turned.
        /// </para>
        /// </summary>
        public double Rotation { get; set; }
    }
}
