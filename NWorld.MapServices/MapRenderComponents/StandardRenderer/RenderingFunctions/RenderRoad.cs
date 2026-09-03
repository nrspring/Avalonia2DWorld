using System.Threading.Tasks;
using NWorld.Map.Models;
using SkiaSharp;

namespace NWorld.MapServices.MapRenderComponents.StandardRenderer.RenderingFunctions
{
    /// <summary>
    /// An old stone road: a bed of packed rubble with worn slabs laid along it, its verge eaten
    /// back to the ground, joined up to whatever road or bridge is next door.
    /// <para>
    /// Everything about how it joins, how it is cached and how it is blitted belongs to
    /// <see cref="StoneSpan"/>, which a bridge uses in exactly the same way. What is left here
    /// is what a road looks like, which is <see cref="StoneWork"/> with a verge on it.
    /// </para>
    /// </summary>
    public static class RenderRoad
    {
        /// <summary>
        /// How wide the roadway is, as a fraction of the tile. Wide enough to read as a road at
        /// a glance and narrow enough that the ground it crosses still shows either side, which
        /// is what keeps a road looking laid on the land rather than cut out of it.
        /// </summary>
        private const float Width = 0.56f;

        private static readonly StoneSpan Span = new(0x510AD5u, Draw);

        public static Task Render(TileRenderContext context) => Span.Render(context);

        /// <inheritdoc cref="StoneSpan.Prewarm"/>
        public static Task Prewarm(int tileSize) => Span.Prewarm(tileSize);

        /// <inheritdoc cref="StoneSpan.ClearCache"/>
        public static void ClearCache() => Span.ClearCache();

        /// <summary>Bed, worn verge, then the stones set into what is left.</summary>
        private static void Draw(SKCanvas canvas, int tileSize, int arms, uint seed)
        {
            var half = tileSize / 2f;
            var reach = tileSize * Width / 2f;

            StoneWork.Bedding(canvas, tileSize, StoneWork.Spans(arms, half, reach), seed);
            StoneWork.Verge(canvas, tileSize, arms, half, reach, seed);

            if (tileSize >= StoneWork.MinStoneTileSize)
                StoneWork.Paving(canvas, arms, half, reach, seed);
        }
    }
}
