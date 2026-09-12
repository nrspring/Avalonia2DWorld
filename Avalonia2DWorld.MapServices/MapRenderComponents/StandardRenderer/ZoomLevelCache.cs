using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Avalonia2DWorld.MapServices.MapRenderComponents.StandardRenderer
{
    /// <summary>Something a render function builds once per tile size and then draws from.</summary>
    internal interface IZoomLevelResource : IDisposable
    {
        /// <summary>Bytes held. Fixed for the resource's lifetime, since the cache budgets on it.</summary>
        long Bytes { get; }
    }

    /// <summary>
    /// Holds one <typeparamref name="T"/> per tile size, within a byte budget, evicting the
    /// zoom levels that have gone longest undrawn.
    /// <para>
    /// The entries are <see cref="Lazy{T}"/> rather than the resources themselves.
    /// ConcurrentDictionary's factory can run on more than one thread and keep only one
    /// result, which with a prewarm running alongside a repaint would mean two full builds, a
    /// leaked resource, and a byte count that never again matches what is actually held. A
    /// Lazy that loses the race is simply never forced.
    /// </para>
    /// </summary>
    internal sealed class ZoomLevelCache<T>(long budgetBytes, Func<int, T> build)
        where T : class, IZoomLevelResource
    {
        private readonly ConcurrentDictionary<int, Entry> _entries = new();
        private long _bytes;

        /// <summary>
        /// The resource for <paramref name="tileSize"/>, building it if this is the first ask.
        /// </summary>
        public T Get(int tileSize)
        {
            if (_entries.TryGetValue(tileSize, out var cached) && cached.Value.IsValueCreated)
            {
                cached.LastUsed = Environment.TickCount64;
                return cached.Value.Value;
            }

            var entry = _entries.GetOrAdd(tileSize, size => new Entry(new Lazy<T>(() =>
            {
                var built = build(size);
                Interlocked.Add(ref _bytes, built.Bytes);
                return built;
            }, LazyThreadSafetyMode.ExecutionAndPublication)));

            var resource = entry.Value.Value;
            entry.LastUsed = Environment.TickCount64;
            Trim(keep: tileSize);
            return resource;
        }

        /// <summary>
        /// Builds for <paramref name="tileSize"/> off the calling thread, so a zoom control can
        /// have the next level ready before the view arrives at it. Drawing at a size that was
        /// never prewarmed still works; it just pays for the build in the frame that asks.
        /// </summary>
        public Task Prewarm(int tileSize) =>
            tileSize <= 0 ? Task.CompletedTask : Task.Run(() => Get(tileSize));

        /// <summary>
        /// Drops everything held; it is rebuilt on the next draw. Not safe to call while a
        /// frame is in flight -- it disposes resources that frame may still be drawing from.
        /// </summary>
        public void Clear()
        {
            foreach (var key in _entries.Keys)
            {
                if (_entries.TryRemove(key, out var entry) && entry.Value.IsValueCreated)
                {
                    Interlocked.Add(ref _bytes, -entry.Value.Value.Bytes);
                    entry.Value.Value.Dispose();
                }
            }
        }

        /// <summary>
        /// Evicts least-recently-drawn zoom levels until the cache is back inside its budget.
        /// Only ever runs just after a build, never on the per-draw path, and never touches
        /// <paramref name="keep"/> -- the size the caller is about to draw.
        /// </summary>
        private void Trim(int keep)
        {
            if (Interlocked.Read(ref _bytes) <= budgetBytes)
                return;

            // Anything still building is skipped rather than waited on: forcing a half-built
            // Lazy here would block this thread on a resource it is about to throw away.
            var candidates = _entries
                .Where(entry => entry.Key != keep && entry.Value.Value.IsValueCreated)
                .OrderBy(entry => entry.Value.LastUsed)
                .Select(entry => entry.Key)
                .ToList();

            foreach (var size in candidates)
            {
                if (Interlocked.Read(ref _bytes) <= budgetBytes)
                    return;

                if (_entries.TryRemove(size, out var victim) && victim.Value.IsValueCreated)
                {
                    Interlocked.Add(ref _bytes, -victim.Value.Value.Bytes);
                    victim.Value.Value.Dispose();
                }
            }
        }

        private sealed class Entry(Lazy<T> value)
        {
            public Lazy<T> Value { get; } = value;

            /// <summary>
            /// Wall-clock stamp of the last draw, for eviction order. Written without
            /// synchronisation from the draw path; a lost update costs nothing worse than a
            /// slightly stale eviction order.
            /// </summary>
            public long LastUsed;
        }
    }
}
