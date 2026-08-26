using System;
using System.Collections.Generic;
using System.Text;

namespace NWorld.Map.Models
{
    internal class MapRenderComponent
    {
        public required Guid ComponentType { get;set;}
        public string[] Params { get; set;} = [];
    }
}
