using NWorld.Map.Interfaces;
using NWorld.Map.Models;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Text;

namespace NWorld.MapServices.Renderers
{
    public class StandardRenderer : IMapRenderer
    {
        public Task RenderTile(SKCanvas canvas, RenderFrame frame, MapTile tile)
        {
            throw new NotImplementedException();
        }
    }
}
