using System;
using System.Collections.Generic;

namespace WorldGen.Core
{
    /// <summary>
    /// Picks an ocean site on the coarse tectonic mesh for flat map generation.
    /// Analyzes elevation, coast proximity, latitude, and plate boundaries
    /// to choose an interesting location and recommend a terrain archetype.
    /// </summary>
    public static class SiteSelector
    {
        public static SiteContext Select(WorldGenResult result, WorldGenConfig config, Random rng)
        {
            var mesh = result.Mesh;
            var tectonics = result.Tectonics;
            int cellCount = mesh.CellCount;
            float radius = mesh.Radius;

            // 1. Classify cells: ocean vs land by elevation
            bool[] isOcean = new bool[cellCount];
            for (int i = 0; i < cellCount; i++)
                isOcean[i] = tectonics.CellElevation[i] < 0.5f;

            // 2+3. BFS coast distance through ocean cells
            int[] coastDist = new int[cellCount];
            int[] coastSource = new int[cellCount]; // nearest land cell (for direction)
            for (int i = 0; i < cellCount; i++)
            {
                coastDist[i] = int.MaxValue;
                coastSource[i] = -1;
            }

            var queue = new Queue<int>();

            // Seed: ocean cells adjacent to at least one land cell (distance 0)
            for (int i = 0; i < cellCount; i++)
            {
                if (!isOcean[i]) continue;
                foreach (int nb in mesh.CellNeighbors[i])
                {
                    if (!isOcean[nb])
                    {
                        coastDist[i] = 0;
                        coastSource[i] = nb;
                        queue.Enqueue(i);
                        break;
                    }
                }
            }

            // BFS flood through ocean
            while (queue.Count > 0)
            {
                int cell = queue.Dequeue();
                int nextDist = coastDist[cell] + 1;
                foreach (int nb in mesh.CellNeighbors[cell])
                {
                    if (isOcean[nb] && nextDist < coastDist[nb])
                    {
                        coastDist[nb] = nextDist;
                        coastSource[nb] = coastSource[cell];
                        queue.Enqueue(nb);
                    }
                }
            }

            // 4. Compute lat/lng for all cells
            float[] lat = new float[cellCount];
            float[] lng = new float[cellCount];
            for (int i = 0; i < cellCount; i++)
            {
                Vec3 p = mesh.CellCenters[i];
                float r = p.Magnitude;
                if (r < 1e-9f) continue;
                lat[i] = (float)(Math.Asin(Math.Max(-1.0, Math.Min(1.0, p.Y / r))) * 180.0 / Math.PI);
                lng[i] = (float)(Math.Atan2(p.Z, p.X) * 180.0 / Math.PI);
            }

            // 5. Filter candidates by latitude band and coast distance
            var candidates = new List<int>();
            for (int i = 0; i < cellCount; i++)
            {
                if (!isOcean[i]) continue;
                if (coastDist[i] < config.SiteCoastDistMin || coastDist[i] > config.SiteCoastDistMax)
                    continue;
                float absLat = Math.Abs(lat[i]);
                if (absLat < config.SiteLatitudeMin || absLat > config.SiteLatitudeMax)
                    continue;
                candidates.Add(i);
            }

            // Fallback: relax filters if no candidates found
            if (candidates.Count == 0)
            {
                for (int i = 0; i < cellCount; i++)
                {
                    if (isOcean[i] && coastDist[i] >= 0 && coastDist[i] != int.MaxValue)
                        candidates.Add(i);
                }
            }

            if (candidates.Count == 0)
                return null;

            // 6. BFS boundary distance from plate boundary edges
            int[] boundaryDist = new int[cellCount];
            BoundaryType[] nearestBType = new BoundaryType[cellCount];
            float[] nearestBConv = new float[cellCount];
            for (int i = 0; i < cellCount; i++)
            {
                boundaryDist[i] = int.MaxValue;
                nearestBType[i] = BoundaryType.None;
            }

            // Seed: cells adjacent to boundary edges (distance 0)
            for (int e = 0; e < mesh.EdgeCount; e++)
            {
                if (tectonics.EdgeBoundary[e] == BoundaryType.None) continue;
                var (c0, c1) = mesh.EdgeCells[e];
                foreach (int c in new[] { c0, c1 })
                {
                    if (boundaryDist[c] > 0)
                    {
                        boundaryDist[c] = 0;
                        nearestBType[c] = tectonics.EdgeBoundary[e];
                        nearestBConv[c] = tectonics.EdgeConvergence[e];
                        queue.Enqueue(c);
                    }
                }
            }

            while (queue.Count > 0)
            {
                int cell = queue.Dequeue();
                int nextDist = boundaryDist[cell] + 1;
                foreach (int nb in mesh.CellNeighbors[cell])
                {
                    if (nextDist < boundaryDist[nb])
                    {
                        boundaryDist[nb] = nextDist;
                        nearestBType[nb] = nearestBType[cell];
                        nearestBConv[nb] = nearestBConv[cell];
                        queue.Enqueue(nb);
                    }
                }
            }

            // 7. Score candidates
            float latCenter = (config.SiteLatitudeMin + config.SiteLatitudeMax) / 2f;
            float latRange = (config.SiteLatitudeMax - config.SiteLatitudeMin) / 2f;
            if (latRange < 1f) latRange = 1f;

            float[] scores = new float[candidates.Count];
            for (int ci = 0; ci < candidates.Count; ci++)
            {
                int cell = candidates[ci];
                float score = 0f;

                // Boundary proximity — convergent most interesting
                if (boundaryDist[cell] != int.MaxValue && boundaryDist[cell] > 0)
                {
                    float proxWeight = nearestBType[cell] switch
                    {
                        BoundaryType.Convergent => 10f,
                        BoundaryType.Transform => 5f,
                        BoundaryType.Divergent => 3f,
                        _ => 0f,
                    };
                    score += proxWeight / boundaryDist[cell];
                }
                else if (boundaryDist[cell] == 0)
                {
                    // Right on a boundary
                    float proxWeight = nearestBType[cell] switch
                    {
                        BoundaryType.Convergent => 10f,
                        BoundaryType.Transform => 5f,
                        BoundaryType.Divergent => 3f,
                        _ => 0f,
                    };
                    score += proxWeight;
                }

                // Convergence magnitude bonus
                score += Math.Abs(nearestBConv[cell]) * 2f;

                // Multi-plate junction bonus (2-hop neighborhood)
                var nearbyPlates = new HashSet<int>();
                nearbyPlates.Add(tectonics.CellPlate[cell]);
                foreach (int nb in mesh.CellNeighbors[cell])
                {
                    nearbyPlates.Add(tectonics.CellPlate[nb]);
                    foreach (int nb2 in mesh.CellNeighbors[nb])
                        nearbyPlates.Add(tectonics.CellPlate[nb2]);
                }
                if (nearbyPlates.Count >= 3)
                    score += (nearbyPlates.Count - 2) * 3f;

                // Latitude centrality tiebreak
                float latDist = Math.Abs(Math.Abs(lat[cell]) - latCenter);
                score += (1f - latDist / latRange) * 0.5f;

                scores[ci] = score;
            }

            // 8. Select — weighted random from top N for seed-based variety
            int topN = Math.Min(5, candidates.Count);
            int[] sortedIdx = new int[candidates.Count];
            for (int i = 0; i < candidates.Count; i++) sortedIdx[i] = i;
            Array.Sort(sortedIdx, (a, b) => scores[b].CompareTo(scores[a]));

            float totalWeight = 0f;
            for (int i = 0; i < topN; i++)
                totalWeight += scores[sortedIdx[i]];

            int selected;
            if (totalWeight <= 0f)
            {
                selected = candidates[sortedIdx[0]];
            }
            else
            {
                float roll = (float)(rng.NextDouble() * totalWeight);
                float accum = 0f;
                selected = candidates[sortedIdx[topN - 1]]; // default to last in top N
                for (int i = 0; i < topN; i++)
                {
                    accum += scores[sortedIdx[i]];
                    if (roll <= accum)
                    {
                        selected = candidates[sortedIdx[i]];
                        break;
                    }
                }
            }

            // Classify site type from tectonic context
            SiteType siteType = ClassifySiteType(selected, boundaryDist, nearestBType, mesh, tectonics);

            // Coast direction: unit vector from site toward nearest land
            Vec3 coastDir = new Vec3(0, 0, 0);
            if (coastSource[selected] >= 0)
                coastDir = (mesh.CellCenters[coastSource[selected]] - mesh.CellCenters[selected]).Normalized;

            // --- Climate hints ---

            // Wind direction from Earth circulation bands
            float siteLat = lat[selected];
            float siteAbsLat = Math.Abs(siteLat);
            float compassDeg;
            if (siteAbsLat < 30f)
                compassDeg = siteLat >= 0f ? 225f : 315f; // Trade winds
            else if (siteAbsLat < 60f)
                compassDeg = siteLat >= 0f ? 45f : 135f;  // Westerlies
            else
                compassDeg = siteLat >= 0f ? 225f : 315f; // Polar easterlies
            float windRad = compassDeg * (float)Math.PI / 180f;
            float windE = (float)Math.Sin(windRad);
            float windN = (float)Math.Cos(windRad);

            // Ocean current anomaly: plate drift projected onto local north
            Vec3 sitePos = mesh.CellCenters[selected];
            // Local north tangent vector at site
            float latR = siteLat * (float)Math.PI / 180f;
            float lngR = lng[selected] * (float)Math.PI / 180f;
            float sinLatR = (float)Math.Sin(latR);
            float cosLatR = (float)Math.Cos(latR);
            float sinLngR = (float)Math.Sin(lngR);
            float cosLngR = (float)Math.Cos(lngR);
            Vec3 localNorth = new Vec3(-sinLatR * cosLngR, cosLatR, -sinLatR * sinLngR);
            Vec3 localEast = new Vec3(-sinLngR, 0f, cosLngR);

            int plateIdx = tectonics.CellPlate[selected];
            Vec3 drift = tectonics.PlateDrift[plateIdx];
            float northComponent = drift.X * localNorth.X + drift.Y * localNorth.Y + drift.Z * localNorth.Z;
            // Poleward drift → warm current (brings equatorial water)
            float polewardDrift = siteLat >= 0f ? northComponent : -northComponent;
            float latFactor = Math.Max(0.1f, 1f - Math.Abs(siteAbsLat - 40f) / 50f);
            float oceanAnomaly = polewardDrift * latFactor * 8f;
            oceanAnomaly = Math.Max(-8f, Math.Min(8f, oceanAnomaly));

            // Moisture bias: walk ~8 hops upwind, count land/ocean fraction
            Vec3 windDir3D = new Vec3(
                localEast.X * windE + localNorth.X * windN,
                localEast.Y * windE + localNorth.Y * windN,
                localEast.Z * windE + localNorth.Z * windN);
            Vec3 upwindDir = new Vec3(-windDir3D.X, -windDir3D.Y, -windDir3D.Z);
            int walkCell = selected;
            int landCount = 0;
            int totalSampled = 0;
            for (int hop = 0; hop < 8; hop++)
            {
                int bestNb = -1;
                float bestAlign = -2f;
                foreach (int nb in mesh.CellNeighbors[walkCell])
                {
                    Vec3 toNb = new Vec3(
                        mesh.CellCenters[nb].X - mesh.CellCenters[walkCell].X,
                        mesh.CellCenters[nb].Y - mesh.CellCenters[walkCell].Y,
                        mesh.CellCenters[nb].Z - mesh.CellCenters[walkCell].Z);
                    float mag = toNb.Magnitude;
                    if (mag < 1e-9f) continue;
                    float align = (toNb.X * upwindDir.X + toNb.Y * upwindDir.Y + toNb.Z * upwindDir.Z) / mag;
                    if (align > bestAlign)
                    {
                        bestAlign = align;
                        bestNb = nb;
                    }
                }
                if (bestNb < 0) break;
                walkCell = bestNb;
                totalSampled++;
                if (!isOcean[walkCell]) landCount++;
            }
            float landFraction = totalSampled > 0 ? (float)landCount / totalSampled : 0f;
            float moistureBias = 1f - 2f * landFraction;

            return new SiteContext
            {
                CellIndex = selected,
                Latitude = lat[selected],
                Longitude = lng[selected],
                CoastDistanceHops = coastDist[selected],
                CoastDirection = coastDir,
                NearestBoundary = nearestBType[selected],
                BoundaryConvergence = nearestBConv[selected],
                BoundaryDistanceHops = boundaryDist[selected],
                SiteType = siteType,
                OceanCurrentAnomaly = oceanAnomaly,
                MoistureBias = moistureBias,
                WindDirectionEast = windE,
                WindDirectionNorth = windN,
            };
        }

