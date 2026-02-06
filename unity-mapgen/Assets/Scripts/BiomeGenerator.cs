using UnityEngine;
using MapGen.Core;

namespace MapGen
{
    /// <summary>
    /// Unity component for the biome pipeline: derived inputs -> soil -> biome -> vegetation -> fauna.
    /// Sits after rivers in the generation pipeline.
    /// </summary>
    [RequireComponent(typeof(RiverGenerator))]
    public class BiomeGenerator : MonoBehaviour
    {
        [Header("Debug Overlay")]
        public BiomeOverlay Overlay = BiomeOverlay.None;

        private RiverGenerator _riverGenerator;
        private ClimateGenerator _climateGenerator;
        private HeightmapGenerator _heightmapGenerator;
        private CellMeshGenerator _meshGenerator;
        private BiomeData _biomeData;

        public BiomeData BiomeData => _biomeData;

        void Awake()
        {
            _riverGenerator = GetComponent<RiverGenerator>();
            _climateGenerator = GetComponent<ClimateGenerator>();
            _heightmapGenerator = GetComponent<HeightmapGenerator>();
            _meshGenerator = GetComponent<CellMeshGenerator>();
        }

        public void Generate()
        {
            if (_riverGenerator == null)
                _riverGenerator = GetComponent<RiverGenerator>();
            if (_climateGenerator == null)
                _climateGenerator = GetComponent<ClimateGenerator>();
            if (_heightmapGenerator == null)
                _heightmapGenerator = GetComponent<HeightmapGenerator>();
            if (_meshGenerator == null)
                _meshGenerator = GetComponent<CellMeshGenerator>();

            if (_meshGenerator?.Mesh == null)
            {
                Debug.LogError("BiomeGenerator: No cell mesh available.");
                return;
            }
            if (_heightmapGenerator?.HeightGrid == null)
            {
                Debug.LogError("BiomeGenerator: No heightmap available.");
                return;
            }
            if (_climateGenerator?.ClimateData == null)
            {
                Debug.LogError("BiomeGenerator: No climate data available.");
                return;
            }
            if (_riverGenerator?.RiverData == null)
            {
                Debug.LogError("BiomeGenerator: No river data available.");
                return;
            }

            var mesh = _meshGenerator.Mesh;
            var heights = _heightmapGenerator.HeightGrid;
            var climate = _climateGenerator.ClimateData;
            var rivers = _riverGenerator.RiverData;

            _biomeData = new BiomeData(mesh);

            var config = _climateGenerator.Config;

            // Derived inputs
            BiomeOps.ComputeSlope(_biomeData, heights);
            BiomeOps.ComputeSaltProximity(_biomeData, heights);
            BiomeOps.ComputeCellFlux(_biomeData, rivers);
            BiomeOps.ComputeRockType(_biomeData, 42);
            BiomeOps.ComputeLoess(_biomeData, heights, climate, config);

            // Stage 1: Soil
            BiomeOps.ClassifySoil(_biomeData, heights, climate);
            BiomeOps.ComputeFertility(_biomeData, heights, climate);

            // Stage 2: Biome
            BiomeOps.AssignBiomes(_biomeData, heights, climate);
            BiomeOps.ComputeHabitability(_biomeData, heights, rivers);

            // Stage 3: Vegetation
            BiomeOps.ComputeVegetation(_biomeData, heights, climate);

            // Stage 4: Fauna + Subsistence
            BiomeOps.ComputeFauna(_biomeData, heights, climate, rivers);
            BiomeOps.ComputeSubsistence(_biomeData, heights, climate);

            // Movement cost + geological resources
            BiomeOps.ComputeMovementCost(_biomeData, heights);
            BiomeOps.ComputeGeologicalResources(_biomeData, heights, 137, 271, 389);
            BiomeOps.ComputeSaltResource(_biomeData, heights);
            BiomeOps.ComputeStoneResource(_biomeData, heights);

            // Stats
            var (land, water) = heights.CountLandWater();
            float maxSlope = 0f, avgSlope = 0f;
            float maxSalt = 0f;
            float maxFlux = 0f, avgFlux = 0f;
            int landCount = 0;
            for (int i = 0; i < mesh.CellCount; i++)
            {
                if (heights.IsWater(i)) continue;
                landCount++;
                if (_biomeData.Slope[i] > maxSlope) maxSlope = _biomeData.Slope[i];
                avgSlope += _biomeData.Slope[i];
                if (_biomeData.SaltEffect[i] > maxSalt) maxSalt = _biomeData.SaltEffect[i];
                if (_biomeData.CellFlux[i] > maxFlux) maxFlux = _biomeData.CellFlux[i];
                avgFlux += _biomeData.CellFlux[i];
            }
            if (landCount > 0) { avgSlope /= landCount; avgFlux /= landCount; }

            Debug.Log($"Biomes generated: {land} land / {water} water cells");
            Debug.Log($"  slope: avg={avgSlope:F3}, max={maxSlope:F3}");
            Debug.Log($"  salt: max={maxSalt:F2}");
            float maxLoess = 0f, avgLoess = 0f;
            int[] rockCounts = new int[4];
            for (int i = 0; i < mesh.CellCount; i++)
            {
                if (heights.IsWater(i)) continue;
                if (_biomeData.Loess[i] > maxLoess) maxLoess = _biomeData.Loess[i];
                avgLoess += _biomeData.Loess[i];
                rockCounts[(int)_biomeData.Rock[i]]++;
            }
            if (landCount > 0) avgLoess /= landCount;

            Debug.Log($"  cellFlux: avg={avgFlux:F1}, max={maxFlux:F1}");
            Debug.Log($"  loess: avg={avgLoess:F3}, max={maxLoess:F3}");
            Debug.Log($"  rock: granite={rockCounts[0]}, sedimentary={rockCounts[1]}, " +
                      $"limestone={rockCounts[2]}, volcanic={rockCounts[3]}");

            // Soil stats
            int[] soilCounts = _biomeData.SoilCounts(heights);
            string soilStr = "";
            var soilNames = System.Enum.GetNames(typeof(SoilType));
            for (int i = 0; i < soilNames.Length; i++)
                soilStr += $"{soilNames[i]}={soilCounts[i]} ";
            Debug.Log($"  soil: {soilStr.Trim()}");

            float avgFert = 0f, maxFert = 0f;
            for (int i = 0; i < mesh.CellCount; i++)
            {
                if (heights.IsWater(i)) continue;
                avgFert += _biomeData.Fertility[i];
                if (_biomeData.Fertility[i] > maxFert) maxFert = _biomeData.Fertility[i];
            }
            if (landCount > 0) avgFert /= landCount;
            Debug.Log($"  fertility: avg={avgFert:F3}, max={maxFert:F3}");

            // Biome stats
            int[] biomeCounts = _biomeData.BiomeCounts(heights);
            var biomeNames = System.Enum.GetNames(typeof(BiomeId));
            string biomeStr = "";
            for (int i = 0; i < biomeNames.Length; i++)
            {
                if (biomeCounts[i] > 0)
                    biomeStr += $"{biomeNames[i]}={biomeCounts[i]} ";
            }
            Debug.Log($"  biomes: {biomeStr.Trim()}");

            float avgHab = 0f;
            for (int i = 0; i < mesh.CellCount; i++)
            {
                if (heights.IsWater(i)) continue;
                avgHab += _biomeData.Habitability[i];
            }
            if (landCount > 0) avgHab /= landCount;
            Debug.Log($"  habitability: avg={avgHab:F1}");

            // Vegetation stats
            int[] vegCounts = new int[7];
            float avgDensity = 0f;
            for (int i = 0; i < mesh.CellCount; i++)
            {
                if (heights.IsWater(i)) continue;
                vegCounts[(int)_biomeData.Vegetation[i]]++;
                avgDensity += _biomeData.VegetationDensity[i];
            }
            if (landCount > 0) avgDensity /= landCount;
            var vegNames = System.Enum.GetNames(typeof(VegetationType));
            string vegStr = "";
            for (int i = 0; i < vegNames.Length; i++)
            {
                if (vegCounts[i] > 0)
                    vegStr += $"{vegNames[i]}={vegCounts[i]} ";
            }
            Debug.Log($"  vegetation: {vegStr.Trim()}, avgDensity={avgDensity:F3}");

            // Fauna + subsistence stats
            float avgFish = 0f, avgGame = 0f, avgSub = 0f;
            int fishCells = 0;
            for (int i = 0; i < mesh.CellCount; i++)
            {
                if (heights.IsWater(i)) continue;
                avgFish += _biomeData.FishAbundance[i];
                avgGame += _biomeData.GameAbundance[i];
                avgSub += _biomeData.Subsistence[i];
                if (_biomeData.FishAbundance[i] > 0.01f) fishCells++;
            }
            if (landCount > 0) { avgFish /= landCount; avgGame /= landCount; avgSub /= landCount; }
            Debug.Log($"  fauna: avgFish={avgFish:F3} ({fishCells} cells), avgGame={avgGame:F3}");
            Debug.Log($"  subsistence: avg={avgSub:F3}");

            // Movement cost stats
            float avgCost = 0f, minCost = float.MaxValue, maxCost = 0f;
            int ironCells = 0, goldCells = 0, leadCells = 0, saltCells = 0, stoneCells = 0;
            for (int i = 0; i < mesh.CellCount; i++)
            {
                if (heights.IsWater(i)) continue;
                float c = _biomeData.MovementCost[i];
                avgCost += c;
                if (c < minCost) minCost = c;
                if (c > maxCost) maxCost = c;
                if (_biomeData.IronAbundance[i] > 0.01f) ironCells++;
                if (_biomeData.GoldAbundance[i] > 0.01f) goldCells++;
                if (_biomeData.LeadAbundance[i] > 0.01f) leadCells++;
                if (_biomeData.SaltAbundance[i] > 0.01f) saltCells++;
                if (_biomeData.StoneAbundance[i] > 0.01f) stoneCells++;
            }
            if (landCount > 0) avgCost /= landCount;
            Debug.Log($"  movementCost: avg={avgCost:F2}, range=[{minCost:F2}, {maxCost:F2}]");
            Debug.Log($"  resources: iron={ironCells}, gold={goldCells}, lead={leadCells}, salt={saltCells}, stone={stoneCells} cells");
        }

