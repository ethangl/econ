using System.Collections.Generic;
using UnityEngine;
using EconSim.Core.Data;
using EconSim.Bridge;

namespace EconSim.Renderer
{
    /// <summary>
    /// Renders rivers as line strips through cell centers.
    /// </summary>
    public class RiverRenderer : MonoBehaviour
    {
        [Header("River Settings")]
        [SerializeField] private float baseWidth = 0.015f;
        [SerializeField] private float widthScale = 0.003f;  // Multiplied by river.Width
        [SerializeField] private float heightOffset = 0.005f;
        [SerializeField] private Color riverColor = new Color(0.2f, 0.5f, 0.8f, 1f);
        [SerializeField] private int samplesPerSegment = 8;  // Spline samples between each cell center

        [Header("Display Options")]
        [SerializeField] private bool showRivers = true;

        [Header("References")]
        [SerializeField] private Material riverMaterial;

        private MapData mapData;
        private float cellScale;
        private float heightScale;

        private GameObject riverObject;
        private MeshFilter meshFilter;
        private MeshRenderer meshRenderer;
        private Mesh riverMesh;

        public void Initialize(MapData data, float cellScale, float heightScale)
        {
            this.mapData = data;
            this.cellScale = cellScale;
            this.heightScale = heightScale;

            SetupMeshObject();
            GenerateRivers();
        }

        private void SetupMeshObject()
        {
            riverObject = new GameObject("Rivers");
            riverObject.transform.SetParent(transform, false);
            meshFilter = riverObject.AddComponent<MeshFilter>();
            meshRenderer = riverObject.AddComponent<MeshRenderer>();
            meshRenderer.material = riverMaterial;
            riverObject.SetActive(showRivers);
        }

        public void GenerateRivers()
        {
            if (mapData == null || mapData.Rivers == null) return;

            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            var colors = new List<Color32>();

            Color32 col32 = riverColor;

            foreach (var river in mapData.Rivers)
            {
                if (river.CellPath == null || river.CellPath.Count < 2)
                    continue;

                // Get world positions for each cell in the path
                var pathPoints = new List<Vector3>();
                foreach (int cellId in river.CellPath)
                {
                    if (mapData.CellById.TryGetValue(cellId, out var cell))
                    {
                        float height = GetCellHeight(cell) + heightOffset;
                        Vector3 pos = new Vector3(
                            cell.Center.X * cellScale,
                            height,
                            -cell.Center.Y * cellScale
                        );
                        pathPoints.Add(pos);
                    }
                }

                if (pathPoints.Count < 2)
                    continue;

                // Smooth the path using Catmull-Rom spline interpolation
                var smoothedPath = InterpolateCatmullRom(pathPoints, samplesPerSegment);

                // Calculate max width at mouth
                float maxWidth = baseWidth + river.Width * widthScale;

                // Generate quad strip along the path with tapering width
                for (int i = 0; i < smoothedPath.Count - 1; i++)
                {
                    Vector3 current = smoothedPath[i];
                    Vector3 next = smoothedPath[i + 1];

                    // Taper: narrow at source (i=0), wide at mouth (i=count-1)
                    float tCurrent = (float)i / (smoothedPath.Count - 1);
                    float tNext = (float)(i + 1) / (smoothedPath.Count - 1);

                    // Width ranges from 30% at source to 100% at mouth
                    float widthCurrent = maxWidth * (0.3f + 0.7f * tCurrent);
                    float widthNext = maxWidth * (0.3f + 0.7f * tNext);

                    Vector3 direction = (next - current).normalized;
                    Vector3 perpendicular = Vector3.Cross(direction, Vector3.up).normalized;

                    if (perpendicular.sqrMagnitude < 0.001f)
                    {
                        perpendicular = Vector3.Cross(direction, Vector3.forward).normalized;
                    }

                    Vector3 offsetCurrent = perpendicular * (widthCurrent / 2f);
                    Vector3 offsetNext = perpendicular * (widthNext / 2f);

                    int baseIdx = vertices.Count;

                    vertices.Add(current - offsetCurrent);
                    vertices.Add(current + offsetCurrent);
                    vertices.Add(next + offsetNext);
                    vertices.Add(next - offsetNext);

                    colors.Add(col32);
                    colors.Add(col32);
                    colors.Add(col32);
                    colors.Add(col32);

                    triangles.Add(baseIdx);
                    triangles.Add(baseIdx + 1);
                    triangles.Add(baseIdx + 2);

                    triangles.Add(baseIdx);
                    triangles.Add(baseIdx + 2);
                    triangles.Add(baseIdx + 3);
                }
            }

            Debug.Log($"RiverRenderer: Generated {mapData.Rivers.Count} rivers with {vertices.Count} vertices");

            riverMesh = new Mesh();
            riverMesh.name = "RiverMesh";
            riverMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            riverMesh.SetVertices(vertices);
            riverMesh.SetTriangles(triangles, 0);
            riverMesh.SetColors(colors);
            riverMesh.RecalculateNormals();
            riverMesh.RecalculateBounds();

            meshFilter.mesh = riverMesh;
        }

        /// <summary>
        /// Interpolates a path using Catmull-Rom splines for smooth curves.
        /// </summary>
        private List<Vector3> InterpolateCatmullRom(List<Vector3> points, int samples)
        {
            if (points.Count < 2)
                return new List<Vector3>(points);

            var result = new List<Vector3>();

            for (int i = 0; i < points.Count - 1; i++)
            {
                // Get four control points for the spline segment
                // Catmull-Rom needs P0, P1, P2, P3 to interpolate between P1 and P2
                Vector3 p0 = points[Mathf.Max(0, i - 1)];
                Vector3 p1 = points[i];
                Vector3 p2 = points[i + 1];
                Vector3 p3 = points[Mathf.Min(points.Count - 1, i + 2)];

                // Sample the spline segment
                for (int s = 0; s < samples; s++)
                {
                    float t = (float)s / samples;
                    result.Add(CatmullRomPoint(p0, p1, p2, p3, t));
                }
            }

            // Add the final point
            result.Add(points[points.Count - 1]);

            return result;
        }

        /// <summary>
        /// Evaluates a point on a Catmull-Rom spline segment (uniform parameterization).
        /// </summary>
        private Vector3 CatmullRomPoint(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float t2 = t * t;
            float t3 = t2 * t;

            // Catmull-Rom basis functions
            return 0.5f * (
                2f * p1 +
                (-p0 + p2) * t +
                (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
                (-p0 + 3f * p1 - 3f * p2 + p3) * t3
            );
        }

        private float GetCellHeight(Cell cell)
        {
            float normalizedHeight = (cell.Height - mapData.Info.SeaLevel) / 80f;
            return normalizedHeight * heightScale;
        }

        public void SetRiversVisible(bool visible)
        {
            showRivers = visible;
            if (riverObject != null)
            {
                riverObject.SetActive(visible);
            }
        }

        public void SetRiverColor(Color color)
        {
            riverColor = color;
            if (riverMesh != null)
            {
                var colors = new Color32[riverMesh.vertexCount];
                Color32 col32 = color;
                for (int i = 0; i < colors.Length; i++)
                {
                    colors[i] = col32;
                }
                riverMesh.SetColors(colors);
            }
        }

#if UNITY_EDITOR
        [ContextMenu("Toggle Rivers")]
        private void ToggleRivers() => SetRiversVisible(!showRivers);

        [ContextMenu("Regenerate Rivers")]
        private void RegenerateRivers()
        {
            if (mapData != null)
            {
                GenerateRivers();
            }
        }

#endif
    }
}
