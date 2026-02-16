using System;
using System.Collections.Generic;
using EconSim.Core.Data;
using EconSim.Core.Economy;

namespace EconSim.Core.Simulation.Systems
{
    /// <summary>
    /// Economy V2 buy-order posting system.
    /// Posts demand for next-day market clearing.
    /// </summary>
    public class OrderSystem : ITickSystem
    {
        private const float BuyerTransportFeeRate = 0.005f;

        private static readonly string[] BreadSubsistenceGoods = { "wheat", "rye", "barley", "rice_grain" };
        private readonly Dictionary<string, float> _demandByGoodBuffer = new Dictionary<string, float>();
        private readonly List<OrderLine> _tierLinesBuffer = new List<OrderLine>();
        private readonly Dictionary<string, int> _goodRuntimeIdCache = new Dictionary<string, int>();
        private int[] _breadSubsistenceRuntimeIds = Array.Empty<int>();
        private int _goatsRuntimeId = -1;

        private struct OrderLine
        {
            public string GoodId;
            public int GoodRuntimeId;
            public float Quantity;
            public float EffectivePrice;
            public float FullCost;

            public OrderLine(string goodId, int goodRuntimeId, float quantity, float effectivePrice, float fullCost)
            {
                GoodId = goodId;
                GoodRuntimeId = goodRuntimeId;
                Quantity = quantity;
                EffectivePrice = effectivePrice;
                FullCost = fullCost;
            }
        }

        public string Name => "Orders";
        public int TickInterval => SimulationConfig.Intervals.Daily;

        public void Initialize(SimulationState state, MapData mapData)
        {
            _goodRuntimeIdCache.Clear();
            _breadSubsistenceRuntimeIds = new int[BreadSubsistenceGoods.Length];
            for (int i = 0; i < BreadSubsistenceGoods.Length; i++)
            {
                _breadSubsistenceRuntimeIds[i] = ResolveRuntimeId(state?.Economy?.Goods, _goodRuntimeIdCache, BreadSubsistenceGoods[i]);
            }

            _goatsRuntimeId = ResolveRuntimeId(state?.Economy?.Goods, _goodRuntimeIdCache, "goats");
        }

        public void Tick(SimulationState state, MapData mapData)
        {
            var economy = state.Economy;
            if (economy == null)
                return;

            foreach (var county in economy.Counties.Values)
            {
                if (!economy.CountyToMarket.TryGetValue(county.CountyId, out int marketId))
                    continue;

                if (!economy.Markets.TryGetValue(marketId, out var market))
                    continue;

                float transportCost = ResolveTransportCost(mapData, market, county.CountyId);

                PostPopulationOrders(
                    state,
                    economy,
                    county,
                    market,
                    transportCost,
                    _demandByGoodBuffer,
                    _tierLinesBuffer,
                    _breadSubsistenceRuntimeIds,
                    _goatsRuntimeId);
                PostFacilityInputOrders(state, economy, county, market, transportCost, _goodRuntimeIdCache);
            }
        }

        private static void PostPopulationOrders(
            SimulationState state,
            EconomyState economy,
            CountyEconomy county,
            Market market,
            float transportCost,
            Dictionary<string, float> demandByGood,
            List<OrderLine> tierLinesBuffer,
            int[] breadSubsistenceRuntimeIds,
            int goatsRuntimeId)
        {
            int population = county.Population.Total;
            if (population <= 0)
                return;

            demandByGood.Clear();
            foreach (var good in economy.Goods.ConsumerGoods)
            {
                if (!good.NeedCategory.HasValue)
                    continue;

                float perCapita = good.BaseConsumption;
                if (perCapita <= 0f)
                    continue;

                demandByGood[good.Id] = perCapita * population;
            }

            ApplySubsistenceFromStockpile(county, demandByGood, breadSubsistenceRuntimeIds, goatsRuntimeId);

            float budget = Math.Max(0f, county.Population.Treasury);
            budget -= PostTierOrders(state, economy, county, market, transportCost, demandByGood, tierLinesBuffer, NeedCategory.Basic, budget);
            if (budget <= 0f) return;

            budget -= PostTierOrders(state, economy, county, market, transportCost, demandByGood, tierLinesBuffer, NeedCategory.Comfort, budget);
            if (budget <= 0f) return;

            PostTierOrders(state, economy, county, market, transportCost, demandByGood, tierLinesBuffer, NeedCategory.Luxury, budget);
        }

