using UnityEngine;
using MapGen.Core;

namespace MapGen
{
    /// <summary>
    /// Master orchestrator: exposes essential params, derives sub-seeds,
    /// and runs the full generation pipeline with one call.
    /// </summary>
    [RequireComponent(typeof(CellMeshGenerator))]
    [RequireComponent(typeof(HeightmapGenerator))]
    [RequireComponent(typeof(ClimateGenerator))]
    [RequireComponent(typeof(RiverGenerator))]
    [RequireComponent(typeof(BiomeGenerator))]
    [RequireComponent(typeof(CellMeshVisualizer))]
    public class MapGenerator : MonoBehaviour
    {
        public int Seed = 12345;
        public int CellCount = 10000;
        public float AspectRatio = 16f / 9f;
        public HeightmapTemplateType Template = HeightmapTemplateType.LowIsland;
        public float LatitudeSouth = 30f;

        // Sub-seed constants (golden ratio hashing for decorrelation)
        const uint HeightmapXor = 0x9E3779B9;
        const uint RockXor      = 0x517CC1B7;
        const uint IronXor      = 0x6C62272E;
        const uint GoldXor      = 0x2545F491;
        const uint LeadXor      = 0x369DEA0F;

        [System.NonSerialized] public MapStats Stats;

        public void Generate()
        {
            var meshGen = GetComponent<CellMeshGenerator>();
            var heightGen = GetComponent<HeightmapGenerator>();
            var climateGen = GetComponent<ClimateGenerator>();
            var riverGen = GetComponent<RiverGenerator>();
            var biomeGen = GetComponent<BiomeGenerator>();

            // Push params to child generators
            meshGen.Seed = Seed;
            meshGen.CellCount = CellCount;
            meshGen.AspectRatio = AspectRatio;

            heightGen.Template = Template;
            heightGen.HeightmapSeed = (int)((uint)Seed ^ HeightmapXor);

            climateGen.LatitudeSouth = LatitudeSouth;

            biomeGen.RockSeed = (int)((uint)Seed ^ RockXor);
            biomeGen.IronSeed = (int)((uint)Seed ^ IronXor);
            biomeGen.GoldSeed = (int)((uint)Seed ^ GoldXor);
            biomeGen.LeadSeed = (int)((uint)Seed ^ LeadXor);

            // Run pipeline in order
            var sw = System.Diagnostics.Stopwatch.StartNew();

            meshGen.Generate();
            heightGen.Generate();
            climateGen.Generate();
            riverGen.Generate();
            biomeGen.Generate();

            sw.Stop();

            // Invalidate visualizer cache so climate ranges get recomputed
            var visualizer = GetComponent<CellMeshVisualizer>();
            if (visualizer != null)
                visualizer.InvalidateCache();

            // Compute stats
            Stats = MapStats.Compute(meshGen, heightGen, climateGen, riverGen, biomeGen);

            Debug.Log($"Map generated in {sw.ElapsedMilliseconds}ms");
        }
    }

    /// <summary>
    /// Aggregated statistics from all pipeline stages.
    /// </summary>
    public struct MapStats
    {
        // Terrain
        public int CellCount, LandCells, WaterCells;
        public float MapWidth, MapHeight;

        // Climate
        public float TempMin, TempMax;
        public float PrecipLandMin, PrecipLandMax, PrecipLandAvg;

        // Rivers
        public int RiverCount, RiverSegments, LakeVertices, LakeCells;
        public float MaxFlux;

        // Biomes
        public int[] BiomeCounts;
        public string[] BiomeNames;

        // Soil
        public int[] SoilCounts;
        public string[] SoilNames;
        public float FertilityAvg, FertilityMax;

        // Vegetation
        public int[] VegetationCounts;
        public string[] VegetationNames;
        public float VegetationDensityAvg;

        // Resources
        public int IronCells, GoldCells, LeadCells, SaltCells, StoneCells;

        public bool IsValid;

