namespace EconSim.Core.Economy
{
    /// <summary>
    /// Global economy state for v4. Wires counties to markets.
    /// </summary>
    public class EconomyState
    {
        /// <summary>Per-county economy, indexed by county ID (sparse — null for unused slots).</summary>
        public CountyEconomy[] Counties;

        /// <summary>Per-market state, indexed by market ID (1-based, slot 0 unused).</summary>
        public MarketState[] Markets;

        /// <summary>County ID → market ID lookup.</summary>
        public int[] CountyToMarket;

        /// <summary>Number of active markets.</summary>
        public int MarketCount;

        /// <summary>Transport cost between market hubs. [marketId][marketId]. Computed once at init.</summary>
        public float[][] HubToHubCost;

        /// <summary>Total population at initialization (for growth rate tracking).</summary>
        public float InitialTotalPopulation;

        // ── Per-phase timing (ms, updated each tick) ──
        public float PhaseGenerateOrdersMs;
        public float PhaseResolveMarketsMs;
        public float PhaseUpdateMoneyMs;
        public float PhaseUpdateSatisfactionMs;
        public float PhaseUpdatePopulationMs;
    }
}