        void OnDrawGizmosSelected()
        {
            if (Overlay == BiomeOverlay.None || _biomeData == null || _meshGenerator?.Mesh == null)
                return;

            var mesh = _meshGenerator.Mesh;
            var heights = _heightmapGenerator?.HeightGrid;
            if (heights == null) return;

            switch (Overlay)
            {
                case BiomeOverlay.Slope:
                    DrawHeatmap(mesh, heights, _biomeData.Slope, 0f, 1f);
                    break;
                case BiomeOverlay.SaltEffect:
                    DrawHeatmap(mesh, heights, _biomeData.SaltEffect, 0f, 1f);
                    break;
                case BiomeOverlay.Loess:
                    DrawHeatmap(mesh, heights, _biomeData.Loess, 0f, 1f);
                    break;
                case BiomeOverlay.RockType:
                    DrawRockTypeGizmos(mesh, heights);
                    break;
                case BiomeOverlay.Soil:
                    DrawSoilGizmos(mesh, heights);
                    break;
                case BiomeOverlay.Fertility:
                    DrawHeatmap(mesh, heights, _biomeData.Fertility, 0f, 1f);
                    break;
                case BiomeOverlay.Biomes:
                    DrawBiomeGizmos(mesh, heights);
                    break;
                case BiomeOverlay.Habitability:
                    DrawHeatmap(mesh, heights, _biomeData.Habitability, 0f, 100f);
                    break;
                case BiomeOverlay.Vegetation:
                    DrawVegetationGizmos(mesh, heights);
                    break;
                case BiomeOverlay.VegetationDensity:
                    DrawHeatmap(mesh, heights, _biomeData.VegetationDensity, 0f, 1f);
                    break;
                case BiomeOverlay.Subsistence:
                    DrawHeatmap(mesh, heights, _biomeData.Subsistence, 0f, 1f);
                    break;
                case BiomeOverlay.MovementCost:
                    DrawHeatmap(mesh, heights, _biomeData.MovementCost, 1f, 8f);
                    break;
            }
        }

