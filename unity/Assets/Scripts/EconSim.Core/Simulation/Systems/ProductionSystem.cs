using System.Collections.Generic;
using EconSim.Core.Common;
using EconSim.Core.Data;
using EconSim.Core.Economy;

namespace EconSim.Core.Simulation.Systems
{
    /// <summary>
    /// Handles all production: extraction, refining, and manufacturing.
    /// Runs every tick. Order: reset employment → extraction → processing.
    /// </summary>
    public class ProductionSystem : ITickSystem
    {
        public string Name => "Production";
        public int TickInterval => 1;

        // Debug: track production this tick for logging
        private Dictionary<string, float> _producedThisTick = new Dictionary<string, float>();
        private const int LogInterval = 30; // Log every 30 days (monthly)

        public void Initialize(SimulationState state, MapData mapData)
        {
            // Log initial facility counts
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
            var economy = state.Economy;
            if (economy == null) return;

            _producedThisTick.Clear();

            // Phase 1: Reset employment for all counties
            foreach (var county in economy.Counties.Values)
            {
                county.Population.ResetEmployment();
            }

            // Phase 2: Allocate workers and run extraction facilities
            foreach (var facility in economy.Facilities.Values)
            {
                var def = economy.FacilityDefs.Get(facility.TypeId);
                if (def == null || !def.IsExtraction) continue;

                RunExtractionFacility(economy, facility, def, mapData);
            }

            // Phase 3: Run processing facilities (refining + manufacturing)
            foreach (var facility in economy.Facilities.Values)
            {
                var def = economy.FacilityDefs.Get(facility.TypeId);
                if (def == null || def.IsExtraction) continue;

                RunProcessingFacility(economy, facility, def);
            }

            // Debug logging every LogInterval ticks
            if (state.CurrentDay % LogInterval == 0)
            {
                LogProductionSummary(state);
            }
        }

        private void LogProductionSummary(SimulationState state)
        {
            SimLog.Log("Production", $"=== Day {state.CurrentDay} Production Summary ===");
            foreach (var kvp in _producedThisTick)
            {
                SimLog.Log("Production", $"  {kvp.Key}: {kvp.Value:F1} units");
            }

            // Also log total stockpiles across all counties
            var totalStockpile = new Dictionary<string, float>();
            foreach (var county in state.Economy.Counties.Values)
            {
                foreach (var item in county.Stockpile.All)
                {
                    if (!totalStockpile.ContainsKey(item.Key))
                        totalStockpile[item.Key] = 0;
                    totalStockpile[item.Key] += item.Value;
                }
            }

            SimLog.Log("Production", "Global stockpiles:");
            foreach (var kvp in totalStockpile)
            {
                SimLog.Log("Production", $"  {kvp.Key}: {kvp.Value:F1}");
            }
        }

        private void TrackProduction(string goodId, float amount)
        {
            if (!_producedThisTick.ContainsKey(goodId))
                _producedThisTick[goodId] = 0;
            _producedThisTick[goodId] += amount;
        }

        private void RunExtractionFacility(EconomyState economy, Facility facility, FacilityDef def, MapData mapData)
        {
            var county = economy.GetCounty(facility.CellId);
            var goodDef = economy.Goods.Get(def.OutputGoodId);
            if (goodDef == null) return;

            // Check resource availability
            float abundance = county.GetResourceAbundance(def.OutputGoodId);
            if (abundance <= 0f) return;

            // Allocate workers
            AllocateWorkers(county, facility, def);
            if (facility.AssignedWorkers <= 0) return;

            // Calculate production
            float throughput = facility.GetThroughput(def);
            float produced = throughput * abundance;

            // Add to county stockpile
            county.Stockpile.Add(def.OutputGoodId, produced);
            TrackProduction(def.OutputGoodId, produced);
        }

        private void RunProcessingFacility(EconomyState economy, Facility facility, FacilityDef def)
        {
            var county = economy.GetCounty(facility.CellId);
            var goodDef = economy.Goods.Get(def.OutputGoodId);
            if (goodDef == null || goodDef.Inputs == null) return;

            // Allocate workers
            AllocateWorkers(county, facility, def);
            if (facility.AssignedWorkers <= 0) return;

            // Calculate how many batches we can produce
            float throughput = facility.GetThroughput(def);
            int maxBatches = (int)throughput;

            // Limit by available inputs
            int possibleBatches = maxBatches;
            foreach (var input in goodDef.Inputs)
            {
                float available = county.Stockpile.Get(input.GoodId);
                int canMake = (int)(available / input.Quantity);
                if (canMake < possibleBatches)
                    possibleBatches = canMake;
            }

            if (possibleBatches <= 0) return;

            // Consume inputs
            foreach (var input in goodDef.Inputs)
            {
                county.Stockpile.Remove(input.GoodId, input.Quantity * possibleBatches);
            }

            // Produce output
            county.Stockpile.Add(def.OutputGoodId, possibleBatches);
            TrackProduction(def.OutputGoodId, possibleBatches);
        }

        private void AllocateWorkers(CountyEconomy county, Facility facility, FacilityDef def)
        {
            // Convert LaborType to determine which pool to use
            var laborType = def.LaborType;
            int needed = def.LaborRequired;
            int allocated = county.Population.AllocateWorkers(laborType, needed);
            facility.AssignedWorkers = allocated;
        }
    }
}
