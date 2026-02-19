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
        const int Food = (int)GoodType.Food;

        // Basic-needs satisfaction: precomputed indices and weights
        static readonly int[] BasicGoods;
        static readonly float[] BasicWeights;  // ConsumptionPerPop[g] / totalBasicConsumption
        static readonly float TotalBasicConsumption;

        static EconomySystem()
        {
            int count = 0;
            float total = 0f;
            for (int g = 0; g < Goods.Count; g++)
            {
                if (Goods.Defs[g].Need == NeedCategory.Basic)
                {
                    count++;
                    total += ConsumptionPerPop[g];
                }
            }

            BasicGoods = new int[count];
            BasicWeights = new float[count];
            TotalBasicConsumption = total;
            int idx = 0;
            for (int g = 0; g < Goods.Count; g++)
            {
                if (Goods.Defs[g].Need == NeedCategory.Basic)
                {
                    BasicGoods[idx] = g;
                    BasicWeights[idx] = total > 0f ? ConsumptionPerPop[g] / total : 0f;
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

            for (int i = 0; i < counties.Length; i++)
            {
                var ce = counties[i];
                if (ce == null) continue;

                float pop = ce.Population;
                float wf = pop > 0 ? (pop - ce.FacilityWorkers) / pop : 1f;

                // Production — all goods (extraction workforce reduced by facility labor)
                for (int g = 0; g < Goods.Count; g++)
                {
                    float produced = pop * ce.Productivity[g] * wf;
                    ce.Stock[g] += produced;
                    ce.Production[g] = produced;
                }

                // Facility processing — consume input, produce output (same-day availability)
                var facIndices = econ.CountyFacilityIndices;
                if (facIndices != null && i < facIndices.Length && facIndices[i] != null)
                {
                    var indices = facIndices[i];
                    for (int fi = 0; fi < indices.Count; fi++)
                    {
                        var fac = econ.Facilities[indices[fi]];
                        var def = fac.Def;
                        int input = (int)def.InputGood;
                        int output = (int)def.OutputGood;

                        float available = ce.Stock[input];
                        float needed = def.InputAmount;
                        float ratio = Math.Min(1f, available / needed);

                        ce.Stock[input] -= needed * ratio;
                        float produced = def.OutputAmount * ratio;
                        ce.Stock[output] += produced;
                        ce.Production[output] += produced;
                    }
                }

                // Consumption — all goods
                for (int g = 0; g < Goods.Count; g++)
                {
                    float needed = pop * ConsumptionPerPop[g];
                    float consumed = Math.Min(ce.Stock[g], needed);
                    ce.Stock[g] -= consumed;
                    ce.Consumption[g] = consumed;
                    ce.UnmetNeed[g] = needed - consumed;
                }

                // Basic-needs satisfaction EMA (alpha ≈ 2/(30+1) ≈ 0.065, ~30-day smoothing)
                // Weighted average of per-good fulfillment across all Basic needs
                float daily = 0f;
                for (int b = 0; b < BasicGoods.Length; b++)
                {
                    int g = BasicGoods[b];
                    float needed = pop * ConsumptionPerPop[g];
                    float ratio = needed > 0f ? Math.Min(1f, ce.Consumption[g] / needed) : 1f;
                    daily += BasicWeights[b] * ratio;
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

            foreach (int cellId in county.CellIds)
            {
                var cell = mapData.CellById[cellId];
                if (!cell.IsLand) continue;
                landCells++;
                for (int g = 0; g < Goods.Count; g++)
                    output[g] += BiomeProductivity.Get(cell.BiomeId, (GoodType)g);
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

                if (ce.UnmetNeed[Food] > 0)
                    snap.StarvingCounties++;
                else if (ce.Production[Food] < ce.Consumption[Food])
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