        // Vegetation type colors from spec
        static readonly Color[] VegetationColors = new Color[]
        {
            new Color(0.70f, 0.65f, 0.55f), // None - bare ground
            new Color(0.55f, 0.60f, 0.50f), // LichenMoss - muted gray-green
            new Color(0.70f, 0.75f, 0.30f), // Grass - gold-green
            new Color(0.50f, 0.55f, 0.30f), // Shrub - dusty olive
            new Color(0.30f, 0.60f, 0.20f), // DeciduousForest - green
            new Color(0.15f, 0.35f, 0.30f), // ConiferousForest - dark blue-green
            new Color(0.10f, 0.40f, 0.15f), // BroadleafForest - deep green
        };

        void DrawVegetationGizmos(CellMesh mesh, HeightGrid heights)
        {
            for (int i = 0; i < mesh.CellCount; i++)
            {
                Vec2 center = mesh.CellCenters[i];
                Gizmos.color = heights.IsWater(i)
                    ? new Color(0.2f, 0.2f, 0.25f)
                    : VegetationColors[(int)_biomeData.Vegetation[i]];
                Gizmos.DrawSphere(new Vector3(center.X, center.Y, 0), 6f);
            }
        }

        // 18 biome colors - visually distinct for overlay
        static readonly Color[] BiomeColors = new Color[]
        {
            new Color(0.85f, 0.92f, 0.98f), // Glacier - ice white-blue
            new Color(0.70f, 0.75f, 0.65f), // Tundra - gray-green
            new Color(0.95f, 0.93f, 0.85f), // SaltFlat - off-white
            new Color(0.45f, 0.60f, 0.45f), // CoastalMarsh - muted green
            new Color(0.60f, 0.58f, 0.55f), // AlpineBarren - gray
            new Color(0.55f, 0.58f, 0.40f), // MountainShrub - olive
            new Color(0.40f, 0.65f, 0.25f), // Floodplain - bright green
            new Color(0.30f, 0.50f, 0.40f), // Wetland - teal
            new Color(0.92f, 0.82f, 0.45f), // HotDesert - sand yellow
            new Color(0.75f, 0.72f, 0.60f), // ColdDesert - pale brown
            new Color(0.70f, 0.65f, 0.40f), // Scrubland - dusty olive
            new Color(0.10f, 0.45f, 0.15f), // TropicalRainforest - deep green
            new Color(0.25f, 0.50f, 0.20f), // TropicalDryForest - medium green
            new Color(0.75f, 0.70f, 0.30f), // Savanna - gold-green
            new Color(0.20f, 0.35f, 0.30f), // BorealForest - dark blue-green
            new Color(0.25f, 0.55f, 0.20f), // TemperateForest - green
            new Color(0.65f, 0.72f, 0.35f), // Grassland - gold-green
            new Color(0.35f, 0.55f, 0.25f), // Woodland - medium green
        };

