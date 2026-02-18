using System.Collections.Generic;

namespace EconSim.Core.Economy
{
    /// <summary>
    /// Global economy state, initialized from MapData.
    /// </summary>
    public class EconomyState
    {
        /// <summary>Per-county economy, indexed by county ID.</summary>
        public CountyEconomy[] Counties;

        /// <summary>Median productivity across all counties (computed once at init).</summary>
        public float MedianProductivity;

        /// <summary>Recorded each tick for analysis.</summary>
        public List<EconomySnapshot> TimeSeries = new List<EconomySnapshot>();
    }
}
