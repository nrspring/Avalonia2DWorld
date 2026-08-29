using System;
using System.Collections.Generic;
using System.Text;

namespace NWorld.MapServices.MapRenderComponents
{
    public static class MapRenderComponentConstants
    {
        //Miscellaneous types
        public static Guid Empty { get; } = new Guid("00000000-0000-0000-0000-000000000000");

        //Base ground types
        public static Guid Grass { get; } = new Guid("11111111-1111-1111-1111-111111111111");
        public static Guid Water { get; } = new Guid("22222222-2222-2222-2222-222222222222");
        public static Guid DeepWater { get; } = new Guid("33333333-3333-3333-3333-333333333333");
        public static Guid Swamp { get; } = new Guid("44444444-4444-4444-4444-444444444444");
        public static Guid Desert { get; } = new Guid("55555555-5555-5555-5555-555555555555");

        //Highlight types
        public static Guid Hover { get; } = new Guid("66666666-6666-6666-6666-666666666666");
        public static Guid Selected { get; } = new Guid("77777777-7777-7777-7777-777777777777");
        public static Guid Range { get; } = new Guid("88888888-8888-8888-8888-888888888888");
    }
}
