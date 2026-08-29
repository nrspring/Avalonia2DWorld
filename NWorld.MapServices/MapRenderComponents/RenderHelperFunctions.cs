using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using SkiaSharp;
using NWorld.MapServices.MapRenderComponents.RenderingFunctions;

namespace NWorld.MapServices.MapRenderComponents
{
    public static class RenderHelperFunctions
    {
        private static readonly Dictionary<Guid, Func<SKCanvas, int, int, int, Task>> Renderers = new()
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

        public static Task RenderComponent(SKCanvas canvas, int tileSize, int x, int y, Guid componentType)
        {
            if (!Renderers.TryGetValue(componentType, out var renderer))
                throw new ArgumentException($"Unknown component type: {componentType}", nameof(componentType));

            return renderer(canvas, tileSize, x, y);
        }
    }
}
