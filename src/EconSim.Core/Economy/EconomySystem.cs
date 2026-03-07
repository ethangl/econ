using System;
using System.Collections.Generic;
using EconSim.Core.Common;
using EconSim.Core.Data;
using EconSim.Core.Simulation;
using EconSim.Core.Simulation.Systems;

namespace EconSim.Core.Economy
{
    /// <summary>
    /// Multi-good production/consumption loop.
    /// Production: all goods produced per biome productivity.
    /// Consumption: food (staple, 1.0/pop), timber (comfort, 0.2/pop), ore (comfort, 0.1/pop).
    /// Staple shortfall = starvation. Comfort shortfall = unmet need only.
    /// </summary>
    public class EconomySystem : ITickSystem
    {
        public string Name => "Economy";
        public int TickInterval => SimulationConfig.Intervals.Daily;

        static readonly float[] ConsumptionPerPop = Goods.ConsumptionPerPop;
        const int Food = (int)GoodType.Wheat;

        // Satisfaction weights: staple pool + individual basics
        // Total = StapleBudgetPerPop + sum(Basic ConsumptionPerPop)
        static readonly int[] IndividualBasicGoods;   // Non-staple Basic goods (salt, ale)
        static readonly float[] IndividualBasicWeights; // ConsumptionPerPop[g] / TotalSatisfactionDenom
        static readonly float StapleSatisfactionWeight; // StapleBudgetPerPop / TotalSatisfactionDenom

        const float NeedsWeight = 0.7f;
        const float ComfortWeight = 0.3f;
        int[] _countyIds;
        int[] _countyToMarket;
        readonly float[] _effPopBuf = new float[5]; // reusable buffer for NeedCategory effective pop

        static EconomySystem()
        {
            float totalIndividualBasic = 0f;
            int count = 0;
            for (int g = 0; g < Goods.Count; g++)
            {
                if (Goods.Defs[g].Need == NeedCategory.Basic)
                {
                    count++;
                    totalIndividualBasic += ConsumptionPerPop[g];
                }
            }

            float totalDenom = Goods.StapleBudgetPerPop + totalIndividualBasic;
            StapleSatisfactionWeight = totalDenom > 0f ? Goods.StapleBudgetPerPop / totalDenom : 0f;

            IndividualBasicGoods = new int[count];
            IndividualBasicWeights = new float[count];
            int idx = 0;
            for (int g = 0; g < Goods.Count; g++)
            {
                if (Goods.Defs[g].Need == NeedCategory.Basic)
                {
                    IndividualBasicGoods[idx] = g;
                    IndividualBasicWeights[idx] = totalDenom > 0f ? ConsumptionPerPop[g] / totalDenom : 0f;
                    idx++;
                }
            }

        }

        public void Initialize(SimulationState state, MapData mapData)
        {
            _countyIds = new int[mapData.Counties.Count];
            for (int i = 0; i < mapData.Counties.Count; i++)
                _countyIds[i] = mapData.Counties[i].Id;

            int maxCountyId = 0;
            foreach (var county in mapData.Counties)
            {
                if (county.Id > maxCountyId)
                    maxCountyId = county.Id;
            }

            var econ = new EconomyState();
            econ.Counties = new CountyEconomy[maxCountyId + 1];

            var world = mapData.Info.World;
            float mapHeight = mapData.Info.Height;

            foreach (var county in mapData.Counties)
            {
                var ce = new CountyEconomy();
                ce.Population = county.TotalPopulation;
                Estates.ComputeEstatePop(ce.Population, ce.EstatePop);
                ComputeCountyProductivity(county, mapData, ce.Productivity);

                // Cache latitude for seasonal calculations
                float normalizedY = mapHeight > 0 ? county.Centroid.Y / mapHeight : 0.5f;
                ce.Latitude = world.LatitudeSouth + (world.LatitudeNorth - world.LatitudeSouth) * normalizedY;

                // Durable goods start at zero — built up naturally via production

                // Seed treasury so trade + taxation can bootstrap
                ce.Treasury = ce.Population * 1.0f;

                econ.Counties[county.Id] = ce;
            }

            // Compute median productivity per good (static)
            econ.MedianProductivity = new float[Goods.Count];
            for (int g = 0; g < Goods.Count; g++)
            {
                var productivities = new List<float>(mapData.Counties.Count);
                foreach (var county in mapData.Counties)
                    productivities.Add(econ.Counties[county.Id].Productivity[g]);
                productivities.Sort();
                if (productivities.Count > 0)
                {
                    int mid = productivities.Count / 2;
                    econ.MedianProductivity[g] = productivities.Count % 2 == 0
                        ? (productivities[mid - 1] + productivities[mid]) / 2f
                        : productivities[mid];
                }
            }

            // Build county adjacency graph
            econ.CountyAdjacency = BuildCountyAdjacency(mapData, maxCountyId);

            // Facility placement (absorbed from FacilityProductionSystem)
            var facilities = new List<Facility>();
            var countyFacilityIndices = new List<int>[maxCountyId + 1];
            for (int ci = 0; ci <= maxCountyId; ci++)
                countyFacilityIndices[ci] = new List<int>();

            foreach (var county in mapData.Counties)
            {
                var ce = econ.Counties[county.Id];
                if (ce == null) continue;

                for (int f = 0; f < Facilities.Count; f++)
                {
                    var def = Facilities.Defs[f];
                    int idx = facilities.Count;
                    var facility = new Facility(def.Type, county.Id, county.SeatCellId);
                    facilities.Add(facility);
                    countyFacilityIndices[county.Id].Add(idx);
                }
            }

            econ.Facilities = facilities.ToArray();
            econ.CountyFacilityIndices = countyFacilityIndices;

            // Initialize province/realm economy arrays
            int maxProvId = 0;
            foreach (var prov in mapData.Provinces)
                if (prov.Id > maxProvId) maxProvId = prov.Id;

            econ.Provinces = new ProvinceEconomy[maxProvId + 1];
            foreach (var prov in mapData.Provinces)
                econ.Provinces[prov.Id] = new ProvinceEconomy();

            int maxRealmId = 0;
            foreach (var realm in mapData.Realms)
                if (realm.Id > maxRealmId) maxRealmId = realm.Id;

            econ.Realms = new RealmEconomy[maxRealmId + 1];
            foreach (var realm in mapData.Realms)
                econ.Realms[realm.Id] = new RealmEconomy();

            // Population caches per province and realm
            econ.ProvincePop = new float[maxProvId + 1];
            econ.RealmPop = new float[maxRealmId + 1];
            ComputePopulationCaches(econ, mapData);

            // Demand signal array (populated each tick)
            econ.EffectiveDemandPerPop = new float[Goods.Count];

            // Seed market prices from base prices so crown payments are non-zero on day 1
            // (InterRealmTradeSystem updates these later, but runs after FiscalSystem)
            Array.Copy(Goods.BasePrice, econ.MarketPrices, Goods.Count);

            // Derive per-county sabbath day from seat cell's religion
            econ.CountySabbathDay = new int[maxCountyId + 1];
            for (int i = 0; i <= maxCountyId; i++)
                econ.CountySabbathDay[i] = 6; // default Sunday
            foreach (var county in mapData.Counties)
            {
                var cell = mapData.CellById[county.SeatCellId];
                if (cell.ReligionId > 0 && mapData.ReligionById != null
                    && mapData.ReligionById.TryGetValue(cell.ReligionId, out var religion))
                {
                    econ.CountySabbathDay[county.Id] = religion.SabbathDay;
                }
            }

            // One market per realm — hub at realm capital burg's county
            InitializeMarkets(mapData, econ, maxCountyId, state);

            // Virtual overseas market for geographically scarce goods (salt, spices)
            InitializeVirtualMarket(mapData, econ, maxCountyId, state);

            state.Economy = econ;

            // Cache county → market for local price lookups in Tick
            _countyToMarket = econ.CountyToMarket;
        }

