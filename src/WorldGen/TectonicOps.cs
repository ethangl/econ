using System;
using System.Collections.Generic;

namespace WorldGen.Core
{
    /// <summary>
    /// Tectonic plate generation: seed selection, flood-fill growth,
    /// drift vector assignment, and boundary classification.
    /// </summary>
    public static class TectonicOps
    {
        /// <summary>
        /// Run the full tectonic pipeline: seed majors → head-start BFS → seed minors → finish BFS → drift → classify.
        /// Major plates get IDs 0..majorCount-1, minor plates get majorCount..totalCount-1.
        /// </summary>
        public static TectonicData Generate(SphereMesh mesh, int majorCount, int minorCount, int headStartRounds, int seed)
        {
            var rng = new Random(seed);
            int totalCount = majorCount + minorCount;
            totalCount = Math.Min(totalCount, mesh.CellCount);
            majorCount = Math.Min(majorCount, totalCount);
            minorCount = totalCount - majorCount;

            int[] majorSeeds = SelectSeeds(mesh, majorCount, rng);
            var (cellPlate, minorSeeds) = GrowPlates(mesh, majorSeeds, majorCount, minorCount, headStartRounds, rng);

            // Build combined seeds array: major seeds [0..majorCount-1], minor seeds [majorCount..totalCount-1]
            int[] allSeeds = new int[totalCount];
            Array.Copy(majorSeeds, 0, allSeeds, 0, majorCount);
            Array.Copy(minorSeeds, 0, allSeeds, majorCount, minorCount);

            Vec3[] drifts = GenerateDrifts(mesh, allSeeds, totalCount, rng);
            var (edgeBoundary, edgeConvergence) = ClassifyBoundaries(mesh, cellPlate, drifts);

            bool[] isMajor = new bool[totalCount];
            for (int i = 0; i < majorCount; i++)
                isMajor[i] = true;

            return new TectonicData
            {
                CellPlate = cellPlate,
                PlateCount = totalCount,
                PlateSeeds = allSeeds,
                PlateDrift = drifts,
                PlateIsMajor = isMajor,
                EdgeBoundary = edgeBoundary,
                EdgeConvergence = edgeConvergence,
            };
        }

        /// <summary>
        /// Farthest-point heuristic: pick seeds that maximize minimum distance to existing seeds.
        /// </summary>
        internal static int[] SelectSeeds(SphereMesh mesh, int plateCount, Random rng)
        {
            int n = mesh.CellCount;
            int[] seeds = new int[plateCount];

            // First seed: random
            seeds[0] = rng.Next(n);

            // Track min squared distance from each cell to nearest seed
            float[] minDist = new float[n];
            for (int i = 0; i < n; i++)
                minDist[i] = float.MaxValue;

            // Update distances for first seed
            Vec3 seedPos = mesh.CellCenters[seeds[0]];
            for (int i = 0; i < n; i++)
                minDist[i] = Vec3.SqrDistance(mesh.CellCenters[i], seedPos);

            for (int s = 1; s < plateCount; s++)
            {
                // Pick cell with maximum min-distance to any existing seed
                int best = -1;
                float bestDist = -1f;
                for (int i = 0; i < n; i++)
                {
                    if (minDist[i] > bestDist)
                    {
                        bestDist = minDist[i];
                        best = i;
                    }
                }
                seeds[s] = best;

                // Update min distances with new seed
                seedPos = mesh.CellCenters[best];
                for (int i = 0; i < n; i++)
                {
                    float d = Vec3.SqrDistance(mesh.CellCenters[i], seedPos);
                    if (d < minDist[i])
                        minDist[i] = d;
                }
            }

            return seeds;
        }

        /// <summary>
        /// Three-phase level-synchronous BFS:
        /// 1) Major seeds grow for headStartRounds
        /// 2) Minor seeds placed in unclaimed space
        /// 3) All plates grow until every cell is claimed
        /// </summary>
        internal static (int[] cellPlate, int[] minorSeeds) GrowPlates(
            SphereMesh mesh, int[] majorSeeds, int majorCount, int minorCount,
            int headStartRounds, Random rng)
        {
            int n = mesh.CellCount;
            int[] cellPlate = new int[n];
            for (int i = 0; i < n; i++)
                cellPlate[i] = -1;

            // Phase 1: enqueue major seeds, run headStartRounds of level-synchronous BFS
            var frontier = new List<int>();
            for (int p = 0; p < majorCount; p++)
            {
                cellPlate[majorSeeds[p]] = p;
                frontier.Add(majorSeeds[p]);
            }

            for (int round = 0; round < headStartRounds && frontier.Count > 0; round++)
            {
                var nextFrontier = new List<int>();
                for (int f = 0; f < frontier.Count; f++)
                {
                    int cell = frontier[f];
                    int plate = cellPlate[cell];
                    int[] neighbors = mesh.CellNeighbors[cell];
                    for (int i = 0; i < neighbors.Length; i++)
                    {
                        int nb = neighbors[i];
                        if (cellPlate[nb] == -1)
                        {
                            cellPlate[nb] = plate;
                            nextFrontier.Add(nb);
                        }
                    }
                }
                frontier = nextFrontier;
            }

            // Phase 2: seed minor plates in unclaimed space
            int[] minorSeeds = SelectMinorSeeds(mesh, cellPlate, majorSeeds, minorCount, rng);
            for (int p = 0; p < minorCount; p++)
            {
                int plateId = majorCount + p;
                cellPlate[minorSeeds[p]] = plateId;
                frontier.Add(minorSeeds[p]);
            }

            // Phase 3: standard BFS until all cells claimed
            var queue = new Queue<int>(frontier);
            while (queue.Count > 0)
            {
                int cell = queue.Dequeue();
                int plate = cellPlate[cell];
                int[] neighbors = mesh.CellNeighbors[cell];
                for (int i = 0; i < neighbors.Length; i++)
                {
                    int nb = neighbors[i];
                    if (cellPlate[nb] == -1)
                    {
                        cellPlate[nb] = plate;
                        queue.Enqueue(nb);
                    }
                }
            }

            return (cellPlate, minorSeeds);
        }

