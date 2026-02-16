using System.Collections.Generic;
using EconSim.Core.Data;
using EconSim.Core.Economy;

namespace EconSim.Core.Simulation.Systems
{
    /// <summary>
    /// Economy V2 telemetry snapshot system.
    /// </summary>
    public class TelemetrySystem : ITickSystem
    {
        public string Name => "Telemetry";
        public int TickInterval => SimulationConfig.Intervals.Daily;

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

            var telemetry = state.Telemetry ?? new EconomyTelemetry();
            telemetry.GoodMetrics = new Dictionary<string, GoodTelemetry>();

            float moneyInPopulation = 0f;
            foreach (var county in economy.Counties.Values)
                moneyInPopulation += county.Population.Treasury;

            float moneyInFacilities = 0f;
            int active = 0;
            int idle = 0;
            int distressed = 0;
            foreach (var facility in economy.Facilities.Values)
            {
                moneyInFacilities += facility.Treasury;
                if (facility.WageDebtDays >= 3) distressed++;
                if (facility.IsActive) active++;
                else idle++;
            }

            float dailyTradeValue = 0f;
            foreach (var market in economy.Markets.Values)
            {
                if (market.Type == MarketType.Black)
                    continue;

                foreach (var gs in market.Goods.Values)
                {
                    if (!telemetry.GoodMetrics.TryGetValue(gs.GoodId, out var metric))
                        metric = new GoodTelemetry();

                    metric.AvgPrice += gs.Price;
                    metric.TotalSupply += gs.Supply;
                    metric.TotalDemand += gs.Demand;
                    metric.TotalTradeVolume += gs.LastTradeVolume;
                    metric.UnmetDemand += gs.Demand > gs.LastTradeVolume ? (gs.Demand - gs.LastTradeVolume) : 0f;
                    telemetry.GoodMetrics[gs.GoodId] = metric;

                    dailyTradeValue += gs.Revenue;
                }
            }

            int marketCount = 0;
            foreach (var market in economy.Markets.Values)
            {
                if (market.Type != MarketType.Black)
                    marketCount++;
            }
            if (marketCount > 0)
            {
                var keys = new List<string>(telemetry.GoodMetrics.Keys);
                for (int i = 0; i < keys.Count; i++)
                {
                    var metric = telemetry.GoodMetrics[keys[i]];
                    metric.AvgPrice /= marketCount;
                    telemetry.GoodMetrics[keys[i]] = metric;
                }
            }

            telemetry.MoneyInPopulation = moneyInPopulation;
            telemetry.MoneyInFacilities = moneyInFacilities;
            telemetry.TotalMoneySupply = moneyInPopulation + moneyInFacilities;
            telemetry.MoneyVelocity = telemetry.TotalMoneySupply > 0f
                ? dailyTradeValue / telemetry.TotalMoneySupply
                : 0f;
            telemetry.ActiveFacilityCount = active;
            telemetry.IdleFacilityCount = idle;
            telemetry.DistressedFacilityCount = distressed;

            state.Telemetry = telemetry;
        }
    }
}
