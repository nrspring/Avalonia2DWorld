using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SkiaSharp;
using NWorld.Map.Models;
using NWorld.MapServices.MapRenderComponents.RenderingFunctions;

namespace NWorld.MapServices.MapRenderComponents
{
    public static class RenderHelperFunctions
    {
        private static readonly Dictionary<Guid, Func<TileRenderContext, Task>> Renderers = new()
        {
            { MapRenderComponentConstants.Empty, RenderEmpty.Render },
            { MapRenderComponentConstants.Grass, RenderGrass.Render },
            { MapRenderComponentConstants.Water, RenderWater.Render },
            { MapRenderComponentConstants.DeepWater, RenderDeepWater.Render },
            { MapRenderComponentConstants.Swamp, RenderSwamp.Render },
            { MapRenderComponentConstants.Desert, RenderDesert.Render },
            { MapRenderComponentConstants.Hover, RenderHover.Render },
            { MapRenderComponentConstants.Selected, RenderSelected.Render },
            { MapRenderComponentConstants.Range, RenderRange.Render },
        };

        public static Task RenderComponent(TileRenderContext context, Guid componentType)
        {
            if (!Renderers.TryGetValue(componentType, out var renderer))
                throw new ArgumentException($"Unknown component type: {componentType}", nameof(componentType));

            return renderer(context);
        }

        /// <summary>
        /// Draws one of a tile's components, taking its type and arguments from the component itself.
        /// </summary>
        public static Task RenderComponent(SKCanvas canvas, RenderFrame frame, int x, int y, MapRenderComponent component) =>
            RenderComponent(
                new TileRenderContext(canvas, frame, x, y, component.Params),
                component.ComponentType);
    }
}
