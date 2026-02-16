using System;
using System.Collections.Generic;
using EconSim.Core.Data;
using EconSim.Core.Economy;

namespace EconSim.Core.Simulation.Systems
{
    /// <summary>
    /// Economy V2 market clearing system.
    /// Clears consignment lots against posted buy orders with one-day lag.
    /// </summary>
    public class MarketSystem : ITickSystem
    {
        private const float BuyerTransportFeeRate = 0.005f;
        private const float LotCullThreshold = 0.01f;

        public string Name => "Market";
        public int TickInterval => SimulationConfig.Intervals.Daily;

        public void Initialize(SimulationState state, MapData mapData)
        {
        }

        public void Tick(SimulationState state, MapData mapData)
        {
            if (!SimulationConfig.UseEconomyV2)
                return;

            var economy = state.Economy;
            if (economy == null)
                return;

            int dayIndex = state.CurrentDay % 7;
            foreach (var facility in economy.Facilities.Values)
            {
                facility.ClearDayMetrics(dayIndex);
            }

            foreach (var market in economy.Markets.Values)
            {
                if (market.Type == MarketType.Black)
                    continue;

                ApplyDecay(economy, market);

                foreach (var good in economy.Goods.All)
                {
                    ClearGood(state, economy, market, good.Id, dayIndex);
                }

                // Remove all eligible orders (filled or partial). Remaining orders are posted today.
                market.PendingBuyOrders.RemoveAll(o => o.DayPosted < state.CurrentDay || o.Quantity <= LotCullThreshold);
                market.Inventory.RemoveAll(l => l.Quantity <= LotCullThreshold);
            }
        }

        private void ApplyDecay(EconomyState economy, Market market)
        {
            for (int i = 0; i < market.Inventory.Count; i++)
            {
                var lot = market.Inventory[i];
                if (lot.Quantity <= LotCullThreshold)
                    continue;

                var good = economy.Goods.Get(lot.GoodId);
                if (good == null || good.DecayRate <= 0f)
                    continue;

                lot.Quantity *= Math.Max(0f, 1f - good.DecayRate);
                market.Inventory[i] = lot;
            }

            market.Inventory.RemoveAll(l => l.Quantity <= LotCullThreshold);
        }

