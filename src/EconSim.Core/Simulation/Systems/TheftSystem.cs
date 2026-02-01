using System;
using System.Collections.Generic;
using EconSim.Core.Common;
using EconSim.Core.Data;
using EconSim.Core.Economy;

namespace EconSim.Core.Simulation.Systems
{
    /// <summary>
    /// Handles theft from county stockpiles.
    /// Stolen goods feed the black market.
    /// </summary>
    public class TheftSystem : ITickSystem
    {
        public string Name => "Theft";

        // Theft runs daily
        public int TickInterval => SimulationConfig.Intervals.Daily;

        // Base theft rate per day (modified by TheftRisk)
        // At 0.5% base rate and 0.8 TheftRisk, 0.4% of tools stolen per day
        private const float BaseTheftRate = 0.005f;

        // Minimum stockpile before theft occurs (thieves don't bother with scraps)
        private const float MinStockpileForTheft = 1f;

        // Reference to black market
        private Market _blackMarket;

        public void Initialize(SimulationState state, MapData mapData)
        {
            _blackMarket = state.Economy.BlackMarket;
            SimLog.Log("Theft", "Theft system initialized");
        }

        public void Tick(SimulationState state, MapData mapData)
        {
            if (_blackMarket == null) return;

            var economy = state.Economy;
            var totalStolen = new Dictionary<string, float>();

            foreach (var county in economy.Counties.Values)
            {
                ProcessCountyTheft(county, economy, totalStolen);
            }

            // Log summary if meaningful theft occurred
            if (totalStolen.Count > 0)
            {
                LogTheftSummary(state.CurrentDay, totalStolen);
            }
        }

        private void ProcessCountyTheft(
            CountyEconomy county,
            EconomyState economy,
            Dictionary<string, float> totalStolen)
        {
            foreach (var good in economy.Goods.All)
            {
                // Skip goods with no theft appeal
                if (good.TheftRisk <= 0) continue;

                // Only steal finished goods - black markets deal in consumer products
                if (!good.IsFinished) continue;

                float stockpiled = county.Stockpile.Get(good.Id);

                // Skip if stockpile too small
                if (stockpiled < MinStockpileForTheft) continue;

                // Calculate theft amount
                // Higher value goods (higher TheftRisk) get stolen more
                float theftRate = BaseTheftRate * good.TheftRisk;
                float stolen = stockpiled * theftRate;

                // Minimum meaningful theft
                if (stolen < 0.001f) continue;

                // Remove from county stockpile
                county.Stockpile.Remove(good.Id, stolen);

                // Add to black market
                _blackMarket.Goods[good.Id].Supply += stolen;

                // Track for logging
                if (!totalStolen.ContainsKey(good.Id)) totalStolen[good.Id] = 0;
                totalStolen[good.Id] += stolen;
            }
        }

        private void LogTheftSummary(int day, Dictionary<string, float> stolen)
        {
            var summary = new List<string>();
            foreach (var kvp in stolen)
            {
                if (kvp.Value >= 0.1f)
                    summary.Add($"{kvp.Key}:{kvp.Value:F1}");
            }

            if (summary.Count > 0)
            {
                SimLog.Log("Theft", $"Day {day}: stolen from stockpiles=[{string.Join(", ", summary)}]");
            }
        }
    }
}
