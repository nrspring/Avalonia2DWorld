using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia2DWorld.Map.Models;
using SkiaSharp;
using static Avalonia2DWorld.MapServices.MapRenderComponents.StandardRenderer.RenderNoise;

namespace Avalonia2DWorld.MapServices.MapRenderComponents.StandardRenderer.RenderingFunctions
{
    /// <summary>
    /// Draws a grass tile at map coordinate (x, y), i.e. pixel (x * tileSize, y * tileSize).
    /// <para>
    /// Everything is deterministic in (x, y): a tile looks the same on every repaint and
    /// across runs. A tile is drawn in two parts -- a pre-rendered texture chosen from a pool
    /// of variants, then a broad tone taken from a coarse map-space noise field. Drawing a
    /// tile that has been seen before is a blit plus two rects.
    /// </para>
    /// <para>
    /// Keeping the grid invisible is the whole difficulty here, and it drives most of the
    /// decisions below: variant textures wrap at their own edges, they carry no feature large
    /// enough to be noticed where two different variants meet, the noise octaves are shifted
    /// off the tile edge, and all the broad variation is left to the tone field, which is
    /// continuous across tiles by construction.
    /// </para>
    /// <para>
    /// The variants for one tile size live together in a single atlas image rather than one
    /// image each -- see <see cref="VariantAtlas"/> -- and a whole batch of tiles is drawn
    /// with one DrawAtlas and one covering rect per run, rather than two draws per tile. Both
    /// are arrangement decisions a raster canvas is largely indifferent to and a GPU-backed
    /// one is not.
    /// </para>
    /// </summary>
    public static class RenderGrass
    {
        private const uint Seed = 0x6A5F1D3Bu;

        // Noise lattice sizes, in cells per tile. Each field wraps at the tile edge, so a
        // tile is seamless against a copy of itself -- but two *different* variants still
        // disagree along a shared edge, and at low frequencies that disagreement reads as a
        // visible grid. So the ground carries only fine detail, and everything broad enough
        // to notice is left to the map-space tone field, which is continuous across tiles.
        private const int GroundPeriod = 10;
        private const int PatchPeriod = 8;

        private const float ToneStrength = 42f;

        // Lattice sizes for the tone field, in cells across its period: the broad light
        // and dark stretches come out roughly 10 tiles across, the dry ones roughly 20.
        private const int ToneLattice = 12;
        private const int DryLattice = 6;

        // A one-pixel skirt around each cell in the atlas, holding what wraps around from the
        // opposite edge, so that sampling which strays past a cell finds the right pixels
        // rather than the next variant's.
        //
        // This was insurance while tiles were blitted with DrawImage, which constrains reads
        // to the source rect -- a map drawn with and without the skirt came out bit-identical
        // under sub-pixel panning and at 1.25x, 1.5x and 2x display scaling. DrawAtlas gives
        // no such guarantee: sprite padding is the caller's job there, and the artifact it
        // prevents is a faint bright grid over the whole map, which is both the one thing this
        // file exists to avoid and a thoroughly confusing one to track back to here.
        private const int Gutter = 1;

        // Atlas ceilings. The edge cap keeps us inside every sane GL_MAX_TEXTURE_SIZE; the
        // byte caps bound one zoom level and the cache as a whole. All three are sized for a
        // discrete GPU with memory to spare -- on a machine without one they want to come
        // down by roughly an order of magnitude, together with the pools in DetailFor.
        private const int MaxAtlasEdge = 4096;
        private const long MaxAtlasBytes = 64L * 1024 * 1024;
        private const long MaxCacheBytes = 256L * 1024 * 1024;

        // Lazy, not the atlas itself: ConcurrentDictionary's factory can run on more than one
        // thread and keep only one result, which with Prewarm running alongside a repaint
        // would mean two full pool builds, a leaked image, and a byte count that never
        // matches what is actually held. A Lazy that loses the race is simply never forced.
        private static readonly ZoomLevelCache<VariantAtlas> Atlases = new(MaxCacheBytes, BuildAtlas);

