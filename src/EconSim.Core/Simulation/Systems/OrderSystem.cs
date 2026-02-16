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

        private static readonly Dictionary<string, float> V2BaseConsumption = new Dictionary<string, float>
        {
            ["bread"] = 0.5f,
            ["cheese"] = 0.1f,
            ["clothes"] = 0.003f,
            ["shoes"] = 0.003f,
            ["tools"] = 0.003f,
            ["cookware"] = 0.003f,
            ["furniture"] = 0.001f,
            ["jewelry"] = 0.0003f,
            ["spices"] = 0.01f,
            ["sugar"] = 0.01f
        };

        private static readonly string[] BreadSubsistenceGoods = { "wheat", "rye", "barley", "rice_grain" };

        public string Name => "Orders";
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

            foreach (var county in economy.Counties.Values)
            {
                if (!economy.CountyToMarket.TryGetValue(county.CountyId, out int marketId))
                    continue;

                if (!economy.Markets.TryGetValue(marketId, out var market))
                    continue;

                float transportCost = ResolveTransportCost(economy, mapData, market, county.CountyId);

                PostPopulationOrders(state, economy, county, market, transportCost);
                PostFacilityInputOrders(state, economy, county, market, transportCost);
            }
        }

        private static void PostPopulationOrders(
            SimulationState state,
            EconomyState economy,
            CountyEconomy county,
            Market market,
            float transportCost)
        {
            int population = county.Population.Total;
            if (population <= 0)
                return;

            var demandByGood = new Dictionary<string, float>();
            foreach (var good in economy.Goods.ConsumerGoods)
            {
                NeedCategory category = GetNeedCategory(good);
                if (category != NeedCategory.Basic && category != NeedCategory.Comfort && category != NeedCategory.Luxury)
                    continue;

                float perCapita = GetBaseConsumption(good);
                if (perCapita <= 0f)
                    continue;

                demandByGood[good.Id] = perCapita * population;
            }

            ApplySubsistenceFromStockpile(county, demandByGood);

            float budget = Math.Max(0f, county.Population.Treasury);
            budget -= PostTierOrders(state, economy, county, market, transportCost, demandByGood, NeedCategory.Basic, budget);
            if (budget <= 0f) return;

            budget -= PostTierOrders(state, economy, county, market, transportCost, demandByGood, NeedCategory.Comfort, budget);
            if (budget <= 0f) return;

            PostTierOrders(state, economy, county, market, transportCost, demandByGood, NeedCategory.Luxury, budget);
        }

        private static float PostTierOrders(
            SimulationState state,
            EconomyState economy,
            CountyEconomy county,
            Market market,
            float transportCost,
            Dictionary<string, float> demandByGood,
            NeedCategory tier,
            float availableBudget)
        {
            var lines = new List<(string goodId, float qty, float effectivePrice, float fullCost)>();
            float totalCost = 0f;

            foreach (var good in economy.Goods.ConsumerGoods)
            {
                if (!demandByGood.TryGetValue(good.Id, out float qty) || qty <= 0.0001f)
                    continue;

                if (GetNeedCategory(good) != tier)
                    continue;

                if (!market.Goods.TryGetValue(good.Id, out var marketGood))
                    continue;

                float effectivePrice = marketGood.Price * (1f + Math.Max(0f, transportCost) * BuyerTransportFeeRate);
                if (effectivePrice <= 0f)
                    continue;

                float fullCost = qty * effectivePrice;
                lines.Add((good.Id, qty, effectivePrice, fullCost));
                totalCost += fullCost;
            }

            if (lines.Count == 0 || totalCost <= 0f || availableBudget <= 0f)
                return 0f;

            float budget = Math.Min(availableBudget, totalCost);
            float spent = 0f;
            int buyerId = MarketOrderIds.MakePopulationBuyerId(county.CountyId);

            foreach (var line in lines)
            {
                float qty;
                float maxSpend;

                if (budget >= totalCost)
                {
                    qty = line.qty;
                    maxSpend = line.fullCost;
                }
                else
                {
                    float budgetShare = budget * (line.fullCost / totalCost);
                    qty = line.effectivePrice > 0f ? budgetShare / line.effectivePrice : 0f;
                    maxSpend = budgetShare;
                }

                if (qty <= 0.0001f || maxSpend <= 0f)
                    continue;

                market.PendingBuyOrders.Add(new BuyOrder
                {
                    BuyerId = buyerId,
                    GoodId = line.goodId,
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
            float transportCost)
        {
            foreach (int facilityId in county.FacilityIds)
            {
                if (!economy.Facilities.TryGetValue(facilityId, out var facility) || !facility.IsActive)
                    continue;

                var def = economy.FacilityDefs.Get(facility.TypeId);
                if (def == null || def.IsExtraction)
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

                    float needed = input.Quantity * def.BaseThroughput;
                    float have = facility.InputBuffer.Get(input.GoodId);
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

                    market.PendingBuyOrders.Add(new BuyOrder
                    {
                        BuyerId = facility.Id,
                        GoodId = input.GoodId,
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

        private static void ApplySubsistenceFromStockpile(CountyEconomy county, Dictionary<string, float> demandByGood)
        {
            if (demandByGood.TryGetValue("bread", out float breadNeed) && breadNeed > 0f)
            {
                float equivalent = 0f;
                for (int i = 0; i < BreadSubsistenceGoods.Length; i++)
                {
                    equivalent += county.Stockpile.Get(BreadSubsistenceGoods[i]) * 0.5f;
                }

                float covered = Math.Min(breadNeed, equivalent);
                if (covered > 0f)
                {
                    float requiredRaw = covered / 0.5f;
                    RemoveProportional(county.Stockpile, BreadSubsistenceGoods, requiredRaw);
                    demandByGood["bread"] = Math.Max(0f, breadNeed - covered);
                }
            }

            if (demandByGood.TryGetValue("cheese", out float cheeseNeed) && cheeseNeed > 0f)
            {
                float goats = county.Stockpile.Get("goats");
                float equivalent = goats * 0.3f;
                float covered = Math.Min(cheeseNeed, equivalent);
                if (covered > 0f)
                {
                    float goatsToUse = covered / 0.3f;
                    county.Stockpile.Remove("goats", goatsToUse);
                    demandByGood["cheese"] = Math.Max(0f, cheeseNeed - covered);
                }
            }
        }

        private static void RemoveProportional(Stockpile stockpile, string[] goods, float totalToRemove)
        {
            if (totalToRemove <= 0f)
                return;

            float totalAvailable = 0f;
            for (int i = 0; i < goods.Length; i++)
            {
                totalAvailable += stockpile.Get(goods[i]);
            }

            if (totalAvailable <= 0f)
                return;

            float remaining = Math.Min(totalToRemove, totalAvailable);
            for (int i = 0; i < goods.Length; i++)
            {
                string goodId = goods[i];
                float available = stockpile.Get(goodId);
                if (available <= 0f)
                    continue;

                float share = available / totalAvailable;
                float remove = remaining * share;
                stockpile.Remove(goodId, remove);
            }
        }

        private static float ResolveTransportCost(EconomyState economy, MapData mapData, Market market, int countyId)
        {
            if (mapData?.CountyById == null || !mapData.CountyById.TryGetValue(countyId, out var county))
                return 0f;

            if (market.ZoneCellCosts != null && market.ZoneCellCosts.TryGetValue(county.SeatCellId, out float cost))
                return Math.Max(0f, cost);

            return 0f;
        }

        private static NeedCategory GetNeedCategory(GoodDef good)
        {
            if (good.Id == "cheese")
                return NeedCategory.Basic;
            if (good.Id == "clothes")
                return NeedCategory.Comfort;

            return good.NeedCategory ?? NeedCategory.Luxury;
        }

        private static float GetBaseConsumption(GoodDef good)
        {
            if (V2BaseConsumption.TryGetValue(good.Id, out float overrideRate))
                return overrideRate;

            return good.BaseConsumption;
        }
    }
}
