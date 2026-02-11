using System;
using System.Collections.Generic;
using EconSim.Core.Common;
using EconSim.Core.Data;
using EconSim.Core.Economy;

namespace EconSim.Core.Transport
{
    /// <summary>
    /// Result of a pathfinding query.
    /// </summary>
    public struct PathResult
    {
        /// <summary>
        /// Ordered list of cell IDs from start to end (inclusive).
        /// Empty if no path exists.
        /// </summary>
        public List<int> Path;

        /// <summary>
        /// Total transport cost of the path.
        /// float.MaxValue if no path exists.
        /// </summary>
        public float TotalCost;

        /// <summary>
        /// Whether a valid path was found.
        /// </summary>
        public bool Found => Path != null && Path.Count > 0;

        public static PathResult NotFound => new PathResult
        {
            Path = new List<int>(),
            TotalCost = float.MaxValue
        };
    }

    /// <summary>
    /// Handles transport cost calculations and pathfinding between cells.
    /// Uses Dijkstra's algorithm on the cell adjacency graph.
    /// </summary>
    public class TransportGraph
    {
        private readonly MapData _mapData;
        private readonly Dictionary<int, Biome> _biomeById;

        // Cache for computed paths (from -> to -> result)
        private readonly Dictionary<(int, int), PathResult> _pathCache;
        private readonly int _maxCacheSize;

        // Road state for applying road bonuses (set after economy initialization)
        private RoadState _roadState;

        // Default movement cost if biome not found
        private const float DefaultMovementCost = 1.0f;

        // River crossing bonus (multiplier < 1 means easier)
        private const float RiverCrossingBonus = 0.8f;

        // Sea transport costs
        private const float SeaMovementCost = 0.15f;   // ~7x cheaper than base land
        private const float PortTransitionCost = 3.0f; // Cost to load/unload at port

        // Impassable threshold (cells with cost >= this are blocked)
        private const float ImpassableThreshold = 100f;

        public TransportGraph(MapData mapData, int maxCacheSize = 10000)
        {
            _mapData = mapData;
            _maxCacheSize = maxCacheSize;
            _pathCache = new Dictionary<(int, int), PathResult>();

            // Build biome lookup
            _biomeById = new Dictionary<int, Biome>();
            foreach (var biome in mapData.Biomes)
            {
                _biomeById[biome.Id] = biome;
            }
        }

        /// <summary>
        /// Set the road state for applying road cost bonuses.
        /// Call this after economy initialization.
        /// </summary>
        public void SetRoadState(RoadState roadState)
        {
            _roadState = roadState;
            // Clear cache when road state changes (roads affect costs)
            ClearCache();
        }

        /// <summary>
        /// Get the movement cost for entering a cell.
        /// Based on biome and terrain features.
        /// </summary>
        public float GetCellMovementCost(Cell cell)
        {
            // Ocean cells: cheap to traverse by ship
            if (!cell.IsLand)
                return SeaMovementCost;

            float baseCost = DefaultMovementCost;

            // Use biome movement cost if available
            // Biome costs are on a 10-5000 scale, normalize to 1-10 range by dividing by 50
            if (_biomeById.TryGetValue(cell.BiomeId, out var biome) && biome.MovementCost > 0)
            {
                baseCost = biome.MovementCost / 50f;
                // Clamp to reasonable range
                baseCost = Math.Max(1f, Math.Min(baseCost, 20f));
            }

            // Height modifier: higher = harder (mountains)
            // Height 20 = sea level, 100 = peak
            // Cells above 70 get increasingly difficult
            if (cell.Height > 70)
            {
                float heightPenalty = (cell.Height - 70) / 30f; // 0-1 range
                baseCost *= 1f + heightPenalty * 2f; // Up to 3x cost at peak
            }

            return baseCost;
        }

