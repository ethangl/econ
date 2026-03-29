using System;
using System.Collections.Generic;

namespace WorldGen.Core
{
    /// <summary>
    /// Identifies sedimentary basins: connected low-lying continental areas
    /// enclosed between mountain ranges. Flood-fills connected components of
    /// low-elevation continental cells and flattens them toward a basin floor.
    /// </summary>
    public static class BasinOps
    {
        const float SeaLevel = 0.5f;
        const float ContinentalBase = 0.65f;

        public static void Generate(SphereMesh mesh, TectonicData tectonics, WorldGenConfig config)
        {
            int cellCount = mesh.CellCount;

            // Step 1: Identify basin candidate cells.
            // Continental cells above sea level but at least BasinElevationThreshold
            // below ContinentalBase — the low-lying areas pulled down by erosion,
            // rifts, or sitting between mountain ranges.
            bool[] isCandidate = new bool[cellCount];
            float maxElev = ContinentalBase - config.BasinElevationThreshold;

            for (int c = 0; c < cellCount; c++)
            {
                if (tectonics.CellCrustOceanic[c])
                    continue;
                float e = tectonics.CellElevation[c];
                if (e >= SeaLevel && e < maxElev)
                    isCandidate[c] = true;
            }

            // Step 2: Flood-fill connected components of candidate cells.
            // For each component, count how many external boundary neighbors are
            // below sea level vs above. Components where a large fraction of the
            // perimeter faces water are coastal plains, not enclosed basins.
            int[] basinId = new int[cellCount];
            int currentBasin = 0;
            var basinCellLists = new List<List<int>>();
            var basinOceanFraction = new List<float>();

            for (int c = 0; c < cellCount; c++)
            {
                if (!isCandidate[c] || basinId[c] != 0)
                    continue;

                currentBasin++;
                var component = new List<int>();
                var queue = new Queue<int>();
                int borderTotal = 0;
                int borderOcean = 0;
                queue.Enqueue(c);
                basinId[c] = currentBasin;

                while (queue.Count > 0)
                {
                    int cell = queue.Dequeue();
                    component.Add(cell);
                    int[] neighbors = mesh.CellNeighbors[cell];
                    for (int i = 0; i < neighbors.Length; i++)
                    {
                        int nb = neighbors[i];
                        if (isCandidate[nb] && basinId[nb] == 0)
                        {
                            basinId[nb] = currentBasin;
                            queue.Enqueue(nb);
                        }
                        else if (!isCandidate[nb])
                        {
                            borderTotal++;
                            if (tectonics.CellElevation[nb] < SeaLevel)
                                borderOcean++;
                        }
                    }
                }

                float oceanFrac = borderTotal > 0 ? (float)borderOcean / borderTotal : 0f;
                basinCellLists.Add(component);
                basinOceanFraction.Add(oceanFrac);
            }

            // Step 3: Filter basins that are too small or too exposed to ocean, then compact IDs.
            // A basin with more than half its perimeter facing sub-sea-level cells
            // is a coastal lowland, not an enclosed interior basin.
            const float MaxOceanFraction = 0.5f;
            int finalBasinCount = 0;
            int[] idRemap = new int[currentBasin + 1]; // old ID -> new ID (0 = removed)

            for (int b = 0; b < basinCellLists.Count; b++)
            {
                if (basinCellLists[b].Count >= config.BasinMinCells && basinOceanFraction[b] <= MaxOceanFraction)
                {
                    finalBasinCount++;
                    idRemap[b + 1] = finalBasinCount;
                }
            }

            // Remap IDs
            for (int c = 0; c < cellCount; c++)
                basinId[c] = idRemap[basinId[c]];

            // Step 4: Compute per-basin floor elevation and flatten.
            float flattenStrength = config.BasinFlattenStrength;
            int totalBasinCells = 0;

            for (int b = 0; b < basinCellLists.Count; b++)
            {
                if (idRemap[b + 1] == 0)
                    continue; // filtered out

                var cells = basinCellLists[b];
                totalBasinCells += cells.Count;

                // Find minimum elevation in this basin
                float minElev = float.MaxValue;
                for (int i = 0; i < cells.Count; i++)
                {
                    float e = tectonics.CellElevation[cells[i]];
                    if (e < minElev) minElev = e;
                }

                float floor = minElev + config.BasinFloorOffset;

                // Flatten toward floor
                for (int i = 0; i < cells.Count; i++)
                {
                    int c = cells[i];
                    tectonics.CellElevation[c] += flattenStrength * (floor - tectonics.CellElevation[c]);
                }
            }

            // Step 5: Clamp and store.
            for (int c = 0; c < cellCount; c++)
                tectonics.CellElevation[c] = Math.Max(0f, Math.Min(1f, tectonics.CellElevation[c]));

            tectonics.CellBasinId = basinId;
            tectonics.BasinCount = finalBasinCount;

            Console.WriteLine($"    Basins: {finalBasinCount} basins, {totalBasinCells} cells");
        }
    }
}