        /// <summary>
        /// The broad light and dark stretches. Everything in a variant is fine detail, so this
        /// is what keeps a meadow from reading as a pool of tiles -- see <see cref="MapOverlayField"/>.
        /// </summary>
        private static readonly MapOverlayField ToneField = new(static (u, v) =>
        {
            var n = PeriodicFbm(u, v, ToneLattice, 4, Seed ^ 0x70A5u);
            var dry = PeriodicFbm(u, v, DryLattice, 2, Seed ^ 0xD47Au);
            var level = MapOverlayField.Level(n, ToneStrength);

            return new SKColor(
                (byte)Clamp255(level + dry * 10f),
                (byte)Clamp255(level + 2f),
                (byte)Clamp255(level - dry * 12f));
        });

        // Blur is the most expensive thing in a variant build and a mask filter is immutable
        // once made, so clumps wanting a similar sigma share one instead of each allocating.
        private static readonly ConcurrentDictionary<int, SKMaskFilter> BlurFilters = new();

        // Nearest sampling: the atlas is always blitted at 1:1, so filtering would only
        // soften it. Read-only after construction, hence safe to share across draws.
        private static readonly SKPaint BlitPaint = new()
        {
            FilterQuality = SKFilterQuality.None,
            IsAntialias = false,
        };

        private static readonly SKColor GrassDark = new(0x39, 0x63, 0x2C);
        private static readonly SKColor GrassMid = new(0x5C, 0x8B, 0x3B);
        private static readonly SKColor GrassLight = new(0x7C, 0xAA, 0x50);
        private static readonly SKColor GrassDry = new(0x8E, 0x9C, 0x4C);
        private static readonly SKColor BladeShadow = new(0x2C, 0x4A, 0x22);
        private static readonly SKColor BladeLight = new(0x9E, 0xC6, 0x66);
        private static readonly SKColor FlowerPale = new(0xE8, 0xE4, 0xB8);

        /// <summary>
        /// How much work one tile is worth at a given size, and how many different tiles the
        /// view is worth holding.
        /// </summary>
        /// <param name="Variants">
        /// Size of the variant pool. Each variant is also mirrored, so the ground repeats
        /// after twice this many tiles. The pool peaks in the middle of the zoom range rather
        /// than at the top of it: repetition is what you notice when a screenful is thousands
        /// of tiles, whereas by the time a tile is 128px only a few dozen fit on a monitor,
        /// where a big pool buys nothing and costs a big atlas.
        /// </param>
        /// <param name="Tufted">
        /// Whether blades are gathered into clumps with shadows under them and the odd flower,
        /// or the tile is just a mottled mat with a few strays over it.
        /// </param>
        /// <param name="Mirrored">
        /// Whether each variant also gets a mirrored cell, doubling the variety for the cost of
        /// doubling the atlas. Worth it while a screenful is thousands of tiles; above that the
        /// pool already outnumbers what is on screen, and the second copy buys nothing but a
        /// bigger atlas to compose on the first paint.
        /// </param>
        private readonly record struct DetailLevel(
            int Variants,
            int GroundOctaves,
            int PatchOctaves,
            bool Tufted,
            bool Mirrored);

        private static DetailLevel DetailFor(int tileSize) => tileSize switch
        {
            // A blade is about a pixel here, so clumping it and casting a shadow under it only
            // muddies the result -- a mottled mat with a little speckle reads better. The
            // octave counts are cut for the same reason rather than to save time: at this size
            // a third octave's cells are already under a pixel across, so it would contribute
            // aliasing and not detail.
            < 16 => new DetailLevel(Variants: 64, GroundOctaves: 2, PatchOctaves: 1, Tufted: false, Mirrored: true),

            // Full treatment from here up.
            < 32 => new DetailLevel(Variants: 192, GroundOctaves: 3, PatchOctaves: 2, Tufted: true, Mirrored: true),

            // From 64px a screenful is under a thousand tiles, so the pool already covers it
            // several times over and the mirror stops paying for its half of the atlas.
            < 64 => new DetailLevel(Variants: 256, GroundOctaves: 3, PatchOctaves: 2, Tufted: true, Mirrored: true),
            < 128 => new DetailLevel(Variants: 128, GroundOctaves: 3, PatchOctaves: 2, Tufted: true, Mirrored: false),
            _ => new DetailLevel(Variants: 64, GroundOctaves: 3, PatchOctaves: 2, Tufted: true, Mirrored: false),
        };

