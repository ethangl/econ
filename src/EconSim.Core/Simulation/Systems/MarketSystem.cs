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
        private const float ReserveTargetDays = 30f;
        private const float ReserveRefillSharePerDay = 0.04f;
        private const float ReserveReleaseDemandCoverage = 1.00f;
        private const float ReserveReleaseMaxSharePerDay = 0.20f;
        private const float ReserveActivationDemandFloorKg = 0.5f;
        private const float CountyRetainedDaysForGrain = 540f;
        private const float CountyRetainedDaysForSalt = 180f;
        private const float FallbackDailyGrainNeedPerCapitaKg = 0.60f;
        private static readonly string[] ReserveGoodIds = { "wheat", "rye", "barley", "rice_grain", "salt" };
        private static readonly string[] ReserveGrainGoodIds = { "wheat", "rye", "barley", "rice_grain" };
        private readonly Dictionary<int, float> _totalInventoryByGood = new Dictionary<int, float>();
        private readonly Dictionary<int, float> _eligibleDemandByGood = new Dictionary<int, float>();
        private readonly Dictionary<int, float> _eligibleSupplyByGood = new Dictionary<int, float>();
        private readonly Dictionary<int, MarketGoodState> _goodStateByRuntimeId = new Dictionary<int, MarketGoodState>();
        private readonly List<int> _sellerIdsBuffer = new List<int>();
        private readonly Dictionary<int, List<int>> _countyIdsByMarketId = new Dictionary<int, List<int>>();
        private readonly List<int> _marketCountyBuffer = new List<int>();

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
            BuildMarketCountyIndex(economy);

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

            MaintainMarketReserve(economy, market);

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
                if (supply > 0f && market.TryGetTradableInventory(goodRuntimeId, out var sellersByGood))
                {
                    market.TryGetTradableInventoryMinPrices(goodRuntimeId, out var minPriceBySeller);
                    supply = ComputePriceEligibleSupply(sellersByGood, minPriceBySeller, goodState.Price);
                }

                float demand = market.GetTradableDemand(goodRuntimeId);

                // If demand exists and inventory is tradable but no lots are eligible at current price,
                // lift clearing price to the lowest seller floor so funded buyers can transact.
                if (demand > 0f
                    && supply <= LotCullThreshold
                    && market.TryGetTradableInventory(goodRuntimeId, out var unlockSellers)
                    && unlockSellers != null
                    && unlockSellers.Count > 0)
                {
                    market.TryGetTradableInventoryMinPrices(goodRuntimeId, out var unlockMinPrices);
                    float unlockPrice = ComputeMinimumSellerFloor(unlockSellers, unlockMinPrices);
                    if (unlockPrice > goodState.Price + 0.0001f)
                    {
                        goodState.Price = unlockPrice;
                        supply = ComputePriceEligibleSupply(unlockSellers, unlockMinPrices, goodState.Price);
                    }
                }

                if (supply > 0f)
                    _eligibleSupplyByGood[goodRuntimeId] = supply;
                if (demand > 0f)
                    _eligibleDemandByGood[goodRuntimeId] = demand;

                goodState.Supply = supply;
                goodState.SupplyOffered = inventory;
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
                market.TryGetTradableInventoryMinPrices(goodRuntimeId, out var minPriceBySeller);
                _sellerIdsBuffer.Clear();
                foreach (var sellerEntry in sellers)
                {
                    int sellerId = sellerEntry.Key;
                    float sellerQty = sellerEntry.Value;
                    if (sellerQty <= LotCullThreshold)
                        continue;
                    if (!IsSellerPriceEligible(sellerId, goodState.Price, minPriceBySeller))
                        continue;

                    _sellerIdsBuffer.Add(sellerId);
                }
                if (_sellerIdsBuffer.Count == 0)
                    continue;

                for (int i = 0; i < _sellerIdsBuffer.Count; i++)
                {
                    int sellerId = _sellerIdsBuffer[i];
                    if (!sellers.TryGetValue(sellerId, out float sellerQty) || sellerQty <= LotCullThreshold)
                        continue;

                    if (remainingSold <= LotCullThreshold || remainingSupply <= 0f)
                        break;

                    float sold;
                    bool isLast = i == _sellerIdsBuffer.Count - 1;
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

        private void BuildMarketCountyIndex(EconomyState economy)
        {
            _countyIdsByMarketId.Clear();
            if (economy?.CountyToMarket == null)
                return;

            foreach (var kvp in economy.CountyToMarket)
            {
                int countyId = kvp.Key;
                int marketId = kvp.Value;
                if (countyId <= 0 || marketId <= 0)
                    continue;

                if (!_countyIdsByMarketId.TryGetValue(marketId, out var countyIds))
                {
                    countyIds = new List<int>();
                    _countyIdsByMarketId[marketId] = countyIds;
                }

                countyIds.Add(countyId);
            }
        }

        private void MaintainMarketReserve(EconomyState economy, Market market)
        {
            if (economy == null || market == null || market.Type != MarketType.Legitimate)
                return;

            if (!_countyIdsByMarketId.TryGetValue(market.Id, out var countyIds) || countyIds == null || countyIds.Count == 0)
                return;

            for (int i = 0; i < ReserveGoodIds.Length; i++)
            {
                string goodId = ReserveGoodIds[i];
                if (!SimulationConfig.Economy.IsGoodEnabled(goodId))
                    continue;
                if (!economy.Goods.TryGetRuntimeId(goodId, out int runtimeId) || runtimeId < 0)
                    continue;

                if (!market.TryGetGoodState(runtimeId, out var goodState))
                    continue;

                int reserveSellerId = MarketOrderIds.MakeSeedSellerId(market.Id);
                ReclaimUnsoldReserveLots(market, runtimeId, reserveSellerId);

                float tradableDemand = market.GetTradableDemand(runtimeId);
                float popDemand = EstimatePopulationDemandKgPerDay(economy, countyIds, runtimeId);
                float effectiveDemand = Math.Max(tradableDemand, popDemand);
                if (effectiveDemand <= ReserveActivationDemandFloorKg)
                    continue;

                float targetReserve = Math.Max(0f, effectiveDemand * ReserveTargetDays);
                if (targetReserve <= 0f)
                    continue;

                float reserveCurrent = market.GetReserve(runtimeId);
                float reserveDeficit = Math.Max(0f, targetReserve - reserveCurrent);
                if (reserveDeficit > LotCullThreshold)
                {
                    float refillCap = Math.Max(0f, targetReserve * ReserveRefillSharePerDay);
                    float refillTarget = Math.Min(reserveDeficit, refillCap);
                    float transferred = PullCountyStockToReserve(economy, countyIds, runtimeId, refillTarget);
                    if (transferred > 0f)
                        market.AddReserve(runtimeId, transferred);
                }

                float tradableSupply = market.GetTradableSupply(runtimeId);
                float shortage = Math.Max(0f, tradableDemand - tradableSupply);
                if (shortage <= LotCullThreshold)
                    continue;

                float reserveAvailable = market.GetReserve(runtimeId);
                if (reserveAvailable <= LotCullThreshold)
                    continue;

                float releaseTarget = shortage * ReserveReleaseDemandCoverage;
                float releaseCap = Math.Max(0f, targetReserve * ReserveReleaseMaxSharePerDay);
                float release = Math.Min(reserveAvailable, Math.Min(releaseTarget, releaseCap));
                if (release <= LotCullThreshold)
                    continue;

                float released = market.RemoveReserve(runtimeId, release);
                if (released <= LotCullThreshold)
                    continue;

                float reserveAskFloor = Math.Max(0f, goodState.BasePrice * 0.80f);
                market.AddTradableInventory(runtimeId, reserveSellerId, released, reserveAskFloor);
            }
        }

        private static void ReclaimUnsoldReserveLots(Market market, int goodRuntimeId, int reserveSellerId)
        {
            if (market == null || goodRuntimeId < 0 || reserveSellerId == 0)
                return;

            if (!market.TryGetTradableInventory(goodRuntimeId, out var sellers) || sellers == null)
                return;
            if (!sellers.TryGetValue(reserveSellerId, out float unsold) || unsold <= LotCullThreshold)
                return;

            sellers.Remove(reserveSellerId);
            if (sellers.Count == 0)
                market.TradableInventoryByGood.Remove(goodRuntimeId);

            if (market.TryGetTradableInventoryMinPrices(goodRuntimeId, out var minPrices) && minPrices != null)
            {
                minPrices.Remove(reserveSellerId);
                if (minPrices.Count == 0)
                    market.TradableInventoryMinPriceByGood.Remove(goodRuntimeId);
            }

            market.AddReserve(goodRuntimeId, unsold);
        }

        private static float EstimatePopulationDemandKgPerDay(
            EconomyState economy,
            List<int> countyIds,
            int goodRuntimeId)
        {
            if (economy == null || countyIds == null || countyIds.Count == 0 || goodRuntimeId < 0)
                return 0f;

            var good = economy.Goods.GetByRuntimeId(goodRuntimeId);
            if (good == null || good.BaseConsumption <= 0f)
                return 0f;

            float perCapitaPerDay = Math.Max(0f, good.BaseConsumption);
            float total = 0f;
            for (int i = 0; i < countyIds.Count; i++)
            {
                int countyId = countyIds[i];
                if (countyId <= 0 || !economy.Counties.TryGetValue(countyId, out var county))
                    continue;

                int population = county.Population?.Total ?? 0;
                if (population <= 0)
                    continue;

                total += population * perCapitaPerDay;
            }

            return total;
        }

        private float PullCountyStockToReserve(
            EconomyState economy,
            List<int> countyIds,
            int goodRuntimeId,
            float targetQuantity)
        {
            if (economy == null || countyIds == null || countyIds.Count == 0 || goodRuntimeId < 0 || targetQuantity <= LotCullThreshold)
                return 0f;

            _marketCountyBuffer.Clear();
            float totalAvailable = 0f;
            for (int i = 0; i < countyIds.Count; i++)
            {
                int countyId = countyIds[i];
                if (!economy.Counties.TryGetValue(countyId, out var county))
                    continue;

                float availableForTransfer = GetCountyExportableStock(economy, county, goodRuntimeId);
                if (availableForTransfer <= LotCullThreshold)
                    continue;

                totalAvailable += availableForTransfer;
                _marketCountyBuffer.Add(countyId);
            }

            if (_marketCountyBuffer.Count == 0 || totalAvailable <= LotCullThreshold)
                return 0f;

            float toTransfer = Math.Min(totalAvailable, targetQuantity);
            if (toTransfer <= LotCullThreshold)
                return 0f;

            float transferred = 0f;
            for (int i = 0; i < _marketCountyBuffer.Count; i++)
            {
                int countyId = _marketCountyBuffer[i];
                if (!economy.Counties.TryGetValue(countyId, out var county))
                    continue;

                float availableForTransfer = GetCountyExportableStock(economy, county, goodRuntimeId);
                if (availableForTransfer <= LotCullThreshold)
                    continue;

                float share = availableForTransfer / totalAvailable;
                float desired = i == _marketCountyBuffer.Count - 1
                    ? Math.Max(0f, toTransfer - transferred)
                    : toTransfer * share;
                if (desired <= LotCullThreshold)
                    continue;

                transferred += county.Stockpile.Remove(goodRuntimeId, desired);
                if (transferred >= toTransfer - LotCullThreshold)
                    break;
            }

            return transferred;
        }

        private static float GetCountyExportableStock(EconomyState economy, CountyEconomy county, int goodRuntimeId)
        {
            if (economy == null || county == null || goodRuntimeId < 0)
                return 0f;

            float available = county.Stockpile.Get(goodRuntimeId);
            if (available <= LotCullThreshold)
                return 0f;

            float retainedFloor = ComputeCountyRetainedFloor(economy, county, goodRuntimeId);
            return Math.Max(0f, available - retainedFloor);
        }

        private static float ComputeCountyRetainedFloor(EconomyState economy, CountyEconomy county, int goodRuntimeId)
        {
            if (economy == null || county == null || goodRuntimeId < 0)
                return 0f;

            var good = economy.Goods.GetByRuntimeId(goodRuntimeId);
            if (good == null)
                return 0f;

            int population = county.Population?.Total ?? 0;
            if (population <= 0)
                return 0f;

            if (string.Equals(good.Id, "salt", StringComparison.OrdinalIgnoreCase))
            {
                float perCapitaDailySalt = Math.Max(0f, good.BaseConsumption);
                return population * perCapitaDailySalt * CountyRetainedDaysForSalt;
            }

            if (IsReserveGrain(good.Id))
            {
                float totalDailyGrainNeed = ResolveDailyGrainNeedPerCapita(economy);
                int activeGrainCount = CountActiveReserveGrainGoods(economy);
                if (activeGrainCount <= 0)
                    return 0f;

                float perCapitaPerGoodPerDay = totalDailyGrainNeed / activeGrainCount;
                return population * perCapitaPerGoodPerDay * CountyRetainedDaysForGrain;
            }

            float perCapitaDailyNeed = Math.Max(0f, good.BaseConsumption);
            return population * perCapitaDailyNeed * CountyRetainedDaysForSalt;
        }

        private static int CountActiveReserveGrainGoods(EconomyState economy)
        {
            if (economy?.Goods == null)
                return 0;

            int count = 0;
            for (int i = 0; i < ReserveGrainGoodIds.Length; i++)
            {
                string goodId = ReserveGrainGoodIds[i];
                if (!economy.Goods.TryGetRuntimeId(goodId, out int runtimeId) || runtimeId < 0)
                    continue;
                if (!SimulationConfig.Economy.IsGoodEnabled(goodId))
                    continue;
                count++;
            }

            return Math.Max(1, count);
        }

        private static float ResolveDailyGrainNeedPerCapita(EconomyState economy)
        {
            if (economy?.Goods != null
                && economy.Goods.TryGetRuntimeId("flour", out int flourRuntimeId)
                && flourRuntimeId >= 0)
            {
                var flour = economy.Goods.GetByRuntimeId(flourRuntimeId);
                if (flour != null && flour.BaseConsumption > 0f)
                    return flour.BaseConsumption * SimulationConfig.Economy.RawGrainKgPerFlourKg;
            }

            return FallbackDailyGrainNeedPerCapitaKg;
        }

        private static bool IsReserveGrain(string goodId)
        {
            if (string.IsNullOrWhiteSpace(goodId))
                return false;
            if (!SimulationConfig.Economy.IsGoodEnabled(goodId))
                return false;

            for (int i = 0; i < ReserveGrainGoodIds.Length; i++)
            {
                if (string.Equals(ReserveGrainGoodIds[i], goodId, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
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

        private static float ComputePriceEligibleSupply(
            Dictionary<int, float> sellers,
            Dictionary<int, float> minPriceBySeller,
            float clearingPrice)
        {
            if (sellers == null || sellers.Count == 0)
                return 0f;

            float total = 0f;
            foreach (var sellerEntry in sellers)
            {
                if (sellerEntry.Value <= LotCullThreshold)
                    continue;
                if (!IsSellerPriceEligible(sellerEntry.Key, clearingPrice, minPriceBySeller))
                    continue;

                total += sellerEntry.Value;
            }

            return total;
        }

        private static bool IsSellerPriceEligible(
            int sellerId,
            float clearingPrice,
            Dictionary<int, float> minPriceBySeller)
        {
            if (minPriceBySeller == null || !minPriceBySeller.TryGetValue(sellerId, out float minPrice))
                return true;

            return clearingPrice + 0.0001f >= Math.Max(0f, minPrice);
        }

        private static float ComputeMinimumSellerFloor(
            Dictionary<int, float> sellers,
            Dictionary<int, float> minPriceBySeller)
        {
            if (sellers == null || sellers.Count == 0)
                return 0f;

            float minFloor = float.MaxValue;
            foreach (var sellerEntry in sellers)
            {
                if (sellerEntry.Value <= LotCullThreshold)
                    continue;

                float floor = 0f;
                if (minPriceBySeller != null
                    && minPriceBySeller.TryGetValue(sellerEntry.Key, out float sellerFloor))
                {
                    floor = Math.Max(0f, sellerFloor);
                }

                if (floor < minFloor)
                    minFloor = floor;
            }

            return minFloor == float.MaxValue ? 0f : minFloor;
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
