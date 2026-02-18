namespace EconSim.Core.Economy
{
    /// <summary>
    /// Static biome -> productivity lookup. Goods produced per person per day.
    /// </summary>
    public static class BiomeProductivity
    {
        static readonly float[] Values =
        {
            0.0f,  // 0  Glacier
            0.2f,  // 1  Tundra
            0.1f,  // 2  Salt Flat
            0.7f,  // 3  Coastal Marsh
            0.2f,  // 4  Alpine Barren
            0.4f,  // 5  Mountain Shrub
            1.4f,  // 6  Floodplain
            0.7f,  // 7  Wetland
            0.3f,  // 8  Hot Desert
            0.3f,  // 9  Cold Desert
            0.5f,  // 10 Scrubland
            0.8f,  // 11 Tropical Rainforest
            0.9f,  // 12 Tropical Dry Forest
            1.1f,  // 13 Savanna
            0.6f,  // 14 Boreal Forest
            0.9f,  // 15 Temperate Forest
            1.3f,  // 16 Grassland
            1.0f,  // 17 Woodland
            0.0f,  // 18 Lake
        };

        /// <summary>
        /// Get productivity for a biome ID. Returns 0 for unknown biomes.
        /// </summary>
        public static float Get(int biomeId)
        {
            if (biomeId < 0 || biomeId >= Values.Length)
                return 0f;
            return Values[biomeId];
        }
    }
}
