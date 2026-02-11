using System.Collections.Generic;

namespace MapGen.Core
{
    /// <summary>
    /// River generation for V2 based on signed-meter elevation and V2 climate fields.
    /// </summary>
    public static class FlowOpsV2
    {
        public static void Compute(
            RiverFieldV2 data,
            ElevationFieldV2 elevation,
            ClimateFieldV2 climate,
            MapGenV2Config config)
        {
            InterpolateVertexData(data, elevation, climate, config);
            var vertexPairToEdge = BuildVertexPairToEdge(data.Mesh);
            DepressionFill(data);
            FlowAccumulate(data);
            AssignEdgeFlux(data, vertexPairToEdge);
            ExtractRivers(
                data,
                vertexPairToEdge,
                config.RiverThreshold,
                config.RiverTraceThreshold,
                config.MinRiverVertices);
        }

        static long PairKey(int a, int b)
        {
            int lo = a < b ? a : b;
            int hi = a > b ? a : b;
            return ((long)lo << 32) | (uint)hi;
        }

        static Dictionary<long, int> BuildVertexPairToEdge(CellMesh mesh)
        {
            var map = new Dictionary<long, int>(mesh.EdgeCount);
            for (int e = 0; e < mesh.EdgeCount; e++)
            {
                var (v0, v1) = mesh.EdgeVertices[e];
                map[PairKey(v0, v1)] = e;
            }

            return map;
        }

        static void InterpolateVertexData(
            RiverFieldV2 data,
            ElevationFieldV2 elevation,
            ClimateFieldV2 climate,
            MapGenV2Config config)
        {
            var mesh = data.Mesh;
            float fluxScale = config.MaxAnnualPrecipitationMm / 100f;
            if (fluxScale < 1e-6f)
                fluxScale = 1f;

            for (int v = 0; v < mesh.VertexCount; v++)
            {
                int[] cells = mesh.VertexCells[v];
                if (cells == null || cells.Length == 0)
                {
                    data.VertexElevationMeters[v] = 0f;
                    data.VertexPrecipFlux[v] = 0f;
                    continue;
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
                    sumP += climate.PrecipitationMmYear[c] / fluxScale;
                    count++;
                }

                if (count > 0)
                {
                    data.VertexElevationMeters[v] = sumH / count;
                    data.VertexPrecipFlux[v] = sumP / count;
                }
            }
        }

        static void DepressionFill(RiverFieldV2 data)
        {
            var mesh = data.Mesh;
            int n = mesh.VertexCount;

            for (int v = 0; v < n; v++)
                data.WaterLevelMeters[v] = data.VertexElevationMeters[v];

            var visited = new bool[n];
            var queue = new SortedSet<(float level, int vertex)>();

            for (int v = 0; v < n; v++)
            {
                if (data.IsOcean(v))
                    visited[v] = true;
            }

            for (int v = 0; v < n; v++)
            {
                if (data.IsOcean(v))
                    continue;

                bool seed = false;
                int[] neighbors = mesh.VertexNeighbors[v];
                if (neighbors != null)
                {
                    for (int i = 0; i < neighbors.Length; i++)
                    {
                        if (data.IsOcean(neighbors[i]))
                        {
                            seed = true;
                            break;
                        }
                    }
                }

                if (!seed)
                {
                    int[] cells = mesh.VertexCells[v];
                    if (cells != null)
                    {
                        for (int i = 0; i < cells.Length; i++)
                        {
                            int c = cells[i];
                            if (c >= 0 && c < mesh.CellCount && mesh.CellIsBoundary[c])
                            {
                                seed = true;
                                break;
                            }
                        }
                    }
                }

                if (seed)
                {
                    queue.Add((data.WaterLevelMeters[v], v));
                    visited[v] = true;
                }
            }

            while (queue.Count > 0)
            {
                var min = queue.Min;
                queue.Remove(min);
                var (_, vert) = min;

                int[] neighbors = mesh.VertexNeighbors[vert];
                if (neighbors == null)
                    continue;

                for (int i = 0; i < neighbors.Length; i++)
                {
                    int nb = neighbors[i];
                    if (visited[nb])
                        continue;
                    if (data.IsOcean(nb))
                    {
                        visited[nb] = true;
                        continue;
                    }

                    visited[nb] = true;
                    if (data.VertexElevationMeters[nb] >= data.WaterLevelMeters[vert])
                    {
                        data.WaterLevelMeters[nb] = data.VertexElevationMeters[nb];
                    }
                    else
                    {
                        data.WaterLevelMeters[nb] = data.WaterLevelMeters[vert];
                        data.FlowTarget[nb] = vert;
                    }

                    queue.Add((data.WaterLevelMeters[nb], nb));
                }
            }
        }

