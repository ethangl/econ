using System;

namespace EconSim.Core.Economy
{
    /// <summary>
    /// Static biome -> productivity lookup. kg produced per person per day.
    /// Indexed by [biomeId, goodType].
    /// </summary>
    public static class BiomeProductivity
    {
        // [biomeId, goodType] — Bread=0, Timber=1, IronOre=2, GoldOre=3, SilverOre=4, Salt=5, Wool=6, Stone=7, Ale=8, Clay=9, Pottery=10, Furniture=11, Iron=12, Tools=13, Charcoal=14, Clothes=15, Pork=16, Sausage=17, Bacon=18, Milk=19, Cheese=20, Fish=21, SaltedFish=22, Stockfish=23
        static readonly float[,] Values =
        {
            //                     Bread Timber  Iron   Gold   Silver Salt   Wool   Stone  Ale    Clay   Pottery Furn Iron   Tools  Charcoal Clothes Pork  Sausage Bacon  Milk  Cheese Fish  SltFish Stkfish
            /* 0  Glacier         */ { 0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f },
            /* 1  Tundra          */ { 0.18f, 0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.05f, 0.05f, 0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.02f, 0.0f,  0.0f,  0.02f, 0.0f,  0.0f,  0.0f,  0.0f },
            /* 2  Salt Flat       */ { 0.09f, 0.0f,  0.0f,  0.0f,  0.0f,  0.9f,  0.0f,  0.1f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f },
            /* 3  Coastal Marsh   */ { 0.69f, 0.0f,  0.02f, 0.0f,  0.0f,  0.70f, 0.0f,  0.0f,  0.1f,  0.2f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.08f, 0.0f,  0.0f,  0.05f, 0.0f,  0.08f, 0.0f,  0.0f },
            /* 4  Alpine Barren   */ { 0.18f, 0.0f,  0.4f,  0.02f, 0.03f, 0.0f,  0.0f,  0.4f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f },
            /* 5  Mountain Shrub  */ { 0.35f, 0.11f, 0.3f,  0.01f, 0.02f, 0.0f,  0.15f, 0.3f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.08f, 0.0f,  0.0f,  0.10f, 0.0f,  0.0f,  0.0f,  0.0f },
            /* 6  Floodplain      */ { 1.39f, 0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.1f,  0.02f, 1.0f,  0.3f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.15f, 0.0f,  0.0f,  0.25f, 0.0f,  0.10f, 0.0f,  0.0f },
            /* 7  Wetland         */ { 0.69f, 0.11f, 0.03f, 0.0f,  0.0f,  0.15f, 0.0f,  0.0f,  0.2f,  0.15f, 0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.05f, 0.0f,  0.0f,  0.08f, 0.0f,  0.05f, 0.0f,  0.0f },
            /* 8  Hot Desert      */ { 0.23f, 0.0f,  0.25f, 0.0f,  0.01f, 0.0f,  0.0f,  0.2f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f },
            /* 9  Cold Desert     */ { 0.23f, 0.0f,  0.25f, 0.0f,  0.01f, 0.0f,  0.05f, 0.2f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.03f, 0.0f,  0.0f,  0.02f, 0.0f,  0.0f,  0.0f,  0.0f },
            /* 10 Scrubland       */ { 0.46f, 0.11f, 0.15f, 0.0f,  0.005f,0.0f,  0.15f, 0.1f,  0.1f,  0.05f, 0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.10f, 0.0f,  0.0f,  0.08f, 0.0f,  0.0f,  0.0f,  0.0f },
            /* 11 Tropical Rainf  */ { 0.81f, 0.55f, 0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.02f, 0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f },
            /* 12 Tropical Dry F  */ { 0.92f, 0.44f, 0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.03f, 0.2f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.20f, 0.0f,  0.0f,  0.05f, 0.0f,  0.0f,  0.0f,  0.0f },
            /* 13 Savanna         */ { 1.05f, 0.11f, 0.02f, 0.0f,  0.0f,  0.0f,  0.25f, 0.05f, 0.55f, 0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.20f, 0.0f,  0.0f,  0.20f, 0.0f,  0.0f,  0.0f,  0.0f },
            /* 14 Boreal Forest   */ { 0.58f, 0.55f, 0.02f, 0.0f,  0.0f,  0.0f,  0.0f,  0.03f, 0.1f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.10f, 0.0f,  0.0f,  0.05f, 0.0f,  0.0f,  0.0f,  0.0f },
            /* 15 Temperate Fore  */ { 0.87f, 0.55f, 0.02f, 0.0f,  0.0f,  0.0f,  0.05f, 0.05f, 0.35f, 0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.25f, 0.0f,  0.0f,  0.10f, 0.0f,  0.0f,  0.0f,  0.0f },
            /* 16 Grassland       */ { 1.28f, 0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.35f, 0.02f, 0.9f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.15f, 0.0f,  0.0f,  0.30f, 0.0f,  0.0f,  0.0f,  0.0f },
            /* 17 Woodland        */ { 0.98f, 0.33f, 0.02f, 0.0f,  0.0f,  0.0f,  0.1f,  0.05f, 0.3f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.30f, 0.0f,  0.0f,  0.15f, 0.0f,  0.0f,  0.0f,  0.0f },
            /* 18 Lake            */ { 0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f },
        };

        static readonly int BiomeCount;

        static BiomeProductivity()
        {
            BiomeCount = Values.GetLength(0);
            if (Values.GetLength(1) != Goods.Count)
                throw new InvalidOperationException(
                    $"BiomeProductivity table has {Values.GetLength(1)} columns but Goods.Count is {Goods.Count}. " +
                    "Update BiomeProductivity.Values when adding/removing goods.");
        }

        /// <summary>
        /// Get food productivity for a biome ID. Returns 0 for unknown biomes.
        /// Backward-compatible shorthand for Get(biomeId, GoodType.Bread).
        /// </summary>
        public static float GetFood(int biomeId)
        {
            if (biomeId < 0 || biomeId >= BiomeCount)
                return 0f;
            return Values[biomeId, (int)GoodType.Bread];
        }

        /// <summary>
        /// Get productivity for a biome ID and good type. Returns 0 for unknown biomes.
        /// </summary>
        public static float Get(int biomeId, GoodType good)
        {
            if (biomeId < 0 || biomeId >= BiomeCount)
                return 0f;
            return Values[biomeId, (int)good];
        }
    }
}
