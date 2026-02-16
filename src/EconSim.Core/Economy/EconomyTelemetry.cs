using System.Collections.Generic;

namespace EconSim.Core.Economy
{
    /// <summary>
    /// Economy telemetry snapshot populated by the telemetry system.
    /// </summary>
    public class EconomyTelemetry
    {
        public float TotalMoneySupply;
        public float MoneyInPopulation;
        public float MoneyInFacilities;
        public float MoneyVelocity;
        public int ActiveFacilityCount;
        public int IdleFacilityCount;
        public int DistressedFacilityCount;
        public Dictionary<string, GoodTelemetry> GoodMetrics = new Dictionary<string, GoodTelemetry>();
    }

    public struct GoodTelemetry
    {
        public float AvgPrice;
        public float TotalSupply;
        public float TotalClosingSupply;
        public float TotalDemand;
        public float TotalTradeVolume;
        public float UnmetDemand;
    }
}
