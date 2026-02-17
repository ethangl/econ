using System;
using System.Collections.Generic;
using EconSim.Core.Common;
using EconSim.Core.Data;
using EconSim.Core.Economy;

namespace EconSim.Core.Simulation.Systems
{
    /// <summary>
    /// Handles extraction and processing production with persistent staffing,
    /// profitability gating, input/output buffers, and market consignments.
    /// </summary>
    public class ProductionSystem : ITickSystem
    {
        public string Name => "Production";
        public int TickInterval => SimulationConfig.Intervals.Daily;

        private const float V2SubsistenceFraction = 0.20f;
        private const float V2TransportLossRate = 0.01f;
        private const float V2TransportFeeRate = 0.005f;
        private const int V2LossDaysToDeactivate = 42;
        private const int V2TreasuryBufferDays = 5;
        private const float V2ReactivationWageLossTolerance = 0.75f;
        private const float V2LossSeverityWageFraction = 0.10f;
        private const float V2ReactivationLaborFloorRatio = 0.25f;
        private const float V2DemandPressureRatio = 1.05f;
        private const float V2UnemploymentPressureRatio = 0.20f;
        private const int V2GraceDaysOnActivation = 21;
        private const int V2InactiveRecheckDays = 7;
        private const int V2InactiveExtractionRecheckDays = 3;
        private const int V2InactiveMediumDormancyDays = 30;
        private const int V2InactiveLongDormancyDays = 120;
        private const float V2FacilityAskMarkupUnsubsidized = 0.12f;
        private const float V2FacilityAskMarkupSubsidized = 0.03f;
        private const float V2FacilityAskBaseFloorMultiplierUnsubsidized = 0.25f;
        private const float V2FacilityAskBaseFloorMultiplierSubsidized = 0.10f;

        private readonly Dictionary<string, float> _producedThisTick = new Dictionary<string, float>();
        private readonly List<KeyValuePair<int, float>> _outputGoodsBuffer = new List<KeyValuePair<int, float>>(8);
        private readonly List<int> _inputRuntimeIdsBuffer = new List<int>(8);
        private readonly Dictionary<int, int> _availableUnskilledByCounty = new Dictionary<int, int>();
        private readonly Dictionary<int, int> _availableSkilledByCounty = new Dictionary<int, int>();
        private readonly Dictionary<string, int> _goodRuntimeIdCache = new Dictionary<string, int>();

        public void Initialize(SimulationState state, MapData mapData)
        {
            var economy = state.Economy;
            if (economy == null) return;

            var facilityCounts = new Dictionary<string, int>();
            var unitCounts = new Dictionary<string, int>();
            var facilities = economy.GetFacilitiesDense();
            for (int i = 0; i < facilities.Count; i++)
            {
                var f = facilities[i];
                if (!facilityCounts.ContainsKey(f.TypeId))
                {
                    facilityCounts[f.TypeId] = 0;
                    unitCounts[f.TypeId] = 0;
                }
                facilityCounts[f.TypeId]++;
                unitCounts[f.TypeId] += Math.Max(1, f.UnitCount);
            }

            SimLog.Log("Production", $"Initialized with {economy.Facilities.Count} facility clusters:");
            foreach (var kvp in facilityCounts)
            {
                SimLog.Log("Production", $"  {kvp.Key}: clusters={kvp.Value}, units={unitCounts[kvp.Key]}");
            }
        }

        public void Tick(SimulationState state, MapData mapData)
        {
            TickV2(state, mapData);
        }