        static void ComputePopulationCaches(EconomyState econ, MapData mapData)
        {
            Array.Clear(econ.ProvincePop, 0, econ.ProvincePop.Length);
            Array.Clear(econ.RealmPop, 0, econ.RealmPop.Length);

            // Sum county populations per province
            foreach (var county in mapData.Counties)
            {
                var ce = econ.Counties[county.Id];
                if (ce == null) continue;

                int provId = county.ProvinceId;
                if (provId >= 0 && provId < econ.ProvincePop.Length)
                    econ.ProvincePop[provId] += ce.Population;
            }

            // Sum province populations per realm
            foreach (var prov in mapData.Provinces)
            {
                int realmId = prov.RealmId;
                if (realmId >= 0 && realmId < econ.RealmPop.Length)
                    econ.RealmPop[realmId] += econ.ProvincePop[prov.Id];
            }
        }

        static int[][] BuildCountyAdjacency(MapData mapData, int maxCountyId)
        {
            var adj = new int[maxCountyId + 1][];
            var sets = new HashSet<int>[maxCountyId + 1];

            foreach (var county in mapData.Counties)
            {
                sets[county.Id] = new HashSet<int>();
            }

            foreach (var cell in mapData.Cells)
            {
                int cid = cell.CountyId;
                if (cid <= 0 || sets[cid] == null) continue;

                foreach (int nid in cell.NeighborIds)
                {
                    var neighbor = mapData.CellById[nid];
                    int ncid = neighbor.CountyId;
                    if (ncid > 0 && ncid != cid && sets[ncid] != null)
                        sets[cid].Add(ncid);
                }
            }

            for (int i = 0; i <= maxCountyId; i++)
            {
                if (sets[i] != null)
                {
                    adj[i] = new int[sets[i].Count];
                    sets[i].CopyTo(adj[i]);
                }
            }

            return adj;
        }

