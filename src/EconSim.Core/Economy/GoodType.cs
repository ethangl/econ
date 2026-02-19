namespace EconSim.Core.Economy
{
    public enum GoodType
    {
        Food = 0,
        Timber = 1,
        IronOre = 2,
        GoldOre = 3,
        SilverOre = 4,
        Salt = 5,
        Wool = 6,
    }

    public static class Goods
    {
        public const int Count = 7;

        /// <summary>Daily consumption per person (kg/day), indexed by GoodType.</summary>
        public static readonly float[] ConsumptionPerPop =
        {
            1.0f,   // Food (staple)
            0.2f,   // Timber
            0.005f, // IronOre
            0.0f,   // GoldOre (not consumed, minted)
            0.0f,   // SilverOre (not consumed, minted)
            0.05f,  // Salt
            0.1f,   // Wool
        };

        /// <summary>Serialization names, indexed by GoodType.</summary>
        public static readonly string[] Names =
        {
            "food", "timber", "ironOre", "goldOre", "silverOre", "salt", "wool",
        };

        /// <summary>Precious metals — 100% tax rate (regal right), minted into currency.</summary>
        public static bool IsPreciousMetal(int goodIndex)
        {
            return goodIndex == (int)GoodType.GoldOre || goodIndex == (int)GoodType.SilverOre;
        }

        /// <summary>County administrative consumption per capita per day (building upkeep).</summary>
        public static readonly float[] CountyAdminPerPop =
        {   //  Food  Timber  Iron   Gold   Silver Salt   Wool
            0.0f, 0.02f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f,
        };

        /// <summary>Provincial administrative consumption per capita per day (infrastructure).</summary>
        public static readonly float[] ProvinceAdminPerPop =
        {   //  Food  Timber  Iron   Gold   Silver Salt   Wool
            0.0f, 0.01f, 0.001f, 0.0f, 0.0f, 0.0f, 0.0f,
        };

        /// <summary>Realm administrative consumption per capita per day (military upkeep).</summary>
        public static readonly float[] RealmAdminPerPop =
        {   //  Food   Timber  Iron    Gold   Silver Salt   Wool
            0.02f, 0.01f, 0.003f, 0.0f, 0.0f, 0.0f, 0.005f,
        };
    }
}