        private void TickV2(SimulationState state, MapData mapData)
        {
            var economy = state.Economy;
            if (economy == null) return;

            _producedThisTick.Clear();
            BuildAvailableWorkerCaches(economy);
            ApplyFacilitySubsidiesV2(state, economy);

            var facilities = economy.GetFacilitiesDense();
            for (int i = 0; i < facilities.Count; i++)
            {
                var facility = facilities[i];
                facility.BeginDayMetrics(state.CurrentDay);

                var def = economy.FacilityDefs.Get(facility.TypeId);
                if (def == null)
                    continue;

                if (facility.IsActive)
                {
                    facility.InactiveDays = 0;
                }
                else
                {
                    facility.InactiveDays = Math.Min(int.MaxValue - 1, Math.Max(0, facility.InactiveDays) + 1);
                    if (!ShouldEvaluateInactiveFacility(state.CurrentDay, facility, def.IsExtraction))
                        continue;
                }

                ApplyActivationRulesV2(state, mapData, economy, facility, def, _availableUnskilledByCounty, _availableSkilledByCounty);

                if (!facility.IsActive)
                {
                    // A facility may deactivate during this tick from active state.
                    if (facility.InactiveDays <= 0)
                        facility.InactiveDays = 1;
                    continue;
                }

                facility.InactiveDays = 0;
                if (facility.AssignedWorkers <= 0)
                    continue;

                if (def.IsExtraction)
                    RunExtractionFacilityV2(economy, facility, def);
                else
                    RunProcessingFacilityV2(economy, facility, def);

                FlushOutputBufferToMarketV2(state, mapData, economy, facility, def);
            }
        }

        private static void ApplyFacilitySubsidiesV2(SimulationState state, EconomyState economy)
        {
            if (!SimulationConfig.Economy.EnableFacilitySubsidies)
                return;

            float subsistence = state.SubsistenceWage > 0f ? state.SubsistenceWage : 1f;
            float treasuryFloorDays = Math.Max(0f, SimulationConfig.Economy.FacilityTreasuryFloorDays);
            int wageDebtRelief = Math.Max(0, SimulationConfig.Economy.FacilityWageDebtReliefPerDay);

            var facilities = economy.GetFacilitiesDense();
            for (int i = 0; i < facilities.Count; i++)
            {
                var facility = facilities[i];
                if (!facility.IsActive)
                    continue;

                var def = economy.FacilityDefs.Get(facility.TypeId);
                if (def == null)
                    continue;

                int requiredLabor = Math.Max(1, facility.GetRequiredLabor(def));
                float treasuryFloor = requiredLabor * subsistence * treasuryFloorDays;
                if (facility.Treasury < treasuryFloor)
                    facility.Treasury = treasuryFloor;

                if (wageDebtRelief > 0 && facility.WageDebtDays > 0)
                    facility.WageDebtDays = Math.Max(0, facility.WageDebtDays - wageDebtRelief);
            }
        }

