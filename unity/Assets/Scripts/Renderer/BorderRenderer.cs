using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using EconSim.Core.Data;
using EconSim.Bridge;

namespace EconSim.Renderer
{
    /// <summary>
    /// Renders political boundaries (state and province borders) as smooth curved lines.
    /// Chains edge segments into polylines and applies Catmull-Rom smoothing.
    /// </summary>
    public class BorderRenderer : MonoBehaviour
    {
        [Header("Border Settings")]
        [SerializeField] private float stateBorderWidth = 0.025f;
        [SerializeField] private float provinceBorderWidth = 0.01f;
        [SerializeField] private float borderHeightOffset = 0.01f;  // Slight Y offset to prevent z-fighting
        [SerializeField] private Color stateBorderColor = new Color(0.1f, 0.1f, 0.1f, 1f);
        [SerializeField] private Color provinceBorderColor = new Color(0.3f, 0.3f, 0.3f, 0.8f);

        [Header("Smoothing")]
        [SerializeField] private int smoothingSubdivisions = 4;  // Number of Chaikin iterations

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

        // Tolerance for closed loop detection
        private const float ClosedLoopTolerance = 0.001f;

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

            // Chain edges into continuous polylines
            var statePolylines = ChainEdgesIntoPolylines(stateBorderEdges);
            var provincePolylines = ChainEdgesIntoPolylines(provinceBorderEdges);

            Debug.Log($"BorderRenderer: Chained {stateBorderEdges.Count} state edges into {statePolylines.Count} polylines");

            // Smooth the polylines
            var smoothedStatePolylines = statePolylines.Select(p => SmoothPolyline(p)).ToList();
            var smoothedProvincePolylines = provincePolylines.Select(p => SmoothPolyline(p)).ToList();

            // Generate meshes from smoothed polylines
            stateBorderMesh = GeneratePolylineMesh(smoothedStatePolylines, stateBorderWidth, stateBorderColor);
            stateBorderMeshFilter.mesh = stateBorderMesh;

