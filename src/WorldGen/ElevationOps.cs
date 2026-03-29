using System;
using System.Collections.Generic;

namespace WorldGen.Core
{
    /// <summary>
    /// Assigns coarse tectonic elevation to each cell based on plate type,
    /// boundary effects, BFS propagation, and smoothing.
    /// </summary>
    public static class ElevationOps
    {
        const float OceanicBase = 0.15f;
        const float ContinentalBase = 0.65f;
        const float ConvergentLift = 0.4f;
        const float DivergentDrop = -0.4f;
        const float TransformLift = 0.4f;
        const int PropagationDepth = 3;
        const int SmoothingPasses = 2;
        const float SmoothingWeight = 0.2f;

        /// <summary>
        /// Run the full elevation pipeline. Populates tectonics.PlateIsOceanic and tectonics.CellElevation.
        /// </summary>
        const int MaxSubcontinents = 8;

        public static void Generate(SphereMesh mesh, TectonicData tectonics, float oceanFraction, int seed)
        {
            var rng = new Random(seed);
            int cellCount = mesh.CellCount;

            bool[] isOceanic = AssignPlateTypes(tectonics.PlateCount, oceanFraction, rng);

            // Force polar cap plates to oceanic (before subcontinent promotion)
            for (int i = 0; i < tectonics.PolarPlateCount; i++)
                isOceanic[i] = true;

            var plateNeighbors = BuildPlateAdjacency(mesh, tectonics.CellPlate, tectonics.PlateCount);
            PromoteSubcontinents(isOceanic, tectonics.PlateIsMajor, plateNeighbors, MaxSubcontinents, rng);
            float[] elevation = ComputeBaseElevation(cellCount, tectonics.CellPlate, isOceanic);
            ApplyBoundaryEffects(mesh, tectonics, elevation);
            Smooth(mesh, elevation);
            Clamp01(elevation);

            tectonics.PlateIsOceanic = isOceanic;
            tectonics.CellElevation = elevation;
        }

        /// <summary>
        /// Multi-step tectonic elevation via boundary migration. Each step:
        /// 1) Erode existing elevation toward plate base
        /// 2) Migrate boundaries — convergent edges flip cells from retreating to advancing plate
        /// 3) Reclassify boundaries from updated plate assignments
        /// 4) Apply boundary elevation effects as deltas
        /// Populates tectonics.PlateIsOceanic, CellElevation, and history arrays.
        /// </summary>
        public static void GenerateMultiStep(SphereMesh mesh, TectonicData tectonics,
            float oceanFraction, int seed, int steps, float erosionFactor)
        {
            var rng = new Random(seed);
            int cellCount = mesh.CellCount;
            int plateCount = tectonics.PlateCount;

            // Assign plate types (oceanic/continental) once — stays fixed across steps
            bool[] isOceanic = AssignPlateTypes(plateCount, oceanFraction, rng);
            for (int i = 0; i < tectonics.PolarPlateCount; i++)
                isOceanic[i] = true;

            var plateNeighbors = BuildPlateAdjacency(mesh, tectonics.CellPlate, plateCount);
            PromoteSubcontinents(isOceanic, tectonics.PlateIsMajor, plateNeighbors, MaxSubcontinents, rng);

            // Working copy of plate assignment (mutated each step by boundary migration)
            int[] cellPlate = new int[cellCount];
            Array.Copy(tectonics.CellPlate, cellPlate, cellCount);

            // Per-cell crust type — stays with the cell regardless of plate ownership.
            // When an oceanic cell is absorbed by a continental plate, it remains oceanic crust.
            bool[] cellCrustOceanic = new bool[cellCount];
            for (int c = 0; c < cellCount; c++)
                cellCrustOceanic[c] = isOceanic[cellPlate[c]];

            // Base elevation from crust type (erosion target)
            float[] baseElevation = ComputeBaseElevation(cellCount, cellCrustOceanic);

            // Initialize elevation from step 0 (original plate assignment + boundaries)
            float[] elevation = new float[cellCount];
            Array.Copy(baseElevation, elevation, cellCount);
            ApplyBoundaryEffects(mesh, tectonics, elevation);
            Smooth(mesh, elevation);

            // Initialize history arrays
            int[] boundaryExposure = new int[cellCount];
            int[] plateContinuity = new int[cellCount];
            int[] lastBoundaryStep = new int[cellCount];
            for (int i = 0; i < cellCount; i++)
                lastBoundaryStep[i] = -1;

            // Record step 0 history
            UpdateHistory(mesh, tectonics.EdgeBoundary, boundaryExposure, plateContinuity,
                lastBoundaryStep, 0);

            // Current boundary state (mutated each step)
            BoundaryType[] edgeBoundary = tectonics.EdgeBoundary;
            float[] edgeConvergence = tectonics.EdgeConvergence;

            // Steps 1..steps-1: erode, migrate boundaries, reclassify, apply effects
            for (int step = 1; step < steps; step++)
            {
                // Erode toward base elevation
                for (int c = 0; c < cellCount; c++)
                    elevation[c] += (baseElevation[c] - elevation[c]) * erosionFactor;

                // Migrate boundaries: flip cells at convergent edges
                MigrateBoundaries(mesh, cellPlate, edgeBoundary, edgeConvergence,
                    tectonics.PlateDrift, isOceanic);

                // Base elevation always comes from crust type, not plate ownership
                // (cellCrustOceanic doesn't change — subducted ocean stays ocean)

                // Reclassify boundaries from updated plate assignments
                var (newBoundary, newConvergence) = TectonicOps.ClassifyBoundaries(
                    mesh, cellPlate, tectonics.PlateDrift);
                edgeBoundary = newBoundary;
                edgeConvergence = newConvergence;

                // Apply boundary effects as deltas
                var stepTectonics = new TectonicData
                {
                    CellPlate = cellPlate,
                    PlateCount = plateCount,
                    EdgeBoundary = edgeBoundary,
                    EdgeConvergence = edgeConvergence,
                };
                ApplyBoundaryEffects(mesh, stepTectonics, elevation);
                Smooth(mesh, elevation);

                // Update history
                UpdateHistory(mesh, edgeBoundary, boundaryExposure, plateContinuity,
                    lastBoundaryStep, step);
            }

            Clamp01(elevation);

            // Write final state back to tectonics
            Array.Copy(cellPlate, tectonics.CellPlate, cellCount);
            tectonics.PlateIsOceanic = isOceanic;
            tectonics.CellElevation = elevation;
            tectonics.EdgeBoundary = edgeBoundary;
            tectonics.EdgeConvergence = edgeConvergence;
            tectonics.CellBoundaryExposure = boundaryExposure;
            tectonics.CellPlateContinuity = plateContinuity;
            tectonics.CellLastBoundaryStep = lastBoundaryStep;
        }

