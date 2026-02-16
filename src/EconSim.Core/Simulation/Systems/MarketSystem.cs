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
        private readonly Dictionary<string, float> _totalInventoryByGood = new Dictionary<string, float>();
        private readonly Dictionary<string, float> _eligibleDemandByGood = new Dictionary<string, float>();
        private readonly Dictionary<string, float> _eligibleSupplyByGood = new Dictionary<string, float>();
        private readonly Dictionary<string, List<int>> _eligibleOrdersByGood = new Dictionary<string, List<int>>();
        private readonly Dictionary<string, List<int>> _eligibleLotsByGood = new Dictionary<string, List<int>>();

        public string Name => "Market";
        public int TickInterval => SimulationConfig.Intervals.Daily;

        public void Initialize(SimulationState state, MapData mapData)
        {
        }

        public void Tick(SimulationState state, MapData mapData)
        {
            var economy = state.Economy;
            if (economy == null)
                return;

            int dayIndex = state.CurrentDay % 7;

            foreach (var market in economy.Markets.Values)
            {
                if (market.Type == MarketType.Black)
                    continue;

                ApplyDecay(economy, market);
                ClearMarket(state, economy, market, dayIndex);

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

        private void ClearMarket(
            SimulationState state,
            EconomyState economy,
            Market market,
            int dayIndex)
        {
            int currentDay = state.CurrentDay;
            _totalInventoryByGood.Clear();
            _eligibleDemandByGood.Clear();
            _eligibleSupplyByGood.Clear();
            ClearIndexLists(_eligibleOrdersByGood);
            ClearIndexLists(_eligibleLotsByGood);

            for (int i = 0; i < market.Inventory.Count; i++)
            {
                var lot = market.Inventory[i];
                if (lot.Quantity > 0f)
                {
                    _totalInventoryByGood.TryGetValue(lot.GoodId, out float inventory);
                    _totalInventoryByGood[lot.GoodId] = inventory + lot.Quantity;
                }

                if (lot.DayListed >= currentDay || lot.Quantity <= LotCullThreshold)
                    continue;

                if (!_eligibleLotsByGood.TryGetValue(lot.GoodId, out var lotIndices))
                {
                    lotIndices = new List<int>();
                    _eligibleLotsByGood[lot.GoodId] = lotIndices;
                }

                lotIndices.Add(i);
                _eligibleSupplyByGood.TryGetValue(lot.GoodId, out float supply);
                _eligibleSupplyByGood[lot.GoodId] = supply + lot.Quantity;
            }

            for (int i = 0; i < market.PendingBuyOrders.Count; i++)
            {
                var order = market.PendingBuyOrders[i];
                if (order.DayPosted >= currentDay || order.Quantity <= LotCullThreshold)
                    continue;

                if (!_eligibleOrdersByGood.TryGetValue(order.GoodId, out var orderIndices))
                {
                    orderIndices = new List<int>();
                    _eligibleOrdersByGood[order.GoodId] = orderIndices;
                }

                orderIndices.Add(i);
                _eligibleDemandByGood.TryGetValue(order.GoodId, out float demand);
                _eligibleDemandByGood[order.GoodId] = demand + order.Quantity;
            }

            foreach (var goodState in market.Goods.Values)
            {
                goodState.Supply = _totalInventoryByGood.TryGetValue(goodState.GoodId, out float inventory)
                    ? inventory
                    : 0f;
                goodState.SupplyOffered = goodState.Supply;
                goodState.Demand = _eligibleDemandByGood.TryGetValue(goodState.GoodId, out float demand)
                    ? demand
                    : 0f;
                goodState.LastTradeVolume = 0f;
                goodState.Revenue = 0f;
            }

            foreach (var demandEntry in _eligibleOrdersByGood)
            {
                if (demandEntry.Value.Count == 0)
                    continue;

                string goodId = demandEntry.Key;
                if (!_eligibleLotsByGood.TryGetValue(goodId, out var eligibleLots) || eligibleLots.Count == 0)
                    continue;
                if (!market.Goods.TryGetValue(goodId, out var goodState))
                    continue;

                float totalDemand = _eligibleDemandByGood[goodId];
                if (!_eligibleSupplyByGood.TryGetValue(goodId, out float totalSupply))
                    continue;

                float plannedTraded = Math.Min(totalDemand, totalSupply);
                if (plannedTraded <= 0f)
                    continue;

                float buyerFillRatio = totalDemand > 0f ? plannedTraded / totalDemand : 0f;
                float actualDemandFilled = 0f;

                var eligibleOrders = demandEntry.Value;
                foreach (int orderIndex in eligibleOrders)
                {
                    var order = market.PendingBuyOrders[orderIndex];
                    float desiredQty = order.Quantity * buyerFillRatio;
                    if (desiredQty <= LotCullThreshold)
                        continue;

                    if (!TryResolveBuyer(economy, order, out var facilityBuyer, out var buyerCountyId, out float treasury))
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
                        facilityBuyer.BeginDayMetrics(currentDay);
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
                    continue;

                // FIFO lot resolution: lots are usually already in list-order by listing day.
                if (!IsFifoOrdered(eligibleLots, market.Inventory))
                {
                    eligibleLots.Sort((a, b) =>
                    {
                        var left = market.Inventory[a];
                        var right = market.Inventory[b];
                        int dayCmp = left.DayListed.CompareTo(right.DayListed);
                        return dayCmp != 0 ? dayCmp : a.CompareTo(b);
                    });
                }

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
                    CreditSellerRevenue(economy, market, lot.SellerId, payout, dayIndex, currentDay);
                    sellerRevenue += payout;
                }

                float traded = Math.Max(0f, actualDemandFilled - remaining);
                goodState.Supply = Math.Max(0f, goodState.Supply - traded);
                goodState.LastTradeVolume = traded;
                goodState.Revenue = sellerRevenue;
            }
        }

        private static void ClearIndexLists(Dictionary<string, List<int>> map)
        {
            foreach (var entry in map.Values)
            {
                entry.Clear();
            }
        }

        private static bool IsFifoOrdered(List<int> lotIndices, List<ConsignmentLot> inventory)
        {
            if (lotIndices.Count < 2)
                return true;

            int prevIndex = lotIndices[0];
            int prevDay = inventory[prevIndex].DayListed;

            for (int i = 1; i < lotIndices.Count; i++)
            {
                int index = lotIndices[i];
                int day = inventory[index].DayListed;
                if (day < prevDay)
                    return false;
                if (day == prevDay && index < prevIndex)
                    return false;

                prevDay = day;
                prevIndex = index;
            }

            return true;
        }

        private static void CreditSellerRevenue(EconomyState economy, Market market, int sellerId, float amount, int dayIndex, int currentDay)
        {
            if (amount <= 0f)
                return;

            if (sellerId > 0 && economy.Facilities.TryGetValue(sellerId, out var facility))
            {
                facility.BeginDayMetrics(currentDay);
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
            BuyOrder order,
            out Facility facilityBuyer,
            out int countyId,
            out float treasury)
        {
            facilityBuyer = null;
            countyId = 0;
            treasury = 0f;

            if (!order.IsPopulationOrder)
            {
                if (!economy.Facilities.TryGetValue(order.FacilityId, out facilityBuyer))
                    return false;

                countyId = facilityBuyer.CountyId;
                treasury = facilityBuyer.Treasury;
                return countyId > 0;
            }

            countyId = order.CountyId;
            if (countyId <= 0)
                return false;

            if (!economy.Counties.TryGetValue(countyId, out var county))
                return false;

            treasury = county.Population.Treasury;
            return true;
        }
    }
}
