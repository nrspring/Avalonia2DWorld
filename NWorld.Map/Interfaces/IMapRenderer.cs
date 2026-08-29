using NWorld.Map.Models;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Text;

namespace NWorld.Map.Interfaces
{
    public interface IMapRenderer
    {
        /// <summary>
        /// Draws one tile. <paramref name="frame"/> carries the tile size and the frame's
        /// timestamp, both of which the caller samples once and reuses for every tile it draws.
        /// </summary>
        Task RenderTile(SKCanvas canvas, RenderFrame frame, MapTile tile);
    }
}
