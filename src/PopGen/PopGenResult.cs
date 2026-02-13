using MapGen.Core;

namespace PopGen.Core
{
    public struct PopColor32
    {
        public byte R;
        public byte G;
        public byte B;
        public byte A;

        public PopColor32(byte r, byte g, byte b, byte a = 255)
        {
            R = r;
            G = g;
            B = b;
            A = a;
        }
    }

    public sealed class PopBurg
    {
        public int Id;
        public string Name;
        public Vec2 Position;
        public int CellId;
        public int RealmId;
        public int CultureId;
        public float Population;
        public bool IsCapital;
        public bool IsPort;
        public string Type;
        public string Group;
    }

    public sealed class PopCounty
    {
        public int Id;
        public string Name;
        public int SeatCellId;
        public int[] CellIds;
        public int ProvinceId;
        public int RealmId;
        public float TotalPopulation;
        public Vec2 Centroid;
    }

    public sealed class PopProvince
    {
        public int Id;
        public string Name;
        public string FullName;
        public int RealmId;
        public int CenterCellId;
        public int CapitalBurgId;
        public PopColor32 Color;
        public Vec2 LabelPosition;
        public int[] CellIds;
    }

    public sealed class PopRealm
    {
        public int Id;
        public string Name;
        public string FullName;
        public string GovernmentForm;
        public int CapitalBurgId;
        public int CenterCellId;
        public int CultureId;
        public PopColor32 Color;
        public Vec2 LabelPosition;
        public int[] ProvinceIds;
        public int[] NeighborRealmIds;
        public float UrbanPopulation;
        public float RuralPopulation;
        public int TotalArea;
    }

    /// <summary>
    /// PopGen output consumed by world-import layers.
    /// </summary>
    public sealed class PopGenResult
    {
        public PopBurg[] Burgs;
        public PopCounty[] Counties;
        public PopProvince[] Provinces;
        public PopRealm[] Realms;
        public PopCulture[] Cultures;
        public PopReligion[] Religions;
        public int[] CellBurgId;

        public PopGenResult()
        {
            Burgs = System.Array.Empty<PopBurg>();
            Counties = System.Array.Empty<PopCounty>();
            Provinces = System.Array.Empty<PopProvince>();
            Realms = System.Array.Empty<PopRealm>();
            Cultures = System.Array.Empty<PopCulture>();
            Religions = System.Array.Empty<PopReligion>();
            CellBurgId = System.Array.Empty<int>();
        }
    }
}