        public void Tick(SimulationState state, MapData mapData)
        {
            var econ = state.Economy;
            var counties = econ.Counties;
            var countyFacilityIndices = econ.CountyFacilityIndices;
            var countyIds = _countyIds;
            int goodsCount = Goods.Count;
            int todayDow = Calendar.DayOfWeek(state.CurrentDay);
            int dayOfYear = (state.CurrentDay - 1) % Calendar.DaysPerYear;

            // Compute production capacity per good (structural capacity for price discovery)
            var productionCap = econ.ProductionCapacity;
            Array.Clear(productionCap, 0, goodsCount);
            // Precompute seasonal wave from day of year (shared across counties)
            float seasonalWave = (float)Math.Cos(2.0 * Math.PI * (dayOfYear - SimulationConfig.Seasonality.SummerSolsticeDay) / Calendar.DaysPerYear);
            float globalSeverity = SimulationConfig.Seasonality.GlobalSeverity;
            // Long-term climate wave: slow oscillation over decades (baseline shift)
            float climateWave = (float)Math.Cos(2.0 * Math.PI * state.CurrentDay / ((double)SimulationConfig.Seasonality.ClimateWavePeriodYears * Calendar.DaysPerYear));
            float climateAmplitude = SimulationConfig.Seasonality.ClimateWaveAmplitude;

            // Extraction capacity
            for (int i = 0; i < countyIds.Length; i++)
            {
                var ce = counties[countyIds[i]];
                if (ce == null) continue;
                float wave = ce.Latitude < 0 ? -seasonalWave : seasonalWave;
                float amplitude = Math.Abs(ce.Latitude) / 90f;
                for (int g = 0; g < goodsCount; g++)
                {
                    float cap = ce.Population * ce.Productivity[g];
                    float sens = Goods.SeasonalSensitivity[g];
                    float sm = 1f + sens * amplitude * (globalSeverity * wave * 0.5f + climateAmplitude * climateWave);
                    productionCap[g] += cap * sm;
                }
            }
            // Facility labor capacity
            var allFacilities = econ.Facilities;
            if (allFacilities != null)
            {
                for (int i = 0; i < countyIds.Length; i++)
                {
                    int countyId = countyIds[i];
                    var ce = counties[countyId];
                    if (ce == null) continue;
                    var indices = countyFacilityIndices != null && countyId < countyFacilityIndices.Length
                        ? countyFacilityIndices[countyId] : null;
                    if (indices == null || indices.Count == 0) continue;
                    float pop = ce.Population;
                    for (int fi = 0; fi < indices.Count; fi++)
                    {
                        var def = allFacilities[indices[fi]].Def;
                        if (def.LaborPerUnit > 0 && def.OutputAmount > 0f)
                            productionCap[(int)def.OutputGood] += pop * def.MaxLaborFraction / def.LaborPerUnit * def.OutputAmount;
                    }
                }
            }

            for (int i = 0; i < countyIds.Length; i++)
            {
                int countyId = countyIds[i];
                var ce = counties[countyId];
                if (ce == null) continue;

                float pop = ce.Population;
                float[] effPop = _effPopBuf;
                Estates.ComputeEffectivePop(ce.EstatePop, effPop);
                var indices = countyFacilityIndices != null && countyId < countyFacilityIndices.Length
                    ? countyFacilityIndices[countyId]
                    : null;

                // Local market prices for this county's market zone
                int countyMarketId = _countyToMarket != null && countyId < _countyToMarket.Length
                    ? _countyToMarket[countyId] : 0;
                var localPrices = econ.PerMarketPrices != null
                    && countyMarketId > 0 && countyMarketId < econ.PerMarketPrices.Length
                    ? econ.PerMarketPrices[countyMarketId] : econ.MarketPrices;

                // Compute per-county seasonal modifier inputs (shared across goods)
                float countyWave = ce.Latitude < 0 ? -seasonalWave : seasonalWave;
                float countyAmplitude = Math.Abs(ce.Latitude) / 90f;

                bool isRestDay = todayDow == econ.CountySabbathDay[countyId];
                if (isRestDay)
                {
                    // Sunday: no production, zero out for clean downstream reads
                    for (int g = 0; g < goodsCount; g++)
                        ce.Production[g] = 0f;
                    ce.FacilityWorkers = 0f;
                }
                else
                {
                    float wf = pop > 0 ? (pop - ce.FacilityWorkers) / pop : 1f;

                    // Compute facility input demand (two-pass: durables first, then intermediates)
                    Array.Clear(ce.FacilityInputNeed, 0, goodsCount);
                    if (indices != null && indices.Count > 0)
                    {
                        const float DurableBufferMultiplier = 3.0f;

                        // Pass 1: Durable outputs (self-contained demand from TargetStockPerPop)
                        // Populates FacilityInputNeed for their inputs (iron, charcoal, wool, etc.)
                        for (int fi = 0; fi < indices.Count; fi++)
                        {
                            var def = econ.Facilities[indices[fi]].Def;
                            if (def.OutputAmount <= 0f) continue;
                            int output = (int)def.OutputGood;
                            if (!Goods.IsDurable[output]) continue;

                            float maxByLabor = def.LaborPerUnit > 0
                                ? pop * def.MaxLaborFraction / def.LaborPerUnit * def.OutputAmount
                                : float.MaxValue;

                            float targetStock = pop * Goods.TargetStockPerPop[output];
                            float maintenance = ce.Stock[output] * Goods.Defs[output].SpoilageRate;
                            float gap = Math.Max(0f, targetStock - ce.Stock[output]);
                            float dailyNeed = maintenance + gap * Goods.DurableCatchUpRate[output] * DurableBufferMultiplier;
                            float throughput = Math.Min(maxByLabor, dailyNeed);

                            float scale = throughput / def.OutputAmount;
                            for (int ii = 0; ii < def.Inputs.Length; ii++)
                                ce.FacilityInputNeed[(int)def.Inputs[ii].Good] += scale * def.Inputs[ii].Amount;
                        }

                        // Pass 2: Remaining facilities — signal input demand based on downstream need.
                        // Note: within this pass, smelter (enum 2) runs before charcoalBurner (enum 4),
                        // so charcoal's FacilityInputNeed includes smelter demand when charcoalBurner reads it.
                        for (int fi = 0; fi < indices.Count; fi++)
                        {
                            var def = econ.Facilities[indices[fi]].Def;
                            if (def.OutputAmount <= 0f) continue;
                            int output = (int)def.OutputGood;
                            if (Goods.IsDurable[output]) continue;

                            float maxByLabor = def.LaborPerUnit > 0
                                ? pop * def.MaxLaborFraction / def.LaborPerUnit * def.OutputAmount
                                : float.MaxValue;
                            float throughput = maxByLabor;

                            // Demand planning: cap by downstream need, NOT current stock.
                            // Stock ceilings only apply during actual production — demand
                            // must flow through the chain even when intermediates are stalled.
                            if (Goods.IsDurableInput[output])
                            {
                                float downstreamDemand = ce.FacilityInputNeed[output];
                                throughput = Math.Min(throughput, downstreamDemand);
                            }

                            float scale = throughput / def.OutputAmount;
                            for (int ii = 0; ii < def.Inputs.Length; ii++)
                                ce.FacilityInputNeed[(int)def.Inputs[ii].Good] += scale * def.Inputs[ii].Amount;
                        }
                    }

                    // Production — all goods (extraction workforce reduced by facility labor)
                    for (int g = 0; g < goodsCount; g++)
                    {
                        float produced = pop * ce.Productivity[g] * wf;

                        // Seasonal + climate extraction modifier (seasonal oscillates around climate-shifted baseline)
                        float sens = Goods.SeasonalSensitivity[g];
                        float sm = 1f + sens * countyAmplitude * (globalSeverity * countyWave * 0.5f + climateAmplitude * climateWave);
                        produced *= sm;

                        // Stock-ceiling cap for durable-chain raws (demand-driven extraction)
                        if (Goods.IsDurableInput[g])
                        {
                            float localDemand = ce.FacilityInputNeed[g];
                            const float ChainBufferDays = 14f;
                            float targetStock = localDemand * ChainBufferDays;
                            float gap = Math.Max(0f, targetStock - ce.Stock[g]);
                            produced = Math.Min(produced, gap);
                        }
                        // Price-based extraction throttle for commodity intermediates
                        else if (!Goods.HasDirectDemand[g] && Goods.BasePrice[g] > 0f)
                        {
                            float priceRatio = localPrices[g] / Goods.BasePrice[g];
                            if (priceRatio < 1f) produced *= priceRatio;
                        }

                        ce.Stock[g] += produced;
                        ce.Production[g] = produced;
                    }

                    // Facility processing — input/labor constrained, price-throttled for intermediates.
                    // Two-pass: chain intermediates (IsDurableInput) first so they get priority
                    // access to shared inputs (e.g. charcoalBurner gets timber before carpenter).
                    if (indices != null && indices.Count > 0)
                    {
                        float totalFacWorkers = 0f;
                        for (int pass = 0; pass < 2; pass++)
                        {
                        for (int fi = 0; fi < indices.Count; fi++)
                        {
                            var fac = econ.Facilities[indices[fi]];
                            var def = fac.Def;
                            int output = (int)def.OutputGood;
                            bool isChain = Goods.IsDurableInput[output];
                            if ((pass == 0) != isChain) continue;

                            // Material constraint: min across all inputs
                            float maxByInput = float.MaxValue;
                            for (int ii = 0; ii < def.Inputs.Length; ii++)
                            {
                                float avail = ce.Stock[(int)def.Inputs[ii].Good] / def.Inputs[ii].Amount * def.OutputAmount;
                                if (avail < maxByInput) maxByInput = avail;
                            }

                            // Labor constraint
                            float maxByLabor = def.LaborPerUnit > 0
                                ? pop * def.MaxLaborFraction / def.LaborPerUnit * def.OutputAmount
                                : float.MaxValue;

                            float throughput = Math.Min(maxByInput, maxByLabor);
                            if (throughput < 0f) throughput = 0f;

                            // Stock-gap production cap for durables
                            if (Goods.IsDurable[output])
                            {
                                float targetStock = pop * Goods.TargetStockPerPop[output];
                                float currentStock = ce.Stock[output];
                                float maintenance = currentStock * Goods.Defs[output].SpoilageRate;
                                float gap = Math.Max(0f, targetStock - currentStock);
                                const float DurableBufferMultiplier = 3.0f;
                                float dailyNeed = maintenance + gap * Goods.DurableCatchUpRate[output] * DurableBufferMultiplier;
                                throughput = Math.Min(throughput, dailyNeed);
                            }

                            // Stock-ceiling cap for durable-chain intermediates (iron, charcoal)
                            if (Goods.IsDurableInput[output])
                            {
                                float downstreamDemand = ce.FacilityInputNeed[output];
                                const float ChainBufferDays = 7f;
                                float targetStock = downstreamDemand * ChainBufferDays;
                                float gap = Math.Max(0f, targetStock - ce.Stock[output]);
                                throughput = Math.Min(throughput, gap);
                            }

                            // Price-based facility throttle (skip durables and durable inputs — they use stock caps above)
                            if (Goods.BasePrice[output] > 0f && !Goods.IsDurable[output] && !Goods.IsDurableInput[output])
                            {
                                float priceRatio = localPrices[output] / Goods.BasePrice[output];
                                if (priceRatio < 1f) throughput *= priceRatio;
                            }

                            // Consume all inputs proportionally
                            for (int ii = 0; ii < def.Inputs.Length; ii++)
                            {
                                float used = def.OutputAmount > 0f
                                    ? throughput / def.OutputAmount * def.Inputs[ii].Amount
                                    : 0f;
                                ce.Stock[(int)def.Inputs[ii].Good] -= used;
                            }
                            ce.Stock[output] += throughput;
                            ce.Production[output] += throughput;

                            fac.Throughput = throughput;
                            fac.Workforce = def.OutputAmount > 0f
                                ? throughput / def.OutputAmount * def.LaborPerUnit
                                : 0f;
                            totalFacWorkers += fac.Workforce;
                        }
                        }
                        ce.FacilityWorkers = totalFacWorkers;
                    }
                }

                // Consumption — non-staple goods (durables and individual basics)
                for (int g = 0; g < goodsCount; g++)
                {
                    if (Goods.Defs[g].Need == NeedCategory.Staple)
                        continue; // handled in pooled pass below

                    float tgt = Goods.TargetStockPerPop[g];
                    if (tgt > 0f)
                    {
                        // Durable: only wear removes stock; deficit is a demand signal
                        float targetStock = effPop[(int)Goods.Defs[g].Need] * tgt;
                        float replacement = ce.Stock[g] * Goods.Defs[g].SpoilageRate;
                        float wear = Math.Min(ce.Stock[g], replacement);
                        ce.Stock[g] -= wear;
                        ce.Consumption[g] = wear;
                        // Measure gap post-wear (consistent with InterRealmTradeSystem deficit scan)
                        float deficit = Math.Max(0f, targetStock - ce.Stock[g]);
                        ce.UnmetNeed[g] = deficit * Goods.DurableCatchUpRate[g];
                    }
                    else
                    {
                        float needed = effPop[(int)Goods.Defs[g].Need] * ConsumptionPerPop[g];
                        float consumed = Math.Min(ce.Stock[g], needed);
                        ce.Stock[g] -= consumed;
                        ce.Consumption[g] = consumed;
                        ce.UnmetNeed[g] = needed - consumed;
                    }
                }

                // Pooled staple consumption — people eat 1 kg/day of any combination
                float stapleBudget = effPop[(int)NeedCategory.Staple] * Goods.StapleBudgetPerPop;
                float totalStapleAvail = 0f;
                for (int s = 0; s < Goods.StapleGoods.Length; s++)
                    totalStapleAvail += ce.Stock[Goods.StapleGoods[s]];

                float totalStapleConsumed = 0f;
                if (totalStapleAvail >= stapleBudget)
                {
                    // Plenty: consume proportional to availability
                    for (int s = 0; s < Goods.StapleGoods.Length; s++)
                    {
                        int g = Goods.StapleGoods[s];
                        float consumed = ce.Stock[g] / totalStapleAvail * stapleBudget;
                        ce.Stock[g] -= consumed;
                        ce.Consumption[g] = consumed;
                        totalStapleConsumed += consumed;
                    }
                }
                else
                {
                    // Scarce: eat everything
                    for (int s = 0; s < Goods.StapleGoods.Length; s++)
                    {
                        int g = Goods.StapleGoods[s];
                        ce.Consumption[g] = ce.Stock[g];
                        totalStapleConsumed += ce.Stock[g];
                        ce.Stock[g] = 0f;
                    }
                }

                // Trade demand signals: shortfall against ideal share
                for (int s = 0; s < Goods.StapleGoods.Length; s++)
                {
                    int g = Goods.StapleGoods[s];
                    ce.UnmetNeed[g] = Math.Max(0f, effPop[(int)NeedCategory.Staple] * Goods.StapleIdealPerPop[g] - ce.Consumption[g]);
                }

                // Basic-needs fulfillment EMA (alpha ≈ 2/(30+1) ≈ 0.065, ~30-day smoothing)
                // Staple pool fulfillment + individual basic fulfillment — drives birth/death
                float stapleFulfillment = stapleBudget > 0f
                    ? Math.Min(1f, totalStapleConsumed / stapleBudget) : 1f;
                float dailyNeeds = StapleSatisfactionWeight * stapleFulfillment;
                for (int b = 0; b < IndividualBasicGoods.Length; b++)
                {
                    int g = IndividualBasicGoods[b];
                    float needed = effPop[(int)NeedCategory.Basic] * ConsumptionPerPop[g];
                    float ratio = needed > 0f ? Math.Min(1f, ce.Consumption[g] / needed) : 1f;
                    dailyNeeds += IndividualBasicWeights[b] * ratio;
                }
                ce.BasicSatisfaction += 0.065f * (dailyNeeds - ce.BasicSatisfaction);

                // Comfort fulfillment — average across categories, sum within each category
                var comfortCats = Goods.ComfortCategories;
                var comfortCatGoods = Goods.ComfortCategoryGoods;
                float comfortSum = 0f;
                for (int cat = 0; cat < comfortCats.Length; cat++)
                {
                    var catDef = comfortCats[cat];
                    float catTarget = effPop[(int)NeedCategory.Comfort] * catDef.TargetPerPop;
                    float catActual = 0f;
                    var members = comfortCatGoods[cat];
                    for (int j = 0; j < members.Length; j++)
                    {
                        int g = members[j];
                        catActual += catDef.IsDurable ? ce.Stock[g] : ce.Consumption[g];
                    }
                    comfortSum += catTarget > 0f ? Math.Min(1f, catActual / catTarget) : 1f;
                }
                float comfortFulfillment = comfortCats.Length > 0 ? comfortSum / comfortCats.Length : 1f;

                // Blended satisfaction: needs + comfort — drives migration
                float dailySatisfaction = NeedsWeight * dailyNeeds + ComfortWeight * comfortFulfillment;
                ce.Satisfaction += 0.065f * (dailySatisfaction - ce.Satisfaction);
            }

            // Compute effective demand per pop (used by FiscalSystem)
            var demandPerPop = econ.EffectiveDemandPerPop;
            float totalPop = 0f;
            for (int i = 0; i < countyIds.Length; i++)
                totalPop += counties[countyIds[i]].Population;
            for (int g = 0; g < goodsCount; g++)
            {
                if (Goods.TargetStockPerPop[g] > 0f && totalPop > 0f)
                {
                    float totalDemand = 0f;
                    for (int i = 0; i < countyIds.Length; i++)
                    {
                        var c = counties[countyIds[i]];
                        totalDemand += c.Consumption[g] + c.UnmetNeed[g];
                    }
                    demandPerPop[g] = totalDemand / totalPop;
                }
                else
                {
                    demandPerPop[g] = Goods.Defs[g].Need == NeedCategory.Staple
                        ? Goods.StapleIdealPerPop[g]
                        : ConsumptionPerPop[g];
                }
            }

            // Record snapshot only when explicitly enabled (EconDebugBridge runs).
            if (econ.CaptureSnapshots)
                econ.TimeSeries.Add(BuildSnapshot(state.CurrentDay, econ));
        }

