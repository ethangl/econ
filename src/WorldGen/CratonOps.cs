using System;
using System.Collections.Generic;

namespace WorldGen.Core
{
    /// <summary>
    /// Identifies ancient stable continental interiors (cratons/shields) and
    /// flattens their elevation toward the continental base.
    /// Cells far from any plate boundary on continental plates get a craton strength
    /// gradient (0-1) used for elevation flattening and noise dampening in DenseTerrainOps.
    /// </summary>
    public static class CratonOps
    {
        const float ContinentalBase = 0.65f;
        const float SeaLevel = 0.5f;

        public static void Generate(SphereMesh mesh, TectonicData tectonics, WorldGenConfig config)
        {
            int cellCount = mesh.CellCount;

            // Step 1: Multi-source BFS from all plate boundary edges.
            // Computes minimum hop distance to any boundary for every cell.
            int[] boundaryDist = new int[cellCount];
            for (int i = 0; i < cellCount; i++)
                boundaryDist[i] = -1;

            var queue = new Queue<int>();
            int edgeCount = mesh.EdgeCount;
            for (int e = 0; e < edgeCount; e++)
            {
                if (tectonics.EdgeBoundary[e] == BoundaryType.None)
                    continue;

                var (c0, c1) = mesh.EdgeCells[e];
                if (boundaryDist[c0] == -1)
                {
                    boundaryDist[c0] = 0;
                    queue.Enqueue(c0);
                }
                if (boundaryDist[c1] == -1)
                {
                    boundaryDist[c1] = 0;
                    queue.Enqueue(c1);
                }
            }

            while (queue.Count > 0)
            {
                int cell = queue.Dequeue();
                int nextDist = boundaryDist[cell] + 1;

                int[] neighbors = mesh.CellNeighbors[cell];
                for (int i = 0; i < neighbors.Length; i++)
                {
                    int nb = neighbors[i];
                    if (boundaryDist[nb] == -1)
                    {
                        boundaryDist[nb] = nextDist;
                        queue.Enqueue(nb);
                    }
                }
            }

            // Step 2: Compute craton strength for continental cells above sea level.
            float[] cratonStrength = new float[cellCount];
            int minDist = config.CratonMinBoundaryDistance;
            int rampWidth = config.CratonRampWidth;

            for (int c = 0; c < cellCount; c++)
            {
                if (tectonics.PlateIsOceanic[tectonics.CellPlate[c]])
                    continue;
                if (tectonics.CellElevation[c] < SeaLevel)
                    continue;

                int dist = boundaryDist[c];
                if (dist < 0 || dist < minDist)
                    continue;

                float strength = rampWidth > 0
                    ? Math.Min(1f, (float)(dist - minDist) / rampWidth)
                    : 1f;
                cratonStrength[c] = strength;
            }

            // Step 3: Flatten elevation toward ContinentalBase.
            float flattenStrength = config.CratonFlattenStrength;
            int cratonCellCount = 0;
            float totalStrength = 0f;

            for (int c = 0; c < cellCount; c++)
            {
                if (cratonStrength[c] <= 0f)
                    continue;

                float t = cratonStrength[c] * flattenStrength;
                tectonics.CellElevation[c] += t * (ContinentalBase - tectonics.CellElevation[c]);
                cratonCellCount++;
                totalStrength += cratonStrength[c];
            }

            // Step 4: Clamp and store.
            for (int c = 0; c < cellCount; c++)
                tectonics.CellElevation[c] = Math.Max(0f, Math.Min(1f, tectonics.CellElevation[c]));

            tectonics.CellCratonStrength = cratonStrength;

            float avgStrength = cratonCellCount > 0 ? totalStrength / cratonCellCount : 0f;
            Console.WriteLine($"    Cratons: {cratonCellCount} cells tagged, avg strength {avgStrength:F2}");
        }
    }
}