        /// <summary>
        /// Draws every grass tile in the batch. Grass does not animate, so
        /// <see cref="TileRenderContext.TimeSeconds"/> is ignored and the atlas stays cached
        /// across frames.
        /// <para>
        /// Two draws for the whole batch where there used to be two per tile: one DrawAtlas
        /// for the ground, and one covering rect per run of horizontally adjacent tiles for
        /// the tone. The tone shader is anchored to the canvas rather than to the rect being
        /// filled, so a run produces the same pixels as the tiles it replaces.
        /// </para>
        /// </summary>
        public static Task Render(TileRenderContext context)
        {
            var canvas = context.Canvas;
            var tileSize = context.TileSize;
            var tiles = context.Tiles;

            if (canvas is null || tileSize <= 0 || tiles.Length == 0)
                return Task.CompletedTask;

            var atlas = Atlases.Get(tileSize);

            BlitTiles(canvas, atlas, tiles, tileSize);
            PaintMeadowTone(canvas, tiles, tileSize);

            // Last, over the finished ground -- see ElevationShade. Grass draws in a way
            // nothing else here does, and this is the one part of it that is not its own.
            ElevationShade.Apply(context);

            return Task.CompletedTask;
        }

        /// <summary>
        /// Lays every tile's texture down in one DrawAtlas call.
        /// <para>
        /// Note that DrawAtlas, unlike DrawImage with a source rect, does not constrain
        /// sampling to the sprite -- which is what finally makes <see cref="Gutter"/> earn its
        /// keep rather than being insurance.
        /// </para>
        /// </summary>
        private static void BlitTiles(SKCanvas canvas, VariantAtlas atlas, ReadOnlySpan<TilePlacement> tiles, int tileSize)
        {
            var count = tiles.Length;
            var (sprites, transforms) = SpriteBatch.Reserve(count);

            for (var i = 0; i < count; i++)
            {
                var tile = tiles[i];

                // One hash picks both the variant and which way round it faces, since the
                // mirror is just the odd cell of the pair.
                var cell = (int)(Hash(tile.X, tile.Y, Seed) % (uint)atlas.Cells);

                sprites[i] = atlas.Source(cell);
                transforms[i] = new SKRotationScaleMatrix(1f, 0f, tile.X * tileSize, tile.Y * tileSize);
            }

            SpriteBatch.Draw(canvas, atlas.Image, sprites, transforms, count, BlitPaint);
        }

        /// <summary>
        /// Shades the tiles against a coarse noise field sampled in map space, one texel per
        /// tile and filtered smoothly, so the map gets lighter and darker stretches that run
        /// across tile boundaries instead of stopping at them.
        /// <para>
        /// Drawn as one rect per run of horizontally adjacent tiles. The run is found by
        /// walking the batch in the order the renderer collected it and extending while the
        /// next tile is the previous one's right-hand neighbour; a batch that arrives in
        /// reading order collapses to about one rect per row, and one that arrives shuffled
        /// still draws correctly, just with more rects.
        /// </para>
        /// <para>
        /// This is the draw a GPU backend cannot make cheap, and coalescing is the reason it
        /// no longer matters much. Overlay is one of Skia's advanced blend modes, and unless
        /// the driver advertises advanced blend equations -- the ANGLE/D3D path a Windows app
        /// usually lands on does not -- the GPU backend implements it by reading the
        /// destination back. It survives because the obvious replacement does not work: Skia
        /// branches Overlay on the *destination*, so no fixed source reproduces it with
        /// coefficient modes. Multiplying by min(2s, 1) and then screening with max(2s - 1, 0)
        /// -- exact if the branch were on the source -- comes out a mean of 10/255 and a peak
        /// of 87/255 away from it across the grass palette, which is a plainly different
        /// ground.
        /// </para>
        /// </summary>
        private static void PaintMeadowTone(SKCanvas canvas, ReadOnlySpan<TilePlacement> tiles, int tileSize) =>
            TileRuns.Fill(canvas, tiles, tileSize, ToneField.PaintFor(tileSize));

        /// <summary>
        /// Builds the atlas for <paramref name="tileSize"/> off the calling thread, so a zoom
        /// control can have the next level ready before the view arrives at it. Drawing at a
        /// size that was never prewarmed still works; it just pays for the build in the frame
        /// that first asks for it.
        /// </summary>
        public static Task Prewarm(int tileSize) => Atlases.Prewarm(tileSize);

