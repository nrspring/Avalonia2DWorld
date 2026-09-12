using System.Threading.Tasks;
using Avalonia2DWorld.Map.Models;

namespace Avalonia2DWorld.MapServices.MapRenderComponents.StandardRenderer.RenderingFunctions
{
    /// <summary>
    /// Draws nothing. Empty is the default component type -- the all-zero Guid, which is what
    /// an uninitialised <see cref="MapRenderComponent.ComponentType"/> holds -- and it means
    /// there is nothing on this layer.
    /// <para>
    /// The renderer drops these before they ever reach a batch, so in the normal course this is
    /// never called. It exists so that Empty can still do the one thing every component type
    /// must: a caller reaching <see cref="RenderHelperFunctions"/> directly gets a no-op rather
    /// than an exception, which is the right answer to "draw nothing here".
    /// </para>
    /// </summary>
    public static class RenderEmpty
    {
        public static Task Render(TileRenderContext context) => Task.CompletedTask;
    }
}
