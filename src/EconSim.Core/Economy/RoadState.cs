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
    /// Tracks accumulated traffic on cell edges and determines road status.
    /// Roads emerge from trade patterns - high traffic edges become paths, then roads.
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
        /// Pending traffic accumulated between road development ticks.
        /// </summary>
        public Dictionary<(int, int), float> PendingEdgeTraffic;

        /// <summary>
        /// Increments whenever one or more edges cross a road-tier boundary
        /// during a development tick.
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
            PendingEdgeTraffic = new Dictionary<(int, int), float>();
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
        /// Record traffic on an edge.
        /// </summary>
        public void RecordTraffic(int fromCell, int toCell, float volume)
        {
            if (volume <= 0) return;

            var key = NormalizeKey(fromCell, toCell);
            if (!PendingEdgeTraffic.TryGetValue(key, out var current))
                current = 0;

            PendingEdgeTraffic[key] = current + volume;
        }

        /// <summary>
        /// Record traffic along a path (list of cell IDs).
        /// </summary>
        public void RecordTrafficAlongPath(List<int> path, float volume)
        {
            if (path == null || path.Count < 2 || volume <= 0)
                return;

            for (int i = 0; i < path.Count - 1; i++)
            {
                RecordTraffic(path[i], path[i + 1], volume);
            }
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
        /// Commit pending traffic into the road network.
        /// Returns number of edges that changed tier in this commit.
        /// </summary>
        public int CommitPendingTraffic()
        {
            if (PendingEdgeTraffic.Count == 0)
                return 0;

            int changedEdges = 0;

            foreach (var kvp in PendingEdgeTraffic)
            {
                var key = kvp.Key;
                float pending = kvp.Value;

                if (!EdgeTraffic.TryGetValue(key, out var committed))
                    committed = 0f;

                var oldTier = GetTierForTraffic(committed);
                float updated = committed + pending;
                var newTier = GetTierForTraffic(updated);

                EdgeTraffic[key] = updated;

                if (newTier != oldTier)
                    changedEdges++;
            }

            PendingEdgeTraffic.Clear();

            if (changedEdges > 0)
                Revision++;

            return changedEdges;
        }

        private RoadTier GetTierForTraffic(float traffic)
        {
            if (traffic >= RoadThreshold)
                return RoadTier.Road;
            if (traffic >= PathThreshold)
                return RoadTier.Path;
            return RoadTier.None;
        }
    }
}