        /// <summary>
        /// Get the cost of traveling between two adjacent cells.
        /// </summary>
        public float GetEdgeCost(Cell from, Cell to)
        {
            // Base cost: average of the two cells' movement costs
            float fromCost = GetCellMovementCost(from);
            float toCost = GetCellMovementCost(to);

            if (fromCost >= ImpassableThreshold || toCost >= ImpassableThreshold)
                return float.MaxValue;

            float baseCost = (fromCost + toCost) / 2f;

            // Distance factor (Euclidean distance between cell centers)
            float distance = Vec2.Distance(from.Center, to.Center);

            // Scale by some factor to normalize (cells are roughly 10-50 units apart)
            float distanceFactor = distance / 30f; // Normalize around typical cell spacing

            float totalCost = baseCost * distanceFactor;

            // Port transition: crossing between land and sea incurs loading/unloading cost
            bool landToSea = from.IsLand && !to.IsLand;
            bool seaToLand = !from.IsLand && to.IsLand;
            if (landToSea || seaToLand)
            {
                totalCost += PortTransitionCost;
            }

            // River and road bonuses: take the better one, don't stack
            // Only applies to land travel
            if (from.IsLand && to.IsLand)
            {
                float bestMultiplier = 1f;

                // Check river bonus
                if (from.HasRiver && to.HasRiver && from.RiverId == to.RiverId)
                {
                    bestMultiplier = Math.Min(bestMultiplier, RiverCrossingBonus);
                }

                // Check road bonus
                if (_roadState != null)
                {
                    float roadMultiplier = _roadState.GetCostMultiplier(from.Id, to.Id);
                    bestMultiplier = Math.Min(bestMultiplier, roadMultiplier);
                }

                totalCost *= bestMultiplier;
            }

            return totalCost;
        }

        /// <summary>
        /// Find the shortest path between two cells using Dijkstra's algorithm.
        /// Returns the path and total cost.
        /// </summary>
        public PathResult FindPath(int fromCellId, int toCellId)
        {
            // Check cache
            var cacheKey = (fromCellId, toCellId);
            if (_pathCache.TryGetValue(cacheKey, out var cached))
                return cached;

            // Same cell
            if (fromCellId == toCellId)
            {
                var sameCellResult = new PathResult
                {
                    Path = new List<int> { fromCellId },
                    TotalCost = 0
                };
                CacheResult(cacheKey, sameCellResult);
                return sameCellResult;
            }

            // Validate cells exist
            if (!_mapData.CellById.TryGetValue(fromCellId, out var fromCell) ||
                !_mapData.CellById.TryGetValue(toCellId, out var toCell))
            {
                return PathResult.NotFound;
            }

            // Dijkstra's algorithm
            var dist = new Dictionary<int, float>();
            var prev = new Dictionary<int, int>();
            var visited = new HashSet<int>();

            // Priority queue: (cost, cellId)
            var pq = new SortedSet<(float cost, int cellId)>(
                Comparer<(float cost, int cellId)>.Create((a, b) =>
                {
                    int c = a.cost.CompareTo(b.cost);
                    return c != 0 ? c : a.cellId.CompareTo(b.cellId);
                }));

            dist[fromCellId] = 0;
            pq.Add((0, fromCellId));

            while (pq.Count > 0)
            {
                var (currentCost, currentId) = pq.Min;
                pq.Remove(pq.Min);

                if (visited.Contains(currentId))
                    continue;

                visited.Add(currentId);

                // Found destination
                if (currentId == toCellId)
                {
                    var result = ReconstructPath(prev, fromCellId, toCellId, currentCost);
                    CacheResult(cacheKey, result);
                    return result;
                }

                var currentCell = _mapData.CellById[currentId];

                // Explore neighbors
                foreach (var neighborId in currentCell.NeighborIds)
                {
                    if (visited.Contains(neighborId))
                        continue;

                    if (!_mapData.CellById.TryGetValue(neighborId, out var neighborCell))
                        continue;

                    float edgeCost = GetEdgeCost(currentCell, neighborCell);
                    if (edgeCost >= float.MaxValue)
                        continue; // Impassable

                    float newDist = currentCost + edgeCost;

                    if (!dist.TryGetValue(neighborId, out var oldDist) || newDist < oldDist)
                    {
                        // Remove old entry if exists (SortedSet doesn't update)
                        if (dist.ContainsKey(neighborId))
                            pq.Remove((oldDist, neighborId));

                        dist[neighborId] = newDist;
                        prev[neighborId] = currentId;
                        pq.Add((newDist, neighborId));
                    }
                }
            }

            // No path found
            var notFound = PathResult.NotFound;
            CacheResult(cacheKey, notFound);
            return notFound;
        }

