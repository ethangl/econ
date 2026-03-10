namespace EconSim.Core.Economy.V4
{
    /// <summary>
    /// Global economy state for v4. Wires counties to markets.
    /// </summary>
    public class EconomyStateV4
    {
        /// <summary>Per-county economy, indexed by county ID (sparse — null for unused slots).</summary>
        public CountyEconomyV4[] Counties;

        /// <summary>Per-market state, indexed by market ID (1-based, slot 0 unused).</summary>
        public MarketStateV4[] Markets;

        /// <summary>County ID → market ID lookup.</summary>
        public int[] CountyToMarket;

        /// <summary>Number of active markets.</summary>
        public int MarketCount;

        /// <summary>Transport cost between market hubs. [marketId][marketId]. Computed once at init.</summary>
        public float[][] HubToHubCost;
    }
}
