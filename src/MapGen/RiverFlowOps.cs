using System;
using System.Collections.Generic;

namespace MapGen.Core
{
    /// <summary>
    /// mapgen4-style flow accumulation on the Voronoi vertex graph.
    /// Rivers flow along cell boundaries (edges), computed via vertices
    /// (Delaunay triangle circumcenters).
    ///
    /// Algorithm:
    /// 1. Interpolate elevation + moisture from cells to vertices
    /// 2. BFS from ocean vertices uphill (priority flood) to build a downslope DAG
    /// 3. Reverse-traverse DAG (leaves first) to accumulate flow
    /// </summary>
    public static class RiverFlowOps
    {
        /// <summary>Flow contribution multiplier applied to moisture^2 per vertex.</summary>
        const float FlowScale = 1.0f;

        public static void Compute(
            RiverField data,
            ElevationField elevation,
            ClimateField climate,
            MapGenConfig config)
        {
            InterpolateVertexData(data, elevation, climate, config);
            BuildVertexEdgeLookup(data.Mesh, out var vertexEdges, out var edgeOtherVertex);
            AssignDownslope(data, vertexEdges, edgeOtherVertex);
            AssignFlow(data, config);
            data.RiverThreshold = config.EffectiveRiverThreshold;
            data.RiverTraceThreshold = config.EffectiveRiverTraceThreshold;
            ExtractRivers(
                data,
                config.EffectiveRiverThreshold,
                config.EffectiveRiverTraceThreshold,
                config.EffectiveMinRiverVertices);
        }

        static void InterpolateVertexData(
            RiverField data,
            ElevationField elevation,
            ClimateField climate,
            MapGenConfig config)
        {
            var mesh = data.Mesh;
            float maxPrecip = config.MaxAnnualPrecipitationMm;
            if (maxPrecip < 1f) maxPrecip = 1f;

            ParallelOps.For(0, mesh.VertexCount, v =>
            {
                int[] cells = mesh.VertexCells[v];
                if (cells == null || cells.Length == 0)
                {
                    data.VertexElevationMeters[v] = 0f;
                    return;
                }

                float sumH = 0f;
                float sumP = 0f;
                int count = 0;

                for (int i = 0; i < cells.Length; i++)
                {
                    int c = cells[i];
                    if (c < 0 || c >= elevation.CellCount)
                        continue;

                    sumH += elevation[c];
                    sumP += climate.PrecipitationMmYear[c];
                    count++;
                }

                if (count > 0)
                {
                    data.VertexElevationMeters[v] = sumH / count;
                    // Store normalized moisture [0,1] in WaterLevelMeters temporarily.
                    // Will be overwritten with actual water levels during AssignDownslope.
                    data.WaterLevelMeters[v] = (sumP / count) / maxPrecip;
                }
            });
        }

        /// <summary>
        /// Build per-vertex edge adjacency: for each vertex, which edges touch it
        /// and what vertex is on the other end.
        /// </summary>
        static void BuildVertexEdgeLookup(
            CellMesh mesh,
            out int[][] vertexEdges,
            out int[][] edgeOtherVertex)
        {
            int vCount = mesh.VertexCount;
            var edgeLists = new List<int>[vCount];
            var otherLists = new List<int>[vCount];

            for (int e = 0; e < mesh.EdgeCount; e++)
            {
                var (v0, v1) = mesh.EdgeVertices[e];
                if (v0 < 0 || v0 >= vCount || v1 < 0 || v1 >= vCount)
                    continue;

                (edgeLists[v0] ??= new List<int>()).Add(e);
                (otherLists[v0] ??= new List<int>()).Add(v1);

                (edgeLists[v1] ??= new List<int>()).Add(e);
                (otherLists[v1] ??= new List<int>()).Add(v0);
            }

            vertexEdges = new int[vCount][];
            edgeOtherVertex = new int[vCount][];
            for (int v = 0; v < vCount; v++)
            {
                vertexEdges[v] = edgeLists[v]?.ToArray() ?? Array.Empty<int>();
                edgeOtherVertex[v] = otherLists[v]?.ToArray() ?? Array.Empty<int>();
            }
        }

