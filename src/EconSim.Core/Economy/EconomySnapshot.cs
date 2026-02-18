namespace EconSim.Core.Economy
{
    /// <summary>
    /// Single time series data point, aggregated across all counties.
    /// </summary>
    public class EconomySnapshot
    {
        public int Day;
        public float TotalStock;
        public float TotalProduction;
        public float TotalConsumption;
        public float TotalUnmetNeed;
        public int SurplusCounties;
        public int DeficitCounties;
        public int StarvingCounties;
        public float MinStock;
        public float MaxStock;
        public float MedianProductivity;
    }
}
