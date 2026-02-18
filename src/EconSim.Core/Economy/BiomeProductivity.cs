namespace EconSim.Core.Economy
{
    /// <summary>
    /// Static biome -> productivity lookup. kg produced per person per day.
    /// Indexed by [biomeId, goodType].
    /// </summary>
    public static class BiomeProductivity
    {
        // [biomeId, goodType] — Food=0, Timber=1, Ore=2
        static readonly float[,] Values =
        {
            //                     Food  Timber  Ore
            /* 0  Glacier         */ { 0.0f, 0.0f, 0.0f },
            /* 1  Tundra          */ { 0.2f, 0.0f, 0.0f },
            /* 2  Salt Flat       */ { 0.1f, 0.0f, 0.1f },
            /* 3  Coastal Marsh   */ { 0.7f, 0.0f, 0.0f },
            /* 4  Alpine Barren   */ { 0.2f, 0.0f, 0.4f },
            /* 5  Mountain Shrub  */ { 0.4f, 0.1f, 0.3f },
            /* 6  Floodplain      */ { 1.4f, 0.0f, 0.0f },
            /* 7  Wetland         */ { 0.7f, 0.1f, 0.0f },
            /* 8  Hot Desert      */ { 0.3f, 0.0f, 0.2f },
            /* 9  Cold Desert     */ { 0.3f, 0.0f, 0.2f },
            /* 10 Scrubland       */ { 0.5f, 0.1f, 0.1f },
            /* 11 Tropical Rainf  */ { 0.8f, 0.5f, 0.0f },
            /* 12 Tropical Dry F  */ { 0.9f, 0.4f, 0.0f },
            /* 13 Savanna         */ { 1.1f, 0.1f, 0.0f },
            /* 14 Boreal Forest   */ { 0.6f, 0.5f, 0.0f },
            /* 15 Temperate Fore  */ { 0.9f, 0.5f, 0.0f },
            /* 16 Grassland       */ { 1.3f, 0.0f, 0.0f },
            /* 17 Woodland        */ { 1.0f, 0.3f, 0.0f },
            /* 18 Lake            */ { 0.0f, 0.0f, 0.0f },
        };

        static readonly int BiomeCount = Values.GetLength(0);

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
