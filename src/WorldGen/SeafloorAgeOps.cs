using System;
using System.Collections.Generic;

namespace WorldGen.Core
{
    /// <summary>
    /// Computes seafloor age as BFS distance from divergent boundaries across oceanic cells.
    /// Older crust (farther from ridges) is cooler, denser, and sinks deeper.
    /// Modifies oceanic cell elevation to create a depth gradient instead of flat ocean floor.
    /// </summary>
    public static class SeafloorAgeOps
    {
        /// <summary>Ridge elevation (young crust, shallowest ocean floor).</summary>
        const float RidgeElevation = 0.30f;

        /// <summary>Deep ocean elevation (oldest crust, deepest floor).</summary>
        const float DeepOceanElevation = 0.08f;

        /// <summary>BFS hops at which ocean floor reaches maximum depth.
        /// Beyond this, elevation stays at DeepOceanElevation.</summary>
        const int MaxAgeHops = 8;

        /// <summary>
        /// Compute seafloor age and apply depth gradient to oceanic cells.
        /// Call after plate types are assigned but before boundary effects.
        /// </summary>
        public static void Apply(SphereMesh mesh, TectonicData tectonics, bool[] plateIsOceanic, float[] elevation)
        {
            int cellCount = mesh.CellCount;
            int[] age = new int[cellCount];
            for (int i = 0; i < cellCount; i++)
                age[i] = -1;

            // Seed BFS from oceanic cells adjacent to divergent boundaries
            var queue = new Queue<int>();
            int edgeCount = mesh.EdgeCount;
            for (int e = 0; e < edgeCount; e++)
            {
                if (tectonics.EdgeBoundary[e] != BoundaryType.Divergent)
                    continue;

                var (c0, c1) = mesh.EdgeCells[e];

                if (plateIsOceanic[tectonics.CellPlate[c0]] && age[c0] == -1)
                {
                    age[c0] = 0;
                    queue.Enqueue(c0);
                }
                if (plateIsOceanic[tectonics.CellPlate[c1]] && age[c1] == -1)
                {
                    age[c1] = 0;
                    queue.Enqueue(c1);
                }
            }

            // BFS across oceanic cells within the same plate.
            // Don't cross plate boundaries — each oceanic plate gets its own
            // age gradient from its own ridges, preserving depth discontinuities
            // at convergent and transform boundaries between oceanic plates.
            while (queue.Count > 0)
            {
                int cell = queue.Dequeue();
                int nextAge = age[cell] + 1;
                int plate = tectonics.CellPlate[cell];

                int[] neighbors = mesh.CellNeighbors[cell];
                for (int i = 0; i < neighbors.Length; i++)
                {
                    int nb = neighbors[i];
                    if (age[nb] != -1)
                        continue;
                    if (tectonics.CellPlate[nb] != plate)
                        continue;

                    age[nb] = nextAge;
                    queue.Enqueue(nb);
                }
            }

            // Oceanic cells unreached by BFS (no divergent boundary in their plate)
            // are treated as maximum-age old crust
            for (int c = 0; c < cellCount; c++)
            {
                if (age[c] == -1 && plateIsOceanic[tectonics.CellPlate[c]])
                    age[c] = MaxAgeHops;
            }

            // Apply depth gradient to oceanic cells
            for (int c = 0; c < cellCount; c++)
            {
                if (age[c] < 0)
                    continue;

                // Lerp from ridge (young) to deep ocean (old)
                float t = Math.Min((float)age[c] / MaxAgeHops, 1f);
                // Square root curve: rapid deepening near ridge, flattening at depth
                // Matches real bathymetry (Parsons-Sclater cooling model)
                float sqrtT = (float)Math.Sqrt(t);
                elevation[c] = RidgeElevation + (DeepOceanElevation - RidgeElevation) * sqrtT;
            }

            tectonics.CellSeafloorAge = age;
        }
    }
}