        /// <summary>
        /// Priority-flood BFS from ocean vertices uphill.
        /// Builds the downslope DAG (DownslopeEdge per vertex) and traversal order.
        /// Also computes WaterLevelMeters for lake detection.
        /// </summary>
        static void AssignDownslope(
            RiverField data,
            int[][] vertexEdges,
            int[][] edgeOtherVertex)
        {
            var mesh = data.Mesh;
            int n = mesh.VertexCount;

            // Read moisture before we overwrite WaterLevelMeters
            var moisture = new float[n];
            Array.Copy(data.WaterLevelMeters, moisture, n);

            // Initialize water levels
            for (int v = 0; v < n; v++)
                data.WaterLevelMeters[v] = data.VertexElevationMeters[v];

            // Priority queue: (elevation, vertex)
            // Use SortedSet with tiebreaker to avoid duplicate-key issues
            var queue = new SortedSet<(float elev, int vertex)>();
            var visited = new bool[n];
            var order = new List<int>(n);

            // Seed: all ocean vertices (elevation <= 0)
            for (int v = 0; v < n; v++)
            {
                if (data.VertexElevationMeters[v] <= 0f)
                {
                    visited[v] = true;
                    order.Add(v);

                    // Ocean vertices: find lowest neighbor for downslope (like mapgen4)
                    float bestElev = data.VertexElevationMeters[v];
                    int bestEdge = -1;
                    int[] edges = vertexEdges[v];
                    int[] others = edgeOtherVertex[v];
                    for (int i = 0; i < edges.Length; i++)
                    {
                        float ne = data.VertexElevationMeters[others[i]];
                        if (ne < bestElev)
                        {
                            bestElev = ne;
                            bestEdge = edges[i];
                        }
                    }
                    data.DownslopeEdge[v] = bestEdge;
                    data.WaterLevelMeters[v] = 0f; // ocean water level = 0
                    queue.Add((data.VertexElevationMeters[v], v));
                }
            }

            // Also seed boundary-adjacent land vertices (map edges drain off)
            for (int v = 0; v < n; v++)
            {
                if (visited[v])
                    continue;

                bool isBoundaryVertex = false;
                int[] cells = mesh.VertexCells[v];
                if (cells != null)
                {
                    for (int i = 0; i < cells.Length; i++)
                    {
                        int c = cells[i];
                        if (c >= 0 && c < mesh.CellCount && mesh.CellIsBoundary[c])
                        {
                            isBoundaryVertex = true;
                            break;
                        }
                    }
                }

                if (isBoundaryVertex)
                {
                    visited[v] = true;
                    order.Add(v);
                    data.DownslopeEdge[v] = -1; // drains off map
                    data.WaterLevelMeters[v] = data.VertexElevationMeters[v];
                    queue.Add((data.VertexElevationMeters[v], v));
                }
            }

            // BFS: process in elevation order, flood uphill
            while (queue.Count > 0)
            {
                var min = queue.Min;
                queue.Remove(min);
                int current = min.vertex;

                int[] edges = vertexEdges[current];
                int[] others = edgeOtherVertex[current];

                for (int i = 0; i < edges.Length; i++)
                {
                    int nb = others[i];
                    if (visited[nb])
                        continue;

                    visited[nb] = true;
                    order.Add(nb);

                    // Downslope edge points back toward current (water flows from nb → current)
                    data.DownslopeEdge[nb] = edges[i];

                    // Derive FlowTarget: nb flows toward current
                    data.FlowTarget[nb] = current;

                    // Water level for lake detection: max of own elevation and parent's water level
                    // (priority flood — depressions fill to their spillway level)
                    float parentWaterLevel = data.WaterLevelMeters[current];
                    float ownElevation = data.VertexElevationMeters[nb];
                    data.WaterLevelMeters[nb] = Math.Max(ownElevation, parentWaterLevel);

                    queue.Add((data.VertexElevationMeters[nb], nb));
                }
            }

            // Store moisture back for flow computation
            // (we temporarily used WaterLevelMeters for moisture, now it has water levels)
            // Moisture goes into a local used by AssignFlow
            data.VertexOrder = order.ToArray();

            // Stash moisture in VertexFlux temporarily (will be overwritten in AssignFlow)
            for (int v = 0; v < n; v++)
                data.VertexFlux[v] = moisture[v];
        }

        /// <summary>
        /// Reverse-traverse the DAG (leaves first) to accumulate flow.
        /// flow = FlowScale * moisture^2 for land vertices, 0 for ocean.
        /// </summary>
        static void AssignFlow(
            RiverField data,
            MapGenConfig config)
        {
            var mesh = data.Mesh;
            int n = mesh.VertexCount;
            var order = data.VertexOrder;

            // Read moisture from VertexFlux (stashed by AssignDownslope)
            var moisture = new float[n];
            Array.Copy(data.VertexFlux, moisture, n);

            // Initialize flow per vertex
            float fluxScale = config.MaxAnnualPrecipitationMm / 100f;
            if (fluxScale < 1f) fluxScale = 1f;

            for (int v = 0; v < n; v++)
            {
                if (data.VertexElevationMeters[v] > 0f)
                {
                    float m = moisture[v];
                    data.VertexFlux[v] = FlowScale * m * m * fluxScale;
                }
                else
                {
                    data.VertexFlux[v] = 0f;
                }
            }

            // Clear edge flux
            Array.Clear(data.EdgeFlux, 0, data.EdgeFlux.Length);

            // Reverse traversal: leaves first, accumulate toward ocean
            for (int i = order.Length - 1; i >= 0; i--)
            {
                int v = order[i];
                int downslopeEdge = data.DownslopeEdge[v];
                if (downslopeEdge < 0)
                    continue;

                // Find the trunk vertex (other end of the downslope edge)
                int trunk = data.FlowTarget[v];
                if (trunk < 0)
                    continue;

                // Accumulate flow
                data.VertexFlux[trunk] += data.VertexFlux[v];
                data.EdgeFlux[downslopeEdge] += data.VertexFlux[v];

                // Fix backward slopes: if trunk is higher than tributary, lower it
                if (data.VertexElevationMeters[trunk] > data.VertexElevationMeters[v]
                    && data.VertexElevationMeters[v] > 0f)
                {
                    data.VertexElevationMeters[trunk] = data.VertexElevationMeters[v];
                }
            }
        }

