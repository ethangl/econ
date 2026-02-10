using System;

namespace MapGen.Core
{
    /// <summary>
    /// Static per-cell suitability scoring combining habitability, subsistence,
    /// geographic bonuses, and visible resources.
    /// Feeds future population seeding and settlement placement.
    /// </summary>
    public static class SuitabilityOps
    {
        // --- Base score weights ---
        const float SubsistenceFloor = 0.3f;
        const float SubsistenceWeight = 0.7f;

        // --- Hard gates (smoothed values must exceed these to score at all) ---
        const float MinHabitability = 10f;
        const float MinSubsistence = 0.05f;

        // --- Neighborhood smoothing ---
        const float SelfWeight = 0.33f; // weight of cell's own value vs neighbor average

        // --- Geographic multiplier weights (scale base score) ---
        const float CoastalMult = 0.05f;
        const float EstuaryMult = 0.6f;
        const float ConfluenceMult = 0.35f;
        const float SafeHarborMult = 0.03f;
        const float DefensibilityMult = 0.08f;
        const float TimberMult = 0.06f;
        const float FreshWaterMult = 0.05f;
        const float ChokepointMult = 0.06f;

        // --- Resource bonus (additive, small) ---
        const float SaltWeight = 3f;
        const float StoneWeight = 3f;

        // --- Detection thresholds ---
        const float ConfluenceFluxThreshold = 30f;
        const float DefensibilityCenter = 0.375f;  // normalized height ~50 out of 80 land range
        const float DefensibilitySigma = 0.25f;

        public static void ComputeSuitability(BiomeData biome, HeightGrid heights,
            ClimateData climate, RiverData rivers)
        {
            var mesh = biome.Mesh;
            int n = mesh.CellCount;

            // Pre-compute smoothed habitability and subsistence (2-hop neighborhood average)
            float[] smoothHab = new float[n];
            float[] smoothSub = new float[n];
            float neighborWeight = 1f - SelfWeight;

            for (int i = 0; i < n; i++)
            {
                if (heights.IsWater(i) || biome.IsLakeCell[i]) continue;

                float habSum = 0f, subSum = 0f;
                int count = 0;
                int[] neighbors = mesh.CellNeighbors[i];

                for (int nb = 0; nb < neighbors.Length; nb++)
                {
                    int n1 = neighbors[nb];
                    if (heights.IsWater(n1) || biome.IsLakeCell[n1]) continue;

                    habSum += biome.Habitability[n1];
                    subSum += biome.Subsistence[n1];
                    count++;

                    // 2-hop neighbors
                    int[] hop2 = mesh.CellNeighbors[n1];
                    for (int nb2 = 0; nb2 < hop2.Length; nb2++)
                    {
                        int n2 = hop2[nb2];
                        if (n2 == i) continue;
                        if (heights.IsWater(n2) || biome.IsLakeCell[n2]) continue;

                        habSum += biome.Habitability[n2];
                        subSum += biome.Subsistence[n2];
                        count++;
                    }
                }

                float neighborHab = count > 0 ? habSum / count : biome.Habitability[i];
                float neighborSub = count > 0 ? subSum / count : biome.Subsistence[i];

                smoothHab[i] = SelfWeight * biome.Habitability[i] + neighborWeight * neighborHab;
                smoothSub[i] = SelfWeight * biome.Subsistence[i] + neighborWeight * neighborSub;
            }

            // Main scoring loop
            for (int i = 0; i < n; i++)
            {
                if (heights.IsWater(i) || biome.IsLakeCell[i]) continue;

                // Hard gates: area must meet minimum habitability and subsistence
                if (smoothHab[i] < MinHabitability || smoothSub[i] < MinSubsistence) continue;

                // Base score uses smoothed values
                float baseScore = smoothHab[i] * (SubsistenceFloor + SubsistenceWeight * (float)Math.Sqrt(smoothSub[i]));

                // --- Geographic detection ---
                int[] neighbors = mesh.CellNeighbors[i];
                int[] edges = mesh.CellEdges[i];

                int waterNeighborCount = 0;
                int landNeighborCount = 0;
                int totalNeighborCount = neighbors.Length;

                for (int nb = 0; nb < neighbors.Length; nb++)
                {
                    if (heights.IsWater(neighbors[nb]))
                        waterNeighborCount++;
                    else
                        landNeighborCount++;
                }

                bool isCoastal = waterNeighborCount > 0;
                bool isSafeHarbor = waterNeighborCount == 1;

                // Estuary: coastal AND any edge with river flux
                bool isEstuary = false;
                if (isCoastal && edges != null)
                {
                    for (int e = 0; e < edges.Length; e++)
                    {
                        if (rivers.EdgeFlux[edges[e]] > 0f)
                        {
                            isEstuary = true;
                            break;
                        }
                    }
                }

                // Confluence: 2+ edges with significant flux
                int confluenceEdgeCount = 0;
                if (edges != null)
                {
                    for (int e = 0; e < edges.Length; e++)
                    {
                        if (rivers.EdgeFlux[edges[e]] >= ConfluenceFluxThreshold)
                            confluenceEdgeCount++;
                    }
                }
                bool isConfluence = confluenceEdgeCount >= 2;

                // Defensibility: Gaussian bell on land-normalized height
                float landHeight = ElevationDomains.NormalizeLandHeight(heights.Heights[i], heights.Domain);
                if (landHeight < 0f) landHeight = 0f;
                float dh = landHeight - DefensibilityCenter;
                float defensibility = (float)Math.Exp(-0.5 * (dh * dh) / (DefensibilitySigma * DefensibilitySigma));

                // Timber: forest vegetation types * density
                float timber = 0f;
                var veg = biome.Vegetation[i];
                if (veg == VegetationType.DeciduousForest ||
                    veg == VegetationType.ConiferousForest ||
                    veg == VegetationType.BroadleafForest)
                {
                    timber = biome.VegetationDensity[i];
                }

                // Fresh water: precipitation / 100, clamped to 1
                float freshWater = climate.Precipitation[i] / 100f;
                if (freshWater > 1f) freshWater = 1f;

                // Chokepoint: (1 - landNeighbors/totalNeighbors), nonzero only for mixed cells
                float chokepoint = 0f;
                if (waterNeighborCount > 0 && landNeighborCount > 0 && totalNeighborCount > 0)
                {
                    chokepoint = 1f - (float)landNeighborCount / totalNeighborCount;
                }

                // --- Accumulate geographic multiplier ---
                float geoMult = 0f;
                if (isCoastal) geoMult += CoastalMult;
                if (isEstuary) geoMult += EstuaryMult;
                if (isConfluence) geoMult += ConfluenceMult;
                if (isSafeHarbor) geoMult += SafeHarborMult;
                geoMult += defensibility * DefensibilityMult;
                geoMult += timber * TimberMult;
                geoMult += freshWater * FreshWaterMult;
                geoMult += chokepoint * ChokepointMult;

                // --- Resource bonus (small additive) ---
                float resourceBonus = biome.SaltAbundance[i] * SaltWeight
                                    + biome.StoneAbundance[i] * StoneWeight;

                float geoBonus = baseScore * geoMult;
                biome.SuitabilityGeo[i] = geoBonus;
                biome.Suitability[i] = baseScore + geoBonus + resourceBonus;
            }
        }
    }
}
