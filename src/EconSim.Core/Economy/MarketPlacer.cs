using System;
using System.Collections.Generic;
using System.Linq;
using EconSim.Core.Common;
using EconSim.Core.Data;
using EconSim.Core.Transport;

namespace EconSim.Core.Economy
{
    /// <summary>
    /// Handles market placement using suitability scoring.
    /// </summary>
    public static class MarketPlacer
    {
        private const float LegacyDefaultMarketZoneMaxCost = 100f;
        private const float MinMarketZoneMaxCost = 50f;

        /// <summary>
        /// Compute market suitability score for a cell.
        /// Higher score = better market location.
        /// </summary>
        public static float ComputeSuitability(
            Cell cell,
            MapData mapData,
            TransportGraph transport,
            EconomyState economy)
        {
            if (!cell.IsLand) return 0;

            float score = 0;

            // 1. Settlement bonus: existing burgs are natural market locations
            if (cell.HasBurg)
            {
                var burg = mapData.Burgs.FirstOrDefault(b => b.Id == cell.BurgId);
                if (burg != null)
                {
                    // Capital cities get big bonus
                    if (burg.IsCapital) score += 50;
                    // City/town bonus based on population
                    score += Math.Min(burg.Population / 100f, 30);
                    // Port bonus
                    if (burg.IsPort) score += 20;
                }
            }

            // 2. Coastal bonus: access to sea trade (future)
            if (cell.CoastDistance >= 0 && cell.CoastDistance <= 2)
            {
                score += 15;
            }

            // 3. River bonus: rivers are historical trade routes
            if (cell.HasRiver)
            {
                score += 10;
                // Extra bonus for high-flow rivers (major rivers)
                if (cell.RiverFlow > 50) score += 5;
            }

            // 4. Population: more people = more trade
            score += Math.Min(cell.Population / 500f, 20);

            // 5. Accessibility: low average movement cost to neighbors
            float avgNeighborCost = 0;
            int neighborCount = 0;
            foreach (var neighborId in cell.NeighborIds)
            {
                if (mapData.CellById.TryGetValue(neighborId, out var neighbor) && neighbor.IsLand)
                {
                    avgNeighborCost += transport.GetCellMovementCost(neighbor);
                    neighborCount++;
                }
            }
            if (neighborCount > 0)
            {
                avgNeighborCost /= neighborCount;
                // Lower cost = better (invert and scale)
                float accessScore = Math.Max(0, 20 - avgNeighborCost * 5);
                score += accessScore;
            }

            // 6. Resource diversity: access to multiple resource types nearby
            var county = economy.GetCountyForCell(cell.Id);
            if (county != null)
            {
                // Count distinct resources in this county and immediate neighbors
                var resourceTypes = new HashSet<string>(county.Resources.Keys);
                foreach (var neighborId in cell.NeighborIds)
                {
                    var neighborCounty = economy.GetCountyForCell(neighborId);
                    if (neighborCounty != null)
                    {
                        foreach (var res in neighborCounty.Resources.Keys)
                        {
                            resourceTypes.Add(res);
                        }
                    }
                }
                score += resourceTypes.Count * 5;
            }

            // 7. Centrality bonus: prefer cells with many land neighbors
            int landNeighbors = cell.NeighborIds.Count(id =>
                mapData.CellById.TryGetValue(id, out var n) && n.IsLand);
            score += landNeighbors * 2;

            return score;
        }

        /// <summary>
        /// Find the best cell for a market based on suitability scoring.
        /// </summary>
        public static int FindBestMarketLocation(
            MapData mapData,
            TransportGraph transport,
            EconomyState economy,
            HashSet<int> excludeCells = null,
            HashSet<int> excludeRealms = null)
        {
            float bestScore = float.MinValue;
            int bestCellId = -1;

            foreach (var cell in mapData.Cells)
            {
                if (!cell.IsLand) continue;
                if (excludeCells != null && excludeCells.Contains(cell.Id)) continue;
                if (excludeRealms != null && excludeRealms.Contains(cell.RealmId)) continue;

                float score = ComputeSuitability(cell, mapData, transport, economy);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestCellId = cell.Id;
                }
            }

            return bestCellId;
        }

        /// <summary>
        /// Place markets optimally. For v1, places a single market at the best location.
        /// </summary>
        public static List<Market> PlaceMarkets(
            MapData mapData,
            TransportGraph transport,
            EconomyState economy,
            int count = 1)
        {
            var markets = new List<Market>();
            var usedCells = new HashSet<int>();

            for (int i = 0; i < count; i++)
            {
                int cellId = FindBestMarketLocation(mapData, transport, economy, usedCells);
                if (cellId < 0) break;

                var cell = mapData.CellById[cellId];
                var burg = cell.HasBurg
                    ? mapData.Burgs.FirstOrDefault(b => b.Id == cell.BurgId)
                    : null;

                var market = new Market
                {
                    Id = i + 1,
                    LocationCellId = cellId,
                    Name = burg?.Name ?? $"Market {i + 1}",
                    SuitabilityScore = ComputeSuitability(cell, mapData, transport, economy)
                };

                // Initialize market goods for all tradeable goods
                foreach (var good in economy.Goods.All)
                {
                    market.Goods[good.Id] = new MarketGoodState
                    {
                        GoodId = good.Id,
                        BasePrice = good.BasePrice,
                        Price = good.BasePrice,
                        Supply = 0,
                        Demand = 0
                    };
                }

                markets.Add(market);
                usedCells.Add(cellId);

                SimLog.Log("Market", $"Placed market '{market.Name}' at cell {cellId} (score: {market.SuitabilityScore:F1})");
            }

            return markets;
        }

        /// <summary>
        /// Compute the market zone: all cells that can economically access this market.
        /// Uses transport cost threshold.
        /// </summary>
        public static float ResolveMarketZoneMaxTransportCost(MapData mapData)
        {
            if (mapData?.Info == null)
                throw new InvalidOperationException("ResolveMarketZoneMaxTransportCost requires MapData.Info.");

            float spanCost = WorldScale.ResolveMapSpanCost(mapData.Info);
            if (WorldScale.LegacyReferenceMapSpanCost <= 0f)
                throw new InvalidOperationException("Legacy reference map span cost must be > 0.");

            float scaled = LegacyDefaultMarketZoneMaxCost * (spanCost / WorldScale.LegacyReferenceMapSpanCost);
            if (float.IsNaN(scaled) || float.IsInfinity(scaled))
                throw new InvalidOperationException($"Computed market zone transport cost is not finite: {scaled}");

            return Math.Max(MinMarketZoneMaxCost, scaled);
        }

        public static void ComputeMarketZone(
            Market market,
            MapData mapData,
            TransportGraph transport,
            float maxTransportCost = -1f)
        {
            market.ZoneCellIds.Clear();
            market.ZoneCellCosts.Clear();

            float resolvedMaxTransportCost = maxTransportCost > 0f
                ? maxTransportCost
                : ResolveMarketZoneMaxTransportCost(mapData);

            var reachable = transport.FindReachable(market.LocationCellId, resolvedMaxTransportCost);
            foreach (var kvp in reachable)
            {
                if (mapData.CellById.TryGetValue(kvp.Key, out var cell) && cell.IsLand)
                {
                    market.ZoneCellIds.Add(kvp.Key);
                    market.ZoneCellCosts[kvp.Key] = kvp.Value;
                }
            }

            SimLog.Log("Market", $"Market '{market.Name}' zone: {market.ZoneCellIds.Count} cells (max cost: {resolvedMaxTransportCost:F1})");
        }
    }
}