        private void ApplyActivationRulesV2(
            SimulationState state,
            MapData mapData,
            EconomyState economy,
            Facility facility,
            FacilityDef def,
            Dictionary<int, int> availableUnskilledByCounty,
            Dictionary<int, int> availableSkilledByCounty)
        {
            float subsistence = state.SubsistenceWage > 0f ? state.SubsistenceWage : 1f;

            if (facility.IsActive)
            {
                // Keep extractive sectors stable to avoid raw-input starvation cascades.
                if (def.IsExtraction)
                    return;

                if (facility.AssignedWorkers <= 0)
                {
                    if (facility.GraceDaysRemaining > 0)
                    {
                        facility.GraceDaysRemaining--;
                        return;
                    }

                    facility.IsActive = false;
                    return;
                }

                int requiredLabor = Math.Max(1, facility.GetRequiredLabor(def));
                int staffedWorkers = Math.Max(1, facility.AssignedWorkers > 0 ? facility.AssignedWorkers : requiredLabor);
                float severeLossThreshold = -subsistence * staffedWorkers * V2LossSeverityWageFraction;
                if (facility.RollingProfit < severeLossThreshold)
                {
                    facility.ConsecutiveLossDays++;
                }
                else if (facility.ConsecutiveLossDays > 0)
                {
                    // Let neutral/positive days unwind loss streaks gradually instead of hard-resets.
                    facility.ConsecutiveLossDays--;
                }

                if (facility.GraceDaysRemaining > 0)
                {
                    facility.GraceDaysRemaining--;
                    return;
                }

                float treasuryShutdownBuffer = staffedWorkers * subsistence * V2TreasuryBufferDays;
                if (facility.ConsecutiveLossDays >= V2LossDaysToDeactivate
                    && facility.Treasury <= treasuryShutdownBuffer)
                {
                    int releasedWorkers = facility.AssignedWorkers;
                    facility.IsActive = false;
                    facility.AssignedWorkers = 0;
                    if (releasedWorkers > 0)
                        AdjustAvailableWorkers(availableUnskilledByCounty, availableSkilledByCounty, facility.CountyId, def.LaborType, releasedWorkers);
                }

                return;
            }

            int availableWorkers = GetAvailableWorkers(availableUnskilledByCounty, availableSkilledByCounty, facility.CountyId, def.LaborType);
            int requiredLaborForActivation = Math.Max(1, facility.GetRequiredLabor(def));
            if (availableWorkers < Math.Max(1, (int)Math.Ceiling(requiredLaborForActivation * V2ReactivationLaborFloorRatio)))
                return;

            var market = economy.GetMarketForCounty(facility.CountyId);
            if (market == null)
                return;

            float transportCost = economy.GetCountyTransportCost(facility.CountyId);
            float sellEfficiency = 1f / (1f + transportCost * V2TransportLossRate);

            int outputRuntimeId = ResolveRuntimeId(economy.Goods, def.OutputGoodId);
            if (outputRuntimeId < 0)
                return;

            if (!market.TryGetGoodState(outputRuntimeId, out var outputMarket))
                return;

            float outputPrice = outputMarket.Price;
            float nominalThroughput = facility.GetNominalThroughput(def);
            float hypoRevenue = outputPrice * nominalThroughput * sellEfficiency;
            float hypoSellFee = nominalThroughput * outputPrice * transportCost * V2TransportFeeRate;

            float hypoInputCost = 0f;
            var outputGood = economy.Goods.Get(def.OutputGoodId);
            var inputs = outputGood != null ? (def.InputOverrides ?? outputGood.Inputs) : null;
            if (inputs != null)
            {
                foreach (var input in inputs)
                {
                    int inputRuntimeId = ResolveRuntimeId(economy.Goods, input.GoodId);
                    if (inputRuntimeId < 0)
                        continue;

                    float inputPrice = market.TryGetGoodState(inputRuntimeId, out var inputMarket)
                        ? inputMarket.Price
                        : economy.Goods.GetByRuntimeId(inputRuntimeId)?.BasePrice ?? 0f;

                    hypoInputCost += inputPrice * input.QuantityKg * nominalThroughput * (1f + transportCost * V2TransportFeeRate);
                }
            }

            float hypoWageBill = subsistence * requiredLaborForActivation;
            float hypoProfit = (hypoRevenue - hypoSellFee - hypoInputCost - hypoWageBill) * 0.7f;
            float toleratedLoss = hypoWageBill * V2ReactivationWageLossTolerance;
            bool demandPressure = outputMarket.Demand > outputMarket.Supply * V2DemandPressureRatio;
            bool laborPressure = HasLaborPressure(economy, facility.CountyId, def.LaborType);

            if (def.IsExtraction || demandPressure || laborPressure || hypoProfit > -toleratedLoss)
            {
                facility.IsActive = true;
                facility.GraceDaysRemaining = V2GraceDaysOnActivation;
                facility.ConsecutiveLossDays = 0;
            }
        }

        private static bool ShouldEvaluateInactiveFacility(int currentDay, Facility facility, bool isExtraction)
        {
            int period = ResolveInactiveRecheckPeriod(facility, isExtraction);
            if (period <= 1)
                return true;

            int phase = facility.Id % period;
            if (phase < 0)
                phase += period;
            return currentDay % period == phase;
        }

