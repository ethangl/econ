using UnityEngine;
using MapGen.Core;

namespace MapGen
{
    /// <summary>
    /// Unity component for the political pipeline: landmass -> capitals -> realms -> provinces -> counties.
    /// Sits after biomes in the generation pipeline.
    /// </summary>
    [RequireComponent(typeof(BiomeGenerator))]
    public class PoliticalGenerator : MonoBehaviour
    {
        private BiomeGenerator _biomeGenerator;
        private HeightmapGenerator _heightmapGenerator;
        private CellMeshGenerator _meshGenerator;
        private PoliticalData _politicalData;

        public PoliticalData PoliticalData => _politicalData;

        void Awake()
        {
            _biomeGenerator = GetComponent<BiomeGenerator>();
            _heightmapGenerator = GetComponent<HeightmapGenerator>();
            _meshGenerator = GetComponent<CellMeshGenerator>();
        }

        public void Generate()
        {
            if (_biomeGenerator == null)
                _biomeGenerator = GetComponent<BiomeGenerator>();
            if (_heightmapGenerator == null)
                _heightmapGenerator = GetComponent<HeightmapGenerator>();
            if (_meshGenerator == null)
                _meshGenerator = GetComponent<CellMeshGenerator>();

            if (_meshGenerator?.Mesh == null)
            {
                Debug.LogError("PoliticalGenerator: No cell mesh available.");
                return;
            }
            if (_heightmapGenerator?.HeightGrid == null)
            {
                Debug.LogError("PoliticalGenerator: No heightmap available.");
                return;
            }
            if (_biomeGenerator?.BiomeData == null)
            {
                Debug.LogError("PoliticalGenerator: No biome data available.");
                return;
            }

            var mesh = _meshGenerator.Mesh;
            var heights = _heightmapGenerator.HeightGrid;
            var biomes = _biomeGenerator.BiomeData;

            _politicalData = new PoliticalData(mesh);

            PoliticalOps.DetectLandmasses(_politicalData, heights, biomes);
            PoliticalOps.PlaceCapitals(_politicalData, biomes, heights);
            PoliticalOps.GrowRealms(_politicalData, biomes, heights);
            PoliticalOps.NormalizeRealms(_politicalData);
            PoliticalOps.SubdivideProvinces(_politicalData, biomes, heights);
            PoliticalOps.GroupCounties(_politicalData, biomes, heights);
        }
    }
}
