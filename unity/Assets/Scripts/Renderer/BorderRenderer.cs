using System.Collections.Generic;
using UnityEngine;
using EconSim.Core.Data;
using EconSim.Bridge;

namespace EconSim.Renderer
{
    /// <summary>
    /// Renders political boundaries (state and province borders) as line geometry.
    /// Uses quad strips for visible borders with configurable thickness.
    /// </summary>
    public class BorderRenderer : MonoBehaviour
    {
        [Header("Border Settings")]
        [SerializeField] private float stateBorderWidth = 0.02f;
        [SerializeField] private float provinceBorderWidth = 0.01f;
        [SerializeField] private float borderHeightOffset = 0.01f;  // Slight Y offset to prevent z-fighting
        [SerializeField] private Color stateBorderColor = new Color(0.1f, 0.1f, 0.1f, 1f);
        [SerializeField] private Color provinceBorderColor = new Color(0.3f, 0.3f, 0.3f, 0.8f);

        [Header("Display Options")]
        [SerializeField] private bool showStateBorders = true;
        [SerializeField] private bool showProvinceBorders = false;

        [Header("References")]
        [SerializeField] private Material borderMaterial;

        private MapData mapData;
        private float cellScale;
        private float heightScale;

        private MeshFilter stateBorderMeshFilter;
        private MeshFilter provinceBorderMeshFilter;
        private MeshRenderer stateBorderRenderer;
        private MeshRenderer provinceBorderRenderer;

        private Mesh stateBorderMesh;
        private Mesh provinceBorderMesh;

        // Edge storage to avoid duplicates (store as sorted pair)
        private HashSet<(int, int)> processedStateEdges = new HashSet<(int, int)>();
        private HashSet<(int, int)> processedProvinceEdges = new HashSet<(int, int)>();

        public void Initialize(MapData data, float cellScale, float heightScale)
        {
            this.mapData = data;
            this.cellScale = cellScale;
            this.heightScale = heightScale;

            SetupMeshObjects();
            GenerateBorders();
        }

        private void SetupMeshObjects()
        {
            // State borders child object
            var stateBorderObj = new GameObject("StateBorders");
            stateBorderObj.transform.SetParent(transform, false);
            stateBorderMeshFilter = stateBorderObj.AddComponent<MeshFilter>();
            stateBorderRenderer = stateBorderObj.AddComponent<MeshRenderer>();
            stateBorderRenderer.material = borderMaterial;

            // Province borders child object
            var provinceBorderObj = new GameObject("ProvinceBorders");
            provinceBorderObj.transform.SetParent(transform, false);
            provinceBorderMeshFilter = provinceBorderObj.AddComponent<MeshFilter>();
            provinceBorderRenderer = provinceBorderObj.AddComponent<MeshRenderer>();
            provinceBorderRenderer.material = borderMaterial;

            // Initial visibility
            stateBorderObj.SetActive(showStateBorders);
            provinceBorderObj.SetActive(showProvinceBorders);
        }

        public void GenerateBorders()
        {
            if (mapData == null) return;

            processedStateEdges.Clear();
            processedProvinceEdges.Clear();

            var stateBorderEdges = new List<BorderEdge>();
            var provinceBorderEdges = new List<BorderEdge>();

            // Find all border edges
            foreach (var cell in mapData.Cells)
            {
                if (!cell.IsLand) continue;

                foreach (int neighborId in cell.NeighborIds)
                {
                    if (!mapData.CellById.TryGetValue(neighborId, out var neighbor))
                        continue;

                    // Skip water neighbors for cleaner borders
                    if (!neighbor.IsLand) continue;

                    // Check for state border
                    if (cell.StateId != neighbor.StateId && cell.StateId > 0 && neighbor.StateId > 0)
                    {
                        var edgeKey = GetEdgeKey(cell.Id, neighbor.Id);
                        if (!processedStateEdges.Contains(edgeKey))
                        {
                            processedStateEdges.Add(edgeKey);
                            var edge = FindSharedEdge(cell, neighbor);
                            if (edge.HasValue)
                            {
                                stateBorderEdges.Add(edge.Value);
                            }
                        }
                    }
                    // Check for province border (within same state)
                    else if (cell.ProvinceId != neighbor.ProvinceId &&
                             cell.ProvinceId > 0 && neighbor.ProvinceId > 0 &&
                             cell.StateId == neighbor.StateId)
                    {
                        var edgeKey = GetEdgeKey(cell.Id, neighbor.Id);
                        if (!processedProvinceEdges.Contains(edgeKey))
                        {
                            processedProvinceEdges.Add(edgeKey);
                            var edge = FindSharedEdge(cell, neighbor);
                            if (edge.HasValue)
                            {
                                provinceBorderEdges.Add(edge.Value);
                            }
                        }
                    }
                }
            }

            Debug.Log($"BorderRenderer: Found {stateBorderEdges.Count} state border edges, {provinceBorderEdges.Count} province border edges");

            // Generate meshes
            stateBorderMesh = GenerateBorderMesh(stateBorderEdges, stateBorderWidth, stateBorderColor);
            stateBorderMeshFilter.mesh = stateBorderMesh;

            provinceBorderMesh = GenerateBorderMesh(provinceBorderEdges, provinceBorderWidth, provinceBorderColor);
            provinceBorderMeshFilter.mesh = provinceBorderMesh;
        }

