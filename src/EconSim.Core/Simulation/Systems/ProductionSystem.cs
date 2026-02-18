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
        private const float V2FacilityAskMarkupSubsidized = 0.00f;
        private const float V2FacilityAskBaseFloorMultiplierUnsubsidized = 0.25f;
        private const float V2FacilityAskBaseFloorMultiplierSubsidized = 0.10f;
        private const float V2ProcessingDemandFloorKg = 0.5f;
        private const float GrainReserveTargetDays = 540f; // 1.5 years of staple reserve.
        private const float MillDemandBufferFactor = 1.10f;
        private const float MaltTargetDaysCover = 14f;
        private const float MaltDesiredRampUpFractionPerDay = 0.20f;
        private const float MaltDesiredRampDownFractionPerDay = 0.20f;
        private const float MaltDesiredMinStepKgPerDay = 10f;
        private static readonly string[] ReserveGrainGoods = { "wheat", "rye", "barley", "rice_grain" };

        private readonly Dictionary<string, float> _producedThisTick = new Dictionary<string, float>();
        private readonly List<KeyValuePair<int, float>> _outputGoodsBuffer = new List<KeyValuePair<int, float>>(8);
        private readonly List<int> _inputRuntimeIdsBuffer = new List<int>(8);
        private readonly Dictionary<int, int> _availableUnskilledByCounty = new Dictionary<int, int>();
        private readonly Dictionary<int, int> _availableSkilledByCounty = new Dictionary<int, int>();
        private readonly Dictionary<string, int> _goodRuntimeIdCache = new Dictionary<string, int>();
        private readonly Dictionary<int, List<int>> _downstreamOutputsByInputRuntimeId = new Dictionary<int, List<int>>();
        private readonly Dictionary<long, bool> _demandReachabilityMemo = new Dictionary<long, bool>();
        private readonly HashSet<int> _demandReachabilityVisited = new HashSet<int>();
        private readonly Dictionary<int, float> _maltDesiredByMarket = new Dictionary<int, float>();
        private readonly Dictionary<int, int> _maltDesiredDayByMarket = new Dictionary<int, int>();

        public void Initialize(SimulationState state, MapData mapData)
        {
            var economy = state.Economy;
            if (economy == null) return;

            _goodRuntimeIdCache.Clear();
            _maltDesiredByMarket.Clear();
            _maltDesiredDayByMarket.Clear();
            RebuildDownstreamDemandGraph(economy);

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
            _demandReachabilityMemo.Clear();
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

                if (!SimulationConfig.Economy.IsFacilityEnabled(facility.TypeId)
                    || !SimulationConfig.Economy.IsGoodEnabled(def.OutputGoodId))
                {
                    facility.IsActive = false;
                    facility.AssignedWorkers = 0;
                    facility.InactiveDays = 1;
                    continue;
                }

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
                    RunProcessingFacilityV2(economy, facility, def, state.CurrentDay);

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
                if (!SimulationConfig.Economy.IsFacilityEnabled(facility.TypeId)
                    || !SimulationConfig.Economy.IsGoodEnabled(def.OutputGoodId))
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
            var market = economy.GetMarketForCounty(facility.CountyId);
            int outputRuntimeId = ResolveRuntimeId(economy.Goods, def.OutputGoodId);
            bool hasDemandPull = def.IsExtraction
                || (market != null && outputRuntimeId >= 0 && HasDirectOrDownstreamDemand(economy, market, outputRuntimeId));

            if (facility.IsActive)
            {
                // Keep extractive sectors stable to avoid raw-input starvation cascades.
                if (def.IsExtraction)
                    return;

                if (!hasDemandPull)
                {
                    if (facility.GraceDaysRemaining > 0)
                    {
                        facility.GraceDaysRemaining--;
                        return;
                    }

                    int releasedWorkers = facility.AssignedWorkers;
                    facility.IsActive = false;
                    facility.AssignedWorkers = 0;
                    if (releasedWorkers > 0)
                        AdjustAvailableWorkers(availableUnskilledByCounty, availableSkilledByCounty, facility.CountyId, def.LaborType, releasedWorkers);
                    return;
                }

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

            if (market == null)
                return;

            float transportCost = economy.GetCountyTransportCost(facility.CountyId);
            float sellEfficiency = 1f / (1f + transportCost * V2TransportLossRate);

            if (outputRuntimeId < 0)
                return;

            if (!hasDemandPull)
                return;

            if (!market.TryGetGoodState(outputRuntimeId, out var outputMarket))
                return;

            float outputPrice = outputMarket.Price;
            float nominalThroughput = facility.GetNominalThroughput(def);
            float hypoRevenue = outputPrice * nominalThroughput * sellEfficiency;
            float flatHaulPerUnit = Math.Max(0f, transportCost) * SimulationConfig.Economy.FlatHaulingFeePerKgPerTransportCostUnit;
            float hypoSellFee = nominalThroughput * flatHaulPerUnit;

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

                    float landedInputPrice = inputPrice + flatHaulPerUnit;
                    hypoInputCost += landedInputPrice * input.QuantityKg * nominalThroughput;
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

        private bool HasDirectOrDownstreamDemand(EconomyState economy, Market market, int goodRuntimeId)
        {
            if (economy == null || market == null || goodRuntimeId < 0)
                return false;

            long memoKey = ((long)market.Id << 32) | (uint)goodRuntimeId;
            if (_demandReachabilityMemo.TryGetValue(memoKey, out bool cached))
                return cached;

            _demandReachabilityVisited.Clear();
            bool hasDemand = HasDirectOrDownstreamDemandRecursive(economy, market, goodRuntimeId);
            _demandReachabilityMemo[memoKey] = hasDemand;
            return hasDemand;
        }

        private bool HasDirectOrDownstreamDemandRecursive(EconomyState economy, Market market, int goodRuntimeId)
        {
            if (!_demandReachabilityVisited.Add(goodRuntimeId))
                return false;

            try
            {
                if (market.TryGetGoodState(goodRuntimeId, out var goodState))
                {
                    if (goodState.Demand > V2ProcessingDemandFloorKg)
                        return true;
                    if (goodState.Demand > goodState.Supply * V2DemandPressureRatio)
                        return true;
                }

                if (!_downstreamOutputsByInputRuntimeId.TryGetValue(goodRuntimeId, out var downstreamOutputs)
                    || downstreamOutputs == null
                    || downstreamOutputs.Count == 0)
                {
                    return false;
                }

                for (int i = 0; i < downstreamOutputs.Count; i++)
                {
                    int downstreamRuntimeId = downstreamOutputs[i];
                    if (downstreamRuntimeId < 0)
                        continue;

                    var downstreamGood = economy.Goods.GetByRuntimeId(downstreamRuntimeId);
                    if (downstreamGood == null || !SimulationConfig.Economy.IsGoodEnabled(downstreamGood.Id))
                        continue;

                    if (HasDirectOrDownstreamDemandRecursive(economy, market, downstreamRuntimeId))
                        return true;
                }

                return false;
            }
            finally
            {
                _demandReachabilityVisited.Remove(goodRuntimeId);
            }
        }

        private void RebuildDownstreamDemandGraph(EconomyState economy)
        {
            _downstreamOutputsByInputRuntimeId.Clear();
            if (economy?.FacilityDefs == null || economy.Goods == null)
                return;

            foreach (var facilityDef in economy.FacilityDefs.ProcessingFacilities)
            {
                if (facilityDef == null)
                    continue;
                if (!SimulationConfig.Economy.IsFacilityEnabled(facilityDef.Id)
                    || !SimulationConfig.Economy.IsGoodEnabled(facilityDef.OutputGoodId))
                {
                    continue;
                }

                int outputRuntimeId = ResolveRuntimeId(economy.Goods, facilityDef.OutputGoodId);
                if (outputRuntimeId < 0)
                    continue;

                var outputGood = economy.Goods.Get(facilityDef.OutputGoodId);
                foreach (var inputs in EnumerateInputVariants(facilityDef, outputGood))
                {
                    if (inputs == null || inputs.Count == 0)
                        continue;

                    for (int i = 0; i < inputs.Count; i++)
                    {
                        int inputRuntimeId = ResolveRuntimeId(economy.Goods, inputs[i].GoodId);
                        if (inputRuntimeId < 0)
                            continue;

                        if (!_downstreamOutputsByInputRuntimeId.TryGetValue(inputRuntimeId, out var outputs))
                        {
                            outputs = new List<int>();
                            _downstreamOutputsByInputRuntimeId[inputRuntimeId] = outputs;
                        }

                        bool exists = false;
                        for (int j = 0; j < outputs.Count; j++)
                        {
                            if (outputs[j] == outputRuntimeId)
                            {
                                exists = true;
                                break;
                            }
                        }

                        if (!exists)
                            outputs.Add(outputRuntimeId);
                    }
                }
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

            if (IsReserveGrainGood(def.OutputGoodId))
            {
                float reserveTarget = ComputeCountyGrainReserveTargetKg(economy, county);
                float reserveCurrent = GetCountyGrainReserveKg(economy, county);
                float reserveNeeded = Math.Max(0f, reserveTarget - reserveCurrent);
                float toReserve = Math.Min(produced, reserveNeeded);
                float toMarket = produced - toReserve;

                if (toReserve > 0f)
                    county.Stockpile.Add(outputRuntimeId, toReserve);
                if (toMarket > 0f)
                    facility.OutputBuffer.Add(outputRuntimeId, toMarket);
            }
            else
            {
                if (subsistence > 0f)
                    county.Stockpile.Add(outputRuntimeId, subsistence);

                if (forMarket > 0f)
                    facility.OutputBuffer.Add(outputRuntimeId, forMarket);
            }

            TrackProduction(def.OutputGoodId, produced);
        }

        private void RunProcessingFacilityV2(EconomyState economy, Facility facility, FacilityDef def, int currentDay)
        {
            var outputGood = economy.Goods.Get(def.OutputGoodId);
            if (outputGood == null)
                return;
            int outputRuntimeId = outputGood.RuntimeId;
            if (outputRuntimeId < 0)
                return;

            float throughput = facility.GetThroughput(def);
            if (throughput <= 0f)
                return;

            if (ShouldDemandLimitProcessing(def))
            {
                var market = economy.GetMarketForCounty(facility.CountyId);
                if (market != null && market.TryGetGoodState(outputRuntimeId, out var outputState))
                {
                    if (string.Equals(def.Id, "malt_house", StringComparison.OrdinalIgnoreCase))
                    {
                        float desiredThroughput = ComputeMaltHouseDerivedDemandThroughput(economy, market, outputState, currentDay);
                        throughput = Math.Min(throughput, desiredThroughput);
                    }
                    else
                    {
                        float unmetDemand = Math.Max(0f, outputState.Demand - outputState.Supply);
                        float demandLimitedThroughput = unmetDemand * MillDemandBufferFactor;
                        throughput = Math.Min(throughput, demandLimitedThroughput);
                    }
                }
            }

            if (throughput <= 0.001f)
                return;

            var county = economy.GetCounty(facility.CountyId);
            bool useCountyGrainReserve = IsMillFacility(def) && county != null;
            List<GoodInput> selectedInputs = null;
            float possibleBatches = 0f;
            bool preferVariantOrder = IsMillFacility(def);
            foreach (var candidateInputs in EnumerateInputVariants(def, outputGood))
            {
                if (candidateInputs == null || candidateInputs.Count == 0)
                    continue;

                float candidateBatches = throughput;
                bool valid = true;
                for (int i = 0; i < candidateInputs.Count; i++)
                {
                    var input = candidateInputs[i];
                    if (input.QuantityKg <= 0f)
                    {
                        valid = false;
                        break;
                    }

                    int inputRuntimeId = ResolveRuntimeId(economy.Goods, input.GoodId);
                    if (inputRuntimeId < 0)
                    {
                        valid = false;
                        break;
                    }

                    float available = facility.InputBuffer.Get(inputRuntimeId);
                    if (useCountyGrainReserve && IsReserveGrainRuntimeId(economy, inputRuntimeId))
                        available += county.Stockpile.Get(inputRuntimeId);
                    float canMake = available / input.QuantityKg;
                    if (canMake < candidateBatches)
                        candidateBatches = canMake;
                }

                if (!valid || candidateBatches <= 0.001f)
                    continue;

                if (preferVariantOrder)
                {
                    selectedInputs = candidateInputs;
                    possibleBatches = candidateBatches;
                    break;
                }

                if (candidateBatches <= possibleBatches)
                    continue;

                selectedInputs = candidateInputs;
                possibleBatches = candidateBatches;
            }

            if (selectedInputs == null || possibleBatches <= 0.001f)
                return;

            _inputRuntimeIdsBuffer.Clear();
            for (int i = 0; i < selectedInputs.Count; i++)
            {
                int inputRuntimeId = ResolveRuntimeId(economy.Goods, selectedInputs[i].GoodId);
                if (inputRuntimeId < 0)
                    return;
                _inputRuntimeIdsBuffer.Add(inputRuntimeId);
            }

            for (int i = 0; i < selectedInputs.Count; i++)
            {
                var input = selectedInputs[i];
                int inputRuntimeId = _inputRuntimeIdsBuffer[i];
                float required = input.QuantityKg * possibleBatches;
                float pulledFromBuffer = facility.InputBuffer.Remove(inputRuntimeId, required);
                float remaining = required - pulledFromBuffer;
                if (remaining > 0f && useCountyGrainReserve && IsReserveGrainRuntimeId(economy, inputRuntimeId))
                    county.Stockpile.Remove(inputRuntimeId, remaining);
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
                if (!SimulationConfig.Economy.IsGoodEnabled(good.Id))
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

                float haulingFee = quantity * Math.Max(0f, transportCost) * SimulationConfig.Economy.FlatHaulingFeePerKgPerTransportCostUnit;
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
            if (outputGood != null)
            {
                float bestVariantCost = float.MaxValue;
                foreach (var inputs in EnumerateInputVariants(def, outputGood))
                {
                    if (inputs == null || inputs.Count == 0)
                        continue;

                    float variantCost = 0f;
                    bool valid = true;
                    for (int i = 0; i < inputs.Count; i++)
                    {
                        var input = inputs[i];
                        if (input.QuantityKg <= 0f)
                        {
                            valid = false;
                            break;
                        }

                        int inputRuntimeId = ResolveRuntimeId(economy.Goods, input.GoodId);
                        float inputPrice;
                        if (inputRuntimeId >= 0 && market != null && market.TryGetGoodState(inputRuntimeId, out var inputState))
                            inputPrice = inputState.Price;
                        else
                            inputPrice = economy.Goods.Get(input.GoodId)?.BasePrice ?? 0f;

                        float landedInputPrice = inputPrice + Math.Max(0f, transportCost) * SimulationConfig.Economy.FlatHaulingFeePerKgPerTransportCostUnit;
                        variantCost += input.QuantityKg * landedInputPrice;
                    }

                    if (valid && variantCost < bestVariantCost)
                        bestVariantCost = variantCost;
                }

                if (bestVariantCost < float.MaxValue)
                    inputCostPerUnit = bestVariantCost;
            }

            float haulingCostPerUnit = Math.Max(0f, transportCost) * SimulationConfig.Economy.FlatHaulingFeePerKgPerTransportCostUnit;
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

        private static IEnumerable<List<GoodInput>> EnumerateInputVariants(FacilityDef def, GoodDef outputGood)
        {
            if (def?.InputOverrides != null && def.InputOverrides.Count > 0)
            {
                yield return def.InputOverrides;
                yield break;
            }

            bool yieldedVariant = false;
            if (outputGood?.InputVariants != null)
            {
                for (int i = 0; i < outputGood.InputVariants.Count; i++)
                {
                    var variant = outputGood.InputVariants[i];
                    if (variant?.Inputs == null || variant.Inputs.Count == 0)
                        continue;

                    yieldedVariant = true;
                    yield return variant.Inputs;
                }
            }

            if (!yieldedVariant && outputGood?.Inputs != null && outputGood.Inputs.Count > 0)
                yield return outputGood.Inputs;
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

        private static bool IsMillFacility(FacilityDef def)
        {
            if (def == null)
                return false;

            return def.Id == "mill" || def.Id == "rye_mill" || def.Id == "barley_mill";
        }

        private static bool ShouldDemandLimitProcessing(FacilityDef def)
        {
            if (def == null)
                return false;

            if (IsMillFacility(def))
                return true;

            return def.Id == "bakery" || def.Id == "brewery" || def.Id == "malt_house";
        }

        private static bool IsReserveGrainGood(string goodId)
        {
            if (string.IsNullOrWhiteSpace(goodId))
                return false;
            if (!SimulationConfig.Economy.IsGoodEnabled(goodId))
                return false;

            for (int i = 0; i < ReserveGrainGoods.Length; i++)
            {
                if (string.Equals(ReserveGrainGoods[i], goodId, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static bool IsReserveGrainRuntimeId(EconomyState economy, int runtimeId)
        {
            if (economy == null || runtimeId < 0 || !economy.Goods.TryGetByRuntimeId(runtimeId, out var good))
                return false;

            return IsReserveGrainGood(good.Id);
        }

        private float ComputeMaltHouseDerivedDemandThroughput(
            EconomyState economy,
            Market market,
            MarketGoodState maltState,
            int currentDay)
        {
            if (economy == null || market == null || maltState == null)
                return 0f;

            if (!economy.Goods.TryGetRuntimeId("beer", out int beerRuntimeId) || beerRuntimeId < 0)
                return 0f;
            if (!market.TryGetGoodState(beerRuntimeId, out var beerState) || beerState == null)
                return 0f;

            float projectedBeerDemandKg = Math.Max(0f, Math.Max(beerState.LastTradeVolume, beerState.Demand));
            if (projectedBeerDemandKg <= 0.001f)
                return 0f;

            float beerPerMaltKg = Math.Max(0.001f, SimulationConfig.Economy.BeerKgPerMaltKg);
            float requiredMaltPerDay = projectedBeerDemandKg / beerPerMaltKg;
            float targetMaltInventory = requiredMaltPerDay * MaltTargetDaysCover;
            float currentMaltInventory = Math.Max(0f, maltState.SupplyOffered);

            float totalDesiredForMarket;
            if (targetMaltInventory > 0f && currentMaltInventory >= targetMaltInventory)
            {
                totalDesiredForMarket = 0f;
            }
            else
            {
                float refillGap = Math.Max(0f, targetMaltInventory - currentMaltInventory);
                totalDesiredForMarket = requiredMaltPerDay + Math.Min(requiredMaltPerDay, refillGap);
            }

            totalDesiredForMarket = ApplyMaltDesiredDamping(market.Id, currentDay, totalDesiredForMarket);

            int activeMaltHouses = CountActiveFacilitiesForMarketAndType(economy, market.Id, "malt_house");
            if (activeMaltHouses <= 0)
                activeMaltHouses = 1;

            return Math.Max(0f, totalDesiredForMarket / activeMaltHouses);
        }

        private float ApplyMaltDesiredDamping(int marketId, int currentDay, float desiredTotalForMarket)
        {
            desiredTotalForMarket = Math.Max(0f, desiredTotalForMarket);

            if (!_maltDesiredByMarket.TryGetValue(marketId, out float previous))
            {
                _maltDesiredByMarket[marketId] = desiredTotalForMarket;
                _maltDesiredDayByMarket[marketId] = currentDay;
                return desiredTotalForMarket;
            }

            if (_maltDesiredDayByMarket.TryGetValue(marketId, out int lastDay) && lastDay == currentDay)
                return previous;

            float delta = desiredTotalForMarket - previous;
            float rampFraction = delta >= 0f
                ? MaltDesiredRampUpFractionPerDay
                : MaltDesiredRampDownFractionPerDay;
            float stepCap = Math.Max(MaltDesiredMinStepKgPerDay, Math.Max(previous, desiredTotalForMarket) * rampFraction);
            float clampedDelta = Math.Max(-stepCap, Math.Min(stepCap, delta));
            float next = Math.Max(0f, previous + clampedDelta);

            _maltDesiredByMarket[marketId] = next;
            _maltDesiredDayByMarket[marketId] = currentDay;
            return next;
        }

        private static int CountActiveFacilitiesForMarketAndType(
            EconomyState economy,
            int marketId,
            string facilityTypeId)
        {
            if (economy == null || marketId <= 0 || string.IsNullOrWhiteSpace(facilityTypeId))
                return 0;

            var facilities = economy.GetFacilitiesDense();
            int count = 0;
            for (int i = 0; i < facilities.Count; i++)
            {
                var facility = facilities[i];
                if (facility == null || !facility.IsActive || facility.AssignedWorkers <= 0)
                    continue;

                if (!string.Equals(facility.TypeId, facilityTypeId, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!economy.CountyToMarket.TryGetValue(facility.CountyId, out int facilityMarketId))
                    continue;

                if (facilityMarketId == marketId)
                    count++;
            }

            return count;
        }

        private static float ComputeCountyGrainReserveTargetKg(EconomyState economy, CountyEconomy county)
        {
            if (economy == null || county == null)
                return 0f;

            int population = county.Population.Total;
            if (population <= 0)
                return 0f;

            float stapleFlourPerCapitaPerDay = 160f / 365f;
            var flour = economy.Goods.Get("flour");
            if (flour != null && flour.NeedCategory == NeedCategory.Basic && flour.BaseConsumptionKgPerCapitaPerDay > 0f)
                stapleFlourPerCapitaPerDay = flour.BaseConsumptionKgPerCapitaPerDay;

            float dailyRawNeed = population * stapleFlourPerCapitaPerDay * SimulationConfig.Economy.RawGrainKgPerFlourKg;
            return Math.Max(0f, dailyRawNeed * GrainReserveTargetDays);
        }

        private static float GetCountyGrainReserveKg(EconomyState economy, CountyEconomy county)
        {
            if (economy == null || county == null)
                return 0f;

            float total = 0f;
            for (int i = 0; i < ReserveGrainGoods.Length; i++)
            {
                string goodId = ReserveGrainGoods[i];
                if (!SimulationConfig.Economy.IsGoodEnabled(goodId))
                    continue;
                if (!economy.Goods.TryGetRuntimeId(goodId, out int runtimeId) || runtimeId < 0)
                    continue;

                total += county.Stockpile.Get(runtimeId);
            }

            return total;
        }
    }
}
