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
        private readonly Dictionary<int, float> _totalInventoryByGood = new Dictionary<int, float>();
        private readonly Dictionary<int, float> _eligibleDemandByGood = new Dictionary<int, float>();
        private readonly Dictionary<int, float> _eligibleSupplyByGood = new Dictionary<int, float>();
        private readonly Dictionary<int, MarketGoodState> _goodStateByRuntimeId = new Dictionary<int, MarketGoodState>();

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
                market.PromotePendingBooks(state.CurrentDay);
                ApplyDecay(economy, market);
                ClearMarket(state, economy, market, dayIndex);

                // Tradable orders are one-shot daily books; keep only newly posted pending books.
                market.ClearTradableOrders();
                market.CullBooks(LotCullThreshold);
            }
        }

        private void ApplyDecay(EconomyState economy, Market market)
        {
            foreach (var goodState in market.Goods.Values)
            {
                var good = goodState.RuntimeId >= 0
                    ? economy.Goods.GetByRuntimeId(goodState.RuntimeId)
                    : economy.Goods.Get(goodState.GoodId);
                if (good == null || good.DecayRate <= 0f)
                    continue;

                if (good.RuntimeId >= 0)
                    market.ApplyDecayForGood(good.RuntimeId, good.DecayRate, LotCullThreshold);
            }
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
            _goodStateByRuntimeId.Clear();

            foreach (var goodState in market.Goods.Values)
            {
                int goodRuntimeId = goodState.RuntimeId;
                if (goodRuntimeId < 0)
                    continue;

                _goodStateByRuntimeId[goodRuntimeId] = goodState;

                float inventory = market.GetTotalInventory(goodRuntimeId);
                if (inventory > 0f)
                    _totalInventoryByGood[goodRuntimeId] = inventory;

                float supply = market.GetTradableSupply(goodRuntimeId);
                if (supply > 0f)
                    _eligibleSupplyByGood[goodRuntimeId] = supply;

                float demand = market.GetTradableDemand(goodRuntimeId);
                if (demand > 0f)
                    _eligibleDemandByGood[goodRuntimeId] = demand;

                goodState.Supply = inventory;
                goodState.SupplyOffered = goodState.Supply;
                goodState.Demand = demand;
                goodState.LastTradeVolume = 0f;
                goodState.Revenue = 0f;
            }

            foreach (var demandEntry in _eligibleDemandByGood)
            {
                int goodRuntimeId = demandEntry.Key;
                if (!market.TryGetTradableInventory(goodRuntimeId, out var sellers) || sellers.Count == 0)
                    continue;
                if (!market.TryGetTradableOrders(goodRuntimeId, out var orders) || orders.Count == 0)
                    continue;
                if (!_goodStateByRuntimeId.TryGetValue(goodRuntimeId, out var goodState))
                    continue;

                float totalDemand = demandEntry.Value;
                if (!_eligibleSupplyByGood.TryGetValue(goodRuntimeId, out float totalSupply))
                    continue;

                float plannedTraded = Math.Min(totalDemand, totalSupply);
                if (plannedTraded <= 0f)
                    continue;

                float buyerFillRatio = totalDemand > 0f ? plannedTraded / totalDemand : 0f;
                float actualDemandFilled = 0f;

                foreach (var orderEntry in orders)
                {
                    var order = orderEntry.Value;
                    if (order.Quantity <= LotCullThreshold)
                        continue;

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
                        facilityBuyer.InputBuffer.Add(goodRuntimeId, desiredQty);
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

                float soldTarget = Math.Min(actualDemandFilled, totalSupply);
                if (soldTarget <= LotCullThreshold)
                    continue;

                float remainingSold = soldTarget;
                float remainingSupply = totalSupply;
                float sellerRevenue = 0f;
                var sellerIds = new List<int>(sellers.Keys);
                for (int i = 0; i < sellerIds.Count; i++)
                {
                    int sellerId = sellerIds[i];
                    if (!sellers.TryGetValue(sellerId, out float sellerQty) || sellerQty <= LotCullThreshold)
                        continue;

                    if (remainingSold <= LotCullThreshold || remainingSupply <= 0f)
                        break;

                    float sold;
                    bool isLast = i == sellerIds.Count - 1;
                    if (isLast)
                    {
                        sold = Math.Min(sellerQty, remainingSold);
                    }
                    else
                    {
                        float proportional = remainingSold * (sellerQty / remainingSupply);
                        sold = Math.Min(sellerQty, proportional);
                    }

                    sellers[sellerId] = Math.Max(0f, sellerQty - sold);
                    remainingSold -= sold;
                    remainingSupply -= sellerQty;

                    float payout = sold * goodState.Price;
                    CreditSellerRevenue(economy, market, sellerId, payout, dayIndex, currentDay);
                    sellerRevenue += payout;
                }

                float traded = Math.Max(0f, soldTarget - remainingSold);
                goodState.Supply = Math.Max(0f, goodState.Supply - traded);
                goodState.LastTradeVolume = traded;
                goodState.Revenue = sellerRevenue;
            }
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
