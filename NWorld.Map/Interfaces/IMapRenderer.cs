using NWorld.Map.Models;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Text;

namespace NWorld.Map.Interfaces
{
    public interface IMapRenderer
    {
        Task RenderTile(SKCanvas canvas, MapTile tile);
    }
}