        private static int ResolveInactiveRecheckPeriod(Facility facility, bool isExtraction)
        {
            int basePeriod = isExtraction ? V2InactiveExtractionRecheckDays : V2InactiveRecheckDays;
            int dormantDays = facility != null ? Math.Max(0, facility.InactiveDays) : 0;

            if (dormantDays >= V2InactiveLongDormancyDays)
                return basePeriod * 4;
            if (dormantDays >= V2InactiveMediumDormancyDays)
                return basePeriod * 2;

            return basePeriod;
        }

        private void BuildAvailableWorkerCaches(EconomyState economy)
        {
            _availableUnskilledByCounty.Clear();
            _availableSkilledByCounty.Clear();

            foreach (var county in economy.Counties.Values)
            {
                _availableUnskilledByCounty[county.CountyId] = county.Population.TotalUnskilled;
                _availableSkilledByCounty[county.CountyId] = county.Population.TotalSkilled;
            }

            var facilities = economy.GetFacilitiesDense();
            for (int i = 0; i < facilities.Count; i++)
            {
                var facility = facilities[i];
                if (!facility.IsActive || facility.AssignedWorkers <= 0)
                    continue;

                var def = economy.FacilityDefs.Get(facility.TypeId);
                if (def == null)
                    continue;

                AdjustAvailableWorkers(
                    _availableUnskilledByCounty,
                    _availableSkilledByCounty,
                    facility.CountyId,
                    def.LaborType,
                    -facility.AssignedWorkers);
            }
        }

        private static void AdjustAvailableWorkers(
            Dictionary<int, int> availableUnskilledByCounty,
            Dictionary<int, int> availableSkilledByCounty,
            int countyId,
            LaborType laborType,
            int delta)
        {
            if (laborType == LaborType.Unskilled)
            {
                availableUnskilledByCounty.TryGetValue(countyId, out int current);
                availableUnskilledByCounty[countyId] = Math.Max(0, current + delta);
            }
            else
            {
                availableSkilledByCounty.TryGetValue(countyId, out int current);
                availableSkilledByCounty[countyId] = Math.Max(0, current + delta);
            }
        }

        private static bool HasLaborPressure(EconomyState economy, int countyId, LaborType laborType)
        {
            if (!economy.Counties.TryGetValue(countyId, out var county))
                return false;

            int totalWorkers = laborType == LaborType.Unskilled
                ? county.Population.TotalUnskilled
                : county.Population.TotalSkilled;
            if (totalWorkers <= 0)
                return false;

            int idleWorkers = county.Population.IdleWorkers(laborType);
            float idleRatio = (float)idleWorkers / totalWorkers;
            return idleRatio >= V2UnemploymentPressureRatio;
        }

        private void RunExtractionFacilityV2(EconomyState economy, Facility facility, FacilityDef def)
        {
            var county = economy.GetCounty(facility.CountyId);
            if (county == null)
                return;

            float abundance = county.GetResourceAbundance(def.OutputGoodId);
            if (abundance <= 0f)
                return;

            float produced = facility.GetThroughput(def) * abundance;
            if (produced <= 0f)
                return;

            float subsistence = produced * V2SubsistenceFraction;
            float forMarket = produced - subsistence;
            int outputRuntimeId = ResolveRuntimeId(economy.Goods, def.OutputGoodId);
            if (outputRuntimeId < 0)
                return;

            if (subsistence > 0f)
                county.Stockpile.Add(outputRuntimeId, subsistence);

            if (forMarket > 0f)
                facility.OutputBuffer.Add(outputRuntimeId, forMarket);

            TrackProduction(def.OutputGoodId, produced);
        }

