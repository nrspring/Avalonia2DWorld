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
    }
}
