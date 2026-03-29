using System;
using System.Collections.Generic;

namespace WorldGen.Core
{
    /// <summary>
    /// Generates seamounts and abyssal hills on oceanic crust.
    /// Data phase: tags coarse oceanic cells with seamount density based on
    /// hotspot trails and young crust near ridges. Scatters peak positions
    /// for cone-stamping at dense terrain resolution.
    /// </summary>
    public static class SeamountOps
    {
        /// <summary>
        /// Compute seamount density per coarse cell and scatter peak positions.
        /// Call after hotspots, volcanic arcs, and seafloor age are computed.
        /// Peak elevation is applied later in DenseTerrainOps.
        /// </summary>
        public static void Generate(SphereMesh mesh, TectonicData tectonics, WorldGenConfig config)
        {
            int cellCount = mesh.CellCount;
            float[] density = new float[cellCount];

            // Tag oceanic cells with seamount density from two sources:
            // 1. Hotspot trails on oceanic crust (seamount chains)
            // 2. Young oceanic crust near ridges (abyssal hills)
            for (int c = 0; c < cellCount; c++)
            {
                if (!tectonics.CellCrustOceanic[c])
                    continue;

                // Hotspot trail contribution — oceanic hotspot trails produce
                // seamount chains (e.g. Hawaiian-Emperor chain)
                float hotspotContrib = 0f;
                if (tectonics.CellHotspotIntensity != null)
                    hotspotContrib = tectonics.CellHotspotIntensity[c];

                // Young crust contribution — newly formed crust near ridges
                // has abundant small volcanic features (abyssal hills).
                // Age 0 = at the ridge itself (already has ridge elevation), skip.
                // Ages 1..MaxAge produce hills with decaying density.
                float ageContrib = 0f;
                if (tectonics.CellSeafloorAge != null && tectonics.CellSeafloorAge[c] > 0)
                {
                    int age = tectonics.CellSeafloorAge[c];
                    if (age <= config.SeamountYoungCrustMaxAge)
                        ageContrib = 1f - (float)(age - 1) / config.SeamountYoungCrustMaxAge;
                }

                density[c] = Math.Max(hotspotContrib, ageContrib);
            }

            // Scatter peak positions within tagged cells
            var peaks = new List<SeamountPeakData>();
            var rng = new Random(config.Seed + 700);

            for (int c = 0; c < cellCount; c++)
            {
                if (density[c] <= 0f)
                    continue;

                // Number of peaks scales with density.
                // At full density: SeamountPeaksPerCell peaks.
                // Fractional part is probabilistic.
                float expected = density[c] * config.SeamountPeaksPerCell;
                int numPeaks = (int)expected;
                if ((float)rng.NextDouble() < expected - numPeaks)
                    numPeaks++;

                for (int p = 0; p < numPeaks; p++)
                {
                    // Jitter position within the cell by offsetting toward a random neighbor
                    Vec3 center = mesh.CellCenters[c];
                    int[] neighbors = mesh.CellNeighbors[c];
                    int ni = rng.Next(neighbors.Length);
                    Vec3 toNeighbor = mesh.CellCenters[neighbors[ni]] - center;
                    float jitter = (float)rng.NextDouble() * 0.4f;
                    Vec3 pos = (center + toNeighbor * jitter).Normalized * config.Radius;

                    // Height varies: hotspot seamounts are taller, abyssal hills shorter
                    float baseHeight = density[c] * config.SeamountMaxElevation;
                    float height = baseHeight * (0.4f + 0.6f * (float)rng.NextDouble());

                    peaks.Add(new SeamountPeakData
                    {
                        Cell = c,
                        Position = pos,
                        Height = height,
                    });
                }
            }

            tectonics.CellSeamountDensity = density;
            tectonics.Seamounts = peaks.ToArray();

            int taggedCells = 0;
            for (int c = 0; c < cellCount; c++)
                if (density[c] > 0f) taggedCells++;

            Console.WriteLine($"  Seamounts: {peaks.Count} peaks across {taggedCells} oceanic cells");
        }
    }
}