        static void ComputeCountyProductivity(County county, MapData mapData, float[] output)
        {
            int landCells = 0;
            for (int g = 0; g < Goods.Count; g++)
                output[g] = 0f;

            const int FishIdx = (int)GoodType.Fish;

            foreach (int cellId in county.CellIds)
            {
                var cell = mapData.CellById[cellId];
                if (!cell.IsLand) continue;
                landCells++;
                float cellTemp = cell.Temperature;
                for (int g = 0; g < Goods.Count; g++)
                {
                    float y = Goods.BiomeYield[cell.BiomeId, g];
                    if (y > 0f && Goods.HasTemperatureGate[g]
                        && (cellTemp < Goods.MinTemperature[g] || cellTemp > Goods.MaxTemperature[g]))
                        continue;
                    y *= Goods.RockYieldModifier[cell.RockId, g];
                    output[g] += y;
                }

                // Coast-proximity fishing bonus
                switch (cell.CoastDistance)
                {
                    case 0: output[FishIdx] += 0.45f; break;
                    case 1: output[FishIdx] += 0.22f; break;
                    case 2: output[FishIdx] += 0.08f; break;
                }
            }

            if (landCells > 0)
            {
                for (int g = 0; g < Goods.Count; g++)
                    output[g] /= landCells;
            }
        }

