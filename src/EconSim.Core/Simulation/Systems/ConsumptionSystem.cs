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
        private Dictionary<string, float> _decayedThisTick = new Dictionary<string, float>();
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
            _decayedThisTick.Clear();

            foreach (var county in economy.Counties.Values)
            {
                // Clear previous unmet demand
                county.UnmetDemand.Clear();

                int population = county.Population.Total;

                // Consume each consumer good
                if (population > 0)
                {
                    foreach (var good in economy.Goods.ConsumerGoods)
                    {
                        ConsumeGood(county, good, population);
                    }
                }

                // Apply decay to all goods in stockpile (perishability)
                ApplyDecay(county, economy);
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

            // Log decay if significant
            if (_decayedThisTick.Count > 0)
            {
                var decaySummary = new List<string>();
                foreach (var kvp in _decayedThisTick)
                {
                    if (kvp.Value >= 0.1f)
                        decaySummary.Add($"{kvp.Key}:{kvp.Value:F1}");
                }
                if (decaySummary.Count > 0)
                {
                    SimLog.Log("Consumption", $"  Spoilage: {string.Join(", ", decaySummary)}");
                }
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

        private void ApplyDecay(CountyEconomy county, EconomyState economy)
        {
            // Get all goods currently in stockpile
            var goodsInStock = new List<string>();
            foreach (var kvp in county.Stockpile.All)
            {
                goodsInStock.Add(kvp.Key);
            }

            foreach (var goodId in goodsInStock)
            {
                var goodDef = economy.Goods.Get(goodId);
                if (goodDef == null || goodDef.DecayRate <= 0)
                    continue;

                float currentStock = county.Stockpile.Get(goodId);
                if (currentStock <= 0)
                    continue;

                // Calculate decay: stock * decayRate per day
                float decayed = currentStock * goodDef.DecayRate;
                if (decayed < 0.001f)
                    continue;  // Don't bother with tiny amounts

                county.Stockpile.Remove(goodId, decayed);

                // Track for logging
                if (!_decayedThisTick.ContainsKey(goodId))
                    _decayedThisTick[goodId] = 0;
                _decayedThisTick[goodId] += decayed;
            }
        }
    }
}