        /// <summary>
        /// Migrate plate boundaries based on drift vectors. At convergent edges,
        /// the cell on the retreating plate flips to the advancing plate.
        /// At ocean-continent convergent edges, the oceanic cell subducts (flips to continental).
        /// </summary>
        internal static void MigrateBoundaries(SphereMesh mesh, int[] cellPlate,
            BoundaryType[] edgeBoundary, float[] edgeConvergence,
            Vec3[] plateDrift, bool[] isOceanic)
        {
            int edgeCount = mesh.EdgeCount;

            // Collect cells to flip (defer mutation to avoid order-dependent artifacts)
            // Key: cell index, Value: plate to flip to
            var flips = new Dictionary<int, int>();

            for (int e = 0; e < edgeCount; e++)
            {
                if (edgeBoundary[e] != BoundaryType.Convergent)
                    continue;

                float conv = edgeConvergence[e];
                if (Math.Abs(conv) < 0.3f)
                    continue; // weak convergence — boundary doesn't migrate

                var (c0, c1) = mesh.EdgeCells[e];
                int p0 = cellPlate[c0];
                int p1 = cellPlate[c1];
                if (p0 == p1)
                    continue;

                // Determine which plate is advancing (overriding) and which retreats.
                // Edge direction is c0→c1. Positive convergence = p0 moves toward c1.
                // At ocean-continent boundaries, the oceanic plate always subducts.
                int advancingPlate, retreatingCell;
                if (isOceanic[p0] != isOceanic[p1])
                {
                    // Ocean-continent: oceanic plate subducts
                    if (isOceanic[p0])
                    {
                        advancingPlate = p1;
                        retreatingCell = c0;
                    }
                    else
                    {
                        advancingPlate = p0;
                        retreatingCell = c1;
                    }
                }
                else
                {
                    // Same type: advancing plate determined by convergence direction
                    if (conv > 0)
                    {
                        advancingPlate = p0;
                        retreatingCell = c1;
                    }
                    else
                    {
                        advancingPlate = p1;
                        retreatingCell = c0;
                    }
                }

                // Only flip if not already claimed by a stronger convergence
                if (!flips.ContainsKey(retreatingCell))
                    flips[retreatingCell] = advancingPlate;
            }

            // Apply flips
            foreach (var (cell, plate) in flips)
                cellPlate[cell] = plate;
        }

