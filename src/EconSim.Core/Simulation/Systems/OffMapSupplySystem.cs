using EconSim.Core.Common;
using EconSim.Core.Data;
using EconSim.Core.Economy;

namespace EconSim.Core.Simulation.Systems
{
    /// <summary>
    /// Replenishes supply for off-map markets each trade tick.
    /// Runs before TradeSystem so counties can buy off-map goods.
    /// Only replenishes goods that the off-map market is configured to supply
    /// (those with inflated BasePrice from OffMapMarketPlacer).
    /// </summary>
    public class OffMapSupplySystem : ITickSystem
    {
        public string Name => "OffMapSupply";

        // Runs on the same schedule as TradeSystem (weekly)
        public int TickInterval => SimulationConfig.Intervals.Weekly;

        // Target supply level per good per off-map market
        private const float TargetSupply = 1000f;

        public void Initialize(SimulationState state, MapData mapData)
        {
            int offMapCount = 0;
            foreach (var market in state.Economy.Markets.Values)
            {
                if (market.Type == MarketType.OffMap)
                    offMapCount++;
            }
            SimLog.Log("OffMapSupply", $"Initialized for {offMapCount} off-map markets");
        }

        public void Tick(SimulationState state, MapData mapData)
        {
            foreach (var market in state.Economy.Markets.Values)
            {
                if (market.Type != MarketType.OffMap)
                    continue;

                if (market.OffMapGoodIds == null || market.OffMapGoodIds.Count == 0)
                    continue;

                foreach (var goodId in market.OffMapGoodIds)
                {
                    if (!market.Goods.TryGetValue(goodId, out var goodState))
                        continue;

                    // Replenish to target level
                    if (goodState.Supply < TargetSupply)
                    {
                        goodState.Supply = TargetSupply;
                        goodState.SupplyOffered = TargetSupply;
                    }
                }
            }
        }
    }
}
