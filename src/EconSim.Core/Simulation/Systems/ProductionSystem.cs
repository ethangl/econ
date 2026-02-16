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

        private readonly Dictionary<string, float> _producedThisTick = new Dictionary<string, float>();
        private readonly List<KeyValuePair<int, float>> _outputGoodsBuffer = new List<KeyValuePair<int, float>>(8);
        private readonly List<KeyValuePair<string, float>> _outputGoodsFallbackBuffer = new List<KeyValuePair<string, float>>(4);
        private readonly Dictionary<int, int> _availableUnskilledByCounty = new Dictionary<int, int>();
        private readonly Dictionary<int, int> _availableSkilledByCounty = new Dictionary<int, int>();
        private readonly Dictionary<string, int> _goodRuntimeIdCache = new Dictionary<string, int>();

        public void Initialize(SimulationState state, MapData mapData)
        {
            var economy = state.Economy;
            if (economy == null) return;

            var facilityCounts = new Dictionary<string, int>();
            foreach (var f in economy.Facilities.Values)
            {
                if (!facilityCounts.ContainsKey(f.TypeId))
                    facilityCounts[f.TypeId] = 0;
                facilityCounts[f.TypeId]++;
            }

            SimLog.Log("Production", $"Initialized with {economy.Facilities.Count} facilities:");
            foreach (var kvp in facilityCounts)
            {
                SimLog.Log("Production", $"  {kvp.Key}: {kvp.Value}");
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

            foreach (var facility in economy.Facilities.Values)
            {
                facility.BeginDayMetrics(state.CurrentDay);

                var def = economy.FacilityDefs.Get(facility.TypeId);
                if (def == null)
                    continue;

                if (!facility.IsActive && !ShouldEvaluateInactiveFacility(state.CurrentDay, facility.Id, def.IsExtraction))
                    continue;

                ApplyActivationRulesV2(state, mapData, economy, facility, def, _availableUnskilledByCounty, _availableSkilledByCounty);

                if (!facility.IsActive || facility.AssignedWorkers <= 0)
                    continue;

                if (def.IsExtraction)
                    RunExtractionFacilityV2(economy, facility, def);
                else
                    RunProcessingFacilityV2(economy, facility, def);

                FlushOutputBufferToMarketV2(state, mapData, economy, facility);
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

                int staffedWorkers = Math.Max(1, facility.AssignedWorkers > 0 ? facility.AssignedWorkers : def.LaborRequired);
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
            if (availableWorkers < Math.Max(1, (int)Math.Ceiling(def.LaborRequired * V2ReactivationLaborFloorRatio)))
                return;

            var market = economy.GetMarketForCounty(facility.CountyId);
            if (market == null)
                return;

            float transportCost = ResolveCountyTransportCost(mapData, market, facility.CountyId);
            float sellEfficiency = 1f / (1f + transportCost * V2TransportLossRate);

            if (!market.Goods.TryGetValue(def.OutputGoodId, out var outputMarket))
                return;

            float outputPrice = outputMarket.Price;
            float hypoRevenue = outputPrice * def.BaseThroughput * sellEfficiency;
            float hypoSellFee = def.BaseThroughput * outputPrice * transportCost * V2TransportFeeRate;

            float hypoInputCost = 0f;
            var outputGood = economy.Goods.Get(def.OutputGoodId);
            var inputs = outputGood != null ? (def.InputOverrides ?? outputGood.Inputs) : null;
            if (inputs != null)
            {
                foreach (var input in inputs)
                {
                    float inputPrice = market.Goods.TryGetValue(input.GoodId, out var inputMarket)
                        ? inputMarket.Price
                        : economy.Goods.Get(input.GoodId)?.BasePrice ?? 0f;

                    hypoInputCost += inputPrice * input.Quantity * def.BaseThroughput * (1f + transportCost * V2TransportFeeRate);
                }
            }

            float hypoWageBill = subsistence * def.LaborRequired;
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

        private static bool ShouldEvaluateInactiveFacility(int currentDay, int facilityId, bool isExtraction)
        {
            int period = isExtraction ? V2InactiveExtractionRecheckDays : V2InactiveRecheckDays;
            if (period <= 1)
                return true;

            int phase = facilityId % period;
            if (phase < 0)
                phase += period;
            return currentDay % period == phase;
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

            foreach (var facility in economy.Facilities.Values)
            {
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

            if (subsistence > 0f)
            {
                if (outputRuntimeId >= 0)
                    county.Stockpile.Add(outputRuntimeId, subsistence);
                else
                    county.Stockpile.Add(def.OutputGoodId, subsistence);
            }

            if (forMarket > 0f)
            {
                if (outputRuntimeId >= 0)
                    facility.OutputBuffer.Add(outputRuntimeId, forMarket);
                else
                    facility.OutputBuffer.Add(def.OutputGoodId, forMarket);
            }

            TrackProduction(def.OutputGoodId, produced);
        }

        private void RunProcessingFacilityV2(EconomyState economy, Facility facility, FacilityDef def)
        {
            var outputGood = economy.Goods.Get(def.OutputGoodId);
            if (outputGood == null)
                return;
            int outputRuntimeId = outputGood.RuntimeId;

            var inputs = def.InputOverrides ?? outputGood.Inputs;
            if (inputs == null || inputs.Count == 0)
                return;

            float throughput = facility.GetThroughput(def);
            if (throughput <= 0f)
                return;

            float possibleBatches = throughput;
            foreach (var input in inputs)
            {
                int inputRuntimeId = ResolveRuntimeId(economy.Goods, input.GoodId);
                float available = inputRuntimeId >= 0
                    ? facility.InputBuffer.Get(inputRuntimeId)
                    : facility.InputBuffer.Get(input.GoodId);
                float canMake = available / input.Quantity;
                if (canMake < possibleBatches)
                    possibleBatches = canMake;
            }

            if (possibleBatches <= 0.001f)
                return;

            foreach (var input in inputs)
            {
                int inputRuntimeId = ResolveRuntimeId(economy.Goods, input.GoodId);
                if (inputRuntimeId >= 0)
                    facility.InputBuffer.Remove(inputRuntimeId, input.Quantity * possibleBatches);
                else
                    facility.InputBuffer.Remove(input.GoodId, input.Quantity * possibleBatches);
            }

            if (outputRuntimeId >= 0)
                facility.OutputBuffer.Add(outputRuntimeId, possibleBatches);
            else
                facility.OutputBuffer.Add(def.OutputGoodId, possibleBatches);
            TrackProduction(def.OutputGoodId, possibleBatches);
        }

        private void FlushOutputBufferToMarketV2(
            SimulationState state,
            MapData mapData,
            EconomyState economy,
            Facility facility)
        {
            var market = economy.GetMarketForCounty(facility.CountyId);
            if (market == null)
                return;

            float transportCost = ResolveCountyTransportCost(mapData, market, facility.CountyId);
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

                float marketPrice = market.Goods.TryGetValue(goodId, out var marketGood)
                    ? marketGood.Price
                    : economy.Goods.Get(goodId)?.BasePrice ?? 0f;

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
                    DayListed = state.CurrentDay
                });
            }

            // Fallback for unresolved string-only entries, which can exist before a stockpile is bound.
            _outputGoodsFallbackBuffer.Clear();
            foreach (var kvp in facility.OutputBuffer.All)
            {
                if (economy.Goods.TryGetRuntimeId(kvp.Key, out _))
                    continue;
                _outputGoodsFallbackBuffer.Add(kvp);
            }

            foreach (var kvp in _outputGoodsFallbackBuffer)
            {
                string goodId = kvp.Key;
                float quantity = kvp.Value;
                if (quantity <= 0.001f)
                    continue;

                float marketPrice = market.Goods.TryGetValue(goodId, out var marketGood)
                    ? marketGood.Price
                    : economy.Goods.Get(goodId)?.BasePrice ?? 0f;

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

                facility.OutputBuffer.Remove(goodId, quantity);
                facility.Treasury -= haulingFee;
                county.Population.Treasury += haulingFee;

                market.AddInventoryLot(new ConsignmentLot
                {
                    SellerId = facility.Id,
                    GoodId = goodId,
                    GoodRuntimeId = null,
                    Quantity = arrived,
                    DayListed = state.CurrentDay
                });
            }
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

        private static float ResolveCountyTransportCost(MapData mapData, Market market, int countyId)
        {
            if (mapData?.CountyById == null || !mapData.CountyById.TryGetValue(countyId, out var county))
                return 0f;

            if (market.ZoneCellCosts != null && market.ZoneCellCosts.TryGetValue(county.SeatCellId, out float cost))
                return Math.Max(0f, cost);

            return 0f;
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