        private void RunProcessingFacilityV2(EconomyState economy, Facility facility, FacilityDef def)
        {
            var outputGood = economy.Goods.Get(def.OutputGoodId);
            if (outputGood == null)
                return;
            int outputRuntimeId = outputGood.RuntimeId;
            if (outputRuntimeId < 0)
                return;

            var inputs = def.InputOverrides ?? outputGood.Inputs;
            if (inputs == null || inputs.Count == 0)
                return;

            float throughput = facility.GetThroughput(def);
            if (throughput <= 0f)
                return;

            _inputRuntimeIdsBuffer.Clear();
            for (int i = 0; i < inputs.Count; i++)
            {
                int inputRuntimeId = ResolveRuntimeId(economy.Goods, inputs[i].GoodId);
                if (inputRuntimeId < 0)
                    return;
                _inputRuntimeIdsBuffer.Add(inputRuntimeId);
            }

            float possibleBatches = throughput;
            for (int i = 0; i < inputs.Count; i++)
            {
                var input = inputs[i];
                int inputRuntimeId = _inputRuntimeIdsBuffer[i];
                float available = facility.InputBuffer.Get(inputRuntimeId);
                float canMake = available / input.QuantityKg;
                if (canMake < possibleBatches)
                    possibleBatches = canMake;
            }

            if (possibleBatches <= 0.001f)
                return;

            for (int i = 0; i < inputs.Count; i++)
            {
                var input = inputs[i];
                int inputRuntimeId = _inputRuntimeIdsBuffer[i];
                facility.InputBuffer.Remove(inputRuntimeId, input.QuantityKg * possibleBatches);
            }

            facility.OutputBuffer.Add(outputRuntimeId, possibleBatches);
            TrackProduction(def.OutputGoodId, possibleBatches);
        }

        private void FlushOutputBufferToMarketV2(
            SimulationState state,
            MapData mapData,
            EconomyState economy,
            Facility facility,
            FacilityDef def)
        {
            var market = economy.GetMarketForCounty(facility.CountyId);
            if (market == null)
                return;

            float transportCost = economy.GetCountyTransportCost(facility.CountyId);
            float efficiency = 1f / (1f + transportCost * V2TransportLossRate);
            if (efficiency <= 0f)
                return;

            var county = economy.GetCounty(facility.CountyId);
            _outputGoodsBuffer.Clear();
            foreach (var kvp in facility.OutputBuffer.AllRuntime)
            {
                _outputGoodsBuffer.Add(kvp);
            }

            foreach (var kvp in _outputGoodsBuffer)
            {
                int goodRuntimeId = kvp.Key;
                float quantity = kvp.Value;
                if (quantity <= 0.001f)
                    continue;
                if (!economy.Goods.TryGetByRuntimeId(goodRuntimeId, out var good))
                    continue;

                string goodId = good.Id;

                float marketPrice = market.TryGetGoodState(goodRuntimeId, out var marketGood)
                    ? marketGood.Price
                    : good.BasePrice;
                float minUnitPrice = ComputeFacilityMinUnitAskPrice(
                    economy,
                    market,
                    facility,
                    def,
                    marketPrice,
                    transportCost);

                float haulingFee = quantity * marketPrice * transportCost * V2TransportFeeRate;
                if (haulingFee > 0f && facility.Treasury < haulingFee)
                {
                    float scale = facility.Treasury / haulingFee;
                    quantity *= scale;
                    haulingFee = facility.Treasury;
                }

                if (quantity <= 0.001f)
                    continue;

                float arrived = quantity * efficiency;
                if (arrived <= 0.001f)
                    continue;

                facility.OutputBuffer.Remove(goodRuntimeId, quantity);
                facility.Treasury -= haulingFee;
                county.Population.Treasury += haulingFee;

                market.AddInventoryLot(new ConsignmentLot
                {
                    SellerId = facility.Id,
                    GoodId = goodId,
                    GoodRuntimeId = goodRuntimeId,
                    Quantity = arrived,
                    MinUnitPrice = minUnitPrice,
                    DayListed = state.CurrentDay
                });
            }
        }