        void DrawBiomeGizmos(CellMesh mesh, HeightGrid heights)
        {
            for (int i = 0; i < mesh.CellCount; i++)
            {
                Vec2 center = mesh.CellCenters[i];
                Gizmos.color = heights.IsWater(i)
                    ? new Color(0.2f, 0.2f, 0.25f)
                    : BiomeColors[(int)_biomeData.Biome[i]];
                Gizmos.DrawSphere(new Vector3(center.X, center.Y, 0), 6f);
            }
        }

        // Soil colors from spec: visually distinct, match real-world analogues
        static readonly Color[] SoilColors = new Color[]
        {
            new Color(0.60f, 0.65f, 0.80f), // Permafrost - blue-gray
            new Color(0.90f, 0.88f, 0.82f), // Saline - white/pale
            new Color(0.55f, 0.55f, 0.55f), // Lithosol - rocky gray
            new Color(0.35f, 0.22f, 0.10f), // Alluvial - dark brown
            new Color(0.85f, 0.78f, 0.45f), // Aridisol - sandy yellow
            new Color(0.80f, 0.35f, 0.15f), // Laterite - red-orange
            new Color(0.50f, 0.42f, 0.35f), // Podzol - gray-brown
            new Color(0.15f, 0.12f, 0.08f), // Chernozem - near-black
        };