        private static float PostTierOrders(
            SimulationState state,
            EconomyState economy,
            CountyEconomy county,
            Market market,
            float transportCost,
            Dictionary<string, float> demandByGood,
            List<OrderLine> linesBuffer,
            NeedCategory tier,
            float availableBudget)
        {
            linesBuffer.Clear();
            float totalCost = 0f;

            foreach (var good in economy.Goods.ConsumerGoods)
            {
                if (!demandByGood.TryGetValue(good.Id, out float qty) || qty <= 0.0001f)
                    continue;

                if (good.NeedCategory != tier)
                    continue;

                if (!market.Goods.TryGetValue(good.Id, out var marketGood))
                    continue;

                float effectivePrice = marketGood.Price * (1f + Math.Max(0f, transportCost) * BuyerTransportFeeRate);
                if (effectivePrice <= 0f)
                    continue;

                float fullCost = qty * effectivePrice;
                linesBuffer.Add(new OrderLine(good.Id, good.RuntimeId, qty, effectivePrice, fullCost));
                totalCost += fullCost;
            }

            if (linesBuffer.Count == 0 || totalCost <= 0f || availableBudget <= 0f)
                return 0f;

            float budget = Math.Min(availableBudget, totalCost);
            float spent = 0f;
            int buyerId = MarketOrderIds.MakePopulationBuyerId(county.CountyId);

            foreach (var line in linesBuffer)
            {
                float qty;
                float maxSpend;

                if (budget >= totalCost)
                {
                    qty = line.Quantity;
                    maxSpend = line.FullCost;
                }
                else
                {
                    float budgetShare = budget * (line.FullCost / totalCost);
                    qty = line.EffectivePrice > 0f ? budgetShare / line.EffectivePrice : 0f;
                    maxSpend = budgetShare;
                }

                if (qty <= 0.0001f || maxSpend <= 0f)
                    continue;

                market.AddPendingBuyOrder(new BuyOrder
                {
                    BuyerId = buyerId,
                    GoodId = line.GoodId,
                    GoodRuntimeId = line.GoodRuntimeId,
                    Quantity = qty,
                    MaxSpend = maxSpend,
                    TransportCost = transportCost,
                    DayPosted = state.CurrentDay
                });

                spent += maxSpend;
            }

            return Math.Min(spent, budget);
        }

        private static void PostFacilityInputOrders(
            SimulationState state,
            EconomyState economy,
            CountyEconomy county,
            Market market,
            float transportCost,
            Dictionary<string, int> runtimeIdCache)
        {
            foreach (int facilityId in county.FacilityIds)
            {
                if (!economy.Facilities.TryGetValue(facilityId, out var facility) || !facility.IsActive)
                    continue;

                var def = economy.FacilityDefs.Get(facility.TypeId);
                if (def == null || def.IsExtraction)
                    continue;

                // Zero-staffed facilities cannot consume inputs this tick.
                if (facility.AssignedWorkers <= 0)
                    continue;

                float currentThroughput = facility.GetThroughput(def);
                if (currentThroughput <= 0f)
                    continue;

                var output = economy.Goods.Get(def.OutputGoodId);
                if (output == null)
                    continue;

                var inputs = def.InputOverrides ?? output.Inputs;
                if (inputs == null || inputs.Count == 0)
                    continue;

                float remainingTreasury = Math.Max(0f, facility.Treasury);
                if (remainingTreasury <= 0f)
                    continue;

                foreach (var input in inputs)
                {
                    if (!market.Goods.TryGetValue(input.GoodId, out var marketGood))
                        continue;

                    int inputRuntimeId = ResolveRuntimeId(economy.Goods, runtimeIdCache, input.GoodId);
                    float needed = input.Quantity * currentThroughput;
                    float have = inputRuntimeId >= 0
                        ? facility.InputBuffer.Get(inputRuntimeId)
                        : facility.InputBuffer.Get(input.GoodId);
                    float toBuy = Math.Max(0f, needed - have);
                    if (toBuy <= 0.0001f)
                        continue;

                    float effectivePrice = marketGood.Price * (1f + Math.Max(0f, transportCost) * BuyerTransportFeeRate);
                    if (effectivePrice <= 0f)
                        continue;

                    float maxSpend = Math.Min(remainingTreasury, toBuy * effectivePrice);
                    float quantity = maxSpend / effectivePrice;
                    if (quantity <= 0.0001f || maxSpend <= 0f)
                        continue;

                    market.AddPendingBuyOrder(new BuyOrder
                    {
                        BuyerId = facility.Id,
                        GoodId = input.GoodId,
                        GoodRuntimeId = inputRuntimeId >= 0 ? inputRuntimeId : (int?)null,
                        Quantity = quantity,
                        MaxSpend = maxSpend,
                        TransportCost = transportCost,
                        DayPosted = state.CurrentDay
                    });

                    remainingTreasury -= maxSpend;
                    if (remainingTreasury <= 0f)
                        break;
                }
            }
        }

