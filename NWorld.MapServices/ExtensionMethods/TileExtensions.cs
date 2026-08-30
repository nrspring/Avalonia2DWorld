using System;
using System.Collections.Generic;
using System.Text;

namespace NWorld.MapServices.ExtensionMethods
{
    public static class TileExtensions
    {
        public static void SetBaseGroundType(this Map.Models.MapTile tile, 
                                             Guid baseGroundType,
                                             string[]? parameters = null)
        {
            tile.SetMapRenderComponent(NWorld.Map.Constants.RenderComponentLayers.BaseGround, new Map.Models.MapRenderComponent
            {
                ComponentType = baseGroundType,
                Params = parameters
            });
        }

        public static void SetHoverType(this Map.Models.MapTile tile,
                                        Guid hoverType,
                                        string[]? parameters = null)
        {
            tile.SetMapRenderComponent(NWorld.Map.Constants.RenderComponentLayers.Hover, new Map.Models.MapRenderComponent
            {
                ComponentType = hoverType,
                Params = parameters
            });
        }

        public static void SetUnitType(this Map.Models.MapTile tile,
                                        Guid unitType,
                                        string[]? parameters = null)
        {
            tile.SetMapRenderComponent(NWorld.Map.Constants.RenderComponentLayers.Unit, new Map.Models.MapRenderComponent
            {
                ComponentType = unitType,
                Params = parameters
            });
        }
    }
}
