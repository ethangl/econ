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
        Stone = 7,
    }

    public static class Goods
    {
        public const int Count = 8;

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
            0.0f,   // Stone (admin only, not consumed by pops)
        };

        /// <summary>Serialization names, indexed by GoodType.</summary>
        public static readonly string[] Names =
        {
            "food", "timber", "ironOre", "goldOre", "silverOre", "salt", "wool", "stone",
        };

        /// <summary>Fraction of pure metal per kg of ore (smelting yield).</summary>
        public const float GoldSmeltingYield = 0.01f;   // 1% — rich medieval deposits
        public const float SilverSmeltingYield = 0.05f;  // 5% — silver ores are richer

        /// <summary>Crowns minted per kg of pure metal.</summary>
        public const float CrownsPerKgGold = 1000f;
        public const float CrownsPerKgSilver = 100f;

        /// <summary>Precious metals — 100% tax rate (regal right), minted into currency.</summary>
        public static bool IsPreciousMetal(int goodIndex)
        {
            return goodIndex == (int)GoodType.GoldOre || goodIndex == (int)GoodType.SilverOre;
        }

        /// <summary>County administrative consumption per capita per day (building upkeep).</summary>
        public static readonly float[] CountyAdminPerPop =
        {   //  Food  Timber  Iron   Gold   Silver Salt   Wool   Stone
            0.0f, 0.02f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.005f,
        };

        /// <summary>Provincial administrative consumption per capita per day (infrastructure).</summary>
        public static readonly float[] ProvinceAdminPerPop =
        {   //  Food  Timber  Iron   Gold   Silver Salt   Wool   Stone
            0.0f, 0.01f, 0.001f, 0.0f, 0.0f, 0.0f, 0.0f, 0.008f,
        };

        /// <summary>Realm administrative consumption per capita per day (military upkeep).</summary>
        public static readonly float[] RealmAdminPerPop =
        {   //  Food   Timber  Iron    Gold   Silver Salt   Wool   Stone
            0.02f, 0.01f, 0.003f, 0.0f, 0.0f, 0.0f, 0.005f, 0.012f,
        };

        // ── Inter-realm trade constants ────────────────────────────

        /// <summary>Goods that can be traded on the inter-realm market.</summary>
        public static readonly int[] TradeableGoods =
        {
            (int)GoodType.Food,
            (int)GoodType.Timber,
            (int)GoodType.IronOre,
            (int)GoodType.Salt,
            (int)GoodType.Wool,
            (int)GoodType.Stone,
        };

        /// <summary>Buy priority order — staples first, stone last (infrastructure can wait).</summary>
        public static readonly int[] BuyPriority =
        {
            (int)GoodType.Food,
            (int)GoodType.IronOre,
            (int)GoodType.Salt,
            (int)GoodType.Wool,
            (int)GoodType.Timber,
            (int)GoodType.Stone,
        };

        /// <summary>Anchor price in Crowns per kg, indexed by GoodType. Gold/Silver = 0 (not traded).</summary>
        public static readonly float[] BasePrice =
        {   //  Food   Timber  Iron   Gold   Silver Salt   Wool   Stone
            1.0f, 0.5f, 5.0f, 0.0f, 0.0f, 3.0f, 2.0f, 0.3f,
        };

        /// <summary>Minimum price (10% of base) — price floor.</summary>
        public static readonly float[] MinPrice =
        {
            0.1f, 0.05f, 0.5f, 0.0f, 0.0f, 0.3f, 0.2f, 0.03f,
        };

        /// <summary>Maximum price (10x base) — price cap.</summary>
        public static readonly float[] MaxPrice =
        {
            10.0f, 5.0f, 50.0f, 0.0f, 0.0f, 30.0f, 20.0f, 3.0f,
        };
    }
}