        private static void ApplySubsistenceFromStockpile(
            CountyEconomy county,
            Dictionary<string, float> demandByGood,
            int[] breadSubsistenceRuntimeIds,
            int goatsRuntimeId)
        {
            if (demandByGood.TryGetValue("bread", out float breadNeed) && breadNeed > 0f)
            {
                float equivalent = 0f;
                for (int i = 0; i < BreadSubsistenceGoods.Length; i++)
                {
                    int runtimeId = (breadSubsistenceRuntimeIds != null && i < breadSubsistenceRuntimeIds.Length)
                        ? breadSubsistenceRuntimeIds[i]
                        : -1;
                    float available = runtimeId >= 0
                        ? county.Stockpile.Get(runtimeId)
                        : county.Stockpile.Get(BreadSubsistenceGoods[i]);
                    equivalent += available * 0.5f;
                }

                float covered = Math.Min(breadNeed, equivalent);
                if (covered > 0f)
                {
                    float requiredRaw = covered / 0.5f;
                    RemoveProportional(county.Stockpile, BreadSubsistenceGoods, breadSubsistenceRuntimeIds, requiredRaw);
                    demandByGood["bread"] = Math.Max(0f, breadNeed - covered);
                }
            }

            if (demandByGood.TryGetValue("cheese", out float cheeseNeed) && cheeseNeed > 0f)
            {
                float goats = goatsRuntimeId >= 0
                    ? county.Stockpile.Get(goatsRuntimeId)
                    : county.Stockpile.Get("goats");
                float equivalent = goats * 0.3f;
                float covered = Math.Min(cheeseNeed, equivalent);
                if (covered > 0f)
                {
                    float goatsToUse = covered / 0.3f;
                    if (goatsRuntimeId >= 0)
                        county.Stockpile.Remove(goatsRuntimeId, goatsToUse);
                    else
                        county.Stockpile.Remove("goats", goatsToUse);
                    demandByGood["cheese"] = Math.Max(0f, cheeseNeed - covered);
                }
            }
        }

        private static void RemoveProportional(
            Stockpile stockpile,
            string[] goods,
            int[] runtimeIds,
            float totalToRemove)
        {
            if (totalToRemove <= 0f)
                return;

            float totalAvailable = 0f;
            for (int i = 0; i < goods.Length; i++)
            {
                int runtimeId = (runtimeIds != null && i < runtimeIds.Length)
                    ? runtimeIds[i]
                    : -1;
                totalAvailable += runtimeId >= 0
                    ? stockpile.Get(runtimeId)
                    : stockpile.Get(goods[i]);
            }

            if (totalAvailable <= 0f)
                return;

            float remaining = Math.Min(totalToRemove, totalAvailable);
            for (int i = 0; i < goods.Length; i++)
            {
                string goodId = goods[i];
                int runtimeId = (runtimeIds != null && i < runtimeIds.Length)
                    ? runtimeIds[i]
                    : -1;
                float available = runtimeId >= 0
                    ? stockpile.Get(runtimeId)
                    : stockpile.Get(goodId);
                if (available <= 0f)
                    continue;

                float share = available / totalAvailable;
                float remove = remaining * share;
                if (runtimeId >= 0)
                    stockpile.Remove(runtimeId, remove);
                else
                    stockpile.Remove(goodId, remove);
            }
        }

        private static float ResolveTransportCost(MapData mapData, Market market, int countyId)
        {
            if (mapData?.CountyById == null || !mapData.CountyById.TryGetValue(countyId, out var county))
                return 0f;

            if (market.ZoneCellCosts != null && market.ZoneCellCosts.TryGetValue(county.SeatCellId, out float cost))
                return Math.Max(0f, cost);

            return 0f;
        }

        private static int ResolveRuntimeId(
            GoodRegistry goods,
            Dictionary<string, int> runtimeIdCache,
            string goodId)
        {
            if (string.IsNullOrWhiteSpace(goodId))
                return -1;

            if (runtimeIdCache != null && runtimeIdCache.TryGetValue(goodId, out int cached))
                return cached;

            int runtimeId = goods != null && goods.TryGetRuntimeId(goodId, out int resolved)
                ? resolved
                : -1;
            if (runtimeIdCache != null)
                runtimeIdCache[goodId] = runtimeId;
            return runtimeId;
        }
    }
}
