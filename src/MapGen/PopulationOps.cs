namespace MapGen.Core
{
    /// <summary>
    /// Converts per-cell suitability scores into population values.
    /// pop = suitability * (cellArea / meanLandArea)
    /// </summary>
    public static class PopulationOps
    {
        public static void ComputePopulation(BiomeData biome, HeightGrid heights)
        {
            var mesh = biome.Mesh;
            int n = mesh.CellCount;
            float[] areas = mesh.CellAreas;

            // Compute mean land cell area
            float areaSum = 0f;
            int landCount = 0;
            for (int i = 0; i < n; i++)
            {
                if (heights.IsWater(i) || biome.IsLakeCell[i]) continue;
                areaSum += areas[i];
                landCount++;
            }

            if (landCount == 0) return;
            float meanArea = areaSum / landCount;

            // Assign population
            for (int i = 0; i < n; i++)
            {
                if (heights.IsWater(i) || biome.IsLakeCell[i]) continue;
                biome.Population[i] = biome.Suitability[i] * (areas[i] / meanArea);
            }
        }
    }
}