        static void InitializeMarkets(MapData mapData, EconomyState econ, int maxCountyId, SimulationState state)
        {
            // Build realm → capital county/cell mapping
            int maxRealmId = 0;
            foreach (var realm in mapData.Realms)
                if (realm.Id > maxRealmId) maxRealmId = realm.Id;

            var realmCapitalCounty = new int[maxRealmId + 1];
            var realmCapitalCell = new int[maxRealmId + 1];
            for (int i = 0; i <= maxRealmId; i++)
            {
                realmCapitalCounty[i] = -1;
                realmCapitalCell[i] = -1;
            }

            if (mapData.Burgs != null)
            {
                foreach (var realm in mapData.Realms)
                {
                    Burg burg = null;
                    foreach (var b in mapData.Burgs)
                        if (b.Id == realm.CapitalBurgId) { burg = b; break; }

                    if (burg != null && mapData.CellById.TryGetValue(burg.CellId, out var cell)
                        && cell.CountyId > 0 && cell.CountyId < econ.Counties.Length
                        && econ.Counties[cell.CountyId] != null)
                    {
                        realmCapitalCounty[realm.Id] = cell.CountyId;
                        realmCapitalCell[realm.Id] = burg.CellId;
                    }
                }
            }

            // Fallback: if a realm has no valid capital, pick highest-pop county in realm
            foreach (var realm in mapData.Realms)
            {
                if (realmCapitalCounty[realm.Id] >= 0) continue;
                int bestId = -1;
                float bestPop = -1f;
                foreach (var county in mapData.Counties)
                {
                    if (county.RealmId != realm.Id) continue;
                    var ce = econ.Counties[county.Id];
                    if (ce != null && ce.Population > bestPop)
                    {
                        bestPop = ce.Population;
                        bestId = county.Id;
                    }
                }
                if (bestId >= 0)
                {
                    realmCapitalCounty[realm.Id] = bestId;
                    foreach (var county in mapData.Counties)
                    {
                        if (county.Id == bestId)
                        {
                            realmCapitalCell[realm.Id] = county.SeatCellId;
                            break;
                        }
                    }
                }
            }

            // Create one MarketInfo per realm. Market IDs are 1-based, assigned in realm order.
            int marketCount = mapData.Realms.Count;
            econ.Markets = new MarketInfo[marketCount + 1]; // slot 0 unused
            var realmIdToMarketId = new int[maxRealmId + 1];

            for (int m = 0; m < mapData.Realms.Count; m++)
            {
                var realm = mapData.Realms[m];
                int marketId = m + 1;
                realmIdToMarketId[realm.Id] = marketId;
                econ.Markets[marketId] = new MarketInfo
                {
                    Id = marketId,
                    HubCountyId = realmCapitalCounty[realm.Id],
                    HubCellId = realmCapitalCell[realm.Id],
                    HubRealmId = realm.Id
                };
            }

            // CountyToMarket: each county maps to its own realm's market
            econ.CountyToMarket = new int[maxCountyId + 1];
            foreach (var county in mapData.Counties)
            {
                int realmId = county.RealmId;
                if (realmId >= 0 && realmId < realmIdToMarketId.Length)
                    econ.CountyToMarket[county.Id] = realmIdToMarketId[realmId];
                else
                    econ.CountyToMarket[county.Id] = 1; // fallback to first market
            }

            // Allocate and seed PerMarketPrices
            econ.PerMarketPrices = new float[marketCount + 1][];
            for (int m = 1; m <= marketCount; m++)
            {
                econ.PerMarketPrices[m] = new float[Goods.Count];
                Array.Copy(Goods.BasePrice, econ.PerMarketPrices[m], Goods.Count);
            }

            // Allocate empty MarketEmbargoes
            econ.MarketEmbargoes = new System.Collections.Generic.HashSet<int>[marketCount + 1];
            for (int m = 1; m <= marketCount; m++)
                econ.MarketEmbargoes[m] = new System.Collections.Generic.HashSet<int>();

            // Compute hub-to-hub transport cost matrix
            econ.HubToHubCost = new float[marketCount + 1][];
            for (int m = 1; m <= marketCount; m++)
                econ.HubToHubCost[m] = new float[marketCount + 1];

            if (state.Transport != null)
            {
                for (int m1 = 1; m1 <= marketCount; m1++)
                {
                    int cell1 = econ.Markets[m1].HubCellId;
                    if (cell1 < 0) continue;
                    for (int m2 = m1 + 1; m2 <= marketCount; m2++)
                    {
                        int cell2 = econ.Markets[m2].HubCellId;
                        if (cell2 < 0) continue;
                        float cost = state.Transport.GetTransportCost(cell1, cell2);
                        if (cost == float.MaxValue) cost = 1000f; // no path — large penalty
                        econ.HubToHubCost[m1][m2] = cost;
                        econ.HubToHubCost[m2][m1] = cost;
                    }
                }
            }
        }