        /// <summary>
        /// Update per-cell history arrays for one step.
        /// </summary>
        internal static void UpdateHistory(SphereMesh mesh, BoundaryType[] edgeBoundary,
            int[] boundaryExposure, int[] plateContinuity,
            int[] lastBoundaryStep, int step)
        {
            int cellCount = mesh.CellCount;

            // Mark cells adjacent to boundaries
            bool[] nearBoundary = new bool[cellCount];
            int edgeCount = mesh.EdgeCount;
            for (int e = 0; e < edgeCount; e++)
            {
                if (edgeBoundary[e] != BoundaryType.None)
                {
                    var (c0, c1) = mesh.EdgeCells[e];
                    nearBoundary[c0] = true;
                    nearBoundary[c1] = true;
                }
            }

            for (int c = 0; c < cellCount; c++)
            {
                if (nearBoundary[c])
                {
                    boundaryExposure[c]++;
                    lastBoundaryStep[c] = step;
                }
                else
                {
                    plateContinuity[c]++;
                }
            }
        }

        /// <summary>
        /// Fisher-Yates shuffle plate indices; first floor(count * fraction) are oceanic.
        /// </summary>
        internal static bool[] AssignPlateTypes(int plateCount, float oceanFraction, Random rng)
        {
            int[] indices = new int[plateCount];
            for (int i = 0; i < plateCount; i++)
                indices[i] = i;

            // Fisher-Yates shuffle
            for (int i = plateCount - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                int tmp = indices[i];
                indices[i] = indices[j];
                indices[j] = tmp;
            }

            int oceanCount = (int)(plateCount * oceanFraction);
            bool[] isOceanic = new bool[plateCount];
            for (int i = 0; i < oceanCount; i++)
                isOceanic[indices[i]] = true;

            return isOceanic;
        }

        /// <summary>
        /// Build plate adjacency: for each plate, which other plates share a boundary edge.
        /// </summary>
        internal static HashSet<int>[] BuildPlateAdjacency(SphereMesh mesh, int[] cellPlate, int plateCount)
        {
            var adj = new HashSet<int>[plateCount];
            for (int p = 0; p < plateCount; p++)
                adj[p] = new HashSet<int>();

            int edgeCount = mesh.EdgeCount;
            for (int e = 0; e < edgeCount; e++)
            {
                var (c0, c1) = mesh.EdgeCells[e];
                int p0 = cellPlate[c0];
                int p1 = cellPlate[c1];
                if (p0 != p1)
                {
                    adj[p0].Add(p1);
                    adj[p1].Add(p0);
                }
            }

            return adj;
        }

        /// <summary>
        /// Flip minor oceanic plates to continental if all their neighbors are oceanic,
        /// creating sub-continent islands. Candidates are shuffled and capped at maxCount.
        /// </summary>
        internal static void PromoteSubcontinents(
            bool[] isOceanic, bool[] isMajor, HashSet<int>[] plateNeighbors, int maxCount, Random rng)
        {
            // Collect candidates: minor, oceanic, all neighbors oceanic
            var candidates = new List<int>();
            for (int p = 0; p < isOceanic.Length; p++)
            {
                if (!isOceanic[p] || isMajor[p])
                    continue;

                bool allNeighborsOceanic = true;
                foreach (int nb in plateNeighbors[p])
                {
                    if (!isOceanic[nb])
                    {
                        allNeighborsOceanic = false;
                        break;
                    }
                }

                if (allNeighborsOceanic)
                    candidates.Add(p);
            }

            // Shuffle and promote up to maxCount
            for (int i = candidates.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                int tmp = candidates[i];
                candidates[i] = candidates[j];
                candidates[j] = tmp;
            }

            int promoted = Math.Min(maxCount, candidates.Count);
            for (int i = 0; i < promoted; i++)
                isOceanic[candidates[i]] = false;
        }

        internal static float[] ComputeBaseElevation(int cellCount, int[] cellPlate, bool[] isOceanic)
        {
            float[] elevation = new float[cellCount];
            for (int c = 0; c < cellCount; c++)
                elevation[c] = isOceanic[cellPlate[c]] ? OceanicBase : ContinentalBase;
            return elevation;
        }

