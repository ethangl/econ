using System.Collections.Generic;
using UnityEngine;
using EconSim.Core.Data;
using EconSim.Core.Economy;
using EconSim.Bridge;

namespace EconSim.Renderer
{
    /// <summary>
    /// Renders roads (emerged from trade traffic) as line segments between cell centers.
    /// </summary>
    public class RoadRenderer : MonoBehaviour
    {
        [Header("Road Settings")]
        [SerializeField] private float pathWidth = 0.008f;
        [SerializeField] private float roadWidth = 0.015f;
        [SerializeField] private float heightOffset = 0.008f;
        [SerializeField] private Color pathColor = new Color(0.6f, 0.5f, 0.3f, 1f);  // Light brown
        [SerializeField] private Color roadColor = new Color(0.4f, 0.35f, 0.25f, 1f); // Darker brown

        [Header("Display Options")]
        [SerializeField] private bool showRoads = true;

        private Material roadMaterial;
        private MapData mapData;
        private RoadState roadState;
        private float cellScale;
        private float heightScale;

        private GameObject roadObject;
        private MeshFilter meshFilter;
        private MeshRenderer meshRenderer;
        private Mesh roadMesh;

        public void Initialize(MapData data, RoadState roads, float cellScale, float heightScale)
        {
            this.mapData = data;
            this.roadState = roads;
            this.cellScale = cellScale;
            this.heightScale = heightScale;

            SetupMeshObject();
        }

        private void SetupMeshObject()
        {
            roadMaterial = new Material(Shader.Find("Sprites/Default"));
            roadObject = new GameObject("Roads");
            roadObject.transform.SetParent(transform, false);
            meshFilter = roadObject.AddComponent<MeshFilter>();
            meshRenderer = roadObject.AddComponent<MeshRenderer>();
            meshRenderer.material = roadMaterial;
            roadObject.SetActive(showRoads);

            // Create empty mesh initially
            roadMesh = new Mesh();
            roadMesh.name = "RoadMesh";
            meshFilter.mesh = roadMesh;
        }

        /// <summary>
        /// Regenerate road mesh from current road state.
        /// Call this periodically to update road visualization.
        /// </summary>
        public void RefreshRoads()
        {
            if (mapData == null || roadState == null) return;

            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            var colors = new List<Color32>();

            var roads = roadState.GetAllRoads();

            foreach (var (cellA, cellB, tier) in roads)
            {
                if (!mapData.CellById.TryGetValue(cellA, out var cellDataA))
                    continue;
                if (!mapData.CellById.TryGetValue(cellB, out var cellDataB))
                    continue;

                // Get positions
                float heightA = GetCellHeight(cellDataA) + heightOffset;
                float heightB = GetCellHeight(cellDataB) + heightOffset;

                Vector3 posA = new Vector3(
                    cellDataA.Center.X * cellScale,
                    heightA,
                    cellDataA.Center.Y * cellScale
                );
                Vector3 posB = new Vector3(
                    cellDataB.Center.X * cellScale,
                    heightB,
                    cellDataB.Center.Y * cellScale
                );

                // Get width and color based on tier
                float width = tier == RoadTier.Road ? roadWidth : pathWidth;
                Color32 color = tier == RoadTier.Road ? roadColor : pathColor;
                float halfWidth = width / 2f;

                // Create quad for this road segment
                Vector3 direction = (posB - posA).normalized;
                Vector3 perpendicular = Vector3.Cross(direction, Vector3.up).normalized;

                if (perpendicular.sqrMagnitude < 0.001f)
                {
                    perpendicular = Vector3.Cross(direction, Vector3.forward).normalized;
                }

                Vector3 offset = perpendicular * halfWidth;

                int baseIdx = vertices.Count;

                vertices.Add(posA - offset);
                vertices.Add(posA + offset);
                vertices.Add(posB + offset);
                vertices.Add(posB - offset);

                colors.Add(color);
                colors.Add(color);
                colors.Add(color);
                colors.Add(color);

                triangles.Add(baseIdx);
                triangles.Add(baseIdx + 1);
                triangles.Add(baseIdx + 2);

                triangles.Add(baseIdx);
                triangles.Add(baseIdx + 2);
                triangles.Add(baseIdx + 3);
            }

            // Update mesh
            roadMesh.Clear();
            if (vertices.Count > 0)
            {
                roadMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                roadMesh.SetVertices(vertices);
                roadMesh.SetTriangles(triangles, 0);
                roadMesh.SetColors(colors);
                roadMesh.RecalculateNormals();
                roadMesh.RecalculateBounds();
            }

            if (roads.Count > 0)
            {
                int paths = 0, roadCount = 0;
                foreach (var (_, _, tier) in roads)
                {
                    if (tier == RoadTier.Path) paths++;
                    else if (tier == RoadTier.Road) roadCount++;
                }
                // Debug.Log($"RoadRenderer: {paths} paths, {roadCount} roads");
            }
        }

        private float GetCellHeight(Cell cell)
        {
            float normalizedHeight = (cell.Height - mapData.Info.SeaLevel) / 80f;
            return normalizedHeight * heightScale;
        }

        public void SetRoadsVisible(bool visible)
        {
            showRoads = visible;
            if (roadObject != null)
            {
                roadObject.SetActive(visible);
            }
        }

#if UNITY_EDITOR
        [ContextMenu("Toggle Roads")]
        private void ToggleRoads() => SetRoadsVisible(!showRoads);

        [ContextMenu("Refresh Roads")]
        private void EditorRefreshRoads() => RefreshRoads();
#endif
    }
}