        static void InitializeVirtualMarket(MapData mapData, EconomyState econ, int maxCountyId, SimulationState state)
        {
            var vm = new VirtualMarketState(Goods.Count, maxCountyId);

            // Globe-derived trade context (null when generating without globe)
            var trade = mapData.Info?.Trade;
            float volumeScale = trade?.TradeVolumeScale ?? 1f;
            float distancePriceFactor = trade != null ? 1f + 0.02f * trade.NearestContinentHops : 1f;

            // Configure traded goods: salt and spices
            int saltIdx = (int)GoodType.Salt;
            int spicesIdx = (int)GoodType.Spices;
            vm.TradedGoods.Add(saltIdx);
            vm.TradedGoods.Add(spicesIdx);

            // Salt: abundant foreign supply
            vm.TargetStock[saltIdx] = 5000f * volumeScale;
            vm.ReplenishRate[saltIdx] = 50f * volumeScale;
            vm.MaxStock[saltIdx] = 10000f * volumeScale;
            vm.Stock[saltIdx] = vm.TargetStock[saltIdx];
            vm.SellPrice[saltIdx] = Goods.BasePrice[saltIdx] * distancePriceFactor;
            vm.BuyPrice[saltIdx] = Goods.BasePrice[saltIdx] * 0.75f;

            // Spices: scarce luxury import
            vm.TargetStock[spicesIdx] = 10000f * volumeScale;
            vm.ReplenishRate[spicesIdx] = 500f * volumeScale;
            vm.MaxStock[spicesIdx] = 25000f * volumeScale;
            vm.Stock[spicesIdx] = vm.TargetStock[spicesIdx];
            vm.SellPrice[spicesIdx] = Goods.BasePrice[spicesIdx] * distancePriceFactor;
            vm.BuyPrice[spicesIdx] = Goods.BasePrice[spicesIdx] * 0.75f;

            // Fur: demand-only (foreign consumption absorbs domestic surplus)
            // Negative replenish = foreign buyers consuming stock over time
            int furIdx = (int)GoodType.Fur;
            vm.TradedGoods.Add(furIdx);
            vm.TargetStock[furIdx] = 5000f * volumeScale;
            vm.ReplenishRate[furIdx] = -500f * volumeScale;   // foreign consumption drain
            vm.MaxStock[furIdx] = 10000f * volumeScale;
            vm.Stock[furIdx] = 0f;              // starts empty — demand only
            vm.SellPrice[furIdx] = Goods.BasePrice[furIdx];
            vm.BuyPrice[furIdx] = Goods.BasePrice[furIdx] * 0.75f / distancePriceFactor;

            vm.OverseasSurcharge = trade?.OverseasSurcharge ?? 0.02f;

            // Precompute per-county port cost via Dijkstra to nearest coast cell
            // Coast cells: land cells adjacent to water (CoastDistance == 1)
            const float CostNormFactor = 0.00003f;
            const int CandidateCount = 5;

            var coastCells = new List<Cell>();
            foreach (var cell in mapData.Cells)
            {
                if (cell.IsLand && cell.CoastDistance == 1)
                    coastCells.Add(cell);
            }

            if (coastCells.Count > 0 && state.Transport != null)
            {
                int reachable = 0;
                int unreachable = 0;

                foreach (var county in mapData.Counties)
                {
                    int seatCellId = county.SeatCellId;
                    if (!mapData.CellById.TryGetValue(seatCellId, out var seatCell))
                    {
                        unreachable++;
                        continue;
                    }

                    // Find nearest ~N coast cells by Euclidean distance
                    // Sort coast cells by distance to seat, take top N candidates
                    float bestCost = float.MaxValue;

                    // Simple O(coastCells) scan for nearest candidates
                    // Use a small buffer to track top-N by Euclidean distance
                    var candidates = new (int cellId, float eucDist)[Math.Min(CandidateCount, coastCells.Count)];
                    for (int i = 0; i < candidates.Length; i++)
                        candidates[i] = (-1, float.MaxValue);

                    for (int i = 0; i < coastCells.Count; i++)
                    {
                        float dist = Vec2.Distance(seatCell.Center, coastCells[i].Center);
                        // Insert into sorted candidates if closer
                        int worstIdx = 0;
                        for (int j = 1; j < candidates.Length; j++)
                        {
                            if (candidates[j].eucDist > candidates[worstIdx].eucDist)
                                worstIdx = j;
                        }
                        if (dist < candidates[worstIdx].eucDist)
                            candidates[worstIdx] = (coastCells[i].Id, dist);
                    }

                    // Dijkstra to each candidate, take minimum
                    for (int i = 0; i < candidates.Length; i++)
                    {
                        if (candidates[i].cellId < 0) continue;
                        float cost = state.Transport.GetTransportCost(seatCellId, candidates[i].cellId);
                        if (cost < bestCost)
                            bestCost = cost;
                    }

                    if (bestCost < float.MaxValue)
                    {
                        vm.CountyPortCost[county.Id] = bestCost * CostNormFactor + vm.OverseasSurcharge;
                        reachable++;
                    }
                    else
                    {
                        unreachable++;
                    }
                }

                SimLog.Log("trade", $"Virtual market init: {coastCells.Count} coast cells, " +
                    $"{reachable} counties reachable, {unreachable} landlocked/unreachable");
            }
            else
            {
                SimLog.Log("trade", "Virtual market init: no coast cells or transport graph — VM disabled");
            }

            econ.VirtualMarket = vm;
        }

