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
        private const float RebalanceWageTolerance = 0.98f;
        private const float RebalanceFillGap = 0.35f;
        private const int DistressedDebtDays = 60;
        private const float DistressedRetentionRatio = 0.75f;

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
                var (facility, def) = facilities[i];
                if (facility.WageDebtDays >= DistressedDebtDays)
                {
                    int retained = Math.Max(1, (int)Math.Ceiling(def.LaborRequired * DistressedRetentionRatio));
                    facility.AssignedWorkers = Math.Min(facility.AssignedWorkers, retained);
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

                if (!pair.facility.IsActive)
                {
                    pair.facility.AssignedWorkers = 0;
                    continue;
                }

                pair.facility.AssignedWorkers = Math.Max(0, Math.Min(pair.facility.AssignedWorkers, pair.def.LaborRequired));
                active.Add(pair);
            }

            active.Sort((a, b) =>
            {
                int wageCmp = b.facility.WageRate.CompareTo(a.facility.WageRate);
                if (wageCmp != 0)
                    return wageCmp;

                float aFill = GetFillRatio(a.facility, a.def);
                float bFill = GetFillRatio(b.facility, b.def);
                int fillCmp = aFill.CompareTo(bFill); // Less-staffed facilities get priority.
                if (fillCmp != 0)
                    return fillCmp;

                // When wages/fill are equal, favor lower labor requirements so scarce labor
                // can seed more facilities instead of concentrating in a few.
                int reqCmp = a.def.LaborRequired.CompareTo(b.def.LaborRequired);
                if (reqCmp != 0)
                    return reqCmp;

                return a.facility.Id.CompareTo(b.facility.Id);
            });

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

                    if (!HasBetterOption(pair, active))
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

        private static bool HasBetterOption(
            (Facility facility, FacilityDef def) current,
            List<(Facility facility, FacilityDef def)> facilities)
        {
            float currentWage = current.facility.WageRate;
            float threshold = currentWage * SwitchThreshold;
            float currentFill = GetFillRatio(current.facility, current.def);

            for (int i = 0; i < facilities.Count; i++)
            {
                var candidate = facilities[i];
                if (candidate.facility.Id == current.facility.Id)
                    continue;

                if (candidate.facility.WageRate > threshold)
                    return true;

                // If wages are near-parity, still allow movement toward much emptier facilities
                // so labor doesn't deadlock into arbitrary early assignments.
                if (candidate.facility.WageRate >= currentWage * RebalanceWageTolerance)
                {
                    float candidateFill = GetFillRatio(candidate.facility, candidate.def);
                    if (candidateFill + RebalanceFillGap < currentFill)
                        return true;
                }
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

        private static float GetFillRatio(Facility facility, FacilityDef def)
        {
            if (def.LaborRequired <= 0)
                return 1f;

            return (float)facility.AssignedWorkers / def.LaborRequired;
        }
    }
}
