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
        private const float InterMarketTransferCost = 120f;
        private const float MillInputOrderHorizonDays = 0.25f;
        private const float ProcessingInputDemandBufferFactor = 1.10f;
        private const float BreweryTradeEmaDays = 14f;
        private const float BreweryDemandEmaDays = 7f;
        private const float BreweryTradeWeight = 0.70f;
        private const float BreweryDemandWeight = 0.30f;
        private const float BreweryStructuralDemandWeight = 0.50f;
        private const float BreweryObservedDemandWeight = 0.50f;
        private const float BreweryDemandForecastBufferFactor = 1.10f;

        private static readonly string[] BreadSubsistenceGoods = { "wheat", "rye", "rice_grain" };
        private static readonly string[] BeerSubsistenceGoods = { "barley" };
        private readonly Dictionary<int, float> _demandByGoodBuffer = new Dictionary<int, float>();
        private readonly List<OrderLine> _tierLinesBuffer = new List<OrderLine>();
        private readonly Dictionary<string, int> _goodRuntimeIdCache = new Dictionary<string, int>();
        private readonly Dictionary<long, float> _countyMarketTransportCostCache = new Dictionary<long, float>();
        private readonly Dictionary<long, BreweryForecastState> _breweryForecastByMarketGood = new Dictionary<long, BreweryForecastState>();
        private readonly Dictionary<int, int> _marketPopulationById = new Dictionary<int, int>();
        private int[] _breadSubsistenceRuntimeIds = Array.Empty<int>();
        private int[] _beerSubsistenceRuntimeIds = Array.Empty<int>();
        private int _breadRuntimeId = -1;
        private int _beerRuntimeId = -1;

        private struct BreweryForecastState
        {
            public float TradeEma;
            public float DemandEma;
            public int LastUpdatedDay;
        }

        private struct OrderLine
        {
            public string GoodId;
            public int GoodRuntimeId;
            public Market TargetMarket;
            public float Quantity;
            public float TransportCost;
            public float EffectivePrice;
            public float FullCost;

            public OrderLine(
                string goodId,
                int goodRuntimeId,
                Market targetMarket,
                float quantity,
                float transportCost,
                float effectivePrice,
                float fullCost)
            {
                GoodId = goodId;
                GoodRuntimeId = goodRuntimeId;
                TargetMarket = targetMarket;
                Quantity = quantity;
                TransportCost = transportCost;
                EffectivePrice = effectivePrice;
                FullCost = fullCost;
            }
        }

        public string Name => "Orders";
        public int TickInterval => SimulationConfig.Intervals.Daily;

        public void Initialize(SimulationState state, MapData mapData)
        {
            _goodRuntimeIdCache.Clear();
            _breweryForecastByMarketGood.Clear();
            _breadSubsistenceRuntimeIds = new int[BreadSubsistenceGoods.Length];
            for (int i = 0; i < BreadSubsistenceGoods.Length; i++)
            {
                if (!SimulationConfig.Economy.IsGoodEnabled(BreadSubsistenceGoods[i]))
                {
                    _breadSubsistenceRuntimeIds[i] = -1;
                    continue;
                }

                _breadSubsistenceRuntimeIds[i] = ResolveRuntimeId(state?.Economy?.Goods, _goodRuntimeIdCache, BreadSubsistenceGoods[i]);
            }

            _beerSubsistenceRuntimeIds = new int[BeerSubsistenceGoods.Length];
            for (int i = 0; i < BeerSubsistenceGoods.Length; i++)
            {
                if (!SimulationConfig.Economy.IsGoodEnabled(BeerSubsistenceGoods[i]))
                {
                    _beerSubsistenceRuntimeIds[i] = -1;
                    continue;
                }

                _beerSubsistenceRuntimeIds[i] = ResolveRuntimeId(state?.Economy?.Goods, _goodRuntimeIdCache, BeerSubsistenceGoods[i]);
            }

            // Apply subsistence cover to staple flour demand (not comfort bread demand).
            _breadRuntimeId = SimulationConfig.Economy.IsGoodEnabled("flour")
                ? ResolveRuntimeId(state?.Economy?.Goods, _goodRuntimeIdCache, "flour")
                : -1;
            _beerRuntimeId = SimulationConfig.Economy.IsGoodEnabled("beer")
                ? ResolveRuntimeId(state?.Economy?.Goods, _goodRuntimeIdCache, "beer")
                : -1;
        }

        public void Tick(SimulationState state, MapData mapData)
        {
            var economy = state.Economy;
            if (economy == null)
                return;

            _countyMarketTransportCostCache.Clear();
            BuildMarketPopulationIndex(economy);
            foreach (var county in economy.Counties.Values)
            {
                if (!economy.CountyToMarket.TryGetValue(county.CountyId, out int marketId))
                    continue;

                if (!economy.Markets.TryGetValue(marketId, out var market))
                    continue;

                float transportCost = economy.GetCountyTransportCost(county.CountyId);

                PostPopulationOrders(
                    state,
                    economy,
                    county,
                    market,
                    transportCost,
                    _demandByGoodBuffer,
                    _tierLinesBuffer,
                    _breadSubsistenceRuntimeIds,
                    _breadRuntimeId,
                    _beerSubsistenceRuntimeIds,
                    _beerRuntimeId,
                    economy.Goods,
                    _countyMarketTransportCostCache);
                PostFacilityInputOrders(
                    state,
                    economy,
                    county,
                    market,
                    transportCost,
                    _goodRuntimeIdCache,
                    _countyMarketTransportCostCache,
                    _breweryForecastByMarketGood,
                    _marketPopulationById);
            }
        }

        private static void PostPopulationOrders(
            SimulationState state,
            EconomyState economy,
            CountyEconomy county,
            Market market,
            float transportCost,
            Dictionary<int, float> demandByGood,
            List<OrderLine> tierLinesBuffer,
            int[] breadSubsistenceRuntimeIds,
            int breadRuntimeId,
            int[] beerSubsistenceRuntimeIds,
            int beerRuntimeId,
            GoodRegistry goods,
            Dictionary<long, float> countyMarketTransportCostCache)
        {
            int population = county.Population.Total;
            if (population <= 0)
                return;

            demandByGood.Clear();
            foreach (var good in economy.Goods.ConsumerGoods)
            {
                if (!good.NeedCategory.HasValue)
                    continue;
                if (!SimulationConfig.Economy.IsGoodEnabled(good.Id))
                    continue;

                float perCapita = good.BaseConsumptionKgPerCapitaPerDay;
                if (perCapita <= 0f)
                    continue;

                if (good.RuntimeId < 0)
                    continue;

                demandByGood[good.RuntimeId] = perCapita * population;
            }

            ApplySubsistenceFromStockpile(
                county,
                demandByGood,
                breadSubsistenceRuntimeIds,
                breadRuntimeId,
                beerSubsistenceRuntimeIds,
                beerRuntimeId,
                goods);

            float budget = Math.Max(0f, county.Population.Treasury);
            budget -= PostTierOrders(state, economy, county, market, transportCost, demandByGood, tierLinesBuffer, NeedCategory.Basic, budget, countyMarketTransportCostCache);
            if (budget <= 0f) return;

            budget -= PostTierOrders(state, economy, county, market, transportCost, demandByGood, tierLinesBuffer, NeedCategory.Comfort, budget, countyMarketTransportCostCache);
            if (budget <= 0f) return;

            PostTierOrders(state, economy, county, market, transportCost, demandByGood, tierLinesBuffer, NeedCategory.Luxury, budget, countyMarketTransportCostCache);
        }

        private static float PostTierOrders(
            SimulationState state,
            EconomyState economy,
            CountyEconomy county,
            Market market,
            float transportCost,
            Dictionary<int, float> demandByGood,
            List<OrderLine> linesBuffer,
            NeedCategory tier,
            float availableBudget,
            Dictionary<long, float> countyMarketTransportCostCache)
        {
            linesBuffer.Clear();
            float totalCost = 0f;

            foreach (var good in economy.Goods.ConsumerGoods)
            {
                if (good.RuntimeId < 0)
                    continue;

                if (!demandByGood.TryGetValue(good.RuntimeId, out float qty) || qty <= 0.0001f)
                    continue;
                float requestedQty = qty;

                if (good.NeedCategory != tier)
                    continue;
                if (!SimulationConfig.Economy.IsGoodEnabled(good.Id))
                    continue;

                if (!TryResolveBestMarketForGood(
                    state,
                    economy,
                    county.CountyId,
                    market,
                    transportCost,
                    good.Id,
                    good.RuntimeId,
                    countyMarketTransportCostCache,
                    out var targetMarket,
                    out float targetTransportCost,
                    out var marketGood))
                {
                    AccumulateUnpostedDemandDiagnostic(market, good.RuntimeId, noRouteQty: requestedQty, priceRejectQty: 0f);
                    continue;
                }

                float effectivePrice = marketGood.Price + Math.Max(0f, targetTransportCost) * SimulationConfig.Economy.FlatHaulingFeePerKgPerTransportCostUnit;
                if (effectivePrice <= 0f)
                {
                    AccumulateUnpostedDemandDiagnostic(market, good.RuntimeId, noRouteQty: 0f, priceRejectQty: requestedQty);
                    continue;
                }

                if (qty <= 0.0001f)
                    continue;

                float fullCost = qty * effectivePrice;
                linesBuffer.Add(new OrderLine(
                    good.Id,
                    good.RuntimeId,
                    targetMarket,
                    qty,
                    targetTransportCost,
                    effectivePrice,
                    fullCost));
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

                line.TargetMarket.AddPendingBuyOrder(new BuyOrder
                {
                    BuyerId = buyerId,
                    GoodId = line.GoodId,
                    GoodRuntimeId = line.GoodRuntimeId,
                    Quantity = qty,
                    MaxSpend = maxSpend,
                    TransportCost = line.TransportCost,
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
            Dictionary<string, int> runtimeIdCache,
            Dictionary<long, float> countyMarketTransportCostCache,
            Dictionary<long, BreweryForecastState> breweryForecastByMarketGood,
            Dictionary<int, int> marketPopulationById)
        {
            foreach (int facilityId in county.FacilityIds)
            {
                if (!economy.Facilities.TryGetValue(facilityId, out var facility) || !facility.IsActive)
                    continue;

                var def = economy.FacilityDefs.Get(facility.TypeId);
                if (def == null || def.IsExtraction)
                    continue;
                if (!SimulationConfig.Economy.IsFacilityEnabled(facility.TypeId)
                    || !SimulationConfig.Economy.IsGoodEnabled(def.OutputGoodId))
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

                if (ShouldDemandLimitInputOrders(def)
                    && output.RuntimeId >= 0
                    && market.TryGetGoodState(output.RuntimeId, out var outputState))
                {
                    float demandLimitedThroughput;
                    if (def.Id == "brewery")
                    {
                        float projectedDemand = GetBreweryProjectedDemand(
                            state.CurrentDay,
                            market.Id,
                            marketPopulationById,
                            output,
                            output.RuntimeId,
                            outputState,
                            breweryForecastByMarketGood);
                        demandLimitedThroughput = projectedDemand * BreweryDemandForecastBufferFactor;
                    }
                    else
                    {
                        float unmetDemand = Math.Max(0f, outputState.Demand - outputState.Supply);
                        demandLimitedThroughput = unmetDemand * ProcessingInputDemandBufferFactor;
                    }

                    currentThroughput = Math.Min(currentThroughput, demandLimitedThroughput);
                }

                if (currentThroughput <= 0.001f)
                    continue;

                var selectedInputs = SelectBestInputVariantForOrders(
                    state,
                    economy,
                    county.CountyId,
                    market,
                    transportCost,
                    def,
                    output,
                    countyMarketTransportCostCache);
                if (selectedInputs == null || selectedInputs.Count == 0)
                    continue;

                float remainingTreasury = Math.Max(0f, facility.Treasury);
                if (remainingTreasury <= 0f)
                    continue;

                foreach (var input in selectedInputs)
                {
                    int inputRuntimeId = ResolveRuntimeId(economy.Goods, runtimeIdCache, input.GoodId);
                    if (inputRuntimeId < 0)
                        continue;
                    if (!SimulationConfig.Economy.IsGoodEnabled(input.GoodId))
                        continue;

                    if (!TryResolveBestMarketForGood(
                        state,
                        economy,
                        county.CountyId,
                        market,
                        transportCost,
                        input.GoodId,
                        inputRuntimeId,
                        countyMarketTransportCostCache,
                        out var targetMarket,
                        out float targetTransportCost,
                        out var marketGood))
                        continue;

                    float needed = input.QuantityKg * currentThroughput;
                    if (IsMillFacility(def))
                        needed *= MillInputOrderHorizonDays;

                    float have = facility.InputBuffer.Get(inputRuntimeId);
                    if (IsMillFacility(def) && IsReserveGrainGood(input.GoodId))
                        have += county.Stockpile.Get(inputRuntimeId);
                    float toBuy = Math.Max(0f, needed - have);
                    if (toBuy <= 0.0001f)
                        continue;

                    float effectivePrice = marketGood.Price + Math.Max(0f, targetTransportCost) * SimulationConfig.Economy.FlatHaulingFeePerKgPerTransportCostUnit;
                    if (effectivePrice <= 0f)
                        continue;

                    float maxSpend = Math.Min(remainingTreasury, toBuy * effectivePrice);
                    float quantity = maxSpend / effectivePrice;
                    if (quantity <= 0.0001f || maxSpend <= 0f)
                        continue;

                    targetMarket.AddPendingBuyOrder(new BuyOrder
                    {
                        BuyerId = facility.Id,
                        GoodId = input.GoodId,
                        GoodRuntimeId = inputRuntimeId,
                        Quantity = quantity,
                        MaxSpend = maxSpend,
                        TransportCost = targetTransportCost,
                        DayPosted = state.CurrentDay
                    });

                    remainingTreasury -= maxSpend;
                    if (remainingTreasury <= 0f)
                        break;
                }
            }
        }

        private static List<GoodInput> SelectBestInputVariantForOrders(
            SimulationState state,
            EconomyState economy,
            int countyId,
            Market localMarket,
            float localTransportCost,
            FacilityDef def,
            GoodDef output,
            Dictionary<long, float> countyMarketTransportCostCache)
        {
            List<GoodInput> bestInputs = null;
            float bestCost = float.MaxValue;
            List<GoodInput> firstValidMillInputs = null;
            bool millPreference = IsMillFacility(def);

            foreach (var inputs in EnumerateInputVariants(def, output))
            {
                if (inputs == null || inputs.Count == 0)
                    continue;

                float variantCost = 0f;
                bool valid = true;
                bool hasOfferedSupply = true;
                for (int i = 0; i < inputs.Count; i++)
                {
                    var input = inputs[i];
                    if (input.QuantityKg <= 0f)
                    {
                        valid = false;
                        break;
                    }

                    if (!economy.Goods.TryGetRuntimeId(input.GoodId, out int inputRuntimeId))
                    {
                        valid = false;
                        break;
                    }
                    if (!TryResolveBestMarketForGood(
                        state,
                        economy,
                        countyId,
                        localMarket,
                        localTransportCost,
                        input.GoodId,
                        inputRuntimeId,
                        countyMarketTransportCostCache,
                        out _,
                        out float targetTransportCost,
                        out var marketGood))
                    {
                        valid = false;
                        break;
                    }

                    if (marketGood.Supply <= 0.001f && marketGood.SupplyOffered <= 0.001f)
                        hasOfferedSupply = false;

                    float effectivePrice = marketGood.Price + Math.Max(0f, targetTransportCost) * SimulationConfig.Economy.FlatHaulingFeePerKgPerTransportCostUnit;
                    if (effectivePrice <= 0f)
                    {
                        valid = false;
                        break;
                    }

                    variantCost += input.QuantityKg * effectivePrice;
                }

                if (millPreference)
                {
                    if (!valid)
                        continue;

                    if (firstValidMillInputs == null)
                        firstValidMillInputs = inputs;

                    if (hasOfferedSupply)
                        return inputs;

                    continue;
                }

                if (!valid || variantCost >= bestCost)
                    continue;

                bestCost = variantCost;
                bestInputs = inputs;
            }

            if (millPreference)
                return firstValidMillInputs;

            return bestInputs;
        }

        private static IEnumerable<List<GoodInput>> EnumerateInputVariants(FacilityDef def, GoodDef output)
        {
            if (def?.InputOverrides != null && def.InputOverrides.Count > 0)
            {
                yield return def.InputOverrides;
                yield break;
            }

            bool yieldedVariant = false;
            if (output?.InputVariants != null)
            {
                for (int i = 0; i < output.InputVariants.Count; i++)
                {
                    var variant = output.InputVariants[i];
                    if (variant?.Inputs == null || variant.Inputs.Count == 0)
                        continue;

                    yieldedVariant = true;
                    yield return variant.Inputs;
                }
            }

            if (!yieldedVariant && output?.Inputs != null && output.Inputs.Count > 0)
                yield return output.Inputs;
        }

        private static void ApplySubsistenceFromStockpile(
            CountyEconomy county,
            Dictionary<int, float> demandByGood,
            int[] breadSubsistenceRuntimeIds,
            int breadRuntimeId,
            int[] beerSubsistenceRuntimeIds,
            int beerRuntimeId,
            GoodRegistry goods)
        {
            if (breadRuntimeId >= 0 && demandByGood.TryGetValue(breadRuntimeId, out float breadNeed) && breadNeed > 0f)
            {
                float equivalent = 0f;
                float flourPerRawKg = SimulationConfig.Economy.FlourKgPerRawGrainKg;
                if (flourPerRawKg <= 0f)
                    return;
                int count = breadSubsistenceRuntimeIds?.Length ?? 0;
                for (int i = 0; i < count; i++)
                {
                    int runtimeId = breadSubsistenceRuntimeIds[i];
                    if (runtimeId < 0)
                        continue;

                    float available = county.Stockpile.Get(runtimeId);
                    equivalent += available * flourPerRawKg;
                }

                float subsistenceShare = Clamp(SimulationConfig.Economy.BreadSubsistenceShare, 0f, 1f);
                float subsistenceCap = breadNeed * subsistenceShare;
                float covered = Math.Min(subsistenceCap, equivalent);
                if (covered > 0f)
                {
                    float requiredRaw = covered / flourPerRawKg;
                    RemovePrioritized(county.Stockpile, breadSubsistenceRuntimeIds, requiredRaw);
                    demandByGood[breadRuntimeId] = Math.Max(0f, breadNeed - covered);
                }
            }

            if (beerRuntimeId >= 0 && demandByGood.TryGetValue(beerRuntimeId, out float beerNeed) && beerNeed > 0f)
            {
                float equivalentBeer = 0f;
                float beerPerRawKg = SimulationConfig.Economy.BeerKgPerRawBarleyKg;
                if (beerPerRawKg <= 0f)
                    return;

                int count = beerSubsistenceRuntimeIds?.Length ?? 0;
                for (int i = 0; i < count; i++)
                {
                    int runtimeId = beerSubsistenceRuntimeIds[i];
                    if (runtimeId < 0)
                        continue;

                    float available = county.Stockpile.Get(runtimeId);
                    equivalentBeer += available * beerPerRawKg;
                }

                float subsistenceShare = Clamp(SimulationConfig.Economy.BeerSubsistenceShare, 0f, 1f);
                float subsistenceCap = beerNeed * subsistenceShare;
                float covered = Math.Min(subsistenceCap, equivalentBeer);
                if (covered > 0f)
                {
                    float requiredRaw = covered / beerPerRawKg;
                    RemoveProportional(county.Stockpile, beerSubsistenceRuntimeIds, requiredRaw);
                    demandByGood[beerRuntimeId] = Math.Max(0f, beerNeed - covered);
                }
            }

            ConsumeStockpiledBasicGoods(county, goods, demandByGood);

        }

        private static void ConsumeStockpiledBasicGoods(
            CountyEconomy county,
            GoodRegistry goods,
            Dictionary<int, float> demandByGood)
        {
            if (county?.Stockpile == null || goods == null || demandByGood == null || demandByGood.Count == 0)
                return;

            var runtimeIds = new List<int>(demandByGood.Keys);
            for (int i = 0; i < runtimeIds.Count; i++)
            {
                int runtimeId = runtimeIds[i];
                if (runtimeId < 0)
                    continue;
                if (!demandByGood.TryGetValue(runtimeId, out float need) || need <= 0f)
                    continue;
                if (!goods.TryGetByRuntimeId(runtimeId, out var good) || good == null)
                    continue;
                if (good.NeedCategory != NeedCategory.Basic)
                    continue;

                float available = county.Stockpile.Get(runtimeId);
                if (available <= 0f)
                    continue;

                float covered = Math.Min(need, available);
                if (covered <= 0f)
                    continue;

                county.Stockpile.Remove(runtimeId, covered);
                demandByGood[runtimeId] = Math.Max(0f, need - covered);
            }
        }

        private static void RemoveProportional(
            Stockpile stockpile,
            int[] runtimeIds,
            float totalToRemove)
        {
            if (totalToRemove <= 0f)
                return;

            float totalAvailable = 0f;
            if (runtimeIds == null || runtimeIds.Length == 0)
                return;

            for (int i = 0; i < runtimeIds.Length; i++)
            {
                int runtimeId = runtimeIds[i];
                if (runtimeId < 0)
                    continue;

                totalAvailable += stockpile.Get(runtimeId);
            }

            if (totalAvailable <= 0f)
                return;

            float remaining = Math.Min(totalToRemove, totalAvailable);
            for (int i = 0; i < runtimeIds.Length; i++)
            {
                int runtimeId = runtimeIds[i];
                if (runtimeId < 0)
                    continue;

                float available = stockpile.Get(runtimeId);
                if (available <= 0f)
                    continue;

                float share = available / totalAvailable;
                float remove = remaining * share;
                stockpile.Remove(runtimeId, remove);
            }
        }

        private static void RemovePrioritized(
            Stockpile stockpile,
            int[] runtimeIds,
            float totalToRemove)
        {
            if (stockpile == null || runtimeIds == null || runtimeIds.Length == 0 || totalToRemove <= 0f)
                return;

            float remaining = totalToRemove;
            for (int i = 0; i < runtimeIds.Length && remaining > 0f; i++)
            {
                int runtimeId = runtimeIds[i];
                if (runtimeId < 0)
                    continue;

                float available = stockpile.Get(runtimeId);
                if (available <= 0f)
                    continue;

                float take = Math.Min(available, remaining);
                stockpile.Remove(runtimeId, take);
                remaining -= take;
            }
        }

        private static void AccumulateUnpostedDemandDiagnostic(
            Market market,
            int goodRuntimeId,
            float noRouteQty,
            float priceRejectQty)
        {
            if (market == null || goodRuntimeId < 0)
                return;
            if (!market.TryGetGoodState(goodRuntimeId, out var goodState) || goodState == null)
                return;

            if (noRouteQty > 0f)
                goodState.UnfilledNoRoute += noRouteQty;
            if (priceRejectQty > 0f)
                goodState.UnfilledPriceReject += priceRejectQty;
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

        private void BuildMarketPopulationIndex(EconomyState economy)
        {
            _marketPopulationById.Clear();
            if (economy?.Counties == null || economy.CountyToMarket == null)
                return;

            foreach (var county in economy.Counties.Values)
            {
                if (county == null || county.Population == null)
                    continue;

                if (!economy.CountyToMarket.TryGetValue(county.CountyId, out int marketId))
                    continue;

                int pop = Math.Max(0, county.Population.Total);
                if (pop <= 0)
                    continue;

                if (_marketPopulationById.TryGetValue(marketId, out int existing))
                    _marketPopulationById[marketId] = existing + pop;
                else
                    _marketPopulationById[marketId] = pop;
            }
        }

        private static float GetBreweryProjectedDemand(
            int currentDay,
            int marketId,
            Dictionary<int, int> marketPopulationById,
            GoodDef outputDef,
            int outputRuntimeId,
            MarketGoodState outputState,
            Dictionary<long, BreweryForecastState> breweryForecastByMarketGood)
        {
            if (outputState == null)
                return 0f;

            float observedTrade = Math.Max(0f, outputState.LastTradeVolume);
            float observedDemand = Math.Max(0f, outputState.Demand);
            if (breweryForecastByMarketGood == null)
                return observedTrade * BreweryTradeWeight + observedDemand * BreweryDemandWeight;

            long key = MakeMarketGoodKey(marketId, outputRuntimeId);
            if (!breweryForecastByMarketGood.TryGetValue(key, out var forecastState))
            {
                forecastState = new BreweryForecastState
                {
                    TradeEma = observedTrade,
                    DemandEma = observedDemand,
                    LastUpdatedDay = currentDay
                };
            }
            else if (forecastState.LastUpdatedDay != currentDay)
            {
                float tradeAlpha = ComputeEmaAlpha(BreweryTradeEmaDays);
                float demandAlpha = ComputeEmaAlpha(BreweryDemandEmaDays);
                forecastState.TradeEma += (observedTrade - forecastState.TradeEma) * tradeAlpha;
                forecastState.DemandEma += (observedDemand - forecastState.DemandEma) * demandAlpha;
                forecastState.LastUpdatedDay = currentDay;
            }

            breweryForecastByMarketGood[key] = forecastState;

            float observedProjectedDemand = forecastState.TradeEma * BreweryTradeWeight
                + forecastState.DemandEma * BreweryDemandWeight;
            if (float.IsNaN(observedProjectedDemand) || float.IsInfinity(observedProjectedDemand))
                return 0f;

            int marketPopulation = 0;
            if (marketPopulationById != null)
                marketPopulationById.TryGetValue(marketId, out marketPopulation);

            float baseBeerDemandPerCapita = Math.Max(0f, outputDef?.BaseConsumptionKgPerCapitaPerDay ?? 0f);
            float homeBrewShare = Clamp(SimulationConfig.Economy.BeerSubsistenceShare, 0f, 1f);
            float structuralMarketDemand = marketPopulation * baseBeerDemandPerCapita * (1f - homeBrewShare);

            float projectedDemand = structuralMarketDemand * BreweryStructuralDemandWeight
                + observedProjectedDemand * BreweryObservedDemandWeight;
            if (float.IsNaN(projectedDemand) || float.IsInfinity(projectedDemand))
                return 0f;

            return Math.Max(0f, projectedDemand);
        }

        private static long MakeMarketGoodKey(int marketId, int runtimeId)
        {
            return ((long)marketId << 32) ^ (uint)runtimeId;
        }

        private static float ComputeEmaAlpha(float windowDays)
        {
            if (windowDays <= 0f)
                return 1f;

            return Clamp(2f / (windowDays + 1f), 0f, 1f);
        }

        private static bool IsMillFacility(FacilityDef def)
        {
            if (def == null)
                return false;

            return def.Id == "mill";
        }

        private static bool ShouldDemandLimitInputOrders(FacilityDef def)
        {
            if (def == null)
                return false;

            if (IsMillFacility(def))
                return true;

            return def.Id == "bakery" || def.Id == "brewery" || def.Id == "malt_house";
        }

        private static bool IsReserveGrainGood(string goodId)
        {
            if (string.IsNullOrWhiteSpace(goodId))
                return false;

            for (int i = 0; i < BreadSubsistenceGoods.Length; i++)
            {
                if (string.Equals(BreadSubsistenceGoods[i], goodId, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static bool TryResolveBestMarketForGood(
            SimulationState state,
            EconomyState economy,
            int countyId,
            Market localMarket,
            float localTransportCost,
            string goodId,
            int goodRuntimeId,
            Dictionary<long, float> countyMarketTransportCostCache,
            out Market targetMarket,
            out float targetTransportCost,
            out MarketGoodState targetGoodState)
        {
            targetMarket = null;
            targetTransportCost = 0f;
            targetGoodState = null;

            if (economy == null || countyId <= 0 || goodRuntimeId < 0)
                return false;

            float bestEffectivePrice = float.MaxValue;
            float bestTransportCost = float.MaxValue;

            if (localMarket != null
                && localMarket.TryGetGoodState(goodRuntimeId, out var localGoodState))
            {
                float localCost = Math.Max(0f, localTransportCost);
                float effectivePrice = localGoodState.Price + localCost * SimulationConfig.Economy.FlatHaulingFeePerKgPerTransportCostUnit;
                if (effectivePrice > 0f)
                {
                    bestEffectivePrice = effectivePrice;
                    bestTransportCost = localCost;
                    targetMarket = localMarket;
                    targetTransportCost = localCost;
                    targetGoodState = localGoodState;
                }
            }

            foreach (var market in economy.Markets.Values)
            {
                if (market == null || market.Type != MarketType.Legitimate)
                    continue;
                if (localMarket != null && market.Id == localMarket.Id)
                    continue;
                if (!market.TryGetGoodState(goodRuntimeId, out var marketGoodState))
                    continue;
                if (!TryResolveCountyToMarketTransportCost(
                    state,
                    economy,
                    countyId,
                    market,
                    localMarket,
                    localTransportCost,
                    countyMarketTransportCostCache,
                    out float transportCost))
                {
                    continue;
                }

                float effectivePrice = marketGoodState.Price + Math.Max(0f, transportCost) * SimulationConfig.Economy.FlatHaulingFeePerKgPerTransportCostUnit;
                if (effectivePrice <= 0f)
                    continue;

                bool cheaper = effectivePrice < bestEffectivePrice - 0.0001f;
                bool tieAndCloser = Math.Abs(effectivePrice - bestEffectivePrice) <= 0.0001f
                    && transportCost < bestTransportCost;
                if (!cheaper && !tieAndCloser)
                    continue;

                bestEffectivePrice = effectivePrice;
                bestTransportCost = transportCost;
                targetMarket = market;
                targetTransportCost = transportCost;
                targetGoodState = marketGoodState;
            }

            if (TryResolveOffMapMarket(economy, countyId, goodId, out var offMapMarket, out float offMapTransportCost)
                && offMapMarket.TryGetGoodState(goodRuntimeId, out var offMapGoodState))
            {
                float effectivePrice = offMapGoodState.Price + Math.Max(0f, offMapTransportCost) * SimulationConfig.Economy.FlatHaulingFeePerKgPerTransportCostUnit;
                if (effectivePrice > 0f)
                {
                    bool cheaper = effectivePrice < bestEffectivePrice - 0.0001f;
                    bool tieAndCloser = Math.Abs(effectivePrice - bestEffectivePrice) <= 0.0001f
                        && offMapTransportCost < bestTransportCost;
                    if (cheaper || tieAndCloser || targetMarket == null)
                    {
                        targetMarket = offMapMarket;
                        targetTransportCost = Math.Max(0f, offMapTransportCost);
                        targetGoodState = offMapGoodState;
                    }
                }
            }

            return targetMarket != null && targetGoodState != null;
        }

        private static bool TryResolveCountyToMarketTransportCost(
            SimulationState state,
            EconomyState economy,
            int countyId,
            Market market,
            Market localMarket,
            float localTransportCost,
            Dictionary<long, float> countyMarketTransportCostCache,
            out float transportCost)
        {
            transportCost = 0f;
            if (economy == null || countyId <= 0 || market == null)
                return false;

            if (localMarket != null && market.Id == localMarket.Id)
            {
                transportCost = Math.Max(0f, localTransportCost);
                return true;
            }

            long cacheKey = ComposeCountyMarketKey(countyId, market.Id);
            if (countyMarketTransportCostCache != null
                && countyMarketTransportCostCache.TryGetValue(cacheKey, out float cachedCost))
            {
                transportCost = cachedCost;
                return true;
            }

            if (!economy.CountySeatCell.TryGetValue(countyId, out int seatCellId) || seatCellId <= 0)
                return false;

            if (market.ZoneCellCosts != null
                && market.ZoneCellCosts.TryGetValue(seatCellId, out float zonedCost)
                && float.IsFinite(zonedCost))
            {
                transportCost = Math.Max(0f, zonedCost);
            }
            else
            {
                float baseCost = localTransportCost > 0f
                    ? localTransportCost
                    : Math.Max(0f, economy.GetCountyTransportCost(countyId));
                transportCost = baseCost + InterMarketTransferCost;
            }

            if (countyMarketTransportCostCache != null)
                countyMarketTransportCostCache[cacheKey] = transportCost;

            return true;
        }

        private static long ComposeCountyMarketKey(int countyId, int marketId)
        {
            return ((long)countyId << 32) | (uint)marketId;
        }

        private static bool TryResolveOffMapMarket(
            EconomyState economy,
            int countyId,
            string goodId,
            out Market offMapMarket,
            out float offMapTransportCost)
        {
            offMapMarket = null;
            offMapTransportCost = 0f;

            if (economy == null || countyId <= 0 || string.IsNullOrWhiteSpace(goodId))
                return false;

            if (!economy.CountySeatCell.TryGetValue(countyId, out int seatCellId) || seatCellId <= 0)
                return false;

            float bestCost = float.MaxValue;
            Market bestMarket = null;
            foreach (var market in economy.Markets.Values)
            {
                if (market.Type != MarketType.OffMap)
                    continue;
                if (market.OffMapGoodIds == null || !market.OffMapGoodIds.Contains(goodId))
                    continue;
                if (market.ZoneCellCosts == null || !market.ZoneCellCosts.TryGetValue(seatCellId, out float cost))
                    continue;
                if (!float.IsFinite(cost))
                    continue;

                if (cost < bestCost)
                {
                    bestCost = cost;
                    bestMarket = market;
                }
            }

            if (bestMarket == null)
                return false;

            offMapMarket = bestMarket;
            offMapTransportCost = Math.Max(0f, bestCost);
            return true;
        }

        private static float Clamp(float value, float min, float max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }
}
