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
        const float OceanicBase = 0.2f;
        const float ContinentalBase = 0.7f;
        const float ConvergentLift = 0.25f;
        const float DivergentDrop = -0.25f;
        const float TransformLift = 0.125f;
        const int PropagationDepth = 3;
        const int SmoothingPasses = 2;
        const float SmoothingWeight = 0.2f;

        /// <summary>
        /// Run the full elevation pipeline. Populates tectonics.PlateIsOceanic and tectonics.CellElevation.
        /// </summary>
        public static void Generate(SphereMesh mesh, TectonicData tectonics, float oceanFraction, int seed)
        {
            var rng = new Random(seed);
            int cellCount = mesh.CellCount;

            bool[] isOceanic = AssignPlateTypes(tectonics.PlateCount, oceanFraction, rng);
            float[] elevation = ComputeBaseElevation(cellCount, tectonics.CellPlate, isOceanic);
            ApplyBoundaryEffects(mesh, tectonics, elevation);
            Smooth(mesh, elevation);
            Clamp01(elevation);

            tectonics.PlateIsOceanic = isOceanic;
            tectonics.CellElevation = elevation;
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

        internal static float[] ComputeBaseElevation(int cellCount, int[] cellPlate, bool[] isOceanic)
        {
            float[] elevation = new float[cellCount];
            for (int c = 0; c < cellCount; c++)
                elevation[c] = isOceanic[cellPlate[c]] ? OceanicBase : ContinentalBase;
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
