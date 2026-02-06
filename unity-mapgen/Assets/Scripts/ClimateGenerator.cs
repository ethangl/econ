using UnityEngine;
using MapGen.Core;

namespace MapGen
{
    /// <summary>
    /// Unity component for generating and visualizing climate data (temperature + precipitation).
    /// Sits between heightmap and rivers in the pipeline.
    /// </summary>
    [RequireComponent(typeof(HeightmapGenerator))]
    public class ClimateGenerator : MonoBehaviour
    {
        [System.NonSerialized] public float LatitudeSouth = 30f;

        private HeightmapGenerator _heightmapGenerator;
        private CellMeshGenerator _meshGenerator;
        private ClimateData _climateData;
        private WorldConfig _config;

        public ClimateData ClimateData => _climateData;
        public WorldConfig Config => _config;

        void Awake()
        {
            _heightmapGenerator = GetComponent<HeightmapGenerator>();
            _meshGenerator = GetComponent<CellMeshGenerator>();
        }

        public void Generate()
        {
            if (_heightmapGenerator == null)
                _heightmapGenerator = GetComponent<HeightmapGenerator>();
            if (_meshGenerator == null)
                _meshGenerator = GetComponent<CellMeshGenerator>();

            if (_meshGenerator == null || _meshGenerator.Mesh == null)
            {
                Debug.LogError("ClimateGenerator: No cell mesh available. Generate mesh first.");
                return;
            }
            if (_heightmapGenerator == null || _heightmapGenerator.HeightGrid == null)
            {
                Debug.LogError("ClimateGenerator: No heightmap available. Generate heightmap first.");
                return;
            }

            var mesh = _meshGenerator.Mesh;
            var heights = _heightmapGenerator.HeightGrid;

            // Build config
            _config = new WorldConfig { LatitudeSouth = LatitudeSouth };
            _config.AutoLatitudeSpan(mesh);

            // Create climate data
            _climateData = new ClimateData(mesh);

            // Temperature
            TemperatureOps.Compute(_climateData, heights, _config);

            // Precipitation
            PrecipitationOps.Compute(_climateData, heights, _config);

            // Log stats
            var (tMin, tMax) = _climateData.TemperatureRange();
            var (pMin, pMax) = _climateData.PrecipitationRange();
            Debug.Log($"Climate generated: temp [{tMin:F1}°C, {tMax:F1}°C], " +
                      $"precip [{pMin:F0}, {pMax:F0}], " +
                      $"lat [{_config.LatitudeSouth:F1}°, {_config.LatitudeNorth:F1}°], " +
                      $"map {mesh.Width:F0} x {mesh.Height:F0} km");

            // Diagnostic: land precipitation distribution
            int[] buckets = new int[10]; // 0-10, 10-20, ..., 90-100
            int landCount = 0;
            float landSum = 0f;
            float landMin = float.MaxValue, landMax = float.MinValue;
            for (int i = 0; i < mesh.CellCount; i++)
            {
                if (heights.IsWater(i)) continue;
                float p = _climateData.Precipitation[i];
                landCount++;
                landSum += p;
                if (p < landMin) landMin = p;
                if (p > landMax) landMax = p;
                int bucket = (int)(p / 10f);
                if (bucket > 9) bucket = 9;
                buckets[bucket]++;
            }
            string hist = "";
            for (int b = 0; b < 10; b++)
                hist += $"  {b * 10}-{b * 10 + 10}: {buckets[b]}";
            Debug.Log($"Land precip: {landCount} cells, range [{landMin:F1}, {landMax:F1}], " +
                      $"avg {landSum / landCount:F1}\n{hist}");

            // Log active wind bands
            for (int b = 0; b < _config.WindBands.Length; b++)
            {
                var band = _config.WindBands[b];
                float overlapMin = Mathf.Max(band.LatMin, _config.LatitudeSouth);
                float overlapMax = Mathf.Min(band.LatMax, _config.LatitudeNorth);
                if (overlapMin >= overlapMax) continue;
                var wv = band.WindVector;
                Debug.Log($"Wind band {b}: lat [{band.LatMin},{band.LatMax}], " +
                          $"compass {band.CompassDegrees}°, " +
                          $"vector ({wv.X:F2}, {wv.Y:F2})");
            }
        }

    }
}
