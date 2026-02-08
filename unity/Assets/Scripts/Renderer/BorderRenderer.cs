using System.Collections.Generic;
using UnityEngine;
using EconSim.Core.Data;
using EconSim.Core.Rendering;
using EconSim.Bridge;

namespace EconSim.Renderer
{
    /// <summary>
    /// Renders political boundaries (province and county borders) as straight-edge polylines
    /// with mitered joints.
    /// </summary>
    public class BorderRenderer : MonoBehaviour
    {
        private float borderWidth = 0.025f;
        private bool showProvinceBorders = true;
        private bool showCountyBorders = true;

        private MapData mapData;
        private float cellScale;
        private float heightScale;
        private PoliticalPalette palette;

        private MeshFilter provinceBorderMeshFilter;
        private MeshFilter countyBorderMeshFilter;
        private MeshRenderer provinceBorderRenderer;
        private MeshRenderer countyBorderRenderer;

        private Mesh provinceBorderMesh;
        private Mesh countyBorderMesh;

        // Edge storage to avoid duplicates (store as sorted pair)
        private HashSet<(int, int)> processedProvinceEdges = new HashSet<(int, int)>();
        private HashSet<(int, int)> processedCountyEdges = new HashSet<(int, int)>();


        public void Initialize(MapData data, float cellScale, float heightScale)
        {
            this.mapData = data;
            this.cellScale = cellScale;
            this.heightScale = heightScale;
            this.palette = new PoliticalPalette(data);

            SetupMeshObjects();
            GenerateBorders();
        }

        private void SetupMeshObjects()
        {
            var simpleShader = Shader.Find("EconSim/SimpleBorder");

            // County borders (bottom layer)
            var countyBorderObj = new GameObject("CountyBorders");
            countyBorderObj.transform.SetParent(transform, false);
            countyBorderMeshFilter = countyBorderObj.AddComponent<MeshFilter>();
            countyBorderRenderer = countyBorderObj.AddComponent<MeshRenderer>();
            countyBorderRenderer.material = new Material(simpleShader);
            countyBorderObj.SetActive(showCountyBorders);

            // Province borders (top layer)
            var provinceBorderObj = new GameObject("ProvinceBorders");
            provinceBorderObj.transform.SetParent(transform, false);
            provinceBorderMeshFilter = provinceBorderObj.AddComponent<MeshFilter>();
            provinceBorderRenderer = provinceBorderObj.AddComponent<MeshRenderer>();
            provinceBorderRenderer.material = new Material(simpleShader);
            provinceBorderObj.SetActive(showProvinceBorders);
        }