        /// <summary>
        /// Compute base elevation from per-cell crust type (for multi-step mode).
        /// </summary>
        internal static float[] ComputeBaseElevation(int cellCount, bool[] cellCrustOceanic)
        {
            float[] elevation = new float[cellCount];
            for (int c = 0; c < cellCount; c++)
                elevation[c] = cellCrustOceanic[c] ? OceanicBase : ContinentalBase;
            return elevation;
        }

        /// <summary>
        /// For each boundary edge, compute effect magnitude and BFS-propagate inward
        /// with linear decay. Max-abs-wins for overlapping effects.
        /// </summary>
        internal static void ApplyBoundaryEffects(SphereMesh mesh, TectonicData tectonics, float[] elevation)
        {
            int cellCount = mesh.CellCount;
            float[] effect = new float[cellCount]; // accumulated boundary effect per cell

            // Collect boundary cells and their direct effects
            // For each boundary edge, both adjacent cells get the effect
            int edgeCount = mesh.EdgeCount;
            for (int e = 0; e < edgeCount; e++)
            {
                if (tectonics.EdgeBoundary[e] == BoundaryType.None)
                    continue;

                float baseLift;
                switch (tectonics.EdgeBoundary[e])
                {
                    case BoundaryType.Convergent: baseLift = ConvergentLift; break;
                    case BoundaryType.Divergent: baseLift = DivergentDrop; break;
                    case BoundaryType.Transform: baseLift = TransformLift; break;
                    default: continue;
                }

                // Scale by convergence magnitude
                float scale = Math.Min(Math.Abs(tectonics.EdgeConvergence[e]) / 2f, 1f);
                float edgeEffect = baseLift * scale;

                var (c0, c1) = mesh.EdgeCells[e];
                // Max-abs-wins
                if (Math.Abs(edgeEffect) > Math.Abs(effect[c0]))
                    effect[c0] = edgeEffect;
                if (Math.Abs(edgeEffect) > Math.Abs(effect[c1]))
                    effect[c1] = edgeEffect;
            }

            // BFS propagation inward from boundary cells
            // Each hop decays linearly: depth 0 = full, depth PropagationDepth = 1/(PropagationDepth+1)
            var queue = new Queue<int>();
            int[] depth = new int[cellCount];
            float[] sourceEffect = new float[cellCount]; // original boundary effect to decay from
            for (int i = 0; i < cellCount; i++)
                depth[i] = -1;

            for (int c = 0; c < cellCount; c++)
            {
                if (effect[c] != 0f)
                {
                    queue.Enqueue(c);
                    depth[c] = 0;
                    sourceEffect[c] = effect[c];
                }
            }

            while (queue.Count > 0)
            {
                int cell = queue.Dequeue();
                int d = depth[cell];
                if (d >= PropagationDepth)
                    continue;

                int nextDepth = d + 1;
                float decay = 1f - (float)nextDepth / (PropagationDepth + 1);
                float propagated = sourceEffect[cell] * decay;

                int[] neighbors = mesh.CellNeighbors[cell];
                for (int i = 0; i < neighbors.Length; i++)
                {
                    int nb = neighbors[i];
                    if (depth[nb] != -1)
                        continue;

                    depth[nb] = nextDepth;
                    sourceEffect[nb] = sourceEffect[cell];
                    // Max-abs-wins for the propagated effect
                    if (Math.Abs(propagated) > Math.Abs(effect[nb]))
                        effect[nb] = propagated;
                    queue.Enqueue(nb);
                }
            }

            // Apply effects to elevation
            for (int c = 0; c < cellCount; c++)
                elevation[c] += effect[c];
        }

        internal static void Smooth(SphereMesh mesh, float[] elevation)
        {
            int cellCount = mesh.CellCount;
            float[] buffer = new float[cellCount];

            for (int pass = 0; pass < SmoothingPasses; pass++)
            {
                for (int c = 0; c < cellCount; c++)
                {
                    int[] neighbors = mesh.CellNeighbors[c];
                    float avg = 0f;
                    for (int i = 0; i < neighbors.Length; i++)
                        avg += elevation[neighbors[i]];
                    avg /= neighbors.Length;

                    buffer[c] = elevation[c] + SmoothingWeight * (avg - elevation[c]);
                }

                Array.Copy(buffer, elevation, cellCount);
            }
        }

        internal static void Clamp01(float[] elevation)
        {
            for (int i = 0; i < elevation.Length; i++)
                elevation[i] = Math.Max(0f, Math.Min(1f, elevation[i]));
        }
    }
}