            provinceBorderMesh = GeneratePolylineMesh(smoothedProvincePolylines, provinceBorderWidth, provinceBorderColor);
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
                End = new Vector3(pos2.x * cellScale, avgHeight, -pos2.y * cellScale),
                StartVertexIdx = v1,
                EndVertexIdx = v2
            };
        }

        private float GetCellHeight(Cell cell)
        {
            float normalizedHeight = (cell.Height - mapData.Info.SeaLevel) / 80f;
            return normalizedHeight * heightScale;
        }

        /// <summary>
        /// Chains individual edge segments into continuous polylines by connecting
        /// edges that share vertex indices.
        /// </summary>
        private List<List<Vector3>> ChainEdgesIntoPolylines(List<BorderEdge> edges)
        {
            if (edges.Count == 0)
                return new List<List<Vector3>>();

            // Build adjacency: map each vertex index to edges that touch it
            var vertexToEdges = new Dictionary<int, List<int>>();

            for (int i = 0; i < edges.Count; i++)
            {
                var edge = edges[i];

                if (!vertexToEdges.ContainsKey(edge.StartVertexIdx))
                    vertexToEdges[edge.StartVertexIdx] = new List<int>();
                vertexToEdges[edge.StartVertexIdx].Add(i);

                if (!vertexToEdges.ContainsKey(edge.EndVertexIdx))
                    vertexToEdges[edge.EndVertexIdx] = new List<int>();
                vertexToEdges[edge.EndVertexIdx].Add(i);
            }

            var usedEdges = new HashSet<int>();
            var polylines = new List<List<Vector3>>();

            // Process each unused edge
            for (int startEdgeIdx = 0; startEdgeIdx < edges.Count; startEdgeIdx++)
            {
                if (usedEdges.Contains(startEdgeIdx))
                    continue;

                // Start a new polyline - track both positions and vertex indices
                var polyline = new List<Vector3>();
                var vertexIndices = new List<int>();
                usedEdges.Add(startEdgeIdx);

                var startEdge = edges[startEdgeIdx];
                polyline.Add(startEdge.Start);
                polyline.Add(startEdge.End);
                vertexIndices.Add(startEdge.StartVertexIdx);
                vertexIndices.Add(startEdge.EndVertexIdx);

                // Extend forward from the end
                ExtendPolyline(polyline, vertexIndices, edges, vertexToEdges, usedEdges, forward: true);

                // Extend backward from the start
                ExtendPolyline(polyline, vertexIndices, edges, vertexToEdges, usedEdges, forward: false);

                polylines.Add(polyline);
            }

            return polylines;
        }

        private void ExtendPolyline(List<Vector3> polyline, List<int> vertexIndices,
            List<BorderEdge> edges, Dictionary<int, List<int>> vertexToEdges,
            HashSet<int> usedEdges, bool forward)
        {
            while (true)
            {
                int endpointIdx = forward ? vertexIndices[vertexIndices.Count - 1] : vertexIndices[0];

                if (!vertexToEdges.TryGetValue(endpointIdx, out var connectedEdges))
                    break;

                int nextEdgeIdx = -1;
                foreach (int edgeIdx in connectedEdges)
                {
                    if (!usedEdges.Contains(edgeIdx))
                    {
                        nextEdgeIdx = edgeIdx;
                        break;
                    }
                }

                if (nextEdgeIdx < 0)
                    break;

                usedEdges.Add(nextEdgeIdx);
                var nextEdge = edges[nextEdgeIdx];

                // Determine which end connects and add the other end
                Vector3 nextPoint;
                int nextVertexIdx;
                if (nextEdge.StartVertexIdx == endpointIdx)
                {
                    nextPoint = nextEdge.End;
                    nextVertexIdx = nextEdge.EndVertexIdx;
                }
                else
                {
                    nextPoint = nextEdge.Start;
                    nextVertexIdx = nextEdge.StartVertexIdx;
                }

                if (forward)
                {
                    polyline.Add(nextPoint);
                    vertexIndices.Add(nextVertexIdx);
                }
                else
                {
                    polyline.Insert(0, nextPoint);
                    vertexIndices.Insert(0, nextVertexIdx);
                }
            }
        }

        /// <summary>
        /// Applies Chaikin's corner-cutting algorithm to smooth the polyline.
        /// This actually rounds corners rather than just interpolating through them.
        /// </summary>
        private List<Vector3> SmoothPolyline(List<Vector3> polyline)
        {
            if (polyline.Count < 3 || smoothingSubdivisions <= 0)
                return polyline;

            var result = polyline;

            // Apply Chaikin subdivision multiple times
            for (int iteration = 0; iteration < smoothingSubdivisions; iteration++)
            {
                result = ChaikinSubdivide(result);
            }

            return result;
        }

        /// <summary>
        /// Single iteration of Chaikin's corner-cutting algorithm.
        /// Creates two new points at 1/4 and 3/4 along each edge.
        /// </summary>
        private List<Vector3> ChaikinSubdivide(List<Vector3> polyline)
        {
            if (polyline.Count < 2)
                return polyline;

            var smoothed = new List<Vector3>();

            // Check if closed loop
            bool isClosed = Vector3.Distance(polyline[0], polyline[polyline.Count - 1]) < ClosedLoopTolerance;

            // Keep first point for open curves
            if (!isClosed)
            {
                smoothed.Add(polyline[0]);
            }

            // Cut corners: for each edge, add points at 1/4 and 3/4
            for (int i = 0; i < polyline.Count - 1; i++)
            {
                Vector3 p0 = polyline[i];
                Vector3 p1 = polyline[i + 1];

                // Q = 3/4 * P0 + 1/4 * P1 (closer to start)
                // R = 1/4 * P0 + 3/4 * P1 (closer to end)
                Vector3 q = 0.75f * p0 + 0.25f * p1;
                Vector3 r = 0.25f * p0 + 0.75f * p1;

                smoothed.Add(q);
                smoothed.Add(r);
            }

            // Keep last point for open curves
            if (!isClosed)
            {
                smoothed.Add(polyline[polyline.Count - 1]);
            }
            else
            {
                // For closed curves, connect back to start
                Vector3 p0 = polyline[polyline.Count - 1];
                Vector3 p1 = polyline[0];
                smoothed.Add(0.75f * p0 + 0.25f * p1);
                smoothed.Add(0.25f * p0 + 0.75f * p1);
            }

            return smoothed;
        }

        /// <summary>
        /// Generates a mesh from multiple polylines with proper mitered joints.
        /// </summary>
        private Mesh GeneratePolylineMesh(List<List<Vector3>> polylines, float width, Color color)
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            var colors = new List<Color32>();

            Color32 col32 = color;
            float halfWidth = width / 2f;

            foreach (var polyline in polylines)
            {
                if (polyline.Count < 2)
                    continue;

                int baseIdx = vertices.Count;

                // Generate vertices for this polyline with mitered corners
                for (int i = 0; i < polyline.Count; i++)
                {
                    Vector3 current = polyline[i];
                    Vector3 perpendicular;

                    if (polyline.Count == 2)
                    {
                        // Simple two-point line
                        Vector3 dir = (polyline[1] - polyline[0]).normalized;
                        perpendicular = GetPerpendicular(dir);
                    }
                    else if (i == 0)
                    {
                        // Start point: use direction to next point
                        Vector3 dir = (polyline[1] - polyline[0]).normalized;
                        perpendicular = GetPerpendicular(dir);
                    }
                    else if (i == polyline.Count - 1)
                    {
                        // End point: use direction from previous point
                        Vector3 dir = (polyline[i] - polyline[i - 1]).normalized;
                        perpendicular = GetPerpendicular(dir);
                    }
                    else
                    {
                        // Middle point: average perpendicular for miter
                        Vector3 dirIn = (polyline[i] - polyline[i - 1]).normalized;
                        Vector3 dirOut = (polyline[i + 1] - polyline[i]).normalized;
                        Vector3 perpIn = GetPerpendicular(dirIn);
                        Vector3 perpOut = GetPerpendicular(dirOut);

                        // Average the perpendiculars and normalize
                        perpendicular = (perpIn + perpOut).normalized;

                        // Adjust length for miter (prevents thinning at sharp corners)
                        float dot = Vector3.Dot(perpIn, perpendicular);
                        if (dot > 0.1f)
                        {
                            perpendicular /= dot;
                        }

                        // Clamp miter length to prevent extreme points
                        float miterLength = perpendicular.magnitude;
                        if (miterLength > 2f)
                        {
                            perpendicular = perpendicular.normalized * 2f;
                        }
                    }

                    Vector3 offset = perpendicular * halfWidth;
                    vertices.Add(current - offset);
                    vertices.Add(current + offset);
                    colors.Add(col32);
                    colors.Add(col32);
                }

                // Generate triangles connecting the vertex pairs
                for (int i = 0; i < polyline.Count - 1; i++)
                {
                    int idx = baseIdx + i * 2;

                    // First triangle
                    triangles.Add(idx);
                    triangles.Add(idx + 1);
                    triangles.Add(idx + 3);

                    // Second triangle
                    triangles.Add(idx);
                    triangles.Add(idx + 3);
                    triangles.Add(idx + 2);
                }
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

        private Vector3 GetPerpendicular(Vector3 direction)
        {
            Vector3 perpendicular = Vector3.Cross(direction, Vector3.up).normalized;

            // Fallback if direction is nearly vertical
            if (perpendicular.sqrMagnitude < 0.001f)
            {
                perpendicular = Vector3.Cross(direction, Vector3.forward).normalized;
            }

            return perpendicular;
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
            public int StartVertexIdx;  // Original vertex index for chaining
            public int EndVertexIdx;
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
