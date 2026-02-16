using System;
using System.Collections.Generic;
using EconSim.Core.Common;
using EconSim.Core.Data;
using EconSim.Core.Economy;

namespace EconSim.Core.Simulation.Systems
{
    /// <summary>
    /// Replenishes supply for off-map markets each trade tick.
    /// Only replenishes goods that the off-map market is configured to supply
    /// (those with inflated BasePrice from OffMapMarketPlacer).
    /// </summary>
        public class OffMapSupplySystem : ITickSystem
        {
            public string Name => "OffMapSupply";
            private readonly Dictionary<int, float> _inventoryByGoodBuffer = new Dictionary<int, float>();

        public int TickInterval => SimulationConfig.Intervals.Daily;

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

            // Pre-seed off-map consignments so first market clear is not supply-starved.
            SeedV2Supply(state, dayListed: Math.Max(0, state.CurrentDay - 1));
        }

        public void Tick(SimulationState state, MapData mapData)
        {
            TickV2(state);
        }

        private void TickV2(SimulationState state)
        {
            SeedV2Supply(state, state.CurrentDay);
        }

        private void SeedV2Supply(SimulationState state, int dayListed)
        {
            foreach (var market in state.Economy.Markets.Values)
            {
                if (market.Type != MarketType.OffMap)
                    continue;

                if (market.OffMapGoodIds == null || market.OffMapGoodIds.Count == 0)
                    continue;

                _inventoryByGoodBuffer.Clear();
                foreach (var offMapGoodId in market.OffMapGoodIds)
                {
                    if (!state.Economy.Goods.TryGetRuntimeId(offMapGoodId, out int runtimeId))
                        continue;

                    float inventory = market.GetTotalInventory(runtimeId);
                    if (inventory > 0f)
                        _inventoryByGoodBuffer[runtimeId] = inventory;
                }

                int sellerId = MarketOrderIds.MakeOffMapSellerId(market.Id);
                foreach (var goodId in market.OffMapGoodIds)
                {
                    if (!state.Economy.Goods.TryGetRuntimeId(goodId, out int runtimeId))
                        continue;

                    _inventoryByGoodBuffer.TryGetValue(runtimeId, out float inventory);

                    if (inventory >= TargetSupply)
                        continue;

                    float needed = TargetSupply - inventory;
                    market.AddInventoryLot(new ConsignmentLot
                    {
                        SellerId = sellerId,
                        GoodId = goodId,
                        GoodRuntimeId = runtimeId,
                        Quantity = needed,
                        DayListed = dayListed
                    });
                }
            }
        }
    }
}
