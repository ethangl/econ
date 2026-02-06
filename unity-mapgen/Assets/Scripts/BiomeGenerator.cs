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
        [System.NonSerialized] public int RockSeed = 42;
        [System.NonSerialized] public int IronSeed = 137;
        [System.NonSerialized] public int GoldSeed = 271;
        [System.NonSerialized] public int LeadSeed = 389;

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

            // Lake detection (must be first — all other steps skip lake cells)
            BiomeOps.ComputeLakeCells(_biomeData, heights, rivers);

            // Derived inputs
            BiomeOps.ComputeSlope(_biomeData, heights);
            BiomeOps.ComputeSaltProximity(_biomeData, heights);
            BiomeOps.ComputeLakeProximity(_biomeData, heights);
            BiomeOps.ComputeCellFlux(_biomeData, rivers);
            BiomeOps.ComputeRockType(_biomeData, RockSeed);
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
            BiomeOps.ComputeGeologicalResources(_biomeData, heights, IronSeed, GoldSeed, LeadSeed);
            BiomeOps.ComputeSaltResource(_biomeData, heights);
            BiomeOps.ComputeStoneResource(_biomeData, heights);

            // Suitability scoring (depends on all above)
            SuitabilityOps.ComputeSuitability(_biomeData, heights, climate, rivers);
        }
    }
}
