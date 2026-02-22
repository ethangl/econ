using System;
using System.Collections.Generic;
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
        const int Food = (int)GoodType.Bread;

        // Satisfaction weights: staple pool + individual basics
        // Total = StapleBudgetPerPop + sum(Basic ConsumptionPerPop)
        static readonly int[] IndividualBasicGoods;   // Non-staple Basic goods (salt, ale)
        static readonly float[] IndividualBasicWeights; // ConsumptionPerPop[g] / TotalSatisfactionDenom
        static readonly float StapleSatisfactionWeight; // StapleBudgetPerPop / TotalSatisfactionDenom

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
            int maxCountyId = 0;
            foreach (var county in mapData.Counties)
            {
                if (county.Id > maxCountyId)
                    maxCountyId = county.Id;
            }

            var econ = new EconomyState();
            econ.Counties = new CountyEconomy[maxCountyId + 1];

            foreach (var county in mapData.Counties)
            {
                var ce = new CountyEconomy();
                ce.Population = county.TotalPopulation;
                ComputeCountyProductivity(county, mapData, ce.Productivity);

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
                    int placementGood = (int)def.PlacementGood;

                    if (ce.Productivity[placementGood] > 0f
                        && ce.Productivity[placementGood] >= def.PlacementMinProductivity)
                    {
                        int idx = facilities.Count;
                        var facility = new Facility(def.Type, county.Id, county.SeatCellId);
                        facilities.Add(facility);
                        countyFacilityIndices[county.Id].Add(idx);
                    }
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

            // Resolve market county: first realm's capital burg → cell → county
            econ.MarketCountyId = ResolveMarketCounty(mapData, econ);

            state.Economy = econ;
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
            int goodsCount = Goods.Count;

            // Compute production capacity per good (for intermediate price discovery)
            var productionCap = econ.ProductionCapacity;
            Array.Clear(productionCap, 0, goodsCount);
            // Extraction capacity
            for (int i = 0; i < counties.Length; i++)
            {
                var ce = counties[i];
                if (ce == null) continue;
                for (int g = 0; g < goodsCount; g++)
                    productionCap[g] += ce.Population * ce.Productivity[g];
            }
            // Facility labor capacity
            var allFacilities = econ.Facilities;
            if (allFacilities != null)
            {
                for (int i = 0; i < counties.Length; i++)
                {
                    var ce = counties[i];
                    if (ce == null) continue;
                    var indices = countyFacilityIndices != null && i < countyFacilityIndices.Length
                        ? countyFacilityIndices[i] : null;
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

            for (int i = 0; i < counties.Length; i++)
            {
                var ce = counties[i];
                if (ce == null) continue;

                float pop = ce.Population;
                float wf = pop > 0 ? (pop - ce.FacilityWorkers) / pop : 1f;
                var indices = countyFacilityIndices != null && i < countyFacilityIndices.Length
                    ? countyFacilityIndices[i]
                    : null;

                // Compute facility input demand for trade retain signal + intermediate price discovery
                Array.Clear(ce.FacilityInputNeed, 0, goodsCount);
                if (indices != null && indices.Count > 0)
                {
                    for (int fi = 0; fi < indices.Count; fi++)
                    {
                        var def = econ.Facilities[indices[fi]].Def;
                        if (def.OutputAmount <= 0f) continue;

                        float maxByLabor = def.LaborPerUnit > 0
                            ? pop * def.MaxLaborFraction / def.LaborPerUnit * def.OutputAmount
                            : float.MaxValue;

                        float scale = maxByLabor / def.OutputAmount;
                        for (int ii = 0; ii < def.Inputs.Length; ii++)
                            ce.FacilityInputNeed[(int)def.Inputs[ii].Good] += scale * def.Inputs[ii].Amount;
                    }
                }

                // Production — all goods (extraction workforce reduced by facility labor)
                for (int g = 0; g < goodsCount; g++)
                {
                    float produced = pop * ce.Productivity[g] * wf;

                    // Price-based extraction throttle for intermediate goods
                    if (!Goods.HasDirectDemand[g] && Goods.BasePrice[g] > 0f)
                    {
                        float priceRatio = econ.MarketPrices[g] / Goods.BasePrice[g];
                        if (priceRatio < 1f) produced *= priceRatio;
                    }

                    ce.Stock[g] += produced;
                    ce.Production[g] = produced;
                }

                // Facility processing — input/labor constrained, price-throttled for intermediates
                if (indices != null && indices.Count > 0)
                {
                    float totalFacWorkers = 0f;
                    for (int fi = 0; fi < indices.Count; fi++)
                    {
                        var fac = econ.Facilities[indices[fi]];
                        var def = fac.Def;
                        int output = (int)def.OutputGood;

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
                            float dailyNeed = maintenance + gap * Goods.DurableCatchUpRate[output];
                            const float DurableBufferMultiplier = 3.0f;
                            throughput = Math.Min(throughput, dailyNeed * DurableBufferMultiplier);
                        }

                        // Price-based facility throttle (skip durables — they use stock-gap cap above)
                        if (Goods.BasePrice[output] > 0f && !Goods.IsDurable[output])
                        {
                            float priceRatio = econ.MarketPrices[output] / Goods.BasePrice[output];
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
                    ce.FacilityWorkers = totalFacWorkers;
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
                        float targetStock = pop * tgt;
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
                        float needed = pop * ConsumptionPerPop[g];
                        float consumed = Math.Min(ce.Stock[g], needed);
                        ce.Stock[g] -= consumed;
                        ce.Consumption[g] = consumed;
                        ce.UnmetNeed[g] = needed - consumed;
                    }
                }

                // Pooled staple consumption — people eat 1 kg/day of any combination
                float stapleBudget = pop * Goods.StapleBudgetPerPop;
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
                    ce.UnmetNeed[g] = Math.Max(0f, pop * Goods.StapleIdealPerPop[g] - ce.Consumption[g]);
                }

                // Basic-needs satisfaction EMA (alpha ≈ 2/(30+1) ≈ 0.065, ~30-day smoothing)
                // Staple pool fulfillment + individual basic fulfillment
                float stapleFulfillment = stapleBudget > 0f
                    ? Math.Min(1f, totalStapleConsumed / stapleBudget) : 1f;
                float daily = StapleSatisfactionWeight * stapleFulfillment;
                for (int b = 0; b < IndividualBasicGoods.Length; b++)
                {
                    int g = IndividualBasicGoods[b];
                    float needed = pop * ConsumptionPerPop[g];
                    float ratio = needed > 0f ? Math.Min(1f, ce.Consumption[g] / needed) : 1f;
                    daily += IndividualBasicWeights[b] * ratio;
                }
                ce.BasicSatisfaction += 0.065f * (daily - ce.BasicSatisfaction);
            }

            // Compute effective demand per pop (used by FiscalSystem)
            var demandPerPop = econ.EffectiveDemandPerPop;
            float totalPop = 0f;
            for (int i = 0; i < counties.Length; i++)
                if (counties[i] != null) totalPop += counties[i].Population;
            for (int g = 0; g < goodsCount; g++)
            {
                if (Goods.TargetStockPerPop[g] > 0f && totalPop > 0f)
                {
                    float totalDemand = 0f;
                    for (int i = 0; i < counties.Length; i++)
                    {
                        var c = counties[i];
                        if (c == null) continue;
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
                for (int g = 0; g < Goods.Count; g++)
                    output[g] += BiomeProductivity.Get(cell.BiomeId, (GoodType)g);

                // Coast-proximity fishing bonus
                switch (cell.CoastDistance)
                {
                    case 0: output[FishIdx] += 0.30f; break;
                    case 1: output[FishIdx] += 0.15f; break;
                    case 2: output[FishIdx] += 0.05f; break;
                }
            }

            if (landCells > 0)
            {
                for (int g = 0; g < Goods.Count; g++)
                    output[g] /= landCells;
            }
        }

        static int ResolveMarketCounty(MapData mapData, EconomyState econ)
        {
            // Try first realm's capital burg → cell → county
            if (mapData.Realms != null && mapData.Realms.Count > 0 && mapData.Burgs != null)
            {
                var realm = mapData.Realms[0];
                Burg burg = null;
                foreach (var b in mapData.Burgs)
                    if (b.Id == realm.CapitalBurgId) { burg = b; break; }

                if (burg != null)
                {
                    var cell = mapData.CellById[burg.CellId];
                    if (cell.CountyId > 0 && cell.CountyId < econ.Counties.Length
                        && econ.Counties[cell.CountyId] != null)
                        return cell.CountyId;
                }
            }

            // Fallback: county with highest population
            int bestId = -1;
            float bestPop = -1f;
            for (int i = 0; i < econ.Counties.Length; i++)
            {
                var ce = econ.Counties[i];
                if (ce != null && ce.Population > bestPop)
                {
                    bestPop = ce.Population;
                    bestId = i;
                }
            }
            return bestId;
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
            snap.TotalCrossRealmTradeBoughtByGood = new float[Goods.Count];
            snap.TotalCrossRealmTradeSoldByGood = new float[Goods.Count];

            int countyCount = 0;
            snap.MinBasicSatisfaction = float.MaxValue;
            snap.MaxBasicSatisfaction = float.MinValue;
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
                    snap.TotalCrossRealmTradeBoughtByGood[g] += ce.CrossRealmTradeBought[g];
                    snap.TotalCrossRealmTradeSoldByGood[g] += ce.CrossRealmTradeSold[g];
                }
                snap.TotalIntraProvTradeSpending += ce.TradeCrownsSpent;
                snap.TotalIntraProvTradeRevenue += ce.TradeCrownsEarned;
                snap.TotalCrossProvTradeSpending += ce.CrossProvTradeCrownsSpent;
                snap.TotalCrossProvTradeRevenue += ce.CrossProvTradeCrownsEarned;
                snap.TotalTradeTollsPaid += ce.TradeTollsPaid;
                snap.TotalCrossRealmTradeSpending += ce.CrossRealmTradeCrownsSpent;
                snap.TotalCrossRealmTradeRevenue += ce.CrossRealmTradeCrownsEarned;
                snap.TotalCrossRealmTollsPaid += ce.CrossRealmTollsPaid;
                snap.TotalCrossRealmTariffsPaid += ce.CrossRealmTariffsPaid;

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
                weightedSatisfaction += ce.Population * ce.BasicSatisfaction;
                if (ce.BasicSatisfaction < snap.MinBasicSatisfaction) snap.MinBasicSatisfaction = ce.BasicSatisfaction;
                if (ce.BasicSatisfaction > snap.MaxBasicSatisfaction) snap.MaxBasicSatisfaction = ce.BasicSatisfaction;
                if (ce.BasicSatisfaction < 0.5f) snap.CountiesInDistress++;

                snap.TotalCountyTreasury += ce.Treasury;
                snap.TotalMonetaryTaxToProvince += ce.MonetaryTaxPaid;
                snap.TotalGranaryRequisitionCrowns += ce.GranaryRequisitionCrownsReceived;
                snap.TotalMarketFeesCollected += ce.MarketFeesReceived;
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
            }

            snap.AvgBasicSatisfaction = snap.TotalPopulation > 0f
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
                    snap.TotalCrossRealmTariffsCollected += re.TradeTariffsCollected;
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

            // Backward-compat scalars = food values
            snap.TotalDucalRelief = snap.TotalDucalReliefByGood[Food];
            snap.TotalProvincialStockpile = snap.TotalProvincialStockpileByGood[Food];
            snap.TotalRoyalStockpile = snap.TotalRoyalStockpileByGood[Food];

            snap.MedianProductivity = econ.MedianProductivity[Food];

            return snap;
        }
    }
}
