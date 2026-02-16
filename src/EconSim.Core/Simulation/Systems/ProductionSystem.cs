using System;
using System.Collections.Generic;
using EconSim.Core.Common;
using EconSim.Core.Data;
using EconSim.Core.Economy;

namespace EconSim.Core.Simulation.Systems
{
    /// <summary>
    /// Handles extraction and processing production.
    /// V1: per-tick worker reset and county-stockpile processing.
    /// V2: persistent staffing, profitability gating, input/output buffers, market consignments.
    /// </summary>
    public class ProductionSystem : ITickSystem
    {
        public string Name => "Production";
        public int TickInterval => SimulationConfig.Intervals.Daily;

        // V1: fraction of extraction output sent to export buffer.
        private const float V1ExportFraction = 0.3f;

        // V2 constants.
        private const float V2SubsistenceFraction = 0.20f;
        private const float V2TransportLossRate = 0.01f;
        private const float V2TransportFeeRate = 0.005f;

        private readonly Dictionary<string, float> _producedThisTick = new Dictionary<string, float>();

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
            if (SimulationConfig.UseEconomyV2)
                TickV2(state, mapData);
            else
                TickV1(state, mapData);
        }

        private void TickV1(SimulationState state, MapData mapData)
        {
            var economy = state.Economy;
            if (economy == null) return;

            _producedThisTick.Clear();

            foreach (var county in economy.Counties.Values)
            {
                county.Population.ResetEmployment();
            }

            foreach (var facility in economy.Facilities.Values)
            {
                var def = economy.FacilityDefs.Get(facility.TypeId);
                if (def == null || !def.IsExtraction) continue;

                RunExtractionFacilityV1(economy, facility, def, mapData);
            }

            foreach (var facility in economy.Facilities.Values)
            {
                var def = economy.FacilityDefs.Get(facility.TypeId);
                if (def == null || def.IsExtraction) continue;

                RunProcessingFacilityV1(economy, facility, def);
            }
        }

        private void TickV2(SimulationState state, MapData mapData)
        {
            var economy = state.Economy;
            if (economy == null) return;

            _producedThisTick.Clear();

            foreach (var facility in economy.Facilities.Values)
            {
                var def = economy.FacilityDefs.Get(facility.TypeId);
                if (def == null)
                    continue;

                ApplyActivationRulesV2(state, mapData, economy, facility, def);

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
            FacilityDef def)
        {
            if (facility.IsActive)
            {
                if (facility.RollingProfit < 0f)
                    facility.ConsecutiveLossDays++;
                else
                    facility.ConsecutiveLossDays = 0;

                if (facility.GraceDaysRemaining > 0)
                {
                    facility.GraceDaysRemaining--;
                    return;
                }

                if (facility.ConsecutiveLossDays >= 7)
                {
                    facility.IsActive = false;
                    facility.AssignedWorkers = 0;
                }

                return;
            }

            int availableWorkers = GetAvailableWorkers(economy, facility.CountyId, def.LaborType);
            if (availableWorkers < Math.Max(1, (int)Math.Ceiling(def.LaborRequired * 0.5f)))
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

            float subsistence = state.SubsistenceWage > 0f ? state.SubsistenceWage : 1f;
            float hypoWageBill = subsistence * def.LaborRequired;
            float hypoProfit = (hypoRevenue - hypoSellFee - hypoInputCost - hypoWageBill) * 0.7f;

            if (hypoProfit > 0f)
            {
                facility.IsActive = true;
                facility.GraceDaysRemaining = 14;
                facility.ConsecutiveLossDays = 0;
            }
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

            if (subsistence > 0f)
                county.Stockpile.Add(def.OutputGoodId, subsistence);

            if (forMarket > 0f)
                facility.OutputBuffer.Add(def.OutputGoodId, forMarket);

            TrackProduction(def.OutputGoodId, produced);
        }

        private void RunProcessingFacilityV2(EconomyState economy, Facility facility, FacilityDef def)
        {
            var outputGood = economy.Goods.Get(def.OutputGoodId);
            if (outputGood == null)
                return;

            var inputs = def.InputOverrides ?? outputGood.Inputs;
            if (inputs == null || inputs.Count == 0)
                return;

            float throughput = facility.GetThroughput(def);
            if (throughput <= 0f)
                return;

            float possibleBatches = throughput;
            foreach (var input in inputs)
            {
                float available = facility.InputBuffer.Get(input.GoodId);
                float canMake = available / input.Quantity;
                if (canMake < possibleBatches)
                    possibleBatches = canMake;
            }

            if (possibleBatches <= 0.001f)
                return;

            foreach (var input in inputs)
            {
                facility.InputBuffer.Remove(input.GoodId, input.Quantity * possibleBatches);
            }

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
            var goods = new List<KeyValuePair<string, float>>();
            foreach (var kvp in facility.OutputBuffer.All)
            {
                goods.Add(kvp);
            }

            foreach (var kvp in goods)
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

                market.Inventory.Add(new ConsignmentLot
                {
                    SellerId = facility.Id,
                    GoodId = goodId,
                    Quantity = arrived,
                    DayListed = state.CurrentDay
                });
            }
        }

