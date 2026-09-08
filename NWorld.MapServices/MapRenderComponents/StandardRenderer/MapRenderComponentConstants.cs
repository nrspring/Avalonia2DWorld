using System;
using System.Collections.Generic;
using System.Text;

namespace NWorld.MapServices.MapRenderComponents.StandardRenderer
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

        //Sea over a shelf. Its own type rather than the Water above, which rivers use: a
        //tile's ground is what the generation passes read the map back out of, and one type
        //for both would leave them unable to tell a river from the bay it runs into.
        public static Guid ShallowWater { get; } = new Guid("5A5A5A5A-5A5A-5A5A-5A5A-5A5A5A5A5A5A");

        //Highlight types
        public static Guid Hover { get; } = new Guid("66666666-6666-6666-6666-666666666666");
        public static Guid Selected { get; } = new Guid("77777777-7777-7777-7777-777777777777");
        public static Guid Range { get; } = new Guid("88888888-8888-8888-8888-888888888888");

        //Resource types
        public static Guid Iron { get; } = new Guid("AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA");
        public static Guid Wood { get; } = new Guid("BBBBBBBB-BBBB-BBBB-BBBB-BBBBBBBBBBBB");
        public static Guid Oil { get; } = new Guid("CCCCCCCC-CCCC-CCCC-CCCC-CCCCCCCCCCCC");
        public static Guid Sulphur { get; } = new Guid("DDDDDDDD-DDDD-DDDD-DDDD-DDDDDDDDDDDD");
        public static Guid Stone { get; } = new Guid("EEEEEEEE-EEEE-EEEE-EEEE-EEEEEEEEEEEE");

        //Enhancement types: what has been built on a tile, as opposed to what the tile is.
        public static Guid Road { get; } = new Guid("F0AD0000-F0AD-F0AD-F0AD-F0ADF0ADF0AD");
        public static Guid Bridge { get; } = new Guid("B41D6E00-B41D-B41D-B41D-B41DB41DB41D");
        public static Guid City { get; } = new Guid("C17700FF-C177-C177-C177-C177C177C177");
        public static Guid Fort { get; } = new Guid("F0770000-F077-F077-F077-F077F077F077");
        public static Guid Factory { get; } = new Guid("FAC70000-FAC7-FAC7-FAC7-FAC7FAC7FAC7");

        //Shaped by the water rather than by the roads -- see RenderShipyard. It is still an
        //enhancement like the rest: what differs is which neighbours it asks about.
        public static Guid Shipyard { get; } = new Guid("54170000-5417-5417-5417-541754175417");

        //AD17 for the adit, which is the way into a working -- the rest of these are spelled in
        //hex the same way. Reads the resource under it to decide what kind of works it is; see
        //RenderWorks.
        public static Guid Works { get; } = new Guid("AD170000-AD17-AD17-AD17-AD17AD17AD17");

        //Unit types: what is standing on a tile, as opposed to what is built there.
        //B111 for the bill, the hedging hook a levied man brought to war with him -- the rest
        //of these are spelled in hex the same way.
        public static Guid Militia { get; } = new Guid("B1110000-B111-B111-B111-B111B111B111");

        //F11E for the file, which is the thing soldiers stand in and militia do not -- see
        //RenderSoldiers, where that is the whole of the difference between the two.
        public static Guid Soldiers { get; } = new Guid("F11E0000-F11E-F11E-F11E-F11EF11EF11E");

        //Ships, named for the vessel rather than for the size: a boat, a cog and a carrack.
        //Sized apart in the drawing by how many masts each carries, which is a count and so
        //needs no second ship beside it to be read -- see Vessel.
        public static Guid SmallShip { get; } = new Guid("B0A70000-B0A7-B0A7-B0A7-B0A7B0A7B0A7");
        public static Guid MediumShip { get; } = new Guid("C0600000-C060-C060-C060-C060C060C060");
        public static Guid LargeShip { get; } = new Guid("CA44AC00-CA44-CA44-CA44-CA44CA44CA44");

        //Overlay types
        public static Guid ElevationLabel { get; } = new Guid("99999999-9999-9999-9999-999999999999");
    }
}