        /// <summary>
        /// Drops every cached atlas and tone paint; they are rebuilt on the next draw. Worth
        /// calling if the palette changes, or to release memory after a long zoom session.
        /// Not safe to call while a frame is in flight -- it disposes images that frame may
        /// still be drawing from.
        /// </summary>
        public static void ClearCache()
        {
            Atlases.Clear();
            ToneField.Clear();
        }

        // ---- variant atlas -------------------------------------------------------

        /// <summary>
        /// Every variant for one tile size, packed into a single image on a padded grid.
        /// <para>
        /// One image per variant is the obvious arrangement, and on a raster canvas it is a
        /// perfectly good one: a blit is a memcpy and it makes no difference which buffer it
        /// came from. On a GPU-backed canvas it is close to the worst arrangement available.
        /// Neighbouring tiles nearly always draw different variants, so the bound texture
        /// changes between them, no two consecutive tiles can be batched, and a zoomed-out
        /// frame becomes five figures of draw calls. Sharing one texture across the whole
        /// pool lets Skia merge long runs of tiles into a handful of batches, and bounds the
        /// working set to one texture per zoom level rather than hundreds.
        /// </para>
        /// </summary>
        private sealed class VariantAtlas : IZoomLevelResource
        {
            public required SKImage Image { get; init; }

            /// <summary>
            /// Drawable cells, which is twice the variant count: a variant and its mirror sit
            /// in the atlas as two cells rather than one cell drawn under a flipped canvas.
            /// DrawAtlas positions each sprite with a rotation and a scale, and a reflection
            /// is neither, so the flip has to be baked. It is close to free to bake -- a
            /// mirrored blit of an image that is already built -- and it buys back the
            /// save/scale/restore that every mirrored tile used to pay.
            /// </summary>
            public required int Cells { get; init; }

            public required int Columns { get; init; }
            public required int TileSize { get; init; }

            /// <summary>Grid pitch: the tile plus its gutter on either side.</summary>
            public required int Cell { get; init; }

            public required long Bytes { get; init; }

            public SKRect Source(int cell)
            {
                var column = cell % Columns;
                var row = cell / Columns;
                return SKRect.Create(
                    column * Cell + Gutter,
                    row * Cell + Gutter,
                    TileSize,
                    TileSize);
            }

            public void Dispose() => Image.Dispose();
        }

