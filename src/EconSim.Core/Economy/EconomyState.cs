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

        /// <summary>Per-province economy (duke's granary), indexed by province ID.</summary>
        public ProvinceEconomy[] Provinces;

        /// <summary>Per-realm economy (king's treasury), indexed by realm ID.</summary>
        public RealmEconomy[] Realms;

        /// <summary>Median productivity across all counties (computed once at init).</summary>
        public float MedianProductivity;

        /// <summary>Recorded each tick for analysis.</summary>
        public List<EconomySnapshot> TimeSeries = new List<EconomySnapshot>();
    }
}
