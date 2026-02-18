namespace EconSim.Core.Economy
{
    /// <summary>
    /// Single time series data point, aggregated across all counties.
    /// Scalar fields are food values for backward compatibility.
    /// </summary>
    public class EconomySnapshot
    {
        public int Day;

        // Per-good aggregates (Layer 3)
        public float[] TotalStockByGood;
        public float[] TotalProductionByGood;

        // Backward-compat scalars = food values
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

        // Feudal redistribution (Layer 2)
        public float TotalDucalTax;
        public float TotalDucalRelief;
        public float TotalProvincialStockpile;
        public float TotalRoyalTax;
        public float TotalRoyalRelief;
        public float TotalRoyalStockpile;
    }
}
