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
    /// Consumption: food only (Phase A). Timber/Ore accumulate.
    /// </summary>
    public class EconomySystem : ITickSystem
    {
        public string Name => "Economy";
        public int TickInterval => SimulationConfig.Intervals.Daily;

        const float ConsumptionPerPop = 1.0f;
        const int Food = (int)GoodType.Food;

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

            state.Economy = econ;
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

                // Production — all goods
                for (int g = 0; g < Goods.Count; g++)
                {
                    float produced = pop * ce.Productivity[g];
                    ce.Stock[g] += produced;
                    ce.Production[g] = produced;
                }

                // Consumption — food only (Phase A)
                float needed = pop * ConsumptionPerPop;
                float consumed = Math.Min(ce.Stock[Food], needed);
                ce.Stock[Food] -= consumed;
                ce.Consumption[Food] = consumed;
                ce.UnmetNeed[Food] = needed - consumed;

                // Timber/Ore: no consumption yet (Phase B)
                ce.Consumption[(int)GoodType.Timber] = 0f;
                ce.Consumption[(int)GoodType.Ore] = 0f;
                ce.UnmetNeed[(int)GoodType.Timber] = 0f;
                ce.UnmetNeed[(int)GoodType.Ore] = 0f;
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

            int countyCount = 0;

            for (int i = 0; i < econ.Counties.Length; i++)
            {
                var ce = econ.Counties[i];
                if (ce == null) continue;

                countyCount++;

                for (int g = 0; g < Goods.Count; g++)
                {
                    snap.TotalStockByGood[g] += ce.Stock[g];
                    snap.TotalProductionByGood[g] += ce.Production[g];
                }

                snap.TotalConsumption += ce.Consumption[Food];
                snap.TotalUnmetNeed += ce.UnmetNeed[Food];
                snap.TotalDucalTax += ce.TaxPaid[Food];
                snap.TotalDucalRelief += ce.Relief[Food];

                float foodStock = ce.Stock[Food];
                if (foodStock < snap.MinStock) snap.MinStock = foodStock;
                if (foodStock > snap.MaxStock) snap.MaxStock = foodStock;

                if (ce.UnmetNeed[Food] > 0)
                    snap.StarvingCounties++;
                else if (ce.Production[Food] < ce.Consumption[Food])
                    snap.DeficitCounties++;
                else
                    snap.SurplusCounties++;
            }

            // Backward-compat scalars = food values
            snap.TotalStock = snap.TotalStockByGood[Food];
            snap.TotalProduction = snap.TotalProductionByGood[Food];

            if (countyCount == 0)
            {
                snap.MinStock = 0;
                snap.MaxStock = 0;
            }

            // Provincial stockpile stats
            if (econ.Provinces != null)
            {
                for (int i = 0; i < econ.Provinces.Length; i++)
                {
                    var pe = econ.Provinces[i];
                    if (pe == null) continue;
                    snap.TotalProvincialStockpile += pe.Stockpile[Food];
                }
            }

            // Royal stockpile stats
            if (econ.Realms != null)
            {
                for (int i = 0; i < econ.Realms.Length; i++)
                {
                    var re = econ.Realms[i];
                    if (re == null) continue;
                    snap.TotalRoyalTax += re.TaxCollected[Food];
                    snap.TotalRoyalRelief += re.ReliefGiven[Food];
                    snap.TotalRoyalStockpile += re.Stockpile[Food];
                }
            }

            snap.MedianProductivity = econ.MedianProductivity[Food];

            return snap;
        }
    }
}
