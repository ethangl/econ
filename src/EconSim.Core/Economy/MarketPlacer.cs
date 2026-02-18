using System;
using System.Collections.Generic;
using EconSim.Core.Common;
using EconSim.Core.Data;
using EconSim.Core.Transport;

namespace EconSim.Core.Economy
{
    /// <summary>
    /// Handles market zone computation.
    /// Market locations are determined by realm capitals (see SimulationRunner.InitializeMarkets).
    /// </summary>
    public static class MarketPlacer
    {
        private const float LegacyDefaultMarketZoneMaxCost = 200f;
        private const float MinMarketZoneMaxCost = 50f;

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
