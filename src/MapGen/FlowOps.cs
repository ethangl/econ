using System.Collections.Generic;

namespace MapGen.Core
{
    public static class FlowOps
    {
        public static void Compute(RiverData data, HeightGrid heights, ClimateData climate,
            float threshold = 30f, int minVertices = 2)
        {
            InterpolateVertexData(data, heights, climate);
            var vertexPairToEdge = BuildVertexPairToEdge(data.Mesh);
            DepressionFill(data);
            FlowAccumulate(data);
            AssignEdgeFlux(data, vertexPairToEdge);
            ExtractRivers(data, vertexPairToEdge, threshold, minVertices);
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

        // --- Step 1: Interpolate Cell Data onto Vertices ---

        static void InterpolateVertexData(RiverData data, HeightGrid heights, ClimateData climate)
        {
            var mesh = data.Mesh;
            for (int v = 0; v < mesh.VertexCount; v++)
            {
                int[] cells = mesh.VertexCells[v];
                if (cells == null || cells.Length == 0)
                {
                    data.VertexHeight[v] = 0;
                    data.VertexPrecip[v] = 0;
                    continue;
                }

                float sumH = 0, sumP = 0;
                int count = 0;
                for (int i = 0; i < cells.Length; i++)
                {
                    int c = cells[i];
                    if (c < 0 || c >= heights.CellCount) continue;
                    sumH += heights.Heights[c];
                    sumP += climate.Precipitation[c];
                    count++;
                }

                if (count > 0)
                {
                    data.VertexHeight[v] = sumH / count;
                    data.VertexPrecip[v] = sumP / count;
                }
            }
        }

        // --- Step 2: Depression Fill (Priority Flood on Vertex Graph) ---

        static void DepressionFill(RiverData data)
        {
            var mesh = data.Mesh;
            int n = mesh.VertexCount;

            for (int v = 0; v < n; v++)
                data.WaterLevel[v] = data.VertexHeight[v];

            var visited = new bool[n];
            var queue = new SortedSet<(float level, int vertex)>();

            // Mark ocean vertices as visited
            for (int v = 0; v < n; v++)
            {
                if (data.IsOcean(v))
                    visited[v] = true;
            }

            // Seed: land vertices adjacent to ocean vertices or boundary cells
            for (int v = 0; v < n; v++)
            {
                if (data.IsOcean(v)) continue;

                bool seed = false;

                // Check if adjacent to ocean vertex
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

                // Check if on boundary (any surrounding cell is boundary)
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
                    queue.Add((data.WaterLevel[v], v));
                    visited[v] = true;
                }
            }

            while (queue.Count > 0)
            {
                var min = queue.Min;
                queue.Remove(min);
                var (_, vert) = min;

                int[] neighbors = mesh.VertexNeighbors[vert];
                if (neighbors == null) continue;

                for (int i = 0; i < neighbors.Length; i++)
                {
                    int nb = neighbors[i];
                    if (visited[nb]) continue;
                    if (data.IsOcean(nb)) { visited[nb] = true; continue; }
                    visited[nb] = true;

                    if (data.VertexHeight[nb] >= data.WaterLevel[vert])
                    {
                        data.WaterLevel[nb] = data.VertexHeight[nb];
                    }
                    else
                    {
                        data.WaterLevel[nb] = data.WaterLevel[vert];
                        data.FlowTarget[nb] = vert;
                    }

                    queue.Add((data.WaterLevel[nb], nb));
                }
            }
        }

        // --- Step 3: Flow Accumulation ---

        static void FlowAccumulate(RiverData data)
        {
            var mesh = data.Mesh;
            int n = mesh.VertexCount;

            var landVerts = new List<int>(n);
            for (int v = 0; v < n; v++)
            {
                if (!data.IsOcean(v))
                    landVerts.Add(v);
            }

            // Sort by water level descending. Tiebreak: terrain ascending (lake interiors first).
            landVerts.Sort((a, b) =>
            {
                int cmp = data.WaterLevel[b].CompareTo(data.WaterLevel[a]);
                if (cmp != 0) return cmp;
                return data.VertexHeight[a].CompareTo(data.VertexHeight[b]);
            });

            foreach (int v in landVerts)
            {
                data.VertexFlux[v] += data.VertexPrecip[v];

                // Assign flow target if not already set (lake vertices already have one)
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
                                ? data.VertexHeight[nb]
                                : data.WaterLevel[nb];
                            if (nbLevel < minLevel)
                            {
                                minLevel = nbLevel;
                                target = nb;
                            }
                        }
                    }
                    data.FlowTarget[v] = target;
                }