        /// <summary>
        /// Get the transport cost between two cells without computing the full path.
        /// Uses A* heuristic for faster estimation when exact path isn't needed.
        /// </summary>
        public float GetTransportCost(int fromCellId, int toCellId)
        {
            var result = FindPath(fromCellId, toCellId);
            return result.TotalCost;
        }

        /// <summary>
        /// Find all cells reachable from a starting cell within a cost budget.
        /// Useful for computing market zones.
        /// </summary>
        public Dictionary<int, float> FindReachable(int fromCellId, float maxCost)
        {
            var reachable = new Dictionary<int, float>();

            if (!_mapData.CellById.TryGetValue(fromCellId, out var fromCell))
                return reachable;

            var visited = new HashSet<int>();
            var pq = new SortedSet<(float cost, int cellId)>(
                Comparer<(float cost, int cellId)>.Create((a, b) =>
                {
                    int c = a.cost.CompareTo(b.cost);
                    return c != 0 ? c : a.cellId.CompareTo(b.cellId);
                }));

            pq.Add((0, fromCellId));
            reachable[fromCellId] = 0;

            while (pq.Count > 0)
            {
                var (currentCost, currentId) = pq.Min;
                pq.Remove(pq.Min);

                if (visited.Contains(currentId))
                    continue;

                visited.Add(currentId);

                var currentCell = _mapData.CellById[currentId];

                foreach (var neighborId in currentCell.NeighborIds)
                {
                    if (visited.Contains(neighborId))
                        continue;

                    if (!_mapData.CellById.TryGetValue(neighborId, out var neighborCell))
                        continue;

                    float edgeCost = GetEdgeCost(currentCell, neighborCell);
                    if (edgeCost >= float.MaxValue)
                        continue;

                    float newCost = currentCost + edgeCost;

                    if (newCost <= maxCost)
                    {
                        if (!reachable.TryGetValue(neighborId, out var oldCost) || newCost < oldCost)
                        {
                            if (reachable.ContainsKey(neighborId))
                                pq.Remove((oldCost, neighborId));

                            reachable[neighborId] = newCost;
                            pq.Add((newCost, neighborId));
                        }
                    }
                }
            }

            return reachable;
        }

        /// <summary>
        /// Clear the path cache.
        /// </summary>
        public void ClearCache()
        {
            _pathCache.Clear();
        }

        private PathResult ReconstructPath(Dictionary<int, int> prev, int from, int to, float totalCost)
        {
            var path = new List<int>();
            int current = to;

            while (current != from)
            {
                path.Add(current);
                if (!prev.TryGetValue(current, out current))
                {
                    // Broken path - shouldn't happen
                    return PathResult.NotFound;
                }
            }

            path.Add(from);
            path.Reverse();

            return new PathResult
            {
                Path = path,
                TotalCost = totalCost
            };
        }

        private void CacheResult((int, int) key, PathResult result)
        {
            // Simple cache eviction: clear half when full
            if (_pathCache.Count >= _maxCacheSize)
            {
                _pathCache.Clear(); // Simple strategy for now
            }

            _pathCache[key] = result;
        }
    }
}
