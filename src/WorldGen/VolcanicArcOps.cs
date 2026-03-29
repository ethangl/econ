using System;
using System.Collections.Generic;

namespace WorldGen.Core
{
    /// <summary>
    /// Identifies convergent ocean-continent boundaries and places volcanic arc
    /// cells offset inland on the overriding plate. Applies elevation bumps and
    /// produces per-cell intensity data for downstream cone-stamping.
    /// </summary>
    public static class VolcanicArcOps
    {
        /// <summary>
        /// Find subduction zones, place arcs, and apply elevation bumps.
        /// Call after ElevationOps + HotspotOps so we add bumps on top of tectonic elevation.
        /// </summary>
        public static void Generate(SphereMesh mesh, TectonicData tectonics, WorldGenConfig config)
        {
            int cellCount = mesh.CellCount;
            int edgeCount = mesh.EdgeCount;
            float[] intensity = new float[cellCount];
            var rng = new Random(config.Seed + 600);

            bool[] isOceanic = tectonics.PlateIsOceanic;
            int[] cellPlate = tectonics.CellPlate;

            // Step 1: Find all convergent ocean-continent boundary edges.
            // For each, record which cell is the continental (overriding) boundary cell.
            var continentalBoundaryCells = new HashSet<int>();
            var cellToEdges = new Dictionary<int, List<int>>(); // continental cell -> qualifying edges

            for (int e = 0; e < edgeCount; e++)
            {
                if (tectonics.EdgeBoundary[e] != BoundaryType.Convergent)
                    continue;

                var (c0, c1) = mesh.EdgeCells[e];
                int p0 = cellPlate[c0];
                int p1 = cellPlate[c1];
                if (p0 == p1)
                    continue;
                if (isOceanic[p0] == isOceanic[p1])
                    continue;

                int continentCell = isOceanic[p0] ? c1 : c0;
                continentalBoundaryCells.Add(continentCell);

                if (!cellToEdges.TryGetValue(continentCell, out var edges))
                {
                    edges = new List<int>();
                    cellToEdges[continentCell] = edges;
                }
                edges.Add(e);
            }

            if (continentalBoundaryCells.Count == 0)
            {
                tectonics.CellVolcanicArcIntensity = intensity;
                tectonics.VolcanicArcs = Array.Empty<VolcanicArcData>();
                Console.WriteLine("    Volcanic arcs: no ocean-continent convergent boundaries found");
                return;
            }

            // Step 2: Group into contiguous arc segments via BFS through
            // continental boundary cells connected by neighbor adjacency.
            // Segments are constrained to a single overriding plate so that
            // triple junctions or narrow seams don't merge two plates' arcs.
            var visited = new HashSet<int>();
            var segments = new List<List<int>>(); // each segment = list of continental boundary cells

            foreach (int startCell in continentalBoundaryCells)
            {
                if (visited.Contains(startCell))
                    continue;

                int plate = cellPlate[startCell];
                var segment = new List<int>();
                var queue = new Queue<int>();
                queue.Enqueue(startCell);
                visited.Add(startCell);

                while (queue.Count > 0)
                {
                    int cell = queue.Dequeue();
                    segment.Add(cell);

                    int[] neighbors = mesh.CellNeighbors[cell];
                    for (int i = 0; i < neighbors.Length; i++)
                    {
                        int nb = neighbors[i];
                        if (!visited.Contains(nb) && continentalBoundaryCells.Contains(nb)
                            && cellPlate[nb] == plate)
                        {
                            visited.Add(nb);
                            queue.Enqueue(nb);
                        }
                    }
                }

                segments.Add(segment);
            }

            // Step 3: Filter short segments
            int minEdges = config.VolcanicArcMinEdges;
            var filteredSegments = new List<List<int>>();
            foreach (var seg in segments)
            {
                // Count total qualifying edges in this segment
                int edgeTotal = 0;
                foreach (int cell in seg)
                {
                    if (cellToEdges.TryGetValue(cell, out var edges))
                        edgeTotal += edges.Count;
                }
                if (edgeTotal >= minEdges)
                    filteredSegments.Add(seg);
            }

            // Step 4: BFS inland from each segment's boundary cells to find arc cells
            int arcOffset = config.VolcanicArcOffset;
            var arcs = new List<VolcanicArcData>();

            foreach (var segment in filteredSegments)
            {
                // Determine overriding plate from the first boundary cell's edges
                int overridingPlate = -1;
                foreach (int cell in segment)
                {
                    if (cellToEdges.TryGetValue(cell, out var edges) && edges.Count > 0)
                    {
                        overridingPlate = cellPlate[cell];
                        break;
                    }
                }
                if (overridingPlate < 0)
                    continue;

                var (arcCells, arcConvergence) = BfsInland(mesh, segment, cellPlate,
                    overridingPlate, continentalBoundaryCells, arcOffset,
                    tectonics, cellToEdges);

                if (arcCells.Count == 0)
                    continue;

                // Step 5: Select peaks — every other arc cell
                var peaks = new List<VolcanoPeakData>();
                for (int i = 0; i < arcCells.Count; i += 2)
                {
                    int cell = arcCells[i];
                    float conv = arcConvergence[i];
                    float peakIntensity = 0.7f + 0.3f * conv;

                    peaks.Add(new VolcanoPeakData
                    {
                        Cell = cell,
                        Position = mesh.CellCenters[cell],
                        Intensity = peakIntensity,
                    });
                }

                // Apply intensities
                foreach (int cell in arcCells)
                    intensity[cell] = Math.Max(intensity[cell], 0.5f);
                foreach (var peak in peaks)
                    intensity[peak.Cell] = Math.Max(intensity[peak.Cell], peak.Intensity);

                arcs.Add(new VolcanicArcData
                {
                    BoundaryCells = segment.ToArray(),
                    ArcCells = arcCells.ToArray(),
                    Peaks = peaks.ToArray(),
                    OverridingPlate = overridingPlate,
                });
            }

            // Step 6: Apply elevation bumps
            float bump = config.VolcanicArcElevation;
            for (int c = 0; c < cellCount; c++)
            {
                if (intensity[c] > 0f)
                    tectonics.CellElevation[c] += intensity[c] * bump;
            }

            // Clamp after bumps
            for (int c = 0; c < cellCount; c++)
                tectonics.CellElevation[c] = Math.Max(0f, Math.Min(1f, tectonics.CellElevation[c]));

            tectonics.CellVolcanicArcIntensity = intensity;
            tectonics.VolcanicArcs = arcs.ToArray();

            // Log diagnostics
            int totalPeaks = 0;
            foreach (var arc in arcs)
                totalPeaks += arc.Peaks.Length;

            Console.WriteLine($"    Volcanic arcs: {arcs.Count} segments, {totalPeaks} peaks");
            for (int a = 0; a < arcs.Count; a++)
            {
                var arc = arcs[a];
                // Compute centroid of arc cells for lat/lon display
                Vec3 centroid = new Vec3(0, 0, 0);
                float avgElev = 0f;
                foreach (int cell in arc.ArcCells)
                {
                    centroid = centroid + mesh.CellCenters[cell];
                    avgElev += tectonics.CellElevation[cell];
                }
                centroid = (centroid * (1f / arc.ArcCells.Length)).Normalized;
                avgElev /= arc.ArcCells.Length;
                float latDeg = (float)(Math.Asin(centroid.Y) * 180.0 / Math.PI);
                float lonDeg = (float)(Math.Atan2(centroid.Z, centroid.X) * 180.0 / Math.PI);
                Console.WriteLine($"      Arc {a}: lat {latDeg:F1} lon {lonDeg:F1}, " +
                    $"{arc.BoundaryCells.Length} boundary cells, {arc.ArcCells.Length} arc cells, " +
                    $"{arc.Peaks.Length} peaks, avg elev {avgElev:F3}");
            }
        }