        private static SiteType ClassifySiteType(
            int cell, int[] boundaryDist, BoundaryType[] nearestBType,
            SphereMesh mesh, TectonicData tectonics)
        {
            int bDist = boundaryDist[cell];
            BoundaryType bType = nearestBType[cell];

            // Count distinct boundary types in 1-hop neighborhood
            var nearbyBoundaryTypes = new HashSet<BoundaryType>();
            foreach (int e in mesh.CellEdges[cell])
            {
                if (tectonics.EdgeBoundary[e] != BoundaryType.None)
                    nearbyBoundaryTypes.Add(tectonics.EdgeBoundary[e]);
            }
            foreach (int nb in mesh.CellNeighbors[cell])
            {
                foreach (int e in mesh.CellEdges[nb])
                {
                    if (tectonics.EdgeBoundary[e] != BoundaryType.None)
                        nearbyBoundaryTypes.Add(tectonics.EdgeBoundary[e]);
                }
            }

            // Count distinct plates in 2-hop neighborhood
            var nearbyPlates = new HashSet<int>();
            nearbyPlates.Add(tectonics.CellPlate[cell]);
            foreach (int nb in mesh.CellNeighbors[cell])
            {
                nearbyPlates.Add(tectonics.CellPlate[nb]);
                foreach (int nb2 in mesh.CellNeighbors[nb])
                    nearbyPlates.Add(tectonics.CellPlate[nb2]);
            }

            // Multiple boundary types or triple junction → Archipelago
            if (nearbyBoundaryTypes.Count >= 2 || nearbyPlates.Count >= 4)
                return SiteType.Archipelago;

            // Strong convergent boundary within 2 hops → Volcanic
            if (bType == BoundaryType.Convergent && bDist <= 2)
                return SiteType.Volcanic;

            // Moderate convergent boundary (3-4 hops) → HighIsland
            if (bType == BoundaryType.Convergent && bDist <= 4)
                return SiteType.HighIsland;

            // Everything else → LowIsland
            return SiteType.LowIsland;
        }
    }
}