        private static VariantAtlas BuildAtlas(int tileSize)
        {
            var detail = DetailFor(tileSize);
            var cell = tileSize + Gutter * 2;

            // Trim the pool to what fits rather than refusing to draw. The caps are on cells,
            // of which a variant needs two, and they only bite at the large tile sizes where
            // the pool is deliberately small to begin with.
            var perEdge = Math.Max(1, MaxAtlasEdge / cell);
            var byBytes = Math.Max(1, (int)(MaxAtlasBytes / (4L * cell * cell)));
            var perVariant = detail.Mirrored ? 2 : 1;
            var cells = Math.Min(detail.Variants * perVariant, Math.Min(perEdge * perEdge, byBytes));
            var variants = Math.Max(1, cells / perVariant);
            cells = variants * perVariant;

            var columns = Math.Min(perEdge, (int)Math.Ceiling(Math.Sqrt(cells)));
            var rows = (cells + columns - 1) / columns;

            // A variant is an independent noise field plus a few hundred stroked paths, so the
            // pool builds across every core the machine has. This is what makes a pool of this
            // size affordable at all: it is the entire first-paint cost of a zoom level, and
            // built one at a time it would be a visible stall on every zoom step.
            var tiles = new SKImage[variants];
            Parallel.For(0, variants, v => tiles[v] = BuildTile(tileSize, v));

            var info = new SKImageInfo(columns * cell, rows * cell, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var surface = SKSurface.Create(info);
            var canvas = surface.Canvas;
            canvas.Clear(SKColors.Transparent);

            for (var v = 0; v < variants; v++)
            {
                PlaceCell(canvas, tiles[v], v * perVariant, columns, cell, tileSize, mirrored: false);

                if (detail.Mirrored)
                    PlaceCell(canvas, tiles[v], v * perVariant + 1, columns, cell, tileSize, mirrored: true);

                tiles[v].Dispose();
            }

            return new VariantAtlas
            {
                Image = surface.Snapshot(),
                Cells = cells,
                Columns = columns,
                TileSize = tileSize,
                Cell = cell,
                Bytes = 4L * info.Width * info.Height,
            };
        }

        /// <summary>
        /// Writes one variant into one atlas cell, mirrored or not, with its gutter filled.
        /// </summary>
        private static void PlaceCell(
            SKCanvas canvas,
            SKImage tile,
            int index,
            int columns,
            int cell,
            int tileSize,
            bool mirrored)
        {
            var originX = index % columns * cell;
            var originY = index / columns * cell;

            canvas.Save();
            canvas.ClipRect(SKRect.Create(originX, originY, cell, cell));

            if (mirrored)
            {
                // Reflect about the cell's right edge, so local x runs back across the cell.
                canvas.Translate(originX + Gutter + tileSize, originY + Gutter);
                canvas.Scale(-1f, 1f);
            }
            else
            {
                canvas.Translate(originX + Gutter, originY + Gutter);
            }

            // The gutter is filled by wrapping and not by smearing the edge pixels: a variant
            // is built to tile against itself, so what belongs just off its left edge is
            // exactly what sits inside its right one. Eight of these nine copies are clipped
            // down to a one-pixel sliver, so the cost is nominal.
            for (var dx = -tileSize; dx <= tileSize; dx += tileSize)
            {
                for (var dy = -tileSize; dy <= tileSize; dy += tileSize)
                    canvas.DrawImage(tile, dx, dy);
            }

            canvas.Restore();
        }

        // ---- tone field ----------------------------------------------------------

        // ---- one variant ---------------------------------------------------------

        private static SKImage BuildTile(int tileSize, int variant)
        {
            var seed = Hash(variant, variant * 31 + 7, Seed);
            var detail = DetailFor(tileSize);

            var info = new SKImageInfo(tileSize, tileSize, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var surface = SKSurface.Create(info);
            var canvas = surface.Canvas;

            PaintGround(canvas, tileSize, seed, detail);
            PaintBlades(canvas, tileSize, seed, detail);

            if (detail.Tufted)
                PaintDetails(canvas, tileSize, seed);

            return surface.Snapshot();
        }

        /// <summary>Lays down the mottled ground out of a few octaves of value noise.</summary>
        private static void PaintGround(SKCanvas canvas, int tileSize, uint seed, DetailLevel detail)
        {
            // Filled as raw RGBA rather than through SKBitmap.Pixels, whose setter converts
            // pixel by pixel and costs more than the noise it would be carrying.
            var pixels = new byte[tileSize * tileSize * 4];
            var scale = 1f / tileSize;
            var i = 0;

            for (var py = 0; py < tileSize; py++)
            {
                var v = py * scale;
                for (var px = 0; px < tileSize; px++)
                {
                    var u = px * scale;

                    // Both fields wrap at the tile edge. Keeping the largest feature well under
                    // a tile is what stops neighbouring tiles from showing a seam.
                    var mottle = PeriodicFbm(u, v, GroundPeriod, detail.GroundOctaves, seed);
                    var patch = PeriodicFbm(u, v, PatchPeriod, detail.PatchOctaves, seed ^ 0x51A3u);

                    var color = LerpColor(GrassDark, GrassLight, 0.30f + mottle * 0.55f + patch * 0.20f);

                    // Sun-bleached patches where the broad noise peaks.
                    color = LerpColor(color, GrassDry, Smoothstep(0.60f, 0.92f, patch) * 0.45f);

                    // Per-pixel grain to break up the smooth gradients.
                    color = Shade(color, (Hash01(px, py, seed ^ 0x77u) - 0.5f) * 0.10f);

                    pixels[i++] = color.Red;
                    pixels[i++] = color.Green;
                    pixels[i++] = color.Blue;
                    pixels[i++] = 0xFF;
                }
            }

            var info = new SKImageInfo(tileSize, tileSize, SKColorType.Rgba8888, SKAlphaType.Opaque);
            using var ground = SKImage.FromPixelCopy(info, pixels);
            canvas.DrawImage(ground, 0, 0);
        }

        /// <summary>
        /// Strokes the blades. Blades are grouped into clumps that share a lean and a rough
        /// height, with a few loose strays on top -- scattering them evenly instead reads as
        /// woven fabric rather than as grass.
        /// </summary>
        private static void PaintBlades(SKCanvas canvas, int tileSize, uint seed, DetailLevel detail)
        {
            var rng = new Rng(seed ^ 0xB1ADE5u);
            var unit = tileSize / 32f;

            // Zero means the blades cast nothing, which is how the small sizes skip a whole
            // draw per blade.
            var shadowOffset = detail.Tufted ? Math.Max(0.5f, unit * 0.7f) : 0f;

            // Clumps are kept small relative to the tile. Features that approach tile size
            // make each tile read as one distinct tuft, and a grid of distinct tufts is
            // exactly the pattern this is trying to avoid.
            var clumpCount = detail.Tufted ? Math.Max(3, (int)(tileSize * tileSize / 170f)) : 0;

            using var paint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeCap = SKStrokeCap.Round,
                StrokeWidth = Math.Max(0.8f, unit),
            };
            using var path = new SKPath();

            // Hoisted out of the clump loop: a fresh paint per clump is a native allocation
            // for every one of the pool's tens of thousands of tufts.
            using var shadowPaint = clumpCount > 0
                ? new SKPaint
                {
                    IsAntialias = true,
                    Style = SKPaintStyle.Fill,
                    Color = BladeShadow.WithAlpha(38),
                }
                : null;

            for (var c = 0; c < clumpCount; c++)
            {
                var clumpX = rng.Range(0f, tileSize);
                var clumpY = rng.Range(0f, tileSize);
                var spread = rng.Range(0.05f, 0.11f) * tileSize;
                var clumpHeight = rng.Range(0.10f, 0.24f) * tileSize;
                var clumpLean = rng.Range(-0.45f, 0.45f);
                var blades = (int)rng.Range(7f, 16f);

                if (shadowOffset > 0f && shadowPaint is not null)
                    DrawClumpShadow(canvas, shadowPaint, clumpX, clumpY, spread, tileSize, shadowOffset);

                for (var b = 0; b < blades; b++)
                {
                    var baseX = clumpX + rng.Range(-spread, spread);
                    var baseY = clumpY + rng.Range(-spread * 0.6f, spread * 0.6f);
                    var height = clumpHeight * rng.Range(0.6f, 1.3f);
                    var lean = (clumpLean + rng.Range(-0.16f, 0.16f)) * height;

                    DrawBlade(canvas, paint, path, baseX, baseY, height, lean, rng.NextFloat(), tileSize, shadowOffset);
                }
            }

            // Strays, so the clumps do not read as discrete tufts. With no clumps to fill the
            // tile they have to carry it alone, so there are more of them.
            var strays = Math.Max(6, (int)(tileSize * tileSize / (detail.Tufted ? 70f : 34f)));
            for (var i = 0; i < strays; i++)
            {
                var baseX = rng.Range(0f, tileSize);
                var baseY = rng.Range(0f, tileSize);
                var height = rng.Range(0.07f, 0.19f) * tileSize;
                var lean = rng.Range(-0.5f, 0.5f) * height;

                DrawBlade(canvas, paint, path, baseX, baseY, height, lean, rng.NextFloat(), tileSize, shadowOffset);
            }
        }

        /// <summary>A soft dark pool under a clump, so the tuft sits in the ground instead of on it.</summary>
        private static void DrawClumpShadow(
            SKCanvas canvas,
            SKPaint paint,
            float cx,
            float cy,
            float spread,
            int tileSize,
            float shadowOffset)
        {
            paint.MaskFilter = GetBlur(spread * 0.45f);

            var rect = SKRect.Create(cx - spread, cy - spread * 0.5f + shadowOffset, spread * 2f, spread);

            // The blur reaches about three sigma past the oval, and a copy that gets skipped
            // here is shadow missing from an edge, so the bounds are deliberately generous.
            var bounds = rect;
            bounds.Inflate(spread * 1.5f, spread * 1.5f);

            foreach (var (dx, dy) in new WrapOffsets(bounds, tileSize))
                canvas.DrawOval(SKRect.Create(rect.Left + dx, rect.Top + dy, rect.Width, rect.Height), paint);
        }

        /// <summary>
        /// A blur for roughly <paramref name="sigma"/>, shared by every clump that rounds to
        /// the same quarter pixel. Mask filters are immutable, so one is safe to hand to all
        /// of the build threads at once.
        /// </summary>
        private static SKMaskFilter GetBlur(float sigma)
        {
            var key = Math.Max(1, (int)MathF.Round(sigma * 4f));
            return BlurFilters.GetOrAdd(key, static k => SKMaskFilter.CreateBlur(SKBlurStyle.Normal, k / 4f));
        }

        private static void DrawBlade(
            SKCanvas canvas,
            SKPaint paint,
            SKPath path,
            float baseX,
            float baseY,
            float height,
            float lean,
            float tint,
            int tileSize,
            float shadowOffset)
        {
            path.Reset();
            path.MoveTo(baseX, baseY);
            path.QuadTo(baseX + lean * 0.25f, baseY - height * 0.6f, baseX + lean, baseY - height);

            // Roughly a third of the blades sit darker than the ground, which is what gives
            // the mat any sense of depth.
            var color = tint < 0.35f
                ? LerpColor(GrassDark, GrassMid, tint / 0.35f)
                : LerpColor(GrassMid, BladeLight, (tint - 0.35f) / 0.65f);
            var bladeColor = color.WithAlpha((byte)(140f + tint * 90f));
            var shadowColor = BladeShadow.WithAlpha(70);
            var withShadow = shadowOffset > 0f;   // zero offset means this level draws no shadows

            // A blade that runs off one edge is drawn again coming in the opposite edge,
            // so the finished tile abuts itself (and any other variant) without a seam.
            var bounds = path.Bounds;
            bounds.Inflate(paint.StrokeWidth, paint.StrokeWidth);
            bounds.Bottom += shadowOffset;

            foreach (var (dx, dy) in new WrapOffsets(bounds, tileSize))
            {
                canvas.Save();
                canvas.Translate(dx, dy);

                if (withShadow)
                {
                    paint.Color = shadowColor;
                    canvas.Translate(0, shadowOffset);
                    canvas.DrawPath(path, paint);
                    canvas.Translate(0, -shadowOffset);
                }

                paint.Color = bladeColor;
                canvas.DrawPath(path, paint);
                canvas.Restore();
            }
        }

        /// <summary>
        /// The tile-sized translations needed to draw a shape wrapped around the tile edges:
        /// always (0, 0), plus a copy for each edge the shape crosses.
        /// <para>
        /// A struct enumerator rather than an iterator method, because this is walked once per
        /// blade and the pool draws millions of them; an iterator would put a heap allocation
        /// behind every one of those.
        /// </para>
        /// </summary>
        private struct WrapOffsets
        {
            private readonly SKRect _bounds;
            private readonly int _tileSize;
            private int _index;

            public WrapOffsets(SKRect bounds, int tileSize)
            {
                _bounds = bounds;
                _tileSize = tileSize;
                _index = -1;
                Current = default;
            }

            public (float Dx, float Dy) Current { get; private set; }

            public readonly WrapOffsets GetEnumerator() => this;

            public bool MoveNext()
            {
                var tile = SKRect.Create(0, 0, _tileSize, _tileSize);

                while (++_index < 9)
                {
                    float dx = (_index / 3 - 1) * _tileSize;
                    float dy = (_index % 3 - 1) * _tileSize;

                    var shifted = SKRect.Create(
                        _bounds.Left + dx,
                        _bounds.Top + dy,
                        _bounds.Width,
                        _bounds.Height);

                    if (!shifted.IntersectsWith(tile))
                        continue;

                    Current = (dx, dy);
                    return true;
                }

                return false;
            }
        }

        /// <summary>Sprinkles a handful of seed heads and tiny flowers.</summary>
        private static void PaintDetails(SKCanvas canvas, int tileSize, uint seed)
        {
            var rng = new Rng(seed ^ 0xF10E7Du);
            var unit = tileSize / 32f;
            var count = (int)(tileSize / 10f);
            if (count <= 0)
                return;

            using var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };

            for (var i = 0; i < count; i++)
            {
                // Only about half the candidates are taken, so the count varies tile to tile.
                if (rng.NextFloat() > 0.55f)
                    continue;

                var cx = rng.Range(0f, tileSize);
                var cy = rng.Range(0f, tileSize);
                var radius = Math.Max(0.6f, unit * rng.Range(0.7f, 1.3f));

                paint.Color = rng.NextFloat() < 0.35f
                    ? FlowerPale.WithAlpha(210)
                    : GrassDry.WithAlpha(190);

                var bounds = SKRect.Create(cx - radius, cy - radius, radius * 2f, radius * 2f);
                foreach (var (dx, dy) in new WrapOffsets(bounds, tileSize))
                    canvas.DrawCircle(cx + dx, cy + dy, radius, paint);
            }
        }

    }
}