        /// <summary>
        /// Extract river polylines by tracing edges from ocean mouths upstream.
        /// </summary>
        static void ExtractRivers(
            RiverField data,
            float threshold,
            float traceThreshold,
            int minVertices)
        {
            int n = data.VertexCount;

            // Build inflow graph: for each vertex, which vertices flow INTO it
            var inflow = new List<int>[n];
            for (int v = 0; v < n; v++)
            {
                int target = data.FlowTarget[v];
                if (target < 0 || target >= n)
                    continue;
                if (data.IsOcean(v))
                    continue;

                (inflow[target] ??= new List<int>()).Add(v);
            }

            // Find mouths: land vertices that flow into ocean with flux >= threshold
            var mouths = new List<int>();
            for (int v = 0; v < n; v++)
            {
                if (data.IsOcean(v))
                    continue;
                int ft = data.FlowTarget[v];
                if (ft < 0 || !data.IsOcean(ft))
                    continue;
                if (data.VertexFlux[v] < threshold)
                    continue;
                mouths.Add(v);
            }
            mouths.Sort((a, b) => data.VertexFlux[b].CompareTo(data.VertexFlux[a]));

            var vertexRiver = new int[n];
            for (int v = 0; v < n; v++)
                vertexRiver[v] = -1;

            var rivers = new List<RiverPath>();
            int nextId = 1;

            // Trace main rivers from mouths upstream
            foreach (int mouth in mouths)
            {
                if (vertexRiver[mouth] >= 0)
                    continue;

                var (verts, source) = TraceUpstream(mouth, nextId, data, inflow, vertexRiver, traceThreshold);
                rivers.Add(new RiverPath
                {
                    Id = nextId,
                    Vertices = verts,
                    MouthVertex = mouth,
                    SourceVertex = source,
                    Discharge = data.VertexFlux[mouth]
                });
                nextId++;
            }

            // Trace tributaries
            var worklist = new Queue<int>();
            var seen = new HashSet<int>();
            foreach (var r in rivers)
                foreach (int v in r.Vertices)
                    if (seen.Add(v))
                        worklist.Enqueue(v);

            while (worklist.Count > 0)
            {
                int vert = worklist.Dequeue();
                if (inflow[vert] == null)
                    continue;

                foreach (int up in inflow[vert])
                {
                    if (vertexRiver[up] >= 0)
                        continue;
                    if (data.VertexFlux[up] < traceThreshold)
                        continue;

                    var (verts, source) = TraceUpstream(up, nextId, data, inflow, vertexRiver, traceThreshold);
                    foreach (int v in verts)
                        if (seen.Add(v))
                            worklist.Enqueue(v);

                    rivers.Add(new RiverPath
                    {
                        Id = nextId,
                        Vertices = verts,
                        MouthVertex = vert,
                        SourceVertex = source,
                        Discharge = data.VertexFlux[up]
                    });
                    nextId++;
                }
            }

            if (minVertices > 1)
                rivers.RemoveAll(r => r.Vertices.Length < minVertices);

            data.Rivers = rivers.ToArray();
        }

        static (int[] vertices, int sourceVertex) TraceUpstream(
            int startVertex,
            int riverId,
            RiverField data,
            List<int>[] inflow,
            int[] vertexRiver,
            float threshold)
        {
            var verts = new List<int> { startVertex };
            vertexRiver[startVertex] = riverId;

            int cur = startVertex;
            while (true)
            {
                if (inflow[cur] == null)
                    break;

                int best = -1;
                float bestFlux = 0f;
                foreach (int up in inflow[cur])
                {
                    if (vertexRiver[up] >= 0)
                        continue;
                    if (data.VertexFlux[up] >= threshold && data.VertexFlux[up] > bestFlux)
                    {
                        bestFlux = data.VertexFlux[up];
                        best = up;
                    }
                }

                if (best < 0)
                    break;

                verts.Add(best);
                vertexRiver[best] = riverId;
                cur = best;
            }

            return (verts.ToArray(), cur);
        }
    }
}
