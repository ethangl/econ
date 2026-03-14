using System.Collections.Generic;
using UnityEngine;
using EconSim.Core.Data;

namespace EconSim.Renderer
{
    /// <summary>
    /// Builds meshes for water rendering:
    /// - Rivers: quad-strip meshes from chained Voronoi edges with noisy displacement
    /// - Water bodies: fan-triangulated Voronoi cell polygons for oceans and lakes
    /// Oceans stay at sea level; lakes respond to heightmap displacement.
    /// </summary>
    public static class WaterMeshBuilder
    {
        public const float DefaultRiverMinHalfWidth = 0.001f;
        public const float DefaultRiverMaxHalfWidth = 0.01f;
        private const float MeshYOffset = 0.002f;

        /// <summary>
        /// A single Voronoi edge with its vertex indices and per-edge data.
        /// </summary>
        private struct EdgeSegment
        {
            public int V0, V1;           // Voronoi vertex indices
            public int CellA, CellB;     // Cell pair
            public float Flux;           // River flux
        }

        /// <summary>
        /// A chain of connected Voronoi vertices forming a continuous polyline.
        /// </summary>
        private struct Chain
        {
            public List<int> VertexIndices;         // Ordered Voronoi vertex indices
            public List<(int CellA, int CellB)> EdgeCellPairs; // Cell pair per segment
            public List<float> EdgeFluxes;          // Flux per segment
        }

