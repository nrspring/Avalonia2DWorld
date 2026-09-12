using System;
using Avalonia2DWorld.Map.Models;

namespace Avalonia2DWorld.Map.Interfaces
{
    /// <summary>
    /// A tile list that knows it is made of rows, and can hand each one over without copying.
    /// <para>
    /// Implemented by the windows <see cref="Models.TileGrid"/> cuts, and understood by
    /// renderers that would rather walk a span than call an interface once per tile. A
    /// renderer that does not know about it still works: every implementation is also an
    /// <see cref="System.Collections.Generic.IReadOnlyList{T}"/> over the same tiles, in the
    /// same order.
    /// </para>
    /// </summary>
    public interface ITileRows
    {
        /// <summary>How many rows the list is made of.</summary>
        int RowCount { get; }

        /// <summary>
        /// Row <paramref name="index"/>, left to right. Rows run top to bottom, so walking
        /// them in order walks the tiles in the same reading order the flat list gives.
        /// </summary>
        ReadOnlySpan<MapTile> Row(int index);
    }
}
