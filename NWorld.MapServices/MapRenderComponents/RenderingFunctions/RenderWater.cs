using System.Threading.Tasks;
using NWorld.Map.Models;

namespace NWorld.MapServices.MapRenderComponents.RenderingFunctions
{
    public static class RenderWater
    {
        // Waves come from context.TimeSeconds. Cache on (tileSize, variant, phase)
        // with a wave function periodic in time, so the last phase wraps into the first.
        public static Task Render(TileRenderContext context)
        {
            throw new System.NotImplementedException();
        }
    }
}