        static void FlowAccumulate(RiverFieldV2 data)
        {
            var mesh = data.Mesh;
            int n = mesh.VertexCount;

            var landVerts = new List<int>(n);
            for (int v = 0; v < n; v++)
            {
                if (!data.IsOcean(v))
                    landVerts.Add(v);
            }

            landVerts.Sort((a, b) =>
            {
                int cmp = data.WaterLevelMeters[b].CompareTo(data.WaterLevelMeters[a]);
                if (cmp != 0) return cmp;
                return data.VertexElevationMeters[a].CompareTo(data.VertexElevationMeters[b]);
            });

            foreach (int v in landVerts)
            {
                data.VertexFlux[v] += data.VertexPrecipFlux[v];

                if (data.FlowTarget[v] < 0)
                {
                    float minLevel = float.MaxValue;
                    int target = -1;

                    int[] neighbors = mesh.VertexNeighbors[v];
                    if (neighbors != null)
                    {
                        for (int i = 0; i < neighbors.Length; i++)
                        {
                            int nb = neighbors[i];
                            float nbLevel = data.IsOcean(nb)
                                ? data.VertexElevationMeters[nb]
                                : data.WaterLevelMeters[nb];
                            if (nbLevel < minLevel)
                            {
                                minLevel = nbLevel;
                                target = nb;
                            }
                        }
                    }

                    data.FlowTarget[v] = target;
                }

                int ft = data.FlowTarget[v];
                if (ft >= 0 && !data.IsOcean(ft))
                    data.VertexFlux[ft] += data.VertexFlux[v];
            }
        }

        static void AssignEdgeFlux(RiverFieldV2 data, Dictionary<long, int> vertexPairToEdge)
        {
            for (int v = 0; v < data.VertexCount; v++)
            {
                if (data.IsOcean(v))
                    continue;

                int ft = data.FlowTarget[v];
                if (ft < 0)
                    continue;

                long key = PairKey(v, ft);
                if (vertexPairToEdge.TryGetValue(key, out int edgeIdx))
                    data.EdgeFlux[edgeIdx] = data.VertexFlux[v];
            }
        }

        static void ExtractRivers(
            RiverFieldV2 data,
            Dictionary<long, int> vertexPairToEdge,
            float threshold,
            float traceThreshold,
            int minVertices)
        {
            int n = data.VertexCount;
            var inflow = new List<int>[n];
            for (int v = 0; v < n; v++)
            {
                if (data.IsOcean(v))
                    continue;

                int ft = data.FlowTarget[v];
                if (ft < 0 || ft >= n)
                    continue;

                if (inflow[ft] == null)
                    inflow[ft] = new List<int>();
                inflow[ft].Add(v);
            }

            var vertexRiver = new int[n];
            for (int v = 0; v < n; v++)
                vertexRiver[v] = -1;

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

            var rivers = new List<RiverV2>();
            int nextId = 1;

            foreach (int mouth in mouths)
            {
                if (vertexRiver[mouth] >= 0)
                    continue;

                var (verts, source) = TraceUpstream(
                    mouth,
                    nextId,
                    data,
                    inflow,
                    vertexRiver,
                    traceThreshold);

                rivers.Add(new RiverV2
                {
                    Id = nextId,
                    Vertices = verts,
                    MouthVertex = mouth,
                    SourceVertex = source,
                    Discharge = data.VertexFlux[mouth]
                });
                nextId++;
            }

            var worklist = new Queue<int>();
            var seen = new HashSet<int>();
            foreach (var r in rivers)
            {
                foreach (int v in r.Vertices)
                {
                    if (seen.Add(v))
                        worklist.Enqueue(v);
                }
            }

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

                    var (verts, source) = TraceUpstream(
                        up,
                        nextId,
                        data,
                        inflow,
                        vertexRiver,
                        traceThreshold);

                    foreach (int v in verts)
                    {
                        if (seen.Add(v))
                            worklist.Enqueue(v);
                    }

                    rivers.Add(new RiverV2
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
            RiverFieldV2 data,
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