        /// <summary>
        /// Farthest-point heuristic among unclaimed cells, measuring distance to all existing seeds.
        /// </summary>
        internal static int[] SelectMinorSeeds(
            SphereMesh mesh, int[] cellPlate, int[] majorSeeds, int minorCount, Random rng)
        {
            int n = mesh.CellCount;
            int[] seeds = new int[minorCount];

            // Track min squared distance from each cell to any seed (major + already-placed minor)
            float[] minDist = new float[n];
            for (int i = 0; i < n; i++)
                minDist[i] = float.MaxValue;

            // Initialize with distances to major seeds
            for (int s = 0; s < majorSeeds.Length; s++)
            {
                Vec3 seedPos = mesh.CellCenters[majorSeeds[s]];
                for (int i = 0; i < n; i++)
                {
                    float d = Vec3.SqrDistance(mesh.CellCenters[i], seedPos);
                    if (d < minDist[i])
                        minDist[i] = d;
                }
            }

            for (int s = 0; s < minorCount; s++)
            {
                // Pick unclaimed cell with maximum min-distance
                int best = -1;
                float bestDist = -1f;
                for (int i = 0; i < n; i++)
                {
                    if (cellPlate[i] == -1 && minDist[i] > bestDist)
                    {
                        bestDist = minDist[i];
                        best = i;
                    }
                }

                if (best == -1)
                    break; // no unclaimed cells left

                seeds[s] = best;

                // Update min distances with new seed
                Vec3 pos = mesh.CellCenters[best];
                for (int i = 0; i < n; i++)
                {
                    float d = Vec3.SqrDistance(mesh.CellCenters[i], pos);
                    if (d < minDist[i])
                        minDist[i] = d;
                }
            }

            return seeds;
        }

        /// <summary>
        /// Generate a random tangent drift vector at each plate's seed position.
        /// </summary>
        internal static Vec3[] GenerateDrifts(SphereMesh mesh, int[] seeds, int plateCount, Random rng)
        {
            Vec3[] drifts = new Vec3[plateCount];

            for (int p = 0; p < plateCount; p++)
            {
                Vec3 radial = mesh.CellCenters[seeds[p]].Normalized;
                Vec3 tangent;

                // Generate random direction and project out radial component
                for (int attempt = 0; attempt < 10; attempt++)
                {
                    Vec3 randomDir = new Vec3(
                        (float)(rng.NextDouble() * 2 - 1),
                        (float)(rng.NextDouble() * 2 - 1),
                        (float)(rng.NextDouble() * 2 - 1)
                    );

                    tangent = randomDir - radial * Vec3.Dot(randomDir, radial);
                    float mag = tangent.Magnitude;

                    if (mag > 0.1f)
                    {
                        // Normalize to a reasonable drift magnitude
                        drifts[p] = tangent * (1f / mag);
                        break;
                    }
                    // Degenerate (random dir near-parallel to radial), retry
                }
            }

            return drifts;
        }

        /// <summary>
        /// Classify each edge as convergent, divergent, transform, or none (interior).
        /// </summary>
        internal static (BoundaryType[], float[]) ClassifyBoundaries(
            SphereMesh mesh, int[] cellPlate, Vec3[] plateDrift)
        {
            int edgeCount = mesh.EdgeCount;
            var boundary = new BoundaryType[edgeCount];
            var convergence = new float[edgeCount];

            for (int e = 0; e < edgeCount; e++)
            {
                var (c0, c1) = mesh.EdgeCells[e];
                int p0 = cellPlate[c0];
                int p1 = cellPlate[c1];

                if (p0 == p1)
                {
                    boundary[e] = BoundaryType.None;
                    convergence[e] = 0f;
                    continue;
                }

                // Edge direction: from cell0 center to cell1 center, normalized
                Vec3 edgeDir = (mesh.CellCenters[c1] - mesh.CellCenters[c0]).Normalized;

                // Project drift vectors onto edge direction
                float proj0 = Vec3.Dot(plateDrift[p0], edgeDir);
                float proj1 = Vec3.Dot(plateDrift[p1], edgeDir);
                float conv = proj0 - proj1; // positive = approaching each other

                // Compute shear (transverse component difference)
                Vec3 trans0 = plateDrift[p0] - edgeDir * proj0;
                Vec3 trans1 = plateDrift[p1] - edgeDir * proj1;
                float shear = (trans0 - trans1).Magnitude;

                convergence[e] = conv;

                if (Math.Abs(conv) > shear * 0.5f)
                {
                    boundary[e] = conv > 0 ? BoundaryType.Convergent : BoundaryType.Divergent;
                }
                else
                {
                    boundary[e] = BoundaryType.Transform;
                }
            }

            return (boundary, convergence);
        }
    }
}
