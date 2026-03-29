using System;
using System.Collections.Generic;

namespace WorldGen.Core
{
    /// <summary>
    /// Generates volcanic hotspots — fixed mantle plumes with trails projected
    /// opposite to plate drift. Applies elevation bumps to coarse cells and
    /// produces per-cell intensity data for downstream rendering.
    /// </summary>
    public static class HotspotOps
    {
        /// <summary>
        /// Place hotspots, trace trails, and apply elevation bumps.
        /// Call after ElevationOps so we can add bumps on top of tectonic elevation.
        /// </summary>
        public static void Generate(SphereMesh mesh, TectonicData tectonics, WorldGenConfig config)
        {
            int cellCount = mesh.CellCount;
            float[] intensity = new float[cellCount];
            var rng = new Random(config.Seed + 500);

            var hotspots = new List<HotspotData>();

            for (int h = 0; h < config.HotspotCount; h++)
            {
                // Place hotspot at random position on sphere
                Vec3 pos = RandomPointOnSphere(rng, config.Radius);

                // Find nearest cell
                int sourceCell = FindNearestCell(mesh, pos);

                // Determine owning plate and its drift
                int plate = tectonics.CellPlate[sourceCell];
                Vec3 drift = tectonics.PlateDrift[plate];

                // Zero drift (e.g. polar cap plates) — stationary plume, no trail
                if (drift.SqrMagnitude < 1e-8f)
                {
                    intensity[sourceCell] = Math.Max(intensity[sourceCell], 1f);
                    hotspots.Add(new HotspotData
                    {
                        Position = pos,
                        SourceCell = sourceCell,
                        TrailCells = new[] { sourceCell },
                        TrailIntensity = new[] { 1f },
                    });
                    continue;
                }

                // Trail goes opposite to drift (plate moved over the hotspot,
                // so older volcanism is upstream of the drift direction)
                Vec3 trailDir = -drift;

                // Walk the trail cell-by-cell
                var trailCells = new List<int>();
                var trailIntensity = new List<float>();
                var visited = new HashSet<int>();

                int current = sourceCell;
                int trailLength = config.HotspotTrailLength;

                for (int t = 0; t < trailLength; t++)
                {
                    if (visited.Contains(current))
                        break;

                    visited.Add(current);
                    float cellIntensity = 1f - (float)t / trailLength;
                    trailCells.Add(current);
                    trailIntensity.Add(cellIntensity);

                    // Max-wins for overlapping hotspot trails
                    if (cellIntensity > intensity[current])
                        intensity[current] = cellIntensity;

                    // Find neighbor most aligned with trail direction (same plate only)
                    int bestNeighbor = -1;
                    float bestDot = float.MinValue;
                    int[] neighbors = mesh.CellNeighbors[current];
                    for (int i = 0; i < neighbors.Length; i++)
                    {
                        int nb = neighbors[i];
                        if (visited.Contains(nb))
                            continue;
                        if (tectonics.CellPlate[nb] != plate)
                            continue;
                        Vec3 dir = mesh.CellCenters[nb] - mesh.CellCenters[current];
                        float d = Vec3.Dot(dir, trailDir);
                        if (d > bestDot)
                        {
                            bestDot = d;
                            bestNeighbor = nb;
                        }
                    }

                    if (bestNeighbor == -1)
                        break;

                    current = bestNeighbor;
                }

                hotspots.Add(new HotspotData
                {
                    Position = pos,
                    SourceCell = sourceCell,
                    TrailCells = trailCells.ToArray(),
                    TrailIntensity = trailIntensity.ToArray(),
                });
            }

            // Apply elevation bumps from hotspot intensity
            float bump = config.HotspotElevation;
            for (int c = 0; c < cellCount; c++)
            {
                if (intensity[c] > 0f)
                    tectonics.CellElevation[c] += intensity[c] * bump;
            }

            // Clamp after bumps
            for (int c = 0; c < cellCount; c++)
                tectonics.CellElevation[c] = Math.Max(0f, Math.Min(1f, tectonics.CellElevation[c]));

            tectonics.CellHotspotIntensity = intensity;
            tectonics.Hotspots = hotspots.ToArray();

            // Log hotspot positions for diagnostic purposes
            for (int h = 0; h < hotspots.Count; h++)
            {
                var hs = hotspots[h];
                Vec3 p = hs.Position.Normalized;
                float latDeg = (float)(Math.Asin(p.Y) * 180.0 / Math.PI);
                float lonDeg = (float)(Math.Atan2(p.Z, p.X) * 180.0 / Math.PI);
                bool oceanic = tectonics.PlateIsOceanic != null &&
                    tectonics.PlateIsOceanic[tectonics.CellPlate[hs.SourceCell]];
                Console.WriteLine($"    Hotspot {h}: lat {latDeg:F1} lon {lonDeg:F1}, " +
                    $"{(oceanic ? "oceanic" : "continental")}, trail {hs.TrailCells.Length} cells");
            }
        }

        static Vec3 RandomPointOnSphere(Random rng, float radius)
        {
            // Uniform random point on sphere via Marsaglia's method
            float x, y, s;
            do
            {
                x = (float)(rng.NextDouble() * 2 - 1);
                y = (float)(rng.NextDouble() * 2 - 1);
                s = x * x + y * y;
            } while (s >= 1f);

            float factor = 2f * (float)Math.Sqrt(1f - s);
            return new Vec3(x * factor, y * factor, 1f - 2f * s) * radius;
        }

        static int FindNearestCell(SphereMesh mesh, Vec3 position)
        {
            int best = 0;
            float bestDist = float.MaxValue;
            for (int c = 0; c < mesh.CellCount; c++)
            {
                float d = Vec3.SqrDistance(mesh.CellCenters[c], position);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = c;
                }
            }
            return best;
        }
    }
}
