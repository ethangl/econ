using UnityEngine;
using MapGen.Core;

namespace MapGen
{
    [RequireComponent(typeof(ClimateGenerator))]
    public class RiverGenerator : MonoBehaviour
    {
        public float Threshold = 90f;
        public int MinVertices = 2;

        [Header("Debug Overlay")]
        public RiverOverlay Overlay = RiverOverlay.None;

        private ClimateGenerator _climateGenerator;
        private HeightmapGenerator _heightmapGenerator;
        private CellMeshGenerator _meshGenerator;
        private RiverData _riverData;

        public RiverData RiverData => _riverData;

        void Awake()
        {
            _climateGenerator = GetComponent<ClimateGenerator>();
            _heightmapGenerator = GetComponent<HeightmapGenerator>();
            _meshGenerator = GetComponent<CellMeshGenerator>();
        }

        public void Generate()
        {
            if (_climateGenerator == null)
                _climateGenerator = GetComponent<ClimateGenerator>();
            if (_heightmapGenerator == null)
                _heightmapGenerator = GetComponent<HeightmapGenerator>();
            if (_meshGenerator == null)
                _meshGenerator = GetComponent<CellMeshGenerator>();

            if (_meshGenerator?.Mesh == null)
            {
                Debug.LogError("RiverGenerator: No cell mesh available.");
                return;
            }
            if (_heightmapGenerator?.HeightGrid == null)
            {
                Debug.LogError("RiverGenerator: No heightmap available.");
                return;
            }
            if (_climateGenerator?.ClimateData == null)
            {
                Debug.LogError("RiverGenerator: No climate data available.");
                return;
            }

            var mesh = _meshGenerator.Mesh;
            var heights = _heightmapGenerator.HeightGrid;
            var climate = _climateGenerator.ClimateData;

            _riverData = new RiverData(mesh);
            FlowOps.Compute(_riverData, heights, climate, Threshold, MinVertices);

            // Stats
            int lakeCount = 0;
            int landVerts = 0;
            float maxFlux = 0;
            int mouthCount = 0;
            for (int v = 0; v < mesh.VertexCount; v++)
            {
                if (_riverData.IsOcean(v)) continue;
                landVerts++;
                if (_riverData.IsLake(v)) lakeCount++;
                if (_riverData.VertexFlux[v] > maxFlux) maxFlux = _riverData.VertexFlux[v];
                int ft = _riverData.FlowTarget[v];
                if (ft >= 0 && _riverData.IsOcean(ft)) mouthCount++;
            }

            int riverSegments = 0;
            int singleVert = 0;
            foreach (var r in _riverData.Rivers)
            {
                riverSegments += r.Vertices.Length - 1;
                if (r.Vertices.Length <= 1) singleVert++;
            }

            Debug.Log($"Rivers: {_riverData.Rivers.Length} rivers ({singleVert} single-vertex), " +
                      $"{riverSegments} segments, {lakeCount} lake vertices, " +
                      $"threshold={Threshold}");
            Debug.Log($"  landVerts={landVerts}, mouths={mouthCount}, maxFlux={maxFlux:F0}");
        }

        void OnDrawGizmosSelected()
        {
            if (Overlay == RiverOverlay.None || _riverData == null || _meshGenerator?.Mesh == null)
                return;

            var mesh = _meshGenerator.Mesh;

            switch (Overlay)
            {
                case RiverOverlay.Rivers:
                    DrawRiverGizmos(mesh);
                    break;
                case RiverOverlay.Flux:
                    DrawFluxGizmos(mesh);
                    break;
                case RiverOverlay.Lakes:
                    DrawLakeGizmos(mesh);
                    break;
            }
        }

        void DrawRiverGizmos(CellMesh mesh)
        {
            float maxDischarge = 0;
            foreach (var r in _riverData.Rivers)
            {
                if (r.Discharge > maxDischarge)
                    maxDischarge = r.Discharge;
            }
            if (maxDischarge < 1f) maxDischarge = 1f;

            foreach (var river in _riverData.Rivers)
            {
                float t = river.Discharge / maxDischarge;
                Gizmos.color = Color.Lerp(
                    new Color(0.4f, 0.6f, 1f),
                    new Color(0f, 0.1f, 0.8f), t);

                // Consecutive vertices are connected by Voronoi edges — just draw lines
                for (int i = 0; i < river.Vertices.Length - 1; i++)
                {
                    Vec2 p0 = mesh.Vertices[river.Vertices[i]];
                    Vec2 p1 = mesh.Vertices[river.Vertices[i + 1]];
                    Gizmos.DrawLine(
                        new Vector3(p0.X, p0.Y, 0),
                        new Vector3(p1.X, p1.Y, 0));
                }
            }
        }

        void DrawFluxGizmos(CellMesh mesh)
        {
            float maxFlux = 0;
            for (int v = 0; v < mesh.VertexCount; v++)
            {
                if (!_riverData.IsOcean(v) && _riverData.VertexFlux[v] > maxFlux)
                    maxFlux = _riverData.VertexFlux[v];
            }
            if (maxFlux < 1f) maxFlux = 1f;

            for (int v = 0; v < mesh.VertexCount; v++)
            {
                if (_riverData.IsOcean(v))
                {
                    Gizmos.color = new Color(0.2f, 0.2f, 0.25f);
                }
                else
                {
                    float t = _riverData.VertexFlux[v] / maxFlux;
                    t = (float)System.Math.Sqrt(t);
                    Gizmos.color = Color.Lerp(
                        new Color(0.3f, 0.15f, 0f),
                        new Color(0.1f, 0.3f, 1f), t);
                }

                Vec2 pos = mesh.Vertices[v];
                Gizmos.DrawSphere(new Vector3(pos.X, pos.Y, 0), 4f);
            }
        }

        void DrawLakeGizmos(CellMesh mesh)
        {
            for (int v = 0; v < mesh.VertexCount; v++)
            {
                Vec2 pos = mesh.Vertices[v];

                if (_riverData.IsOcean(v))
                    Gizmos.color = new Color(0.1f, 0.1f, 0.3f);
                else if (_riverData.IsLake(v))
                    Gizmos.color = new Color(0.2f, 0.4f, 0.9f);
                else
                    Gizmos.color = new Color(0.4f, 0.35f, 0.25f);

                Gizmos.DrawSphere(new Vector3(pos.X, pos.Y, 0), 4f);
            }
        }
    }

    public enum RiverOverlay
    {
        None,
        Rivers,
        Flux,
        Lakes
    }
}
