using SkiaSharp;

namespace Avalonia2DWorld.MapServices.Renderers
{
    /// <summary>
    /// A row of neighbouring tiles being filled with one paint, so a plain costs a rect per row
    /// rather than a rect per tile.
    /// <para>
    /// The same saving <c>TileRuns</c> makes for the standard renderer, which cannot be
    /// borrowed here: that one works from the placements a batch carries, and the renderers
    /// that draw a tile as one flat colour have no batches to put them in. They walk the tiles
    /// themselves and hand them straight to this.
    /// </para>
    /// <para>
    /// Paints are compared by reference, so a caller has to hand back the same instance for
    /// tiles meant to share a run. Choosing from a fixed palette is what makes that true.
    /// </para>
    /// </summary>
    internal struct FillRun(SKCanvas canvas, int tileSize)
    {
        private SKPaint? _paint;
        private int _x;
        private int _y;
        private int _length;

        public void Add(int x, int y, SKPaint paint)
        {
            if (ReferenceEquals(paint, _paint) && y == _y && x == _x + _length)
            {
                _length++;
                return;
            }

            Flush();

            _paint = paint;
            _x = x;
            _y = y;
            _length = 1;
        }

        public void Flush()
        {
            if (_paint is null || _length == 0)
                return;

            canvas.DrawRect(
                SKRect.Create(_x * tileSize, _y * tileSize, _length * tileSize, tileSize),
                _paint);

            _length = 0;
            _paint = null;
        }
    }
}
