using System;
using System.Collections.Generic;

namespace WorldGen.Core
{
    /// <summary>
    /// Applies Airy isostatic adjustment: estimates crustal thickness per cell from
    /// plate type, boundary proximity, and craton strength, then nudges elevation
    /// toward the isostatic equilibrium. Mountains sink slightly (boundary lift
    /// exceeds root support), continental interiors rise slightly (thick cratonic roots).
    /// Should run after all other elevation modifiers, before dense terrain.
    /// </summary>
    public static class IsostaticOps
    {
        const float ContinentalBase = 0.65f;
        const float OceanicBase = 0.15f;

        // Crustal support model: continental base + boundary proximity bonuses/penalties.
        // Convergent boundaries thicken crust (mountain roots) but less than the
        // elevation lift from boundary effects, so mountains net-sink.
        const float ConvergentSupportBonus = 0.25f;
        const float DivergentSupportPenalty = 0.15f;
        const float CratonSupportBonus = 0.10f;

        // BFS range for boundary proximity decay.
        const int MaxHops = 5;

        public static void Generate(SphereMesh mesh, TectonicData tectonics, WorldGenConfig config)
        {
            float strength = config.IsostaticStrength;
            if (strength <= 0f)
            {
                Console.WriteLine("    Isostasy: disabled (strength=0)");
                return;
            }

            int cellCount = mesh.CellCount;
            int edgeCount = mesh.EdgeCount;

            // Step 1: BFS from convergent boundary edges.
            int[] convergentDist = BfsFromBoundaryType(mesh, tectonics, edgeCount, cellCount, BoundaryType.Convergent);

            // Step 2: BFS from divergent boundary edges.
            int[] divergentDist = BfsFromBoundaryType(mesh, tectonics, edgeCount, cellCount, BoundaryType.Divergent);

            // Step 3: Compute isostatic support per cell.
            // Continental cells get support from: base + convergent proximity + craton roots - divergent proximity.
            // Oceanic cells: support = current elevation (no adjustment).
            float[] support = new float[cellCount];
            float[] cratonStrength = tectonics.CellCratonStrength;

            for (int c = 0; c < cellCount; c++)
            {
                if (tectonics.CellCrustOceanic[c])
                {
                    // Oceanic: equilibrium ≈ current elevation, no adjustment needed.
                    support[c] = tectonics.CellElevation[c];
                    continue;
                }

                float s = ContinentalBase;

                // Convergent proximity: mountain roots thicken crust.
                int cd = convergentDist[c];
                if (cd >= 0 && cd <= MaxHops)
                {
                    float proximity = 1f - (float)cd / (MaxHops + 1);
                    s += proximity * ConvergentSupportBonus;
                }

                // Craton roots: ancient thick lithosphere.
                if (cratonStrength != null && cratonStrength[c] > 0f)
                {
                    s += cratonStrength[c] * CratonSupportBonus;
                }

                // Divergent proximity: thinned crust at rifts.
                int dd = divergentDist[c];
                if (dd >= 0 && dd <= MaxHops)
                {
                    float proximity = 1f - (float)dd / (MaxHops + 1);
                    s -= proximity * DivergentSupportPenalty;
                }

                support[c] = s;
            }

            // Step 4: Adjust elevation toward isostatic equilibrium.
            int adjustedCount = 0;
            float totalDelta = 0f;
            float maxSink = 0f;
            float maxRise = 0f;

            int[] basinId = tectonics.CellBasinId;

            for (int c = 0; c < cellCount; c++)
            {
                if (tectonics.CellCrustOceanic[c])
                    continue;

                // Skip basin cells — BasinOps already flattened them to correct elevation.
                if (basinId != null && basinId[c] != 0)
                    continue;

                float delta = strength * (support[c] - tectonics.CellElevation[c]);
                if (Math.Abs(delta) < 1e-6f)
                    continue;

                tectonics.CellElevation[c] += delta;
                adjustedCount++;
                totalDelta += delta;
                if (delta < maxSink) maxSink = delta;
                if (delta > maxRise) maxRise = delta;
            }

            // Step 5: Clamp and store.
            for (int c = 0; c < cellCount; c++)
                tectonics.CellElevation[c] = Math.Max(0f, Math.Min(1f, tectonics.CellElevation[c]));

            tectonics.CellIsostaticSupport = support;

            float avgDelta = adjustedCount > 0 ? totalDelta / adjustedCount : 0f;
            Console.WriteLine($"    Isostasy: {adjustedCount} cells adjusted, avg delta {avgDelta:F4}, sink {maxSink:F3}, rise {maxRise:F3}");
        }

        static int[] BfsFromBoundaryType(SphereMesh mesh, TectonicData tectonics, int edgeCount, int cellCount, BoundaryType type)
        {
            int[] dist = new int[cellCount];
            for (int i = 0; i < cellCount; i++)
                dist[i] = -1;

            var queue = new Queue<int>();

            for (int e = 0; e < edgeCount; e++)
            {
                if (tectonics.EdgeBoundary[e] != type)
                    continue;

                var (c0, c1) = mesh.EdgeCells[e];
                if (dist[c0] == -1)
                {
                    dist[c0] = 0;
                    queue.Enqueue(c0);
                }
                if (dist[c1] == -1)
                {
                    dist[c1] = 0;
                    queue.Enqueue(c1);
                }
            }

            while (queue.Count > 0)
            {
                int cell = queue.Dequeue();
                int nextDist = dist[cell] + 1;
                if (nextDist > MaxHops)
                    continue;

                int[] neighbors = mesh.CellNeighbors[cell];
                for (int i = 0; i < neighbors.Length; i++)
                {
                    int nb = neighbors[i];
                    if (dist[nb] == -1)
                    {
                        dist[nb] = nextDist;
                        queue.Enqueue(nb);
                    }
                }
            }

            return dist;
        }
    }
}
