using System;
using System.Collections.Generic;
using EconSim.Core.Common;
using EconSim.Core.Data;
using EconSim.Core.Economy;

namespace EconSim.Core.Simulation.Systems
{
    /// <summary>
    /// Economy V2 weekly labor allocation system.
    /// </summary>
    public class LaborSystem : ITickSystem
    {
        private const float ReconsiderationRate = 0.15f;
        private const float SwitchThreshold = 1.10f;

        public string Name => "Labor";
        public int TickInterval => SimulationConfig.Intervals.Weekly;

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
                var countyFacilities = new List<(Facility facility, FacilityDef def)>();
                foreach (int facilityId in county.FacilityIds)
                {
                    if (!economy.Facilities.TryGetValue(facilityId, out var facility))
                        continue;

                    var def = economy.FacilityDefs.Get(facility.TypeId);
                    if (def == null)
                        continue;

                    countyFacilities.Add((facility, def));
                }

                HandleDistressedExit(countyFacilities);

                ReallocateType(county, countyFacilities, LaborType.Unskilled, state.SubsistenceWage);
                ReallocateType(county, countyFacilities, LaborType.Skilled, state.SubsistenceWage);

                county.Population.SetEmployment(
                    SumAssigned(countyFacilities, LaborType.Unskilled),
                    SumAssigned(countyFacilities, LaborType.Skilled));
            }
        }

        private static void HandleDistressedExit(List<(Facility facility, FacilityDef def)> facilities)
        {
            for (int i = 0; i < facilities.Count; i++)
            {
                var (facility, _) = facilities[i];
                if (facility.WageDebtDays >= 3)
                {
                    facility.AssignedWorkers = 0;
                }
            }
        }

        private static void ReallocateType(
            CountyEconomy county,
            List<(Facility facility, FacilityDef def)> facilities,
            LaborType laborType,
            float subsistenceWage)
        {
            var active = new List<(Facility facility, FacilityDef def)>();
            for (int i = 0; i < facilities.Count; i++)
            {
                var pair = facilities[i];
                if (pair.def.LaborType != laborType)
                    continue;

                if (!pair.facility.IsActive || pair.facility.WageDebtDays >= 3)
                {
                    pair.facility.AssignedWorkers = 0;
                    continue;
                }

                pair.facility.AssignedWorkers = Math.Max(0, Math.Min(pair.facility.AssignedWorkers, pair.def.LaborRequired));
                active.Add(pair);
            }

            active.Sort(
                DeterministicHelpers.WithStableTieBreak<(Facility facility, FacilityDef def), int>(
                    (a, b) => b.facility.WageRate.CompareTo(a.facility.WageRate),
                    pair => pair.facility.Id));

            int totalWorkers = laborType == LaborType.Unskilled
                ? county.Population.TotalUnskilled
                : county.Population.TotalSkilled;

            int employed = 0;
            for (int i = 0; i < active.Count; i++)
            {
                employed += active[i].facility.AssignedWorkers;
            }

            int reconsiderTarget = (int)(employed * ReconsiderationRate);
            int reconsidered = 0;
            if (reconsiderTarget > 0)
            {
                for (int i = active.Count - 1; i >= 0 && reconsidered < reconsiderTarget; i--)
                {
                    var pair = active[i];
                    int assigned = pair.facility.AssignedWorkers;
                    if (assigned <= 0)
                        continue;

                    if (!HasBetterOption(pair.facility.WageRate, active))
                        continue;

                    int remaining = reconsiderTarget - reconsidered;
                    int pull = Math.Min(assigned, remaining);
                    pair.facility.AssignedWorkers -= pull;
                    reconsidered += pull;
                }
            }

            int currentlyAssigned = 0;
            for (int i = 0; i < active.Count; i++)
            {
                currentlyAssigned += active[i].facility.AssignedWorkers;
            }

            int idle = Math.Max(0, totalWorkers - currentlyAssigned);

            // Fill by wage ranking.
            for (int i = 0; i < active.Count; i++)
            {
                var pair = active[i];
                if (pair.facility.WageRate + 0.0001f < subsistenceWage)
                    continue;

                int needed = Math.Max(0, pair.def.LaborRequired - pair.facility.AssignedWorkers);
                if (needed <= 0 || idle <= 0)
                    continue;

                int allocated = Math.Min(needed, idle);
                pair.facility.AssignedWorkers += allocated;
                idle -= allocated;
            }
        }

        private static bool HasBetterOption(float currentWage, List<(Facility facility, FacilityDef def)> facilities)
        {
            float threshold = currentWage * SwitchThreshold;
            for (int i = 0; i < facilities.Count; i++)
            {
                if (facilities[i].facility.WageRate > threshold)
                    return true;
            }

            return false;
        }

        private static int SumAssigned(List<(Facility facility, FacilityDef def)> facilities, LaborType laborType)
        {
            int sum = 0;
            for (int i = 0; i < facilities.Count; i++)
            {
                if (facilities[i].def.LaborType == laborType)
                    sum += facilities[i].facility.AssignedWorkers;
            }

            return sum;
        }
    }
}
