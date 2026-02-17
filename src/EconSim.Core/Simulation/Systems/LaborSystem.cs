using System;
using System.Collections.Generic;
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
        private const int RebalanceSlices = 7;
        private readonly List<(Facility facility, FacilityDef def)> _countyFacilitiesBuffer = new List<(Facility facility, FacilityDef def)>();
        private readonly List<(Facility facility, FacilityDef def)> _activeFacilitiesBuffer = new List<(Facility facility, FacilityDef def)>();

        public string Name => "Labor";
        public int TickInterval => SimulationConfig.Intervals.Daily;

        public void Initialize(SimulationState state, MapData mapData)
        {
        }

        public void Tick(SimulationState state, MapData mapData)
        {
            var economy = state.Economy;
            if (economy == null)
                return;

            int slice = state.CurrentDay % RebalanceSlices;
            foreach (var county in economy.Counties.Values)
            {
                if (county.CountyId % RebalanceSlices != slice)
                    continue;

                _countyFacilitiesBuffer.Clear();
                foreach (int facilityId in county.FacilityIds)
                {
                    if (!economy.Facilities.TryGetValue(facilityId, out var facility))
                        continue;

                    var def = economy.FacilityDefs.Get(facility.TypeId);
                    if (def == null)
                        continue;

                    _countyFacilitiesBuffer.Add((facility, def));
                }

                HandleDistressedExit(_countyFacilitiesBuffer);

                ReallocateType(county, _countyFacilitiesBuffer, LaborType.Unskilled, state.SubsistenceWage);
                ReallocateType(county, _countyFacilitiesBuffer, LaborType.Skilled, state.SubsistenceWage);

                county.Population.SetEmployment(
                    SumAssigned(_countyFacilitiesBuffer, LaborType.Unskilled),
                    SumAssigned(_countyFacilitiesBuffer, LaborType.Skilled));
            }
        }

        private static void HandleDistressedExit(List<(Facility facility, FacilityDef def)> facilities)
        {
            for (int i = 0; i < facilities.Count; i++)
            {
                var (facility, def) = facilities[i];
                if (facility.WageDebtDays >= DistressedDebtDays)
                {
                    int retained = Math.Max(1, (int)Math.Ceiling(facility.GetRequiredLabor(def) * DistressedRetentionRatio));
                    facility.AssignedWorkers = Math.Min(facility.AssignedWorkers, retained);
                }
            }
        }

        private void ReallocateType(
            CountyEconomy county,
            List<(Facility facility, FacilityDef def)> facilities,
            LaborType laborType,
            float subsistenceWage)
        {
            _activeFacilitiesBuffer.Clear();
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

                int requiredLabor = pair.facility.GetRequiredLabor(pair.def);
                pair.facility.AssignedWorkers = Math.Max(0, Math.Min(pair.facility.AssignedWorkers, requiredLabor));
                _activeFacilitiesBuffer.Add(pair);
            }

            _activeFacilitiesBuffer.Sort(CompareFacilityPriority);

            int totalWorkers = laborType == LaborType.Unskilled
                ? county.Population.TotalUnskilled
                : county.Population.TotalSkilled;

            int employed = 0;
            for (int i = 0; i < _activeFacilitiesBuffer.Count; i++)
            {
                employed += _activeFacilitiesBuffer[i].facility.AssignedWorkers;
            }

            float maxWage = 0f;
            float minFill = 1f;
            if (_activeFacilitiesBuffer.Count > 0)
            {
                maxWage = _activeFacilitiesBuffer[0].facility.WageRate;
                for (int i = 0; i < _activeFacilitiesBuffer.Count; i++)
                {
                    float fill = GetFillRatio(_activeFacilitiesBuffer[i].facility, _activeFacilitiesBuffer[i].def);
                    if (fill < minFill)
                        minFill = fill;
                }
            }

            int reconsiderTarget = (int)(employed * ReconsiderationRate);
            int reconsidered = 0;
            if (reconsiderTarget > 0)
            {
                for (int i = _activeFacilitiesBuffer.Count - 1; i >= 0 && reconsidered < reconsiderTarget; i--)
                {
                    var pair = _activeFacilitiesBuffer[i];
                    int assigned = pair.facility.AssignedWorkers;
                    if (assigned <= 0)
                        continue;

                    if (!HasBetterOption(pair, maxWage, minFill))
                        continue;

                    int remaining = reconsiderTarget - reconsidered;
                    int pull = Math.Min(assigned, remaining);
                    pair.facility.AssignedWorkers -= pull;
                    reconsidered += pull;
                }
            }

            int currentlyAssigned = 0;
            for (int i = 0; i < _activeFacilitiesBuffer.Count; i++)
            {
                currentlyAssigned += _activeFacilitiesBuffer[i].facility.AssignedWorkers;
            }

            int idle = Math.Max(0, totalWorkers - currentlyAssigned);

            // Fill by wage ranking.
            for (int i = 0; i < _activeFacilitiesBuffer.Count; i++)
            {
                var pair = _activeFacilitiesBuffer[i];
                if (pair.facility.WageRate + 0.0001f < subsistenceWage)
                    continue;
                if (pair.facility.WageDebtDays >= DistressedDebtDays)
                    continue;

                int needed = Math.Max(0, pair.facility.GetRequiredLabor(pair.def) - pair.facility.AssignedWorkers);
                if (needed <= 0 || idle <= 0)
                    continue;

                int allocated = Math.Min(needed, idle);
                pair.facility.AssignedWorkers += allocated;
                idle -= allocated;
            }
        }

        private static int CompareFacilityPriority((Facility facility, FacilityDef def) a, (Facility facility, FacilityDef def) b)
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
            int reqCmp = a.facility.GetRequiredLabor(a.def).CompareTo(b.facility.GetRequiredLabor(b.def));
            if (reqCmp != 0)
                return reqCmp;

            return a.facility.Id.CompareTo(b.facility.Id);
        }

        private static bool HasBetterOption(
            (Facility facility, FacilityDef def) current,
            float maxWage,
            float minFill)
        {
            float currentWage = current.facility.WageRate;
            float threshold = currentWage * SwitchThreshold;
            float currentFill = GetFillRatio(current.facility, current.def);

            if (maxWage > threshold)
                return true;

            if (maxWage >= currentWage * RebalanceWageTolerance
                && minFill + RebalanceFillGap < currentFill)
                return true;

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
            int requiredLabor = facility.GetRequiredLabor(def);
            if (requiredLabor <= 0)
                return 1f;

            return (float)facility.AssignedWorkers / requiredLabor;
        }
    }
}
