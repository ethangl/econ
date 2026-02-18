using System;
using System.Collections.Generic;
using EconSim.Core.Data;
using EconSim.Core.Simulation;
using EconSim.Core.Simulation.Systems;

namespace EconSim.Core.Economy
{
    /// <summary>
    /// Single-good production/consumption loop. Each county is an isolated autarky.
    /// </summary>
    public class EconomySystem : ITickSystem
    {
        public string Name => "Economy";
        public int TickInterval => SimulationConfig.Intervals.Daily;

        const float ConsumptionPerPop = 1.0f;

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
                ce.Productivity = ComputeCountyProductivity(county, mapData);
                econ.Counties[county.Id] = ce;
            }

            // Compute median productivity once (it's static)
            var productivities = new List<float>(mapData.Counties.Count);
            foreach (var county in mapData.Counties)
                productivities.Add(econ.Counties[county.Id].Productivity);
            productivities.Sort();
            if (productivities.Count > 0)
            {
                int mid = productivities.Count / 2;
                econ.MedianProductivity = productivities.Count % 2 == 0
                    ? (productivities[mid - 1] + productivities[mid]) / 2f
                    : productivities[mid];
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

                // Production
                float produced = pop * ce.Productivity;
                ce.Stock += produced;
                ce.Production = produced;

                // Consumption
                float needed = pop * ConsumptionPerPop;
                float consumed = Math.Min(ce.Stock, needed);
                ce.Stock -= consumed;
                ce.Consumption = consumed;
                ce.UnmetNeed = needed - consumed;
            }

            // Record snapshot
            econ.TimeSeries.Add(BuildSnapshot(state.CurrentDay, econ));
        }

        static float ComputeCountyProductivity(County county, MapData mapData)
        {
            int landCells = 0;
            float totalProductivity = 0f;

            foreach (int cellId in county.CellIds)
            {
                var cell = mapData.CellById[cellId];
                if (!cell.IsLand) continue;
                landCells++;
                totalProductivity += BiomeProductivity.Get(cell.BiomeId);
            }

            return landCells > 0 ? totalProductivity / landCells : 0f;
        }

        static EconomySnapshot BuildSnapshot(int day, EconomyState econ)
        {
            var snap = new EconomySnapshot();
            snap.Day = day;
            snap.MinStock = float.MaxValue;
            snap.MaxStock = float.MinValue;

            int countyCount = 0;

            for (int i = 0; i < econ.Counties.Length; i++)
            {
                var ce = econ.Counties[i];
                if (ce == null) continue;

                countyCount++;
                snap.TotalStock += ce.Stock;
                snap.TotalProduction += ce.Production;
                snap.TotalConsumption += ce.Consumption;
                snap.TotalUnmetNeed += ce.UnmetNeed;
                snap.TotalDucalTax += ce.TaxPaid;
                snap.TotalDucalRelief += ce.Relief;

                if (ce.Stock < snap.MinStock) snap.MinStock = ce.Stock;
                if (ce.Stock > snap.MaxStock) snap.MaxStock = ce.Stock;

                if (ce.UnmetNeed > 0)
                    snap.StarvingCounties++;
                else if (ce.Production < ce.Consumption)
                    snap.DeficitCounties++;
                else
                    snap.SurplusCounties++;
            }

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
                    snap.TotalProvincialStockpile += pe.Stockpile;
                }
            }

            // Royal stockpile stats
            if (econ.Realms != null)
            {
                for (int i = 0; i < econ.Realms.Length; i++)
                {
                    var re = econ.Realms[i];
                    if (re == null) continue;
                    snap.TotalRoyalTax += re.TaxCollected;
                    snap.TotalRoyalRelief += re.ReliefGiven;
                    snap.TotalRoyalStockpile += re.Stockpile;
                }
            }

            snap.MedianProductivity = econ.MedianProductivity;

            return snap;
        }
    }
}