        public static Mesh Build(
            MapData mapData,
            float cellScale,
            float expand = 0f,
            float riverMinHalfWidth = DefaultRiverMinHalfWidth,
            float riverMaxHalfWidth = DefaultRiverMaxHalfWidth)
        {
            var vertices = new List<Vector3>(16384);
            var uvs = new List<Vector2>(16384);
            var colors = new List<Color>(16384);
            var triangles = new List<int>(32768);

            // --- Rivers (submitted first so stencil gives them priority over water bodies) ---

            // Compute log-flux range for river width interpolation
            float logTrace = 0f;
            float logRange = 1f;
            if (mapData.EdgeRiverFlux != null && mapData.EdgeRiverFlux.Count > 0)
            {
                float traceThreshold = mapData.RiverTraceFluxThreshold;
                float majorThreshold = mapData.RiverFluxThreshold;
                logTrace = Mathf.Log(traceThreshold + 1f);
                float logMax = Mathf.Log(majorThreshold + 1f);
                foreach (var kv in mapData.EdgeRiverFlux)
                    if (kv.Value > majorThreshold)
                        logMax = Mathf.Max(logMax, Mathf.Log(kv.Value + 1f));
                logRange = Mathf.Max(0.01f, logMax - logTrace);
            }

            var riverSegments = new List<EdgeSegment>();
            if (mapData.EdgeRiverFlux != null && mapData.EdgeRiverVertices != null)
            {
                foreach (var kv in mapData.EdgeRiverFlux)
                {
                    if (!mapData.EdgeRiverVertices.TryGetValue(kv.Key, out var vp))
                        continue;
                    riverSegments.Add(new EdgeSegment
                    {
                        V0 = vp.Item1, V1 = vp.Item2,
                        CellA = kv.Key.Item1, CellB = kv.Key.Item2,
                        Flux = kv.Value
                    });
                }
            }

            var riverChains = BuildChains(riverSegments);
            foreach (var chain in riverChains)
            {
                ExtrudeChain(chain, mapData, cellScale, expand, logTrace, logRange,
                    riverMinHalfWidth, riverMaxHalfWidth,
                    vertices, uvs, colors, triangles);
            }

            // --- Water bodies (oceans + lakes, after rivers for stencil priority) ---
            float seaLevel01 = Elevation.NormalizeAbsolute01(
                Elevation.ResolveSeaLevel(mapData.Info), mapData.Info);
            BuildWaterBodies(mapData, cellScale, seaLevel01, expand,
                vertices, uvs, colors, triangles);

            if (vertices.Count == 0)
                return null;

            var mesh = new Mesh();
            mesh.name = "WaterMesh";
            if (vertices.Count > 65535)
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        // ───────────────────────────────────────────────
        //  Water bodies (oceans + lakes)
        // ───────────────────────────────────────────────

        private static void BuildWaterBodies(
            MapData mapData,
            float cellScale,
            float seaLevel01,
            float expand,
            List<Vector3> vertices,
            List<Vector2> uvs,
            List<Color> colors,
            List<int> triangles)
        {
            if (mapData.Cells == null || mapData.Vertices == null)
                return;

            foreach (var cell in mapData.Cells)
            {
                if (cell.IsLand)
                    continue;
                if (cell.VertexIndices == null || cell.VertexIndices.Count < 3)
                    continue;

                // Determine ocean vs lake
                bool isLake = false;
                if (mapData.FeatureById != null &&
                    mapData.FeatureById.TryGetValue(cell.FeatureId, out var feature))
                {
                    isLake = feature.IsLake;
                }

                // Ocean: sea level (flat). Lake: actual cell height.
                float height01 = isLake
                    ? Elevation.NormalizeAbsolute01(
                        Elevation.GetAbsoluteHeight(cell, mapData.Info), mapData.Info)
                    : seaLevel01;

                // Vertex color: R=1.0 ocean, R=0.5 lake
                Color bodyColor = isLake
                    ? new Color(0.5f, 0f, 0f, 1f)
                    : new Color(1f, 0f, 0f, 1f);

                // Build perimeter from straight Voronoi vertices
                var perimeter = new List<Vector2>();
                int polyCount = cell.VertexIndices.Count;

                for (int i = 0; i < polyCount; i++)
                {
                    int vi = cell.VertexIndices[i];
                    if (vi < 0 || vi >= mapData.Vertices.Count)
                        continue;

                    var vPos = mapData.Vertices[vi];
                    perimeter.Add(new Vector2(vPos.X * cellScale, vPos.Y * cellScale));
                }

                if (perimeter.Count < 3)
                    continue;

                // Fan triangulation from cell center
                Vector2 center = new Vector2(
                    cell.Center.X * cellScale,
                    cell.Center.Y * cellScale);

                // Expand perimeter outward from center to cover rasterization gaps
                if (expand > 0f)
                {
                    for (int i = 0; i < perimeter.Count; i++)
                    {
                        Vector2 dir = (perimeter[i] - center).normalized;
                        perimeter[i] += dir * expand;
                    }
                }

                int centerIdx = vertices.Count;
                vertices.Add(new Vector3(center.x, 0f, center.y));
                uvs.Add(new Vector2(height01, 0.5f));
                colors.Add(bodyColor);

                int firstPerimIdx = vertices.Count;
                for (int i = 0; i < perimeter.Count; i++)
                {
                    vertices.Add(new Vector3(perimeter[i].x, 0f, perimeter[i].y));
                    uvs.Add(new Vector2(height01, 0.5f));
                    colors.Add(bodyColor);
                }

                // CCW winding for Z-positive top-down camera
                for (int i = 0; i < perimeter.Count; i++)
                {
                    int next = (i + 1) % perimeter.Count;
                    triangles.Add(centerIdx);
                    triangles.Add(firstPerimIdx + next);
                    triangles.Add(firstPerimIdx + i);
                }
            }

        }

        // ───────────────────────────────────────────────
        //  River edge chaining + extrusion
        // ───────────────────────────────────────────────

        private static List<Chain> BuildChains(List<EdgeSegment> segments)
        {
            if (segments.Count == 0)
                return new List<Chain>();

            // Build adjacency: vertex -> list of segment indices
            var vertexEdges = new Dictionary<int, List<int>>();
            for (int i = 0; i < segments.Count; i++)
            {
                var seg = segments[i];
                if (!vertexEdges.TryGetValue(seg.V0, out var list0))
                {
                    list0 = new List<int>(3);
                    vertexEdges[seg.V0] = list0;
                }
                list0.Add(i);

                if (!vertexEdges.TryGetValue(seg.V1, out var list1))
                {
                    list1 = new List<int>(3);
                    vertexEdges[seg.V1] = list1;
                }
                list1.Add(i);
            }

            var used = new bool[segments.Count];
            var chains = new List<Chain>();

            // Start chains from non-degree-2 vertices (endpoints and junctions)
            var startVertices = new List<int>();
            foreach (var kv in vertexEdges)
            {
                if (kv.Value.Count != 2)
                    startVertices.Add(kv.Key);
            }

            foreach (int startVert in startVertices)
            {
                foreach (int segIdx in vertexEdges[startVert])
                {
                    if (used[segIdx])
                        continue;

                    var chain = WalkChain(segments, vertexEdges, used, segIdx, startVert);
                    if (chain.VertexIndices.Count >= 2)
                        chains.Add(chain);
                }
            }

            // Pick up remaining closed loops (all degree-2 vertices)
            for (int i = 0; i < segments.Count; i++)
            {
                if (used[i])
                    continue;

                var chain = WalkChain(segments, vertexEdges, used, i, segments[i].V0);
                if (chain.VertexIndices.Count >= 2)
                    chains.Add(chain);
            }

            return chains;
        }

        private static Chain WalkChain(
            List<EdgeSegment> segments,
            Dictionary<int, List<int>> vertexEdges,
            bool[] used,
            int startSegIdx,
            int startVertex)
        {
            var vertIndices = new List<int> { startVertex };
            var cellPairs = new List<(int, int)>();
            var fluxes = new List<float>();

            int currentVert = startVertex;
            int currentSeg = startSegIdx;

            while (true)
            {
                used[currentSeg] = true;
                var seg = segments[currentSeg];
                int nextVert = (seg.V0 == currentVert) ? seg.V1 : seg.V0;
                vertIndices.Add(nextVert);
                cellPairs.Add((seg.CellA, seg.CellB));
                fluxes.Add(seg.Flux);

                // Find next unused segment at nextVert
                int nextSeg = -1;
                if (vertexEdges.TryGetValue(nextVert, out var adj))
                {
                    foreach (int candidate in adj)
                    {
                        if (!used[candidate])
                        {
                            nextSeg = candidate;
                            break;
                        }
                    }
                }

                if (nextSeg < 0)
                    break;

                // Stop at junctions (degree 3+)
                if (adj.Count > 2)
                    break;

                currentVert = nextVert;
                currentSeg = nextSeg;
            }

            return new Chain
            {
                VertexIndices = vertIndices,
                EdgeCellPairs = cellPairs,
                EdgeFluxes = fluxes
            };
        }

        private static void ExtrudeChain(
            Chain chain,
            MapData mapData,
            float cellScale,
            float expand,
            float logTrace,
            float logRange,
            float riverMinHalfWidth,
            float riverMaxHalfWidth,
            List<Vector3> outVerts,
            List<Vector2> outUVs,
            List<Color> outColors,
            List<int> outTris)
        {
            int vertCount = chain.VertexIndices.Count;
            if (vertCount < 2)
                return;

            var fullPolyline = new List<Vector2>(vertCount);
            var polylineHalfWidths = new List<float>(vertCount);
            var polylineHeights = new List<float>(vertCount);
            var polylineFluxT = new List<float>(vertCount);

            for (int seg = 0; seg < vertCount - 1; seg++)
            {
                int vi0 = chain.VertexIndices[seg];
                int vi1 = chain.VertexIndices[seg + 1];
                var p0 = mapData.Vertices[vi0];

                var (cellA, cellB) = chain.EdgeCellPairs[seg];

                float flux = chain.EdgeFluxes[seg];
                float fluxT = Mathf.Clamp01((Mathf.Log(flux + 1f) - logTrace) / logRange);
                float halfWidth = Mathf.Lerp(riverMinHalfWidth, riverMaxHalfWidth, fluxT) + expand;
                float height01 = ComputeEdgeHeight01(mapData, cellA, cellB);

                if (seg == 0)
                {
                    fullPolyline.Add(new Vector2(p0.X * cellScale, p0.Y * cellScale));
                    polylineHalfWidths.Add(halfWidth);
                    polylineHeights.Add(height01);
                    polylineFluxT.Add(fluxT);
                }

                var p1 = mapData.Vertices[vi1];
                fullPolyline.Add(new Vector2(p1.X * cellScale, p1.Y * cellScale));
                polylineHalfWidths.Add(halfWidth);
                polylineHeights.Add(height01);
                polylineFluxT.Add(fluxT);
            }

            if (fullPolyline.Count < 2)
                return;

            ExtrudeVariableWidthStrip(
                fullPolyline, polylineHalfWidths, polylineHeights, polylineFluxT,
                outVerts, outUVs, outColors, outTris);
        }

        private static void ExtrudeVariableWidthStrip(
            List<Vector2> polyline,
            List<float> halfWidths,
            List<float> heights,
            List<float> fluxTs,
            List<Vector3> vertices,
            List<Vector2> uvs,
            List<Color> colors,
            List<int> triangles)
        {
            int startVert = vertices.Count;
            int count = polyline.Count;

            for (int i = 0; i < count; i++)
            {
                Vector2 p = polyline[i];
                float hw = halfWidths[i];
                float height01 = heights[i];
                float fluxT = fluxTs[i];

                Vector2 dir;
                if (i == 0)
                    dir = (polyline[1] - polyline[0]).normalized;
                else if (i == count - 1)
                    dir = (polyline[count - 1] - polyline[count - 2]).normalized;
                else
                    dir = (polyline[i + 1] - polyline[i - 1]).normalized;

                if (dir.sqrMagnitude < 1e-8f)
                    dir = Vector2.right;

                Vector2 normal = new Vector2(-dir.y, dir.x);
                Vector2 left = p - normal * hw;
                Vector2 right = p + normal * hw;

                // Y=0; shader displaces using height01 in UV.x
                vertices.Add(new Vector3(left.x, 0f, left.y));
                vertices.Add(new Vector3(right.x, 0f, right.y));

                // UV.x = height01, UV.y = 0/1 for edge AA
                uvs.Add(new Vector2(height01, 0f));
                uvs.Add(new Vector2(height01, 1f));

                // River: R=0, G=fluxT
                Color c = new Color(0f, fluxT, 0f, 1f);
                colors.Add(c);
                colors.Add(c);
            }

            for (int i = 0; i < count - 1; i++)
            {
                int bl = startVert + i * 2;
                int br = bl + 1;
                int tl = bl + 2;
                int tr = bl + 3;

                triangles.Add(bl);
                triangles.Add(tl);
                triangles.Add(br);

                triangles.Add(br);
                triangles.Add(tl);
                triangles.Add(tr);
            }
        }

        // ───────────────────────────────────────────────
        //  Height helpers
        // ───────────────────────────────────────────────

        private static float ComputeEdgeHeight01(MapData mapData, int cellA, int cellB)
        {
            return (GetCellHeight01(mapData, cellA) + GetCellHeight01(mapData, cellB)) * 0.5f;
        }

        private static float GetCellHeight01(MapData mapData, int cellId)
        {
            if (mapData.CellById == null || !mapData.CellById.TryGetValue(cellId, out var cell))
                return 0f;
            float absoluteHeight = Elevation.GetAbsoluteHeight(cell, mapData.Info);
            return Elevation.NormalizeAbsolute01(absoluteHeight, mapData.Info);
        }
    }
}
