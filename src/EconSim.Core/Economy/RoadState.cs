using System;
using System.Collections.Generic;

namespace EconSim.Core.Economy
{
    /// <summary>
    /// Road tier levels.
    /// </summary>
    public enum RoadTier
    {
        None = 0,
        Path = 1,   // Light traffic - minor cost reduction
        Road = 2    // Heavy traffic - significant cost reduction
    }

    /// <summary>
    /// Tracks static transport-path usage on cell edges and determines path tiers.
    /// Built at initialization from major-county route overlap; not evolved during runtime ticks.
    /// </summary>
    [Serializable]
    public class RoadState
    {
        /// <summary>
        /// Committed traffic volume on each edge (used for road tier evaluation).
        /// Key is normalized (smaller cell ID first).
        /// </summary>
        public Dictionary<(int, int), float> EdgeTraffic;

        /// <summary>
        /// Increments when static network data is replaced.
        /// </summary>
        public int Revision { get; private set; }

        /// <summary>
        /// Traffic threshold to become a path.
        /// </summary>
        public float PathThreshold = 500f;

        /// <summary>
        /// Traffic threshold to become a road.
        /// </summary>
        public float RoadThreshold = 2000f;

        /// <summary>
        /// Cost multiplier for paths.
        /// </summary>
        public const float PathCostMultiplier = 0.7f;

        /// <summary>
        /// Cost multiplier for roads.
        /// </summary>
        public const float RoadCostMultiplier = 0.5f;

        public RoadState()
        {
            EdgeTraffic = new Dictionary<(int, int), float>();
            Revision = 0;
        }

        /// <summary>
        /// Normalize edge key so smaller cell ID is always first.
        /// </summary>
        public static (int, int) NormalizeKey(int cellA, int cellB)
        {
            return cellA < cellB ? (cellA, cellB) : (cellB, cellA);
        }

        /// <summary>
        /// Get the road tier for an edge.
        /// </summary>
        public RoadTier GetRoadTier(int cellA, int cellB)
        {
            var key = NormalizeKey(cellA, cellB);
            if (!EdgeTraffic.TryGetValue(key, out var traffic))
                return RoadTier.None;

            if (traffic >= RoadThreshold)
                return RoadTier.Road;
            if (traffic >= PathThreshold)
                return RoadTier.Path;

            return RoadTier.None;
        }

        /// <summary>
        /// Get the cost multiplier for an edge based on road status.
        /// Returns 1.0 if no road.
        /// </summary>
        public float GetCostMultiplier(int cellA, int cellB)
        {
            var tier = GetRoadTier(cellA, cellB);
            return tier switch
            {
                RoadTier.Road => RoadCostMultiplier,
                RoadTier.Path => PathCostMultiplier,
                _ => 1f
            };
        }

        /// <summary>
        /// Get accumulated traffic on an edge.
        /// </summary>
        public float GetTraffic(int cellA, int cellB)
        {
            var key = NormalizeKey(cellA, cellB);
            return EdgeTraffic.TryGetValue(key, out var traffic) ? traffic : 0;
        }

        /// <summary>
        /// Get all edges that have at least path-level traffic.
        /// Returns list of (cellA, cellB, tier) tuples.
        /// </summary>
        public List<(int, int, RoadTier)> GetAllRoads()
        {
            var roads = new List<(int, int, RoadTier)>();
            foreach (var kvp in EdgeTraffic)
            {
                RoadTier tier;
                if (kvp.Value >= RoadThreshold)
                    tier = RoadTier.Road;
                else if (kvp.Value >= PathThreshold)
                    tier = RoadTier.Path;
                else
                    continue;

                roads.Add((kvp.Key.Item1, kvp.Key.Item2, tier));
            }
            return roads;
        }

        /// <summary>
        /// Apply a fully built static network in one shot.
        /// Replaces previous edge traffic and tier thresholds.
        /// </summary>
        public void ApplyStaticTraffic(
            Dictionary<(int, int), float> edgeTraffic,
            float pathThreshold,
            float roadThreshold)
        {
            EdgeTraffic.Clear();

            if (edgeTraffic != null)
            {
                foreach (var kvp in edgeTraffic)
                {
                    if (kvp.Value > 0f)
                        EdgeTraffic[kvp.Key] = kvp.Value;
                }
            }

            PathThreshold = Math.Max(0.01f, pathThreshold);
            RoadThreshold = Math.Max(PathThreshold + 0.01f, roadThreshold);
            Revision++;
        }
    }
}
