using System.Collections.Generic;
using EconSim.Core.Common;
using EconSim.Core.Data;
using EconSim.Core.Economy;

namespace EconSim.Core.Simulation.Systems
{
    /// <summary>
    /// Handles population consumption of goods.
    /// Runs every tick after production.
    /// Tracks unmet demand for effects (future: population decline, unrest).
    /// </summary>
    public class ConsumptionSystem : ITickSystem
    {
        public string Name => "Consumption";
        public int TickInterval => 1;

        // Debug: track consumption this tick
        private Dictionary<string, float> _consumedThisTick = new Dictionary<string, float>();
        private Dictionary<string, float> _unmetThisTick = new Dictionary<string, float>();
        private const int LogInterval = 30;

        public void Initialize(SimulationState state, MapData mapData)
        {
            var economy = state.Economy;
            if (economy == null) return;

            int totalPop = 0;
            foreach (var county in economy.Counties.Values)
            {
                totalPop += county.Population.Total;
            }
            SimLog.Log("Consumption", $"Initialized with {economy.Counties.Count} counties, {totalPop:N0} total population");
        }

        public void Tick(SimulationState state, MapData mapData)
        {
            var economy = state.Economy;
            if (economy == null) return;

            _consumedThisTick.Clear();
            _unmetThisTick.Clear();

            foreach (var county in economy.Counties.Values)
            {
                // Clear previous unmet demand
                county.UnmetDemand.Clear();

                int population = county.Population.Total;
                if (population <= 0) continue;

                // Consume each consumer good
                foreach (var good in economy.Goods.ConsumerGoods)
                {
                    ConsumeGood(county, good, population);
                }
            }

            // Debug logging
            if (state.CurrentDay % LogInterval == 0)
            {
                LogConsumptionSummary(state);
            }
        }

        private void LogConsumptionSummary(SimulationState state)
        {
            SimLog.Log("Consumption", $"=== Day {state.CurrentDay} Consumption Summary ===");
            foreach (var kvp in _consumedThisTick)
            {
                float unmet = _unmetThisTick.ContainsKey(kvp.Key) ? _unmetThisTick[kvp.Key] : 0;
                float satisfaction = kvp.Value + unmet > 0 ? kvp.Value / (kvp.Value + unmet) * 100 : 100;
                SimLog.Log("Consumption", $"  {kvp.Key}: {kvp.Value:F1} consumed, {unmet:F1} unmet ({satisfaction:F0}% satisfied)");
            }
        }

        private void ConsumeGood(CountyEconomy county, GoodDef good, int population)
        {
            // Calculate demand
            float demand = population * good.BaseConsumption;
            if (demand <= 0) return;

            // Try to consume from stockpile
            float available = county.Stockpile.Get(good.Id);
            float consumed = county.Stockpile.Remove(good.Id, demand);
            float unmet = demand - consumed;

            // Track for logging
            if (!_consumedThisTick.ContainsKey(good.Id))
                _consumedThisTick[good.Id] = 0;
            _consumedThisTick[good.Id] += consumed;

            // Track unmet demand
            if (unmet > 0)
            {
                county.UnmetDemand[good.Id] = unmet;

                if (!_unmetThisTick.ContainsKey(good.Id))
                    _unmetThisTick[good.Id] = 0;
                _unmetThisTick[good.Id] += unmet;
            }

            // Future: Apply effects based on need category
            // Basic unmet → population decline, unrest
            // Comfort unmet → slower growth, mild unrest
            // Luxury unmet → just missed economic activity
        }
    }
}
