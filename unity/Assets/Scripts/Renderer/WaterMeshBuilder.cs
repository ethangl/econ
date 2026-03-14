using System.Collections.Generic;
using UnityEngine;
using EconSim.Core.Data;

namespace EconSim.Renderer
{
    /// <summary>
    /// Builds quad-strip meshes for rivers and coasts from Voronoi edge geometry.
    /// Adjacent edges sharing a Voronoi vertex are chained into continuous polylines
    /// before extrusion, producing smooth connected river/coast meshes.
    /// </summary>
    public static class WaterMeshBuilder
    {
        public const float DefaultRiverMinHalfWidth = 0.003f;
        public const float DefaultRiverMaxHalfWidth = 0.025f;
        public const float DefaultCoastHalfWidth = 0.008f;
        private const float RiverAmplitudeScale = 0.4f;
        private const int SmoothSamples = 3;
        private const float MeshYOffset = 0.002f;

        /// <summary>
        /// A single Voronoi edge with its vertex indices and per-edge data.
        /// </summary>
        private struct EdgeSegment
        {
            public int V0, V1;           // Voronoi vertex indices
            public int CellA, CellB;     // Cell pair
            public float Flux;           // River flux (0 for coast)
            public bool IsCoast;
        }

        /// <summary>
        /// A chain of connected Voronoi vertices forming a continuous polyline.
        /// </summary>
        private struct Chain
        {
            public List<int> VertexIndices;         // Ordered Voronoi vertex indices
            public List<(int CellA, int CellB)> EdgeCellPairs; // Cell pair per segment (Count = VertexIndices.Count - 1)
            public List<float> EdgeFluxes;          // Flux per segment
            public bool IsCoast;
        }