        /// <summary>
        /// Multi-source BFS from boundary cells, returning cells at exactly targetDepth
        /// hops inland on the overriding plate. Falls back to shallower depths if needed.
        /// Each returned cell carries the normalized convergence strength of the boundary
        /// cell it was reached from, so intensity scales with local subduction strength
        /// regardless of offset distance.
        /// </summary>
        static (List<int> cells, List<float> convergence) BfsInland(SphereMesh mesh,
            List<int> boundaryCells, int[] cellPlate, int plate,
            HashSet<int> excludeCells, int targetDepth,
            TectonicData tectonics, Dictionary<int, List<int>> cellToEdges)
        {
            var depth = new Dictionary<int, int>();
            var cellConv = new Dictionary<int, float>(); // propagated convergence per cell
            var queue = new Queue<int>();

            // Seed BFS from boundary cells, each carrying its max convergence
            foreach (int cell in boundaryCells)
            {
                depth[cell] = 0;
                float maxConv = 0f;
                if (cellToEdges.TryGetValue(cell, out var edges))
                {
                    foreach (int e in edges)
                    {
                        float c = Math.Abs(tectonics.EdgeConvergence[e]);
                        if (c > maxConv) maxConv = c;
                    }
                }
                cellConv[cell] = Math.Min(maxConv / 4f, 1f);
                queue.Enqueue(cell);
            }

            // Mark other boundary cells as visited so BFS doesn't cross them
            foreach (int cell in excludeCells)
            {
                if (!depth.ContainsKey(cell))
                    depth[cell] = -1;
            }

            var cellsByDepth = new List<int>[targetDepth + 1];
            var convByDepth = new List<float>[targetDepth + 1];
            for (int d = 0; d <= targetDepth; d++)
            {
                cellsByDepth[d] = new List<int>();
                convByDepth[d] = new List<float>();
            }

            while (queue.Count > 0)
            {
                int cell = queue.Dequeue();
                int d = depth[cell];
                if (d >= targetDepth)
                    continue;

                float parentConv = cellConv[cell];
                int[] neighbors = mesh.CellNeighbors[cell];
                for (int i = 0; i < neighbors.Length; i++)
                {
                    int nb = neighbors[i];
                    if (depth.ContainsKey(nb))
                        continue;
                    if (cellPlate[nb] != plate)
                        continue;

                    int nd = d + 1;
                    depth[nb] = nd;
                    cellConv[nb] = parentConv;
                    cellsByDepth[nd].Add(nb);
                    convByDepth[nd].Add(parentConv);
                    queue.Enqueue(nb);
                }
            }

            // Return cells at target depth, falling back to shallower if empty
            for (int d = targetDepth; d >= 1; d--)
            {
                if (cellsByDepth[d].Count > 0)
                    return (cellsByDepth[d], convByDepth[d]);
            }

            // Last resort: boundary cells themselves
            var boundaryConv = new List<float>();
            foreach (int cell in boundaryCells)
                boundaryConv.Add(cellConv.TryGetValue(cell, out float v) ? v : 0f);
            return (boundaryCells, boundaryConv);
        }
    }
}
