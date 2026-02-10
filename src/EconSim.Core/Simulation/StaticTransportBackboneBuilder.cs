using System;
using System.Collections.Generic;
using System.Linq;
using EconSim.Core.Data;
using EconSim.Core.Economy;

namespace EconSim.Core.Simulation
{
    /// <summary>
    /// Summary stats for static transport backbone generation.
    /// </summary>
    public readonly struct StaticBackboneStats
    {
        public readonly int CandidateCountyCount;
        public readonly int MajorCountyCount;
        public readonly int RoutePairCount;
        public readonly int RoutedPairCount;
        public readonly int MissingPairCount;
        public readonly int EdgeCount;
        public readonly float PathThreshold;
        public readonly float RoadThreshold;

        public StaticBackboneStats(
            int candidateCountyCount,
            int majorCountyCount,
            int routePairCount,
            int routedPairCount,
            int missingPairCount,
            int edgeCount,
            float pathThreshold,
            float roadThreshold)
        {
            CandidateCountyCount = candidateCountyCount;
            MajorCountyCount = majorCountyCount;
            RoutePairCount = routePairCount;
            RoutedPairCount = routedPairCount;
            MissingPairCount = missingPairCount;
            EdgeCount = edgeCount;
            PathThreshold = pathThreshold;
            RoadThreshold = roadThreshold;
        }
    }

    /// <summary>
    /// Builds a static, shared-route transport backbone from major counties.
    /// No runtime road evolution is performed after this pass.
    /// </summary>
    public static class StaticTransportBackboneBuilder
    {
        public static StaticBackboneStats Build(SimulationState state, MapData mapData)
        {
            var economy = state?.Economy;
            var transport = state?.Transport;
            var roads = economy?.Roads;
            if (economy == null || transport == null || roads == null || mapData?.Counties == null || mapData.CountyById == null)
            {
                return new StaticBackboneStats(0, 0, 0, 0, 0, 0, 0, 0);
            }

            var majorCountyIds = SelectMajorCounties(mapData);
            if (majorCountyIds.Count < 2)
            {
                roads.ApplyStaticTraffic(new Dictionary<(int, int), float>(), 1f, 2f);
                transport.ClearCache();
                return new StaticBackboneStats(mapData.Counties.Count, majorCountyIds.Count, 0, 0, 0, 0, 1f, 2f);
            }

            var routePairs = BuildRoutePairs(majorCountyIds, mapData);
            var edgeUsage = new Dictionary<(int, int), float>();
            int routedPairs = 0;
            int missingPairs = 0;

            foreach (var (countyAId, countyBId) in routePairs)
            {
                if (!mapData.CountyById.TryGetValue(countyAId, out var countyA) ||
                    !mapData.CountyById.TryGetValue(countyBId, out var countyB))
                {
                    missingPairs++;
                    continue;
                }

                int seatA = countyA.SeatCellId;
                int seatB = countyB.SeatCellId;
                if (seatA <= 0 || seatB <= 0)
                {
                    missingPairs++;
                    continue;
                }

                var path = transport.FindPath(seatA, seatB);
                if (!path.Found || path.Path.Count < 2)
                {
                    missingPairs++;
                    continue;
                }

                float routeWeight = ComputeRouteWeight(countyA, countyB);
                AccumulateLandEdgeUsage(path.Path, routeWeight, mapData, edgeUsage);
                routedPairs++;
            }

            (float pathThreshold, float roadThreshold) = ComputeTierThresholds(edgeUsage.Values);
            roads.ApplyStaticTraffic(edgeUsage, pathThreshold, roadThreshold);
            transport.ClearCache();

            return new StaticBackboneStats(
                mapData.Counties.Count,
                majorCountyIds.Count,
                routePairs.Count,
                routedPairs,
                missingPairs,
                edgeUsage.Count,
                pathThreshold,
                roadThreshold);
        }

