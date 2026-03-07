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
        /// <summary>
        /// Returns ranked list of candidate sites (best first), up to maxSites.
        /// </summary>
        public static List<SiteContext> SelectMultiple(WorldGenResult result, WorldGenConfig config, int maxSites = 10)
        {
            var ctx = BuildAnalysis(result, config);
            if (ctx.SortedIndices == null || ctx.SortedIndices.Length == 0)
                return new List<SiteContext>();

            int count = Math.Min(maxSites, ctx.SortedIndices.Length);
            var sites = new List<SiteContext>(count);
            for (int i = 0; i < count; i++)
            {
                int cell = ctx.Candidates[ctx.SortedIndices[i]];
                sites.Add(BuildSiteContext(cell, result, ctx));
            }
            return sites;
        }

        public static SiteContext Select(WorldGenResult result, WorldGenConfig config, Random rng)
        {
            var ctx = BuildAnalysis(result, config);
            if (ctx.SortedIndices == null || ctx.SortedIndices.Length == 0)
                return null;

            // Weighted random from top N for seed-based variety
            int topN = Math.Min(5, ctx.SortedIndices.Length);
            float totalWeight = 0f;
            for (int i = 0; i < topN; i++)
                totalWeight += ctx.Scores[ctx.SortedIndices[i]];

            int selected;
            if (totalWeight <= 0f)
            {
                selected = ctx.Candidates[ctx.SortedIndices[0]];
            }
            else
            {
                float roll = (float)(rng.NextDouble() * totalWeight);
                float accum = 0f;
                selected = ctx.Candidates[ctx.SortedIndices[topN - 1]];
                for (int i = 0; i < topN; i++)
                {
                    accum += ctx.Scores[ctx.SortedIndices[i]];
                    if (roll <= accum)
                    {
                        selected = ctx.Candidates[ctx.SortedIndices[i]];
                        break;
                    }
                }
            }

            return BuildSiteContext(selected, result, ctx);
        }

        private struct AnalysisContext
        {
            public List<int> Candidates;
            public float[] Scores;
            public int[] SortedIndices;
            public bool[] IsOcean;
            public int[] CoastDist;
            public int[] CoastSource;
            public int[] BoundaryDist;
            public BoundaryType[] NearestBType;
            public float[] NearestBConv;
            public float[] Lat;
            public float[] Lng;
        }

        private static AnalysisContext BuildAnalysis(WorldGenResult result, WorldGenConfig config)
        {
            var mesh = result.Mesh;
            var tectonics = result.Tectonics;
            int cellCount = mesh.CellCount;

            // 1. Classify cells: ocean vs land by elevation
            bool[] isOcean = new bool[cellCount];
            for (int i = 0; i < cellCount; i++)
                isOcean[i] = tectonics.CellElevation[i] < 0.5f;

            // 2+3. BFS coast distance through ocean cells
            int[] coastDist = new int[cellCount];
            int[] coastSource = new int[cellCount];
            for (int i = 0; i < cellCount; i++)
            {
                coastDist[i] = int.MaxValue;
                coastSource[i] = -1;
            }

            var queue = new Queue<int>();

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

            if (candidates.Count == 0)
            {
                for (int i = 0; i < cellCount; i++)
                {
                    if (isOcean[i] && coastDist[i] >= 0 && coastDist[i] != int.MaxValue)
                        candidates.Add(i);
                }
            }

            if (candidates.Count == 0)
            {
                return new AnalysisContext
                {
                    Candidates = candidates,
                    SortedIndices = Array.Empty<int>(),
                };
            }

            // 6. BFS boundary distance from plate boundary edges
            int[] boundaryDist = new int[cellCount];
            BoundaryType[] nearestBType = new BoundaryType[cellCount];
            float[] nearestBConv = new float[cellCount];
            for (int i = 0; i < cellCount; i++)
            {
                boundaryDist[i] = int.MaxValue;
                nearestBType[i] = BoundaryType.None;
            }

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
                    float proxWeight = nearestBType[cell] switch
                    {
                        BoundaryType.Convergent => 10f,
                        BoundaryType.Transform => 5f,
                        BoundaryType.Divergent => 3f,
                        _ => 0f,
                    };
                    score += proxWeight;
                }

                score += Math.Abs(nearestBConv[cell]) * 2f;

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

                float latDist = Math.Abs(Math.Abs(lat[cell]) - latCenter);
                score += (1f - latDist / latRange) * 0.5f;

                scores[ci] = score;
            }

            // Sort by score descending
            int[] sortedIdx = new int[candidates.Count];
            for (int i = 0; i < candidates.Count; i++) sortedIdx[i] = i;
            Array.Sort(sortedIdx, (a, b) => scores[b].CompareTo(scores[a]));

            return new AnalysisContext
            {
                Candidates = candidates,
                Scores = scores,
                SortedIndices = sortedIdx,
                IsOcean = isOcean,
                CoastDist = coastDist,
                CoastSource = coastSource,
                BoundaryDist = boundaryDist,
                NearestBType = nearestBType,
                NearestBConv = nearestBConv,
                Lat = lat,
                Lng = lng,
            };
        }

        private static SiteContext BuildSiteContext(int selected, WorldGenResult result, AnalysisContext ctx)
        {
            var mesh = result.Mesh;
            var tectonics = result.Tectonics;

            SiteType siteType = ClassifySiteType(selected, ctx.BoundaryDist, ctx.NearestBType, mesh, tectonics);

            Vec3 coastDir = new Vec3(0, 0, 0);
            if (ctx.CoastSource[selected] >= 0)
                coastDir = (mesh.CellCenters[ctx.CoastSource[selected]] - mesh.CellCenters[selected]).Normalized;

            // Wind direction from Earth circulation bands
            float siteLat = ctx.Lat[selected];
            float siteAbsLat = Math.Abs(siteLat);
            float compassDeg;
            if (siteAbsLat < 30f)
                compassDeg = siteLat >= 0f ? 225f : 315f;
            else if (siteAbsLat < 60f)
                compassDeg = siteLat >= 0f ? 45f : 135f;
            else
                compassDeg = siteLat >= 0f ? 225f : 315f;
            float windRad = compassDeg * (float)Math.PI / 180f;
            float windE = (float)Math.Sin(windRad);
            float windN = (float)Math.Cos(windRad);

            // Ocean current anomaly
            float latR = siteLat * (float)Math.PI / 180f;
            float lngR = ctx.Lng[selected] * (float)Math.PI / 180f;
            float sinLatR = (float)Math.Sin(latR);
            float cosLatR = (float)Math.Cos(latR);
            float sinLngR = (float)Math.Sin(lngR);
            float cosLngR = (float)Math.Cos(lngR);
            Vec3 localNorth = new Vec3(-sinLatR * cosLngR, cosLatR, -sinLatR * sinLngR);
            Vec3 localEast = new Vec3(-sinLngR, 0f, cosLngR);

            int plateIdx = tectonics.CellPlate[selected];
            Vec3 drift = tectonics.PlateDrift[plateIdx];
            float northComponent = drift.X * localNorth.X + drift.Y * localNorth.Y + drift.Z * localNorth.Z;
            float polewardDrift = siteLat >= 0f ? northComponent : -northComponent;
            float latFactor = Math.Max(0.1f, 1f - Math.Abs(siteAbsLat - 40f) / 50f);
            float oceanAnomaly = polewardDrift * latFactor * 8f;
            oceanAnomaly = Math.Max(-8f, Math.Min(8f, oceanAnomaly));

            // Moisture bias: walk ~8 hops upwind
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
                if (!ctx.IsOcean[walkCell]) landCount++;
            }
            float landFraction = totalSampled > 0 ? (float)landCount / totalSampled : 0f;
            float moistureBias = 1f - 2f * landFraction;

            return new SiteContext
            {
                CellIndex = selected,
                Latitude = ctx.Lat[selected],
                Longitude = ctx.Lng[selected],
                CoastDistanceHops = ctx.CoastDist[selected],
                CoastDirection = coastDir,
                NearestBoundary = ctx.NearestBType[selected],
                BoundaryConvergence = ctx.NearestBConv[selected],
                BoundaryDistanceHops = ctx.BoundaryDist[selected],
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

            var nearbyPlates = new HashSet<int>();
            nearbyPlates.Add(tectonics.CellPlate[cell]);
            foreach (int nb in mesh.CellNeighbors[cell])
            {
                nearbyPlates.Add(tectonics.CellPlate[nb]);
                foreach (int nb2 in mesh.CellNeighbors[nb])
                    nearbyPlates.Add(tectonics.CellPlate[nb2]);
            }

            if (nearbyBoundaryTypes.Count >= 2 || nearbyPlates.Count >= 4)
                return SiteType.Archipelago;

            if (bType == BoundaryType.Convergent && bDist <= 2)
                return SiteType.Volcanic;

            if (bType == BoundaryType.Convergent && bDist <= 4)
                return SiteType.HighIsland;

            return SiteType.LowIsland;
        }
    }
}
