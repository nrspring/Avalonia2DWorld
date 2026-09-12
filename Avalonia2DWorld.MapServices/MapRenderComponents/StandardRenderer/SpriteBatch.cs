using System;
using SkiaSharp;

namespace Avalonia2DWorld.MapServices.MapRenderComponents.StandardRenderer
{
    /// <summary>
    /// The buffers behind a <c>DrawAtlas</c>, and the arithmetic of sizing them.
    /// <para>
    /// SkiaSharp 2.88 has no span overload, so DrawAtlas takes whole arrays that must be
    /// exactly as long as the draw. Sized to the sprite count directly, that reallocates
    /// whenever the count changes by one, which is precisely what panning does as a partial
    /// column scrolls into view -- and at a zoomed-out screen these run to near a megabyte
    /// each, so every such frame put two large-object allocations on the heap.
    /// </para>
    /// <para>
    /// Rounding the length up to a block instead means panning crosses a boundary only rarely,
    /// while a zoom still resizes promptly, because the length must match the rounded size
    /// exactly rather than merely fit inside it. The slack past the sprite count is drawn too
    /// and so is given a zero scale, which collapses the quad to a point and rasterises
    /// nothing.
    /// </para>
    /// <para>
    /// None of that is interesting to a render function, and all of it is easy to get subtly
    /// wrong in a second copy -- forget the zero scale and the slack draws the first sprite a
    /// thousand times over the map's top-left corner. So it lives here once, and a render
    /// function reserves, fills and draws.
    /// </para>
    /// <para>
    /// The buffers are per-thread and shared between every component that draws from an atlas,
    /// which is sound only because a reservation lives until the draw that consumes it:
    /// <see cref="Reserve"/>, fill, <see cref="Draw"/>, and nothing in between. Renderers run
    /// one after another on a frame's thread, so that holds -- but a component that reserved
    /// twice before drawing once would find its first reservation gone.
    /// </para>
    /// </summary>
    internal static class SpriteBatch
    {
        /// <summary>
        /// Granularity of the buffers. Large enough that panning rarely crosses a boundary,
        /// small enough that the slack is nothing beside a screenful of tiles.
        /// </summary>
        private const int CapacityBlock = 1024;

        [ThreadStatic] private static SKRect[]? Sprites;
        [ThreadStatic] private static SKRotationScaleMatrix[]? Transforms;

        /// <summary>
        /// Buffers with room for <paramref name="quads"/> sprites, to be filled from 0 to
        /// <paramref name="quads"/>. Longer than asked for; everything past the count is the
        /// caller's to ignore and <see cref="Draw"/>'s to collapse.
        /// </summary>
        public static (SKRect[] Sprites, SKRotationScaleMatrix[] Transforms) Reserve(int quads)
        {
            var capacity = (quads + CapacityBlock - 1) / CapacityBlock * CapacityBlock;

            var sprites = Sprites;
            var transforms = Transforms;

            if (sprites is null || sprites.Length != capacity)
                Sprites = sprites = new SKRect[capacity];

            if (transforms is null || transforms.Length != capacity)
                Transforms = transforms = new SKRotationScaleMatrix[capacity];

            return (sprites, transforms);
        }

        /// <summary>
        /// Collapses the slack and issues the draw. Takes the buffers back from the caller
        /// rather than reading its own, so that a mismatched pair fails to compile rather than
        /// drawing something strange.
        /// </summary>
        public static void Draw(
            SKCanvas canvas,
            SKImage atlas,
            SKRect[] sprites,
            SKRotationScaleMatrix[] transforms,
            int quads,
            SKPaint paint)
        {
            for (var i = quads; i < sprites.Length; i++)
            {
                sprites[i] = SKRect.Create(0, 0, 1, 1);
                transforms[i] = new SKRotationScaleMatrix(0f, 0f, 0f, 0f);
            }

            canvas.DrawAtlas(atlas, sprites, transforms, paint);
        }
    }
}