        public void GenerateBorders()
        {
            if (mapData == null) return;

            processedProvinceEdges.Clear();
            processedCountyEdges.Clear();

            // Province/county borders: grouped by realm for coloring
            var provinceEdgesByRealm = new Dictionary<int, List<BorderEdge>>();
            var countyEdgesByRealm = new Dictionary<int, List<BorderEdge>>();

            // Find all border edges
            foreach (var cell in mapData.Cells)
            {
                if (!cell.IsLand) continue;

                foreach (int neighborId in cell.NeighborIds)
                {
                    if (!mapData.CellById.TryGetValue(neighborId, out var neighbor))
                        continue;

                    // For province/county borders, require both cells to be land
                    if (!neighbor.IsLand) continue;

                    // Check for province border (within same realm only)
                    if (cell.RealmId == neighbor.RealmId &&
                        cell.ProvinceId != neighbor.ProvinceId &&
                        cell.ProvinceId > 0 && neighbor.ProvinceId > 0)
                    {
                        var edgeKey = GetEdgeKey(cell.Id, neighbor.Id);
                        if (!processedProvinceEdges.Contains(edgeKey))
                        {
                            processedProvinceEdges.Add(edgeKey);
                            var edge = FindSharedEdge(cell, neighbor);
                            if (edge.HasValue)
                            {
                                if (!provinceEdgesByRealm.ContainsKey(cell.RealmId))
                                    provinceEdgesByRealm[cell.RealmId] = new List<BorderEdge>();
                                provinceEdgesByRealm[cell.RealmId].Add(edge.Value);
                            }
                        }
                    }
                    // Check for county border (within same province only)
                    else if (cell.RealmId == neighbor.RealmId &&
                             cell.ProvinceId == neighbor.ProvinceId &&
                             cell.CountyId != neighbor.CountyId &&
                             cell.CountyId > 0 && neighbor.CountyId > 0)
                    {
                        var edgeKey = GetEdgeKey(cell.Id, neighbor.Id);
                        if (!processedCountyEdges.Contains(edgeKey))
                        {
                            processedCountyEdges.Add(edgeKey);
                            var edge = FindSharedEdge(cell, neighbor);
                            if (edge.HasValue)
                            {
                                if (!countyEdgesByRealm.ContainsKey(cell.RealmId))
                                    countyEdgesByRealm[cell.RealmId] = new List<BorderEdge>();
                                countyEdgesByRealm[cell.RealmId].Add(edge.Value);
                            }
                        }
                    }
                }
            }

            Debug.Log($"BorderRenderer: Found {processedProvinceEdges.Count} province, {processedCountyEdges.Count} county border edges");

            // Generate border meshes with per-realm colors
            provinceBorderMesh = GenerateColoredBorderMesh(provinceEdgesByRealm, 0.75f, borderWidth * 0.5f);
            provinceBorderMeshFilter.mesh = provinceBorderMesh;

            countyBorderMesh = GenerateColoredBorderMesh(countyEdgesByRealm, 0.5f, borderWidth * 0.25f);
            countyBorderMeshFilter.mesh = countyBorderMesh;
        }

        /// <summary>
        /// Generate border mesh with per-realm colors.
        /// </summary>
        private Mesh GenerateColoredBorderMesh(Dictionary<int, List<BorderEdge>> edgesByRealm, float opacity, float width = -1f)
        {
            if (width < 0) width = borderWidth;
            var allPolylines = new List<(List<Vector3> polyline, Color color)>();

            foreach (var kvp in edgesByRealm)
            {
                int realmId = kvp.Key;
                var edges = kvp.Value;

                // Get realm color from palette, darkened and desaturated for border
                var coreColor = palette.GetRealmColor(realmId);
                Color.RGBToHSV(new Color(coreColor.R / 255f, coreColor.G / 255f, coreColor.B / 255f), out float h, out float s, out float v);
                s = Mathf.Min(1f, s * 1.5f);  // Increase saturation
                v *= 0.15f;  // Darken significantly
                Color unityColor = Color.HSVToRGB(h, s, v);
                unityColor.a = opacity;

                // Chain this realm's edges into polylines
                var polylines = ChainEdgesIntoPolylines(edges);

                foreach (var poly in polylines)
                {
                    allPolylines.Add((poly, unityColor));
                }
            }

            return GenerateColoredPolylineMesh(allPolylines, width);
        }

        /// <summary>
        /// Generate mesh from polylines with per-polyline colors.
        /// </summary>
        private Mesh GenerateColoredPolylineMesh(List<(List<Vector3> polyline, Color color)> polylines, float width)
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            var colors = new List<Color32>();

            float halfWidth = width / 2f;

