using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;

namespace NWorld.Map.Models
{
    public class MapTile
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int Elevation { get; set; }
        public Dictionary<int, MapRenderComponent> MapRenderComponents { get; set; } = [];

        public void SetMapRenderComponent(int layer, MapRenderComponent component)
        {
            MapRenderComponents[layer] = component;
        }

        /// <summary>
        /// A copy that can be edited without disturbing this one. How <see cref="TileMap"/>
        /// changes a tile: the renderer may be walking the original on another thread, and
        /// writing to a <see cref="Dictionary{TKey, TValue}"/> mid-enumeration throws.
        /// </summary>
        /// <remarks>
        /// The component dictionary is copied but the components in it are shared, which is
        /// safe only because a component is always <b>replaced</b> and never edited in place
        /// -- see the setters on <c>TileExtensions</c>. Start mutating a component's
        /// <see cref="MapRenderComponent.Params"/> and this has to deep-copy them too.
        /// </remarks>
        public MapTile Clone() => new()
        {
            X = X,
            Y = Y,
            Elevation = Elevation,
            MapRenderComponents = new Dictionary<int, MapRenderComponent>(MapRenderComponents),
        };
    }
}
