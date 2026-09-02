using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SkiaSharp;
using NWorld.Map.Models;
using NWorld.MapServices.MapRenderComponents.RenderingFunctions;

namespace NWorld.MapServices.MapRenderComponents
{
    public static class RenderHelperFunctions
    {
        /// <summary>
        /// What a component type can do. <see cref="Prewarm"/> and <see cref="ClearCache"/> are
        /// null for the ones that hold nothing between frames.
        /// </summary>
        private sealed record Component(
            Func<TileRenderContext, Task> Render,
            Func<int, Task>? Prewarm = null,
            Action? ClearCache = null);

        private static readonly Dictionary<Guid, Component> Renderers = new()
        {
            { MapRenderComponentConstants.Empty, new(RenderEmpty.Render) },
            { MapRenderComponentConstants.Grass, new(RenderGrass.Render, RenderGrass.Prewarm, RenderGrass.ClearCache) },
            { MapRenderComponentConstants.Water, new(RenderWater.Render, RenderWater.Prewarm, RenderWater.ClearCache) },
            { MapRenderComponentConstants.DeepWater, new(RenderDeepWater.Render, RenderDeepWater.Prewarm, RenderDeepWater.ClearCache) },
            { MapRenderComponentConstants.ShallowWater, new(RenderShallowWater.Render, RenderShallowWater.Prewarm, RenderShallowWater.ClearCache) },
            { MapRenderComponentConstants.Swamp, new(RenderSwamp.Render, RenderSwamp.Prewarm, RenderSwamp.ClearCache) },
            { MapRenderComponentConstants.Desert, new(RenderDesert.Render, RenderDesert.Prewarm, RenderDesert.ClearCache) },
            { MapRenderComponentConstants.Iron, new(RenderIron.Render, RenderIron.Prewarm, RenderIron.ClearCache) },
            { MapRenderComponentConstants.Wood, new(RenderWood.Render, RenderWood.Prewarm, RenderWood.ClearCache) },
            { MapRenderComponentConstants.Oil, new(RenderOil.Render, RenderOil.Prewarm, RenderOil.ClearCache) },
            { MapRenderComponentConstants.Sulphur, new(RenderSulphur.Render, RenderSulphur.Prewarm, RenderSulphur.ClearCache) },
            { MapRenderComponentConstants.Stone, new(RenderStone.Render, RenderStone.Prewarm, RenderStone.ClearCache) },
            { MapRenderComponentConstants.Hover, new(RenderHover.Render, RenderHover.Prewarm, RenderHover.ClearCache) },
            { MapRenderComponentConstants.Selected, new(RenderSelected.Render, RenderSelected.Prewarm, RenderSelected.ClearCache) },
            { MapRenderComponentConstants.Range, new(RenderRange.Render) },
            { MapRenderComponentConstants.ElevationLabel, new(RenderElevationLabel.Render, RenderElevationLabel.Prewarm, RenderElevationLabel.ClearCache) },
        };

        /// <summary>
        /// Draws one component across every tile in <paramref name="context"/> that carries it.
        /// </summary>
        public static Task RenderComponent(TileRenderContext context, Guid componentType) =>
            Lookup(componentType, nameof(componentType)).Render(context);

        /// <summary>
        /// Draws one component on one tile. A convenience for tests and one-off renders; a
        /// real frame should go through <see cref="Renderers.StandardRenderer"/>, which
        /// batches, rather than calling this in a loop.
        /// </summary>
        public static Task RenderComponent(SKCanvas canvas, RenderFrame frame, int x, int y, MapRenderComponent component) =>
            RenderComponent(
                new TileRenderContext(canvas, frame, x, y, component.Params),
                component.ComponentType);

        /// <summary>
        /// Builds the caches the named components need at <paramref name="tileSize"/>, off the
        /// calling thread and all at once.
        /// <para>
        /// Named rather than all of them, because a cache is not cheap: a water phase set is an
        /// animation and runs to tens of megabytes, and there is no sense building one for a
        /// map with no water on it. Pass the component types the view actually contains.
        /// </para>
        /// </summary>
        public static Task Prewarm(int tileSize, IEnumerable<Guid> componentTypes)
        {
            ArgumentNullException.ThrowIfNull(componentTypes);

            var warming = componentTypes
                .Distinct()
                .Select(type => Lookup(type, nameof(componentTypes)).Prewarm)
                .Where(prewarm => prewarm is not null)
                .Select(prewarm => prewarm!(tileSize));

            return Task.WhenAll(warming);
        }

        /// <summary>
        /// Builds every component's cache at <paramref name="tileSize"/>. Convenient, but it
        /// will build things the map may never draw -- prefer the overload that takes the
        /// component types actually in view.
        /// </summary>
        public static Task PrewarmAll(int tileSize) => Prewarm(tileSize, Renderers.Keys);

        /// <summary>
        /// Drops every component's cached textures. Worth calling to release memory after a
        /// long zoom session. Not safe to call while a frame is in flight -- it disposes
        /// images that frame may still be drawing from.
        /// <para>
        /// This exists so that adding a component with a cache does not silently leave it out
        /// of everyone's teardown: the table below is the one place that has to know.
        /// </para>
        /// </summary>
        public static void ClearCaches()
        {
            foreach (var component in Renderers.Values)
                component.ClearCache?.Invoke();
        }

        private static Component Lookup(Guid componentType, string parameterName) =>
            Renderers.TryGetValue(componentType, out var component)
                ? component
                : throw new ArgumentException($"Unknown component type: {componentType}", parameterName);
    }
}