        private static List<int> SelectMajorCounties(MapData mapData)
        {
            var valid = new List<County>();
            foreach (var county in mapData.Counties)
            {
                if (county.SeatCellId <= 0) continue;
                if (!mapData.CellById.TryGetValue(county.SeatCellId, out var seatCell)) continue;
                if (!seatCell.IsLand) continue;
                valid.Add(county);
            }

            valid.Sort((a, b) => b.TotalPopulation.CompareTo(a.TotalPopulation));
            if (valid.Count == 0)
                return new List<int>();

            int byPercent = (int)Math.Ceiling(valid.Count * SimulationConfig.Roads.MajorCountyTopPercent);
            int target = Math.Max(SimulationConfig.Roads.MajorCountyMinCount, byPercent);
            target = Math.Min(target, SimulationConfig.Roads.MajorCountyMaxCount);
            target = Math.Min(target, valid.Count);

            var selected = new HashSet<int>();
            for (int i = 0; i < target; i++)
            {
                selected.Add(valid[i].Id);
            }

            // Always include capital counties when discoverable from seat burg.
            var burgById = new Dictionary<int, Burg>();
            if (mapData.Burgs != null)
            {
                foreach (var burg in mapData.Burgs)
                {
                    burgById[burg.Id] = burg;
                }
            }

            foreach (var county in valid)
            {
                if (!mapData.CellById.TryGetValue(county.SeatCellId, out var seatCell) || seatCell.BurgId <= 0)
                    continue;

                if (burgById.TryGetValue(seatCell.BurgId, out var burg) && burg.IsCapital)
                {
                    selected.Add(county.Id);
                }
            }

            return selected.ToList();
        }

        private static List<(int countyAId, int countyBId)> BuildRoutePairs(List<int> majorCountyIds, MapData mapData)
        {
            var pairs = new HashSet<(int, int)>();

            foreach (int sourceId in majorCountyIds)
            {
                if (!mapData.CountyById.TryGetValue(sourceId, out var sourceCounty))
                    continue;

                var ranked = new List<(int countyId, float distSq)>();
                foreach (int targetId in majorCountyIds)
                {
                    if (targetId == sourceId) continue;
                    if (!mapData.CountyById.TryGetValue(targetId, out var targetCounty))
                        continue;

                    float dx = sourceCounty.Centroid.X - targetCounty.Centroid.X;
                    float dy = sourceCounty.Centroid.Y - targetCounty.Centroid.Y;
                    ranked.Add((targetId, dx * dx + dy * dy));
                }

                ranked.Sort((a, b) => a.distSq.CompareTo(b.distSq));
                int take = Math.Min(SimulationConfig.Roads.ConnectionsPerMajorCounty, ranked.Count);
                for (int i = 0; i < take; i++)
                {
                    int targetId = ranked[i].countyId;
                    pairs.Add(NormalizePair(sourceId, targetId));
                }
            }

            return pairs.ToList();
        }

        private static float ComputeRouteWeight(County countyA, County countyB)
        {
            float popA = Math.Max(1f, countyA.TotalPopulation);
            float popB = Math.Max(1f, countyB.TotalPopulation);
            float geometricMean = (float)Math.Sqrt(popA * popB);
            return Math.Max(SimulationConfig.Roads.MinRouteWeight, geometricMean / SimulationConfig.Roads.RoutePopulationScale);
        }

        private static void AccumulateLandEdgeUsage(
            List<int> path,
            float weight,
            MapData mapData,
            Dictionary<(int, int), float> edgeUsage)
        {
            for (int i = 0; i < path.Count - 1; i++)
            {
                int cellA = path[i];
                int cellB = path[i + 1];
                if (!mapData.CellById.TryGetValue(cellA, out var a) ||
                    !mapData.CellById.TryGetValue(cellB, out var b) ||
                    !a.IsLand || !b.IsLand)
                {
                    continue;
                }

                var key = RoadState.NormalizeKey(cellA, cellB);
                if (!edgeUsage.TryGetValue(key, out var current))
                    current = 0f;
                edgeUsage[key] = current + weight;
            }
        }

        private static (float pathThreshold, float roadThreshold) ComputeTierThresholds(IEnumerable<float> values)
        {
            var ordered = values.Where(v => v > 0).OrderBy(v => v).ToList();
            if (ordered.Count == 0)
                return (1f, 2f);

            float pathThreshold = Percentile(ordered, SimulationConfig.Roads.PathTierPercentile);
            float roadThreshold = Percentile(ordered, SimulationConfig.Roads.RoadTierPercentile);

            pathThreshold = Math.Max(0.01f, pathThreshold);
            roadThreshold = Math.Max(pathThreshold + 0.01f, roadThreshold);
            return (pathThreshold, roadThreshold);
        }

        private static float Percentile(List<float> sorted, float percentile)
        {
            if (sorted.Count == 0) return 0f;
            float p = Math.Max(0f, Math.Min(1f, percentile));
            float rank = p * (sorted.Count - 1);
            int lo = (int)Math.Floor(rank);
            int hi = (int)Math.Ceiling(rank);
            if (lo == hi) return sorted[lo];
            float t = rank - lo;
            return sorted[lo] * (1f - t) + sorted[hi] * t;
        }

        private static (int, int) NormalizePair(int a, int b)
        {
            return a < b ? (a, b) : (b, a);
        }
    }
}
