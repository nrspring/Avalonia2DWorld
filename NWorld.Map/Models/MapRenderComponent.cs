using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace NWorld.Map.Models
{
    /// <summary>
    /// One thing drawn on one layer of one tile: what to draw, and whatever that kind of thing
    /// needs told.
    /// </summary>
    public class MapRenderComponent
    {
        public required Guid ComponentType { get; set; }

        /// <summary>
        /// The component's own arguments, by name. Null where it takes none, which is what most
        /// of them take.
        /// <para>
        /// Named rather than positional. A list said nothing about itself -- the meaning of a
        /// slot lived only in whichever render function happened to read it -- so a component
        /// could not grow a second parameter without every reader agreeing an order first. What
        /// the names mean is above this library; see <c>ComponentParams</c>.
        /// </para>
        /// <para>
        /// Replaced, never edited, on exactly the same terms as the component that holds it:
        /// <see cref="MapTile.Clone"/> shares components between a tile and its copy, so writing
        /// to this dictionary would write to both -- and to whatever the render thread is
        /// walking at the time.
        /// </para>
        /// </summary>
        public Dictionary<string, string>? Params { get; set; }

        /// <summary>
        /// What a component with no parameters reads as, so that nothing downstream has to keep
        /// checking for null. Shared and empty, hence free.
        /// </summary>
        public static readonly IReadOnlyDictionary<string, string> None =
            ReadOnlyDictionary<string, string>.Empty;
    }
}
