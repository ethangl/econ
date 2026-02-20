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

                // Seed durable goods proportional to population
                for (int g = 0; g < Goods.Count; g++)
                    if (Goods.TargetStockPerPop[g] > 0f)
                        ce.Stock[g] = ce.Population * Goods.TargetStockPerPop[g];

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

            state.Economy = econ;
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

            for (int i = 0; i < counties.Length; i++)
            {
                var ce = counties[i];
                if (ce == null) continue;

                float pop = ce.Population;
                float wf = pop > 0 ? (pop - ce.FacilityWorkers) / pop : 1f;
                var indices = countyFacilityIndices != null && i < countyFacilityIndices.Length
                    ? countyFacilityIndices[i]
                    : null;

                // Compute all local facility input demand once; pure-input extraction reuses this per good.
                Span<float> facilityInputDemand = stackalloc float[Goods.Count];
                if (indices != null && indices.Count > 0)
                {
                    for (int fi = 0; fi < indices.Count; fi++)
                    {
                        var def = econ.Facilities[indices[fi]].Def;
                        if (def.OutputAmount <= 0f) continue;

                        float target = Math.Max(def.BaselineOutput, ce.FacilityQuota[(int)def.OutputGood]);
                        float maxByLabor = def.LaborPerUnit > 0
                            ? pop * def.MaxLaborFraction / def.LaborPerUnit * def.OutputAmount
                            : float.MaxValue;
                        target = Math.Min(target, maxByLabor);

                        float scale = target / def.OutputAmount;
                        for (int ii = 0; ii < def.Inputs.Length; ii++)
                            facilityInputDemand[(int)def.Inputs[ii].Good] += scale * def.Inputs[ii].Amount;
                    }
                }

                // Production — all goods (extraction workforce reduced by facility labor)
                for (int g = 0; g < goodsCount; g++)
                {
                    float produced;
                    if (!Goods.HasDirectDemand[g] && !Goods.IsPreciousMetal(g))
                    {
                        // Facility-input-only: extract only what local facilities need.
                        produced = Math.Min(facilityInputDemand[g], pop * ce.Productivity[g]);
                    }
                    else
                    {
                        produced = pop * ce.Productivity[g] * wf;
                    }
                    ce.Stock[g] += produced;
                    ce.Production[g] = produced;
                }

                // Facility processing — demand-driven, realm-quota-aware
                if (indices != null && indices.Count > 0)
                {
                    float totalFacWorkers = 0f;
                    for (int fi = 0; fi < indices.Count; fi++)
                    {
                        var fac = econ.Facilities[indices[fi]];
                        var def = fac.Def;
                        int output = (int)def.OutputGood;

                        // Target: realm quota (includes local + admin + redistribution needs)
                        // Falls back to baseline on first tick when quota is 0
                        float target = Math.Max(def.BaselineOutput, ce.FacilityQuota[output]);

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

                        float throughput = Math.Min(target, Math.Min(maxByInput, maxByLabor));
                        if (throughput < 0f) throughput = 0f;

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
                        float deficit = Math.Max(0f, targetStock - ce.Stock[g]);
                        float replacement = ce.Stock[g] * Goods.Defs[g].SpoilageRate;
                        float wear = Math.Min(ce.Stock[g], replacement);
                        ce.Stock[g] -= wear;
                        ce.Consumption[g] = wear;
                        ce.UnmetNeed[g] = deficit * 0.1f + Math.Max(0f, replacement - wear);
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

            // Record snapshot
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
            snap.TotalDucalTaxByGood = new float[Goods.Count];
            snap.TotalDucalReliefByGood = new float[Goods.Count];
            snap.TotalProvincialStockpileByGood = new float[Goods.Count];
            snap.TotalRoyalTaxByGood = new float[Goods.Count];
            snap.TotalRoyalReliefByGood = new float[Goods.Count];
            snap.TotalRoyalStockpileByGood = new float[Goods.Count];

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
                    snap.TotalDucalTaxByGood[g] += ce.TaxPaid[g];
                    snap.TotalDucalReliefByGood[g] += ce.Relief[g];
                }

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
                    snap.StarvingCounties++;
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
                        snap.TotalRoyalTaxByGood[g] += re.TaxCollected[g];
                        snap.TotalRoyalReliefByGood[g] += re.ReliefGiven[g];
                        snap.TotalRoyalStockpileByGood[g] += re.Stockpile[g];
                        snap.TotalTradeImportsByGood[g] += re.TradeImports[g];
                        snap.TotalTradeExportsByGood[g] += re.TradeExports[g];
                        snap.TotalRealmDeficitByGood[g] += re.Deficit[g];
                    }
                    snap.TotalTreasury += re.Treasury;
                    snap.TotalGoldMinted += re.GoldMinted;
                    snap.TotalSilverMinted += re.SilverMinted;
                    snap.TotalCrownsMinted += re.CrownsMinted;
                    snap.TotalTradeSpending += re.TradeSpending;
                    snap.TotalTradeRevenue += re.TradeRevenue;
                }
            }

            // Market prices snapshot
            if (econ.MarketPrices != null)
            {
                snap.MarketPrices = new float[Goods.Count];
                Array.Copy(econ.MarketPrices, snap.MarketPrices, Goods.Count);
            }

            // Backward-compat scalars = food values
            snap.TotalDucalTax = snap.TotalDucalTaxByGood[Food];
            snap.TotalDucalRelief = snap.TotalDucalReliefByGood[Food];
            snap.TotalProvincialStockpile = snap.TotalProvincialStockpileByGood[Food];
            snap.TotalRoyalTax = snap.TotalRoyalTaxByGood[Food];
            snap.TotalRoyalRelief = snap.TotalRoyalReliefByGood[Food];
            snap.TotalRoyalStockpile = snap.TotalRoyalStockpileByGood[Food];

            snap.MedianProductivity = econ.MedianProductivity[Food];

            return snap;
        }
    }
}