        static EconomySnapshot BuildSnapshot(int day, EconomyState econ)
        {
            var snap = new EconomySnapshot();
            snap.Day = day;
            snap.MinStock = float.MaxValue;
            snap.MaxStock = float.MinValue;
            snap.TotalStockByGood = new float[Goods.Count];
            snap.TotalProductionByGood = new float[Goods.Count];
            snap.TotalConsumptionByGood = new float[Goods.Count];
            snap.TotalUnmetNeedByGood = new float[Goods.Count];
            snap.TotalDucalReliefByGood = new float[Goods.Count];
            snap.TotalProvincialStockpileByGood = new float[Goods.Count];
            snap.TotalRoyalStockpileByGood = new float[Goods.Count];
            snap.TotalGranaryRequisitionedByGood = new float[Goods.Count];
            snap.TotalIntraProvTradeBoughtByGood = new float[Goods.Count];
            snap.TotalIntraProvTradeSoldByGood = new float[Goods.Count];
            snap.TotalCrossProvTradeBoughtByGood = new float[Goods.Count];
            snap.TotalCrossProvTradeSoldByGood = new float[Goods.Count];
            snap.TotalCrossMarketTradeBoughtByGood = new float[Goods.Count];
            snap.TotalCrossMarketTradeSoldByGood = new float[Goods.Count];
            snap.TotalVMImportedByGood = new float[Goods.Count];
            snap.TotalVMExportedByGood = new float[Goods.Count];

            int countyCount = 0;
            snap.MinBasicSatisfaction = float.MaxValue;
            snap.MaxBasicSatisfaction = float.MinValue;
            snap.MinSatisfaction = float.MaxValue;
            snap.MaxSatisfaction = float.MinValue;
            float weightedBasicSatisfaction = 0f;
            float weightedSatisfaction = 0f;

            for (int i = 0; i < econ.Counties.Length; i++)
            {
                var ce = econ.Counties[i];
                if (ce == null) continue;

                countyCount++;

                for (int g = 0; g < Goods.Count; g++)
                {
                    snap.TotalStockByGood[g] += ce.Stock[g];
                    snap.TotalProductionByGood[g] += ce.Production[g];
                    snap.TotalConsumptionByGood[g] += ce.Consumption[g];
                    snap.TotalUnmetNeedByGood[g] += ce.UnmetNeed[g];
                }

                for (int g = 0; g < Goods.Count; g++)
                {
                    snap.TotalDucalReliefByGood[g] += ce.Relief[g];
                    snap.TotalGranaryRequisitionedByGood[g] += ce.GranaryRequisitioned[g];
                    snap.TotalIntraProvTradeBoughtByGood[g] += ce.TradeBought[g];
                    snap.TotalIntraProvTradeSoldByGood[g] += ce.TradeSold[g];
                    snap.TotalCrossProvTradeBoughtByGood[g] += ce.CrossProvTradeBought[g];
                    snap.TotalCrossProvTradeSoldByGood[g] += ce.CrossProvTradeSold[g];
                    snap.TotalCrossMarketTradeBoughtByGood[g] += ce.CrossMarketTradeBought[g];
                    snap.TotalCrossMarketTradeSoldByGood[g] += ce.CrossMarketTradeSold[g];
                    snap.TotalVMImportedByGood[g] += ce.VirtualMarketBought[g];
                    snap.TotalVMExportedByGood[g] += ce.VirtualMarketSold[g];
                }
                snap.TotalIntraProvTradeSpending += ce.TradeCrownsSpent;
                snap.TotalIntraProvTradeRevenue += ce.TradeCrownsEarned;
                snap.TotalCrossProvTradeSpending += ce.CrossProvTradeCrownsSpent;
                snap.TotalCrossProvTradeRevenue += ce.CrossProvTradeCrownsEarned;
                snap.TotalTradeTollsPaid += ce.TradeTollsPaid;
                snap.TotalCrossMarketTradeSpending += ce.CrossMarketTradeCrownsSpent;
                snap.TotalCrossMarketTradeRevenue += ce.CrossMarketTradeCrownsEarned;
                snap.TotalCrossMarketTollsPaid += ce.CrossMarketTollsPaid;
                snap.TotalCrossMarketTariffsPaid += ce.CrossMarketTariffsPaid;
                snap.TotalVMImportSpending += ce.VirtualMarketCrownsSpent;
                snap.TotalVMExportRevenue += ce.VirtualMarketCrownsEarned;
                snap.TotalVMTariffsPaid += ce.VirtualMarketTariffsPaid;

                float foodStock = ce.Stock[Food];
                if (foodStock < snap.MinStock) snap.MinStock = foodStock;
                if (foodStock > snap.MaxStock) snap.MaxStock = foodStock;

                // Staple-pooled starvation check: actual pool shortfall, not ideal-share unmet
                float totalStapleProd = 0f;
                float totalStapleCons = 0f;
                for (int s = 0; s < Goods.StapleGoods.Length; s++)
                {
                    int sg = Goods.StapleGoods[s];
                    totalStapleProd += ce.Production[sg];
                    totalStapleCons += ce.Consumption[sg];
                }
                float stapleBudget = ce.Population * Goods.StapleBudgetPerPop;

                if (totalStapleCons < stapleBudget * 0.999f)
                    snap.ShortfallCounties++;
                else if (totalStapleProd < totalStapleCons)
                    snap.DeficitCounties++;
                else
                    snap.SurplusCounties++;

                // Population dynamics aggregates
                snap.TotalPopulation += ce.Population;
                snap.TotalBirths += ce.BirthsThisMonth;
                snap.TotalDeaths += ce.DeathsThisMonth;
                weightedBasicSatisfaction += ce.Population * ce.BasicSatisfaction;
                weightedSatisfaction += ce.Population * ce.Satisfaction;
                if (ce.BasicSatisfaction < snap.MinBasicSatisfaction) snap.MinBasicSatisfaction = ce.BasicSatisfaction;
                if (ce.BasicSatisfaction > snap.MaxBasicSatisfaction) snap.MaxBasicSatisfaction = ce.BasicSatisfaction;
                if (ce.Satisfaction < snap.MinSatisfaction) snap.MinSatisfaction = ce.Satisfaction;
                if (ce.Satisfaction > snap.MaxSatisfaction) snap.MaxSatisfaction = ce.Satisfaction;
                if (ce.BasicSatisfaction < 0.5f) snap.CountiesInDistress++;

                snap.TotalCountyTreasury += ce.Treasury;
                snap.TotalMonetaryTaxToProvince += ce.MonetaryTaxPaid;
                snap.TotalGranaryRequisitionCrowns += ce.GranaryRequisitionCrownsReceived;
                snap.TotalMarketFeesCollected += ce.MarketFeesReceived;
                snap.TotalTransportCostsPaid += ce.TransportCostsPaid;
            }

            // Backward-compat scalars = food values
            snap.TotalStock = snap.TotalStockByGood[Food];
            snap.TotalProduction = snap.TotalProductionByGood[Food];
            snap.TotalConsumption = snap.TotalConsumptionByGood[Food];
            snap.TotalUnmetNeed = snap.TotalUnmetNeedByGood[Food];

            if (countyCount == 0)
            {
                snap.MinStock = 0;
                snap.MaxStock = 0;
                snap.MinBasicSatisfaction = 0;
                snap.MaxBasicSatisfaction = 0;
                snap.MinSatisfaction = 0;
                snap.MaxSatisfaction = 0;
            }

            snap.AvgBasicSatisfaction = snap.TotalPopulation > 0f
                ? weightedBasicSatisfaction / snap.TotalPopulation
                : 0f;
            snap.AvgSatisfaction = snap.TotalPopulation > 0f
                ? weightedSatisfaction / snap.TotalPopulation
                : 0f;

            // Provincial stockpile stats
            if (econ.Provinces != null)
            {
                for (int i = 0; i < econ.Provinces.Length; i++)
                {
                    var pe = econ.Provinces[i];
                    if (pe == null) continue;
                    for (int g = 0; g < Goods.Count; g++)
                        snap.TotalProvincialStockpileByGood[g] += pe.Stockpile[g];
                    snap.TotalProvinceTreasury += pe.Treasury;
                    snap.TotalMonetaryTaxToRealm += pe.MonetaryTaxPaidToRealm;
                    snap.TotalProvinceAdminCost += pe.AdminCrownsCost;
                    snap.TotalTradeTollsCollected += pe.TradeTollsCollected;
                }
            }

            // Royal stockpile stats + treasury + trade
            snap.TotalTradeImportsByGood = new float[Goods.Count];
            snap.TotalTradeExportsByGood = new float[Goods.Count];
            snap.TotalRealmDeficitByGood = new float[Goods.Count];

            if (econ.Realms != null)
            {
                for (int i = 0; i < econ.Realms.Length; i++)
                {
                    var re = econ.Realms[i];
                    if (re == null) continue;
                    for (int g = 0; g < Goods.Count; g++)
                    {
                        snap.TotalRoyalStockpileByGood[g] += re.Stockpile[g];
                        snap.TotalTradeImportsByGood[g] += re.TradeImports[g];
                        snap.TotalTradeExportsByGood[g] += re.TradeExports[g];
                        snap.TotalRealmDeficitByGood[g] += re.Deficit[g];
                    }
                    snap.TotalRealmAdminCost += re.AdminCrownsCost;
                    snap.TotalTreasury += re.Treasury;
                    snap.TotalGoldMinted += re.GoldMinted;
                    snap.TotalSilverMinted += re.SilverMinted;
                    snap.TotalCrownsMinted += re.CrownsMinted;
                    snap.TotalTradeSpending += re.TradeSpending;
                    snap.TotalTradeRevenue += re.TradeRevenue;
                    snap.TotalCrossMarketTariffsCollected += re.TradeTariffsCollected;
                }
            }

            snap.TotalDomesticTreasury = snap.TotalCountyTreasury
                + snap.TotalProvinceTreasury + snap.TotalTreasury;

            // Market prices snapshot
            if (econ.MarketPrices != null)
            {
                snap.MarketPrices = new float[Goods.Count];
                Array.Copy(econ.MarketPrices, snap.MarketPrices, Goods.Count);
            }

            // Per-market prices snapshot
            if (econ.PerMarketPrices != null)
            {
                snap.PerMarketPrices = new float[econ.PerMarketPrices.Length][];
                for (int m = 1; m < econ.PerMarketPrices.Length; m++)
                {
                    if (econ.PerMarketPrices[m] != null)
                    {
                        snap.PerMarketPrices[m] = new float[Goods.Count];
                        Array.Copy(econ.PerMarketPrices[m], snap.PerMarketPrices[m], Goods.Count);
                    }
                }
            }

            // Backward-compat scalars = food values
            snap.TotalDucalRelief = snap.TotalDucalReliefByGood[Food];
            snap.TotalProvincialStockpile = snap.TotalProvincialStockpileByGood[Food];
            snap.TotalRoyalStockpile = snap.TotalRoyalStockpileByGood[Food];

            snap.MedianProductivity = econ.MedianProductivity[Food];

            // Virtual market state snapshot
            var vm = econ.VirtualMarket;
            if (vm != null)
            {
                snap.VMStock = new float[Goods.Count];
                snap.VMSellPrice = new float[Goods.Count];
                Array.Copy(vm.Stock, snap.VMStock, Goods.Count);
                Array.Copy(vm.SellPrice, snap.VMSellPrice, Goods.Count);
            }

            return snap;
        }
    }
}