        public static Mesh Build(
            MapData mapData,
            float cellScale,
            float gridHeightScale,
            MapOverlayManager.NoisyEdgeStyle noisyEdgeStyle,
            uint rootSeed,
            float riverMinHalfWidth = DefaultRiverMinHalfWidth,
            float riverMaxHalfWidth = DefaultRiverMaxHalfWidth,
            float coastHalfWidth = DefaultCoastHalfWidth)
        {
            var vertices = new List<Vector3>(8192);
            var uvs = new List<Vector2>(8192);
            var colors = new List<Color>(8192);
            var triangles = new List<int>(16384);

            float baseAmplitude = NoisyEdgeUtils.GetBaseAmplitude(1f, noisyEdgeStyle) * cellScale;

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

            // Collect all edge segments
            var riverSegments = new List<EdgeSegment>();
            var coastSegments = new List<EdgeSegment>();

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
                        Flux = kv.Value, IsCoast = false
                    });
                }
            }

            if (mapData.EdgeCoastVertices != null)
            {
                foreach (var kv in mapData.EdgeCoastVertices)
                {
                    coastSegments.Add(new EdgeSegment
                    {
                        V0 = kv.Value.Item1, V1 = kv.Value.Item2,
                        CellA = kv.Key.Item1, CellB = kv.Key.Item2,
                        Flux = 0f, IsCoast = true
                    });
                }
            }

            // Chain and extrude rivers
            var riverChains = BuildChains(riverSegments, false);
            foreach (var chain in riverChains)
            {
                ExtrudeChain(chain, mapData, cellScale, gridHeightScale, noisyEdgeStyle,
                    baseAmplitude, rootSeed, logTrace, logRange,
                    riverMinHalfWidth, riverMaxHalfWidth, coastHalfWidth,
                    vertices, uvs, colors, triangles);
            }

            // Chain and extrude coasts
            var coastChains = BuildChains(coastSegments, true);
            foreach (var chain in coastChains)
            {
                ExtrudeChain(chain, mapData, cellScale, gridHeightScale, noisyEdgeStyle,
                    baseAmplitude, rootSeed + 7919u, logTrace, logRange,
                    riverMinHalfWidth, riverMaxHalfWidth, coastHalfWidth,
                    vertices, uvs, colors, triangles);
            }

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

        /// <summary>
        /// Chain edge segments into continuous polylines by walking shared Voronoi vertices.
        /// Vertices with degree != 2 are chain endpoints (degree 1 = tip, degree 3+ = junction).
        /// </summary>
        private static List<Chain> BuildChains(List<EdgeSegment> segments, bool isCoast)
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

            // Start chains from non-degree-2 vertices (endpoints and junctions),
            // then sweep remaining loops.
            var startVertices = new List<int>();
            foreach (var kv in vertexEdges)
            {
                if (kv.Value.Count != 2)
                    startVertices.Add(kv.Key);
            }

            // Walk chains from each start vertex
            foreach (int startVert in startVertices)
            {
                foreach (int segIdx in vertexEdges[startVert])
                {
                    if (used[segIdx])
                        continue;

                    var chain = WalkChain(segments, vertexEdges, used, segIdx, startVert, isCoast);
                    if (chain.VertexIndices.Count >= 2)
                        chains.Add(chain);
                }
            }

            // Pick up any remaining closed loops (all degree-2 vertices)
            for (int i = 0; i < segments.Count; i++)
            {
                if (used[i])
                    continue;

                var chain = WalkChain(segments, vertexEdges, used, i, segments[i].V0, isCoast);
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
            int startVertex,
            bool isCoast)
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
                    break; // end of chain (endpoint, junction, or loop closed)

                // Stop at junctions (degree 3+) — don't continue through them
                if (adj.Count > 2)
                    break;

                currentVert = nextVert;
                currentSeg = nextSeg;
            }

            return new Chain
            {
                VertexIndices = vertIndices,
                EdgeCellPairs = cellPairs,
                EdgeFluxes = fluxes,
                IsCoast = isCoast
            };
        }

        private static void ExtrudeChain(
            Chain chain,
            MapData mapData,
            float cellScale,
            float gridHeightScale,
            MapOverlayManager.NoisyEdgeStyle noisyEdgeStyle,
            float baseAmplitude,
            uint rootSeed,
            float logTrace,
            float logRange,
            float riverMinHalfWidth,
            float riverMaxHalfWidth,
            float coastHalfWidth,
            List<Vector3> outVerts,
            List<Vector2> outUVs,
            List<Color> outColors,
            List<int> outTris)
        {
            int vertCount = chain.VertexIndices.Count;
            if (vertCount < 2)
                return;

            // Build the full noisy polyline from the chain's Voronoi vertices.
            // Each edge segment gets its own noisy sub-polyline (seeded by its cell pair),
            // and they're stitched together at shared vertices.
            var fullPolyline = new List<Vector2>(vertCount * 8);
            var polylineHalfWidths = new List<float>(vertCount * 8);
            var polylineHeights = new List<float>(vertCount * 8);
            var polylineFluxT = new List<float>(vertCount * 8);

            for (int seg = 0; seg < vertCount - 1; seg++)
            {
                int vi0 = chain.VertexIndices[seg];
                int vi1 = chain.VertexIndices[seg + 1];
                var p0 = mapData.Vertices[vi0];
                var p1 = mapData.Vertices[vi1];
                Vector2 w0 = new Vector2(p0.X * cellScale, p0.Y * cellScale);
                Vector2 w1 = new Vector2(p1.X * cellScale, p1.Y * cellScale);

                // Noisy polyline for this segment
                var (cellA, cellB) = chain.EdgeCellPairs[seg];
                uint edgeSeed = NoisyEdgeUtils.BuildUnorderedPairSeed(rootSeed, cellA, cellB);
                var controlPts = new List<Vector2> { w0, w1 };
                var subPoly = NoisyEdgeUtils.BuildNoisySmoothedPath(
                    controlPts, edgeSeed, noisyEdgeStyle,
                    baseAmplitude, RiverAmplitudeScale, SmoothSamples);

                if (subPoly == null || subPoly.Count < 2)
                    continue;

                // Per-segment properties
                float flux = chain.EdgeFluxes[seg];
                float fluxT = chain.IsCoast ? 0f : Mathf.Clamp01((Mathf.Log(flux + 1f) - logTrace) / logRange);
                float halfWidth = chain.IsCoast ? coastHalfWidth : Mathf.Lerp(riverMinHalfWidth, riverMaxHalfWidth, fluxT);
                float y = chain.IsCoast
                    ? ComputeCoastHeight(mapData, cellA, cellB, gridHeightScale)
                    : ComputeEdgeHeight(mapData, cellA, cellB, gridHeightScale);

                // Append sub-polyline, skipping first point if not the first segment
                // (the shared vertex was already added as the last point of the previous segment)
                int startIdx = (seg > 0 && fullPolyline.Count > 0) ? 1 : 0;
                for (int i = startIdx; i < subPoly.Count; i++)
                {
                    fullPolyline.Add(subPoly[i]);
                    polylineHalfWidths.Add(halfWidth);
                    polylineHeights.Add(y);
                    polylineFluxT.Add(fluxT);
                }
            }

            if (fullPolyline.Count < 2)
                return;

            // Extrude the combined polyline with per-vertex width
            ExtrudeVariableWidthStrip(
                fullPolyline, polylineHalfWidths, polylineHeights, polylineFluxT,
                chain.IsCoast, outVerts, outUVs, outColors, outTris);
        }

        private static void ExtrudeVariableWidthStrip(
            List<Vector2> polyline,
            List<float> halfWidths,
            List<float> heights,
            List<float> fluxTs,
            bool isCoast,
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
                float y = heights[i];
                float fluxT = fluxTs[i];

                // Compute tangent direction
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

                vertices.Add(new Vector3(left.x, y + MeshYOffset, left.y));
                vertices.Add(new Vector3(right.x, y + MeshYOffset, right.y));

                uvs.Add(new Vector2(0f, 0f)); // left edge
                uvs.Add(new Vector2(0f, 1f)); // right edge

                Color c = isCoast
                    ? new Color(1f, 0f, 0f, 1f)
                    : new Color(0f, fluxT, 0f, 1f);
                colors.Add(c);
                colors.Add(c);
            }

            // Triangles
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

        private static float ComputeEdgeHeight(MapData mapData, int cellA, int cellB, float gridHeightScale)
        {
            float hA = GetCellY(mapData, cellA, gridHeightScale);
            float hB = GetCellY(mapData, cellB, gridHeightScale);
            return (hA + hB) * 0.5f;
        }

        private static float ComputeCoastHeight(MapData mapData, int cellA, int cellB, float gridHeightScale)
        {
            if (mapData.CellById == null)
                return 0f;
            bool aLand = mapData.CellById.TryGetValue(cellA, out var cA) && cA.IsLand;
            bool bLand = mapData.CellById.TryGetValue(cellB, out var cB) && cB.IsLand;
            if (aLand && !bLand)
                return GetCellY(mapData, cellA, gridHeightScale);
            if (bLand && !aLand)
                return GetCellY(mapData, cellB, gridHeightScale);
            return (GetCellY(mapData, cellA, gridHeightScale) + GetCellY(mapData, cellB, gridHeightScale)) * 0.5f;
        }

        private static float GetCellY(MapData mapData, int cellId, float gridHeightScale)
        {
            if (mapData.CellById == null || !mapData.CellById.TryGetValue(cellId, out var cell))
                return 0f;
            float normalizedSignedHeight = Elevation.GetNormalizedSignedHeight(cell, mapData.Info);
            return normalizedSignedHeight * gridHeightScale;
        }
    }
}