            foreach (var (polyline, color) in polylines)
            {
                if (polyline.Count < 2)
                    continue;

                Color32 col32 = color;
                int baseIdx = vertices.Count;

                for (int i = 0; i < polyline.Count; i++)
                {
                    Vector3 current = polyline[i];
                    Vector3 perpendicular;

                    if (polyline.Count == 2)
                    {
                        Vector3 dir = (polyline[1] - polyline[0]).normalized;
                        perpendicular = GetPerpendicular(dir);
                    }
                    else if (i == 0)
                    {
                        Vector3 dir = (polyline[1] - polyline[0]).normalized;
                        perpendicular = GetPerpendicular(dir);
                    }
                    else if (i == polyline.Count - 1)
                    {
                        Vector3 dir = (polyline[i] - polyline[i - 1]).normalized;
                        perpendicular = GetPerpendicular(dir);
                    }
                    else
                    {
                        Vector3 dirIn = (polyline[i] - polyline[i - 1]).normalized;
                        Vector3 dirOut = (polyline[i + 1] - polyline[i]).normalized;
                        Vector3 perpIn = GetPerpendicular(dirIn);
                        Vector3 perpOut = GetPerpendicular(dirOut);

                        perpendicular = (perpIn + perpOut).normalized;
                        float dot = Vector3.Dot(perpIn, perpendicular);
                        if (dot > 0.1f)
                            perpendicular /= dot;

                        float miterLength = perpendicular.magnitude;
                        if (miterLength > 2f)
                            perpendicular = perpendicular.normalized * 2f;
                    }

                    Vector3 offset = perpendicular * halfWidth;
                    Vector3 v0 = current - offset;
                    Vector3 v1 = current + offset;
                    vertices.Add(v0);
                    vertices.Add(v1);
                    colors.Add(col32);
                    colors.Add(col32);
                }

                for (int i = 0; i < polyline.Count - 1; i++)
                {
                    int idx = baseIdx + i * 2;
                    triangles.Add(idx);
                    triangles.Add(idx + 1);
                    triangles.Add(idx + 3);
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

            // Straight edge between the two vertices (2D map coords → 3D world)
            Vector2 p1 = mapData.Vertices[v1].ToUnity();
            Vector2 p2 = mapData.Vertices[v2].ToUnity();
            var points = new List<Vector3>
            {
                new Vector3(p1.x * cellScale, 0f, p1.y * cellScale),
                new Vector3(p2.x * cellScale, 0f, p2.y * cellScale)
            };

            return new BorderEdge
            {
                Points = points,
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
                // Add all points from the first edge
                polyline.AddRange(startEdge.Points);
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

                // Determine which end connects and get points in correct order
                List<Vector3> pointsToAdd;
                int nextVertexIdx;
                if (nextEdge.StartVertexIdx == endpointIdx)
                {
                    // Edge goes from endpoint to EndVertexIdx - use points as-is
                    pointsToAdd = nextEdge.Points;
                    nextVertexIdx = nextEdge.EndVertexIdx;
                }
                else
                {
                    // Edge goes from EndVertexIdx to endpoint - reverse points
                    pointsToAdd = new List<Vector3>(nextEdge.Points);
                    pointsToAdd.Reverse();
                    nextVertexIdx = nextEdge.StartVertexIdx;
                }

                if (forward)
                {
                    // Skip first point (duplicate of current end)
                    polyline.AddRange(pointsToAdd.GetRange(1, pointsToAdd.Count - 1));
                    vertexIndices.Add(nextVertexIdx);
                }
                else
                {
                    // pointsToAdd goes from endpointIdx toward nextVertexIdx
                    // Reverse so it goes from nextVertexIdx toward endpointIdx
                    var reversed = new List<Vector3>(pointsToAdd);
                    reversed.Reverse();
                    // Skip last point (duplicate of current start at endpointIdx)
                    polyline.InsertRange(0, reversed.GetRange(0, reversed.Count - 1));
                    vertexIndices.Insert(0, nextVertexIdx);
                }
            }
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

        public void SetProvinceBordersVisible(bool visible)
        {
            showProvinceBorders = visible;
            if (provinceBorderRenderer != null)
            {
                provinceBorderRenderer.gameObject.SetActive(visible);
            }
        }


        private struct BorderEdge
        {
            public List<Vector3> Points;  // Edge points (2 points for straight edge)
            public int StartVertexIdx;    // Original vertex index for chaining
            public int EndVertexIdx;
        }

#if UNITY_EDITOR
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
