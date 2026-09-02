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
            tile.SetMapRenderComponent(Constants.RenderComponentLayers.BaseGround, new Map.Models.MapRenderComponent
            {
                ComponentType = baseGroundType,
                Params = parameters
            });
        }

        public static void SetHoverType(this Map.Models.MapTile tile,
                                        Guid hoverType,
                                        string[]? parameters = null)
        {
            tile.SetMapRenderComponent(Constants.RenderComponentLayers.Hover, new Map.Models.MapRenderComponent
            {
                ComponentType = hoverType,
                Params = parameters
            });
        }

        public static void SetResourceType(this Map.Models.MapTile tile,
                                           Guid resourceType,
                                           string[]? parameters = null)
        {
            tile.SetMapRenderComponent(Constants.RenderComponentLayers.Resource, new Map.Models.MapRenderComponent
            {
                ComponentType = resourceType,
                Params = parameters
            });
        }

        public static void SetEnhancementType(this Map.Models.MapTile tile,
                                              Guid enhancementType,
                                              string[]? parameters = null)
        {
            tile.SetMapRenderComponent(Constants.RenderComponentLayers.Enhancement, new Map.Models.MapRenderComponent
            {
                ComponentType = enhancementType,
                Params = parameters
            });
        }

        public static void SetUnitType(this Map.Models.MapTile tile,
                                        Guid unitType,
                                        string[]? parameters = null)
        {
            tile.SetMapRenderComponent(Constants.RenderComponentLayers.Unit, new Map.Models.MapRenderComponent
            {
                ComponentType = unitType,
                Params = parameters
            });
        }

        public static void SetElevationLabelType(this Map.Models.MapTile tile,
                                        Guid elevationLabelType,
                                        string[]? parameters = null)
        {
            tile.SetMapRenderComponent(Constants.RenderComponentLayers.ElevationLabel, new Map.Models.MapRenderComponent
            {
                ComponentType = elevationLabelType,
                Params = parameters
            });
        }
    }
}
