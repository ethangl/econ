using System;
using System.Collections.Generic;
using EconSim.Core.Data;
using EconSim.Core.Simulation;
using EconSim.Core.Simulation.Systems;

namespace EconSim.Core.Economy
{
    /// <summary>
    /// Places facilities in eligible counties at init, then runs their production
    /// recipes each tick (consuming input, producing output).
    /// Runs after EconomySystem (extraction) and before TradeSystem.
    /// </summary>
    public class FacilityProductionSystem : ITickSystem
    {
        public string Name => "FacilityProduction";
        public int TickInterval => SimulationConfig.Intervals.Daily;

        public void Initialize(SimulationState state, MapData mapData)
        {
            var econ = state.Economy;
            int maxCountyId = econ.Counties.Length - 1;

            var facilities = new List<Facility>();
            var countyIndices = new List<int>[maxCountyId + 1];
            for (int i = 0; i <= maxCountyId; i++)
                countyIndices[i] = new List<int>();

            foreach (var county in mapData.Counties)
            {
                var ce = econ.Counties[county.Id];
                if (ce == null) continue;

                for (int f = 0; f < Facilities.Count; f++)
                {
                    var def = Facilities.Defs[f];
                    int inputGood = (int)def.InputGood;

                    if (ce.Productivity[inputGood] >= def.PlacementMinProductivity)
                    {
                        int idx = facilities.Count;
                        var facility = new Facility(def.Type, county.Id, county.SeatCellId);
                        facility.Workforce = def.LaborPerUnit;
                        facilities.Add(facility);
                        countyIndices[county.Id].Add(idx);
                        ce.FacilityWorkers += def.LaborPerUnit;
                    }
                }
            }

            econ.Facilities = facilities.ToArray();
            econ.CountyFacilityIndices = countyIndices;
        }

        public void Tick(SimulationState state, MapData mapData)
        {
            // Facility processing moved to EconomySystem.Tick() so output is
            // available for same-day consumption and tracked in ce.Production[].
        }
    }
}