        private (int, int) GetEdgeKey(int cellA, int cellB)
        {
            return cellA < cellB ? (cellA, cellB) : (cellB, cellA);
        }

        private BorderEdge? FindSharedEdge(Cell cellA, Cell cellB)
        {
            // Find two vertices that both cells share
            // These form the edge between them
            var sharedVertices = new List<int>();

            foreach (int vIdx in cellA.VertexIndices)
            {
                if (cellB.VertexIndices.Contains(vIdx))
                {
                    sharedVertices.Add(vIdx);
                }
            }

            if (sharedVertices.Count < 2)
                return null;

            // Get the first two shared vertices (should be the edge)
            int v1 = sharedVertices[0];
            int v2 = sharedVertices[1];

            if (v1 >= mapData.Vertices.Count || v2 >= mapData.Vertices.Count)
                return null;

            Vector2 pos1 = mapData.Vertices[v1].ToUnity();
            Vector2 pos2 = mapData.Vertices[v2].ToUnity();

            // Calculate height as average of both cells
            float height1 = GetCellHeight(cellA);
            float height2 = GetCellHeight(cellB);
            float avgHeight = (height1 + height2) / 2f + borderHeightOffset;

            return new BorderEdge
            {
                Start = new Vector3(pos1.x * cellScale, avgHeight, -pos1.y * cellScale),
                End = new Vector3(pos2.x * cellScale, avgHeight, -pos2.y * cellScale)
            };
        }

        private float GetCellHeight(Cell cell)
        {
            float normalizedHeight = (cell.Height - mapData.Info.SeaLevel) / 80f;
            return normalizedHeight * heightScale;
        }

        private Mesh GenerateBorderMesh(List<BorderEdge> edges, float width, Color color)
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            var colors = new List<Color32>();

            Color32 col32 = color;
            float halfWidth = width / 2f;

            foreach (var edge in edges)
            {
                // Create a quad strip for each edge
                Vector3 direction = (edge.End - edge.Start).normalized;
                Vector3 perpendicular = Vector3.Cross(direction, Vector3.up).normalized;

                // If perpendicular is zero (vertical edge), use a fallback
                if (perpendicular.sqrMagnitude < 0.001f)
                {
                    perpendicular = Vector3.Cross(direction, Vector3.forward).normalized;
                }

                Vector3 offset = perpendicular * halfWidth;

                int baseIdx = vertices.Count;

                // Quad vertices
                vertices.Add(edge.Start - offset);
                vertices.Add(edge.Start + offset);
                vertices.Add(edge.End + offset);
                vertices.Add(edge.End - offset);

                // Colors
                colors.Add(col32);
                colors.Add(col32);
                colors.Add(col32);
                colors.Add(col32);

                // Two triangles for the quad
                triangles.Add(baseIdx);
                triangles.Add(baseIdx + 1);
                triangles.Add(baseIdx + 2);

                triangles.Add(baseIdx);
                triangles.Add(baseIdx + 2);
                triangles.Add(baseIdx + 3);
            }

            var mesh = new Mesh();
            mesh.name = "BorderMesh";
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.SetColors(colors);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            return mesh;
        }

        public void SetStateBordersVisible(bool visible)
        {
            showStateBorders = visible;
            if (stateBorderRenderer != null)
            {
                stateBorderRenderer.gameObject.SetActive(visible);
            }
        }

        public void SetProvinceBordersVisible(bool visible)
        {
            showProvinceBorders = visible;
            if (provinceBorderRenderer != null)
            {
                provinceBorderRenderer.gameObject.SetActive(visible);
            }
        }

        public void SetStateBorderColor(Color color)
        {
            stateBorderColor = color;
            if (stateBorderMesh != null && mapData != null)
            {
                UpdateMeshColors(stateBorderMesh, color);
            }
        }

        public void SetProvinceBorderColor(Color color)
        {
            provinceBorderColor = color;
            if (provinceBorderMesh != null && mapData != null)
            {
                UpdateMeshColors(provinceBorderMesh, color);
            }
        }

        private void UpdateMeshColors(Mesh mesh, Color color)
        {
            var colors = new Color32[mesh.vertexCount];
            Color32 col32 = color;
            for (int i = 0; i < colors.Length; i++)
            {
                colors[i] = col32;
            }
            mesh.SetColors(colors);
        }

        private struct BorderEdge
        {
            public Vector3 Start;
            public Vector3 End;
        }

#if UNITY_EDITOR
        [ContextMenu("Toggle State Borders")]
        private void ToggleStateBorders() => SetStateBordersVisible(!showStateBorders);

        [ContextMenu("Toggle Province Borders")]
        private void ToggleProvinceBorders() => SetProvinceBordersVisible(!showProvinceBorders);

        [ContextMenu("Regenerate Borders")]
        private void RegenerateBorders()
        {
            if (mapData != null)
            {
                GenerateBorders();
            }
        }
#endif
    }
}
