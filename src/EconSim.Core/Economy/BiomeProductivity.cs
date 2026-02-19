using System;

namespace EconSim.Core.Economy
{
    /// <summary>
    /// Static biome -> productivity lookup. kg produced per person per day.
    /// Indexed by [biomeId, goodType].
    /// </summary>
    public static class BiomeProductivity
    {
        // [biomeId, goodType] — Food=0, Timber=1, IronOre=2, GoldOre=3, SilverOre=4, Salt=5, Wool=6, Stone=7, Ale=8
        static readonly float[,] Values =
        {
            //                     Food  Timber  Iron   Gold   Silver Salt   Wool   Stone  Ale
            /* 0  Glacier         */ { 0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f  },
            /* 1  Tundra          */ { 0.16f, 0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.05f, 0.05f, 0.0f  },
            /* 2  Salt Flat       */ { 0.08f, 0.0f,  0.0f,  0.0f,  0.0f,  0.4f,  0.0f,  0.1f,  0.0f  },
            /* 3  Coastal Marsh   */ { 0.63f, 0.0f,  0.02f, 0.0f,  0.0f,  0.3f,  0.0f,  0.0f,  0.1f  },
            /* 4  Alpine Barren   */ { 0.16f, 0.0f,  0.4f,  0.02f, 0.03f, 0.0f,  0.0f,  0.4f,  0.0f  },
            /* 5  Mountain Shrub  */ { 0.32f, 0.1f,  0.3f,  0.01f, 0.02f, 0.0f,  0.15f, 0.3f,  0.0f  },
            /* 6  Floodplain      */ { 1.26f, 0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.1f,  0.02f, 1.0f  },
            /* 7  Wetland         */ { 0.63f, 0.1f,  0.03f, 0.0f,  0.0f,  0.05f, 0.0f,  0.0f,  0.2f  },
            /* 8  Hot Desert      */ { 0.21f, 0.0f,  0.25f, 0.0f,  0.01f, 0.0f,  0.0f,  0.2f,  0.0f  },
            /* 9  Cold Desert     */ { 0.21f, 0.0f,  0.25f, 0.0f,  0.01f, 0.0f,  0.05f, 0.2f,  0.0f  },
            /* 10 Scrubland       */ { 0.42f, 0.1f,  0.15f, 0.0f,  0.005f,0.0f,  0.15f, 0.1f,  0.1f  },
            /* 11 Tropical Rainf  */ { 0.74f, 0.5f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.02f, 0.0f  },
            /* 12 Tropical Dry F  */ { 0.84f, 0.4f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.03f, 0.2f  },
            /* 13 Savanna         */ { 0.95f, 0.1f,  0.02f, 0.0f,  0.0f,  0.0f,  0.25f, 0.05f, 0.55f },
            /* 14 Boreal Forest   */ { 0.53f, 0.5f,  0.02f, 0.0f,  0.0f,  0.0f,  0.0f,  0.03f, 0.1f  },
            /* 15 Temperate Fore  */ { 0.79f, 0.5f,  0.02f, 0.0f,  0.0f,  0.0f,  0.05f, 0.05f, 0.35f },
            /* 16 Grassland       */ { 1.16f, 0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.35f, 0.02f, 0.9f  },
            /* 17 Woodland        */ { 0.89f, 0.3f,  0.02f, 0.0f,  0.0f,  0.0f,  0.1f,  0.05f, 0.3f  },
            /* 18 Lake            */ { 0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f  },
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
        /// Backward-compatible shorthand for Get(biomeId, GoodType.Food).
        /// </summary>
        public static float GetFood(int biomeId)
        {
            if (biomeId < 0 || biomeId >= BiomeCount)
                return 0f;
            return Values[biomeId, (int)GoodType.Food];
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
