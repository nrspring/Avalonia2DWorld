using System;
using System.Collections.Generic;
using System.Text;

namespace NWorld.Map.Models
{
    public class MapRenderComponent
    {
        public required Guid ComponentType { get;set;}
        public string[]? Params { get; set;}
    }
}