        void DrawSoilGizmos(CellMesh mesh, HeightGrid heights)
        {
            for (int i = 0; i < mesh.CellCount; i++)
            {
                Vec2 center = mesh.CellCenters[i];
                Gizmos.color = heights.IsWater(i)
                    ? new Color(0.2f, 0.2f, 0.25f)
                    : SoilColors[(int)_biomeData.Soil[i]];
                Gizmos.DrawSphere(new Vector3(center.X, center.Y, 0), 6f);
            }
        }

        static readonly Color[] RockColors = new Color[]
        {
            new Color(0.55f, 0.55f, 0.55f), // Granite - gray
            new Color(0.76f, 0.70f, 0.50f), // Sedimentary - sandy
            new Color(0.85f, 0.85f, 0.75f), // Limestone - pale cream
            new Color(0.40f, 0.25f, 0.25f), // Volcanic - dark red-brown
        };

        void DrawRockTypeGizmos(CellMesh mesh, HeightGrid heights)
        {
            for (int i = 0; i < mesh.CellCount; i++)
            {
                Vec2 center = mesh.CellCenters[i];
                Gizmos.color = heights.IsWater(i)
                    ? new Color(0.2f, 0.2f, 0.25f)
                    : RockColors[(int)_biomeData.Rock[i]];
                Gizmos.DrawSphere(new Vector3(center.X, center.Y, 0), 6f);
            }
        }

        /// <summary>
        /// Draw a float array as red(low)->white(mid)->blue(high) heatmap.
        /// Water cells drawn as dark gray.
        /// </summary>
        void DrawHeatmap(CellMesh mesh, HeightGrid heights, float[] values, float min, float max)
        {
            float range = max - min;
            if (range < 1e-6f) range = 1f;

            for (int i = 0; i < mesh.CellCount; i++)
            {
                Vec2 center = mesh.CellCenters[i];
                Color color;

                if (heights.IsWater(i))
                {
                    color = new Color(0.2f, 0.2f, 0.25f);
                }
                else
                {
                    float t = (values[i] - min) / range;
                    if (t < 0f) t = 0f;
                    if (t > 1f) t = 1f;

                    if (t < 0.5f)
                        color = Color.Lerp(new Color(0.9f, 0.1f, 0.1f), Color.white, t * 2f);
                    else
                        color = Color.Lerp(Color.white, new Color(0.1f, 0.2f, 0.9f), (t - 0.5f) * 2f);
                }

                Gizmos.color = color;
                Gizmos.DrawSphere(new Vector3(center.X, center.Y, 0), 6f);
            }
        }
    }

    public enum BiomeOverlay
    {
        None,
        Slope,
        SaltEffect,
        Loess,
        RockType,
        Soil,
        Fertility,
        Biomes,
        Habitability,
        Vegetation,
        VegetationDensity,
        Subsistence,
        MovementCost
    }
}