        private void ClearGood(
            SimulationState state,
            EconomyState economy,
            Market market,
            string goodId,
            int dayIndex)
        {
            if (!market.Goods.TryGetValue(goodId, out var goodState))
                return;

            var eligibleOrders = new List<int>();
            var eligibleLots = new List<int>();
            float totalDemand = 0f;
            float totalSupply = 0f;

            for (int i = 0; i < market.PendingBuyOrders.Count; i++)
            {
                var order = market.PendingBuyOrders[i];
                if (order.GoodId != goodId || order.DayPosted >= state.CurrentDay || order.Quantity <= LotCullThreshold)
                    continue;

                eligibleOrders.Add(i);
                totalDemand += order.Quantity;
            }

            for (int i = 0; i < market.Inventory.Count; i++)
            {
                var lot = market.Inventory[i];
                if (lot.GoodId != goodId || lot.DayListed >= state.CurrentDay || lot.Quantity <= LotCullThreshold)
                    continue;

                eligibleLots.Add(i);
                totalSupply += lot.Quantity;
            }

            float plannedTraded = Math.Min(totalDemand, totalSupply);
            if (plannedTraded <= 0f)
            {
                goodState.Supply = SumInventory(market, goodId);
                goodState.Demand = totalDemand;
                goodState.LastTradeVolume = 0f;
                goodState.Revenue = 0f;
                return;
            }

            float buyerFillRatio = totalDemand > 0f ? plannedTraded / totalDemand : 0f;
            float actualDemandFilled = 0f;

            foreach (int orderIndex in eligibleOrders)
            {
                var order = market.PendingBuyOrders[orderIndex];
                float desiredQty = order.Quantity * buyerFillRatio;
                if (desiredQty <= LotCullThreshold)
                    continue;

                if (!TryResolveBuyer(economy, order.BuyerId, out var facilityBuyer, out var buyerCountyId, out float treasury))
                    continue;

                float unitPrice = goodState.Price;
                float baseCost = desiredQty * unitPrice;
                float fee = baseCost * Math.Max(0f, order.TransportCost) * BuyerTransportFeeRate;
                float gross = baseCost + fee;
                if (gross <= 0f)
                    continue;

                if (treasury < gross)
                {
                    float scale = treasury / gross;
                    desiredQty *= scale;
                    baseCost *= scale;
                    fee *= scale;
                    gross = treasury;
                }

                if (desiredQty <= LotCullThreshold || gross <= 0f)
                    continue;

                // Debit buyer.
                if (facilityBuyer != null)
                {
                    facilityBuyer.Treasury -= gross;
                    facilityBuyer.AddInputCostForDay(dayIndex, gross);
                    facilityBuyer.InputBuffer.Add(goodId, desiredQty);
                }
                else
                {
                    var buyerCounty = economy.GetCounty(buyerCountyId);
                    buyerCounty.Population.Treasury -= gross;
                }

                // Buyer transport fee goes to buyer county households/teamsters.
                var countyPop = economy.GetCounty(buyerCountyId).Population;
                countyPop.Treasury += fee;

                actualDemandFilled += desiredQty;
            }

            if (actualDemandFilled <= 0f)
            {
                goodState.Supply = SumInventory(market, goodId);
                goodState.Demand = totalDemand;
                goodState.LastTradeVolume = 0f;
                goodState.Revenue = 0f;
                return;
            }

            // FIFO lot resolution.
            eligibleLots.Sort((a, b) =>
            {
                var left = market.Inventory[a];
                var right = market.Inventory[b];
                int dayCmp = left.DayListed.CompareTo(right.DayListed);
                return dayCmp != 0 ? dayCmp : a.CompareTo(b);
            });

            float remaining = actualDemandFilled;
            float sellerRevenue = 0f;
            foreach (int lotIndex in eligibleLots)
            {
                if (remaining <= LotCullThreshold)
                    break;

                var lot = market.Inventory[lotIndex];
                float sold = Math.Min(lot.Quantity, remaining);
                if (sold <= 0f)
                    continue;

                lot.Quantity -= sold;
                market.Inventory[lotIndex] = lot;
                remaining -= sold;

                float payout = sold * goodState.Price;
                CreditSellerRevenue(economy, market, lot.SellerId, payout, dayIndex);
                sellerRevenue += payout;
            }

            float traded = actualDemandFilled - remaining;
            goodState.Supply = SumInventory(market, goodId);
            goodState.Demand = totalDemand;
            goodState.LastTradeVolume = Math.Max(0f, traded);
            goodState.Revenue = sellerRevenue;
        }

        private static float SumInventory(Market market, string goodId)
        {
            float total = 0f;
            for (int i = 0; i < market.Inventory.Count; i++)
            {
                if (market.Inventory[i].GoodId == goodId)
                    total += market.Inventory[i].Quantity;
            }

            return total;
        }

        private static void CreditSellerRevenue(EconomyState economy, Market market, int sellerId, float amount, int dayIndex)
        {
            if (amount <= 0f)
                return;

            if (sellerId > 0 && economy.Facilities.TryGetValue(sellerId, out var facility))
            {
                facility.Treasury += amount;
                facility.AddRevenueForDay(dayIndex, amount);
                return;
            }

            int countyId = 0;
            if (market.LocationCellId > 0)
            {
                economy.CellToCounty.TryGetValue(market.LocationCellId, out countyId);
            }

            if (countyId <= 0)
                return;

            economy.GetCounty(countyId).Population.Treasury += amount;
        }

        private static bool TryResolveBuyer(
            EconomyState economy,
            int buyerId,
            out Facility facilityBuyer,
            out int countyId,
            out float treasury)
        {
            facilityBuyer = null;
            countyId = 0;
            treasury = 0f;

            if (buyerId > 0)
            {
                if (!economy.Facilities.TryGetValue(buyerId, out facilityBuyer))
                    return false;

                countyId = facilityBuyer.CountyId;
                treasury = facilityBuyer.Treasury;
                return countyId > 0;
            }

            if (!MarketOrderIds.TryGetPopulationCountyId(buyerId, out countyId))
                return false;

            if (!economy.Counties.TryGetValue(countyId, out var county))
                return false;

            treasury = county.Population.Treasury;
            return true;
        }
    }
}