                // Pass flux downstream (only to land vertices — ocean is terminus)
                int ft = data.FlowTarget[v];
                if (ft >= 0 && !data.IsOcean(ft))
                    data.VertexFlux[ft] += data.VertexFlux[v];
            }
        }

        // --- Step 4: Edge Flux ---

        static void AssignEdgeFlux(RiverData data, Dictionary<long, int> vertexPairToEdge)
        {
            for (int v = 0; v < data.VertexCount; v++)
            {
                if (data.IsOcean(v)) continue;
                int ft = data.FlowTarget[v];
                if (ft < 0) continue;

                long key = PairKey(v, ft);
                if (vertexPairToEdge.TryGetValue(key, out int edgeIdx))
                    data.EdgeFlux[edgeIdx] = data.VertexFlux[v];
            }
        }

        // --- Step 5: River Extraction ---

        static void ExtractRivers(RiverData data, Dictionary<long, int> vertexPairToEdge,
            float threshold, int minVertices)
        {
            int n = data.VertexCount;

            // Reverse flow map: which vertices drain into each vertex
            var inflow = new List<int>[n];
            for (int v = 0; v < n; v++)
            {
                if (data.IsOcean(v)) continue;
                int ft = data.FlowTarget[v];
                if (ft < 0 || ft >= n) continue;
                if (inflow[ft] == null) inflow[ft] = new List<int>();
                inflow[ft].Add(v);
            }

            var vertexRiver = new int[n];
            for (int v = 0; v < n; v++) vertexRiver[v] = -1;

            // Find mouths: land vertices draining to ocean with flux >= threshold
            var mouths = new List<int>();
            for (int v = 0; v < n; v++)
            {
                if (data.IsOcean(v)) continue;
                int ft = data.FlowTarget[v];
                if (ft < 0 || !data.IsOcean(ft)) continue;
                if (data.VertexFlux[v] < threshold) continue;
                mouths.Add(v);
            }
            mouths.Sort((a, b) => data.VertexFlux[b].CompareTo(data.VertexFlux[a]));

            var rivers = new List<River>();
            int nextId = 1;

            // Trace main rivers from mouths
            foreach (int mouth in mouths)
            {
                if (vertexRiver[mouth] >= 0) continue;

                var (verts, source) = TraceUpstream(
                    mouth, nextId, data, inflow, vertexRiver, threshold);

                rivers.Add(new River
                {
                    Id = nextId,
                    Vertices = verts,
                    MouthVertex = mouth,
                    SourceVertex = source,
                    Discharge = data.VertexFlux[mouth]
                });
                nextId++;
            }

            // Tributaries: BFS from vertices on existing rivers
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
                if (inflow[vert] == null) continue;

                foreach (int up in inflow[vert])
                {
                    if (vertexRiver[up] >= 0) continue;
                    if (data.VertexFlux[up] < threshold) continue;

                    var (verts, source) = TraceUpstream(
                        up, nextId, data, inflow, vertexRiver, threshold);

                    foreach (int v in verts)
                    {
                        if (seen.Add(v))
                            worklist.Enqueue(v);
                    }

                    rivers.Add(new River
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
            int startVertex, int riverId,
            RiverData data, List<int>[] inflow,
            int[] vertexRiver, float threshold)
        {
            var verts = new List<int> { startVertex };
            vertexRiver[startVertex] = riverId;

            int cur = startVertex;
            while (true)
            {
                if (inflow[cur] == null) break;

                int best = -1;
                float bestFlux = 0;

                foreach (int up in inflow[cur])
                {
                    if (vertexRiver[up] >= 0) continue;
                    if (data.VertexFlux[up] >= threshold && data.VertexFlux[up] > bestFlux)
                    {
                        bestFlux = data.VertexFlux[up];
                        best = up;
                    }
                }

                if (best < 0) break;

                verts.Add(best);
                vertexRiver[best] = riverId;
                cur = best;
            }

            return (verts.ToArray(), cur);
        }
    }
}