        public static MapStats Compute(
            CellMeshGenerator meshGen,
            HeightmapGenerator heightGen,
            ClimateGenerator climateGen,
            RiverGenerator riverGen,
            BiomeGenerator biomeGen)
        {
            var stats = new MapStats { IsValid = true };

            var mesh = meshGen.Mesh;
            var heights = heightGen.HeightGrid;
            var climate = climateGen.ClimateData;
            var rivers = riverGen.RiverData;
            var biomes = biomeGen.BiomeData;

            if (mesh == null || heights == null || climate == null || rivers == null || biomes == null)
            {
                stats.IsValid = false;
                return stats;
            }

            // Terrain
            stats.CellCount = mesh.CellCount;
            var (land, water) = heights.CountLandWater();
            stats.LandCells = land;
            stats.WaterCells = water;
            stats.MapWidth = mesh.Width;
            stats.MapHeight = mesh.Height;

            // Climate
            var (tMin, tMax) = climate.TemperatureRange();
            stats.TempMin = tMin;
            stats.TempMax = tMax;

            float pLandMin = float.MaxValue, pLandMax = float.MinValue, pLandSum = 0f;
            int landCount = 0;
            for (int i = 0; i < mesh.CellCount; i++)
            {
                if (heights.IsWater(i)) continue;
                landCount++;
                float p = climate.Precipitation[i];
                pLandSum += p;
                if (p < pLandMin) pLandMin = p;
                if (p > pLandMax) pLandMax = p;
            }
            stats.PrecipLandMin = landCount > 0 ? pLandMin : 0;
            stats.PrecipLandMax = landCount > 0 ? pLandMax : 0;
            stats.PrecipLandAvg = landCount > 0 ? pLandSum / landCount : 0;

            // Rivers
            stats.RiverCount = rivers.Rivers.Length;
            int totalSegments = 0;
            foreach (var r in rivers.Rivers)
                totalSegments += r.Vertices.Length - 1;
            stats.RiverSegments = totalSegments;

            int lakeCount = 0;
            float maxFlux = 0;
            for (int v = 0; v < mesh.VertexCount; v++)
            {
                if (rivers.IsLake(v)) lakeCount++;
                if (!rivers.IsOcean(v) && rivers.VertexFlux[v] > maxFlux)
                    maxFlux = rivers.VertexFlux[v];
            }
            stats.LakeVertices = lakeCount;
            stats.MaxFlux = maxFlux;

            int lakeCellCount = 0;
            for (int i = 0; i < mesh.CellCount; i++)
            {
                if (biomes.IsLakeCell[i]) lakeCellCount++;
            }
            stats.LakeCells = lakeCellCount;

            // Biomes
            stats.BiomeCounts = biomes.BiomeCounts(heights);
            stats.BiomeNames = System.Enum.GetNames(typeof(BiomeId));

            // Soil
            stats.SoilCounts = biomes.SoilCounts(heights);
            stats.SoilNames = System.Enum.GetNames(typeof(SoilType));

            float fertSum = 0, fertMax = 0;
            for (int i = 0; i < mesh.CellCount; i++)
            {
                if (heights.IsWater(i)) continue;
                fertSum += biomes.Fertility[i];
                if (biomes.Fertility[i] > fertMax) fertMax = biomes.Fertility[i];
            }
            stats.FertilityAvg = landCount > 0 ? fertSum / landCount : 0;
            stats.FertilityMax = fertMax;

            // Vegetation
            int vegTypeCount = System.Enum.GetValues(typeof(VegetationType)).Length;
            stats.VegetationCounts = new int[vegTypeCount];
            stats.VegetationNames = System.Enum.GetNames(typeof(VegetationType));
            float densitySum = 0;
            for (int i = 0; i < mesh.CellCount; i++)
            {
                if (heights.IsWater(i)) continue;
                stats.VegetationCounts[(int)biomes.Vegetation[i]]++;
                densitySum += biomes.VegetationDensity[i];
            }
            stats.VegetationDensityAvg = landCount > 0 ? densitySum / landCount : 0;

            // Resources
            for (int i = 0; i < mesh.CellCount; i++)
            {
                if (heights.IsWater(i)) continue;
                if (biomes.IronAbundance[i] > 0.01f) stats.IronCells++;
                if (biomes.GoldAbundance[i] > 0.01f) stats.GoldCells++;
                if (biomes.LeadAbundance[i] > 0.01f) stats.LeadCells++;
                if (biomes.SaltAbundance[i] > 0.01f) stats.SaltCells++;
                if (biomes.StoneAbundance[i] > 0.01f) stats.StoneCells++;
            }

            return stats;
        }
    }
}