        private int GetAvailableWorkers(EconomyState economy, int countyId, LaborType laborType)
        {
            if (!economy.Counties.TryGetValue(countyId, out var county))
                return 0;

            int total = laborType == LaborType.Unskilled
                ? county.Population.TotalUnskilled
                : county.Population.TotalSkilled;

            int assigned = 0;
            foreach (int facilityId in county.FacilityIds)
            {
                if (!economy.Facilities.TryGetValue(facilityId, out var facility) || !facility.IsActive)
                    continue;

                var def = economy.FacilityDefs.Get(facility.TypeId);
                if (def != null && def.LaborType == laborType)
                    assigned += facility.AssignedWorkers;
            }

            return Math.Max(0, total - assigned);
        }

        private static float ResolveCountyTransportCost(MapData mapData, Market market, int countyId)
        {
            if (mapData?.CountyById == null || !mapData.CountyById.TryGetValue(countyId, out var county))
                return 0f;

            if (market.ZoneCellCosts != null && market.ZoneCellCosts.TryGetValue(county.SeatCellId, out float cost))
                return Math.Max(0f, cost);

            return 0f;
        }

        private static void AllocateWorkersV1(CountyEconomy county, Facility facility, FacilityDef def)
        {
            var laborType = def.LaborType;
            int needed = def.LaborRequired;
            int allocated = county.Population.AllocateWorkers(laborType, needed);
            facility.AssignedWorkers = allocated;
        }

        private void RunExtractionFacilityV1(EconomyState economy, Facility facility, FacilityDef def, MapData mapData)
        {
            var county = economy.GetCounty(facility.CountyId);
            var goodDef = economy.Goods.Get(def.OutputGoodId);
            if (goodDef == null) return;

            float abundance = county.GetResourceAbundance(def.OutputGoodId);
            if (abundance <= 0f) return;

            AllocateWorkersV1(county, facility, def);
            if (facility.AssignedWorkers <= 0) return;

            float throughput = facility.GetThroughput(def);
            float produced = throughput * abundance;

            float forExport = produced * V1ExportFraction;
            float forLocal = produced - forExport;

            county.Stockpile.Add(def.OutputGoodId, forLocal);
            county.ExportBuffer.Add(def.OutputGoodId, forExport);
            TrackProduction(def.OutputGoodId, produced);
        }

        private void RunProcessingFacilityV1(EconomyState economy, Facility facility, FacilityDef def)
        {
            var county = economy.GetCounty(facility.CountyId);
            var goodDef = economy.Goods.Get(def.OutputGoodId);
            if (goodDef == null) return;

            var inputs = def.InputOverrides ?? goodDef.Inputs;
            if (inputs == null || inputs.Count == 0) return;

            AllocateWorkersV1(county, facility, def);
            if (facility.AssignedWorkers <= 0) return;

            float throughput = facility.GetThroughput(def);
            int maxBatches = (int)throughput;

            int possibleBatches = maxBatches;
            foreach (var input in inputs)
            {
                float available = county.Stockpile.Get(input.GoodId);
                int canMake = (int)(available / input.Quantity);
                if (canMake < possibleBatches)
                    possibleBatches = canMake;
            }

            if (possibleBatches <= 0) return;

            foreach (var input in inputs)
            {
                county.Stockpile.Remove(input.GoodId, input.Quantity * possibleBatches);
            }

            county.Stockpile.Add(def.OutputGoodId, possibleBatches);
            TrackProduction(def.OutputGoodId, possibleBatches);
        }

        private void TrackProduction(string goodId, float amount)
        {
            if (!_producedThisTick.ContainsKey(goodId))
                _producedThisTick[goodId] = 0;
            _producedThisTick[goodId] += amount;
        }
    }
}