        private float ComputeFacilityMinUnitAskPrice(
            EconomyState economy,
            Market market,
            Facility facility,
            FacilityDef def,
            float marketPrice,
            float transportCost)
        {
            if (economy == null || facility == null || def == null)
                return Math.Max(0f, marketPrice);

            float throughput = facility.GetThroughput(def);
            if (throughput <= 0.001f)
                return Math.Max(0f, marketPrice);

            float wageCostPerUnit = 0f;
            if (facility.AssignedWorkers > 0 && facility.WageRate > 0f)
            {
                float wageBill = facility.WageRate * facility.AssignedWorkers;
                wageCostPerUnit = wageBill / throughput;
            }

            float inputCostPerUnit = 0f;
            var outputGood = economy.Goods.Get(def.OutputGoodId);
            var inputs = outputGood != null ? (def.InputOverrides ?? outputGood.Inputs) : null;
            if (inputs != null)
            {
                for (int i = 0; i < inputs.Count; i++)
                {
                    var input = inputs[i];
                    int inputRuntimeId = ResolveRuntimeId(economy.Goods, input.GoodId);
                    float inputPrice = 0f;
                    if (inputRuntimeId >= 0 && market != null && market.TryGetGoodState(inputRuntimeId, out var inputState))
                        inputPrice = inputState.Price;
                    else
                        inputPrice = economy.Goods.Get(input.GoodId)?.BasePrice ?? 0f;

                    float landedInputPrice = inputPrice * (1f + Math.Max(0f, transportCost) * V2TransportFeeRate);
                    inputCostPerUnit += input.QuantityKg * landedInputPrice;
                }
            }

            float haulingCostPerUnit = Math.Max(0f, marketPrice) * Math.Max(0f, transportCost) * V2TransportFeeRate;
            float operatingCostPerUnit = inputCostPerUnit + wageCostPerUnit + haulingCostPerUnit;

            bool subsidized = SimulationConfig.Economy.EnableFacilitySubsidies;
            float askMarkup = subsidized
                ? V2FacilityAskMarkupSubsidized
                : V2FacilityAskMarkupUnsubsidized;
            float baseFloorMultiplier = subsidized
                ? V2FacilityAskBaseFloorMultiplierSubsidized
                : V2FacilityAskBaseFloorMultiplierUnsubsidized;

            float minFromCost = operatingCostPerUnit * (1f + askMarkup);
            float minFromBase = outputGood != null
                ? outputGood.BasePrice * baseFloorMultiplier
                : 0f;

            return Math.Max(0f, Math.Max(minFromCost, minFromBase));
        }

        private static int GetAvailableWorkers(
            Dictionary<int, int> availableUnskilledByCounty,
            Dictionary<int, int> availableSkilledByCounty,
            int countyId,
            LaborType laborType)
        {
            if (laborType == LaborType.Unskilled)
                return availableUnskilledByCounty.TryGetValue(countyId, out int unskilled) ? unskilled : 0;

            return availableSkilledByCounty.TryGetValue(countyId, out int skilled) ? skilled : 0;
        }

        private void TrackProduction(string goodId, float amount)
        {
            if (!_producedThisTick.ContainsKey(goodId))
                _producedThisTick[goodId] = 0;
            _producedThisTick[goodId] += amount;
        }

        private int ResolveRuntimeId(GoodRegistry goods, string goodId)
        {
            if (string.IsNullOrWhiteSpace(goodId))
                return -1;

            if (_goodRuntimeIdCache.TryGetValue(goodId, out int cached))
                return cached;

            int runtimeId = goods != null && goods.TryGetRuntimeId(goodId, out int resolved)
                ? resolved
                : -1;
            _goodRuntimeIdCache[goodId] = runtimeId;
            return runtimeId;
        }
    }
}
