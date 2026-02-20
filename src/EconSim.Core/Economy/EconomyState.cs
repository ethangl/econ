using EconSim.Core.Common;

namespace EconSim.Core.Economy
{
    /// <summary>
    /// Global economy state, initialized from MapData.
    /// </summary>
    public class EconomyState
    {
        /// <summary>10 years of daily snapshots.</summary>
        const int TimeSeriesCapacity = 3650;

        /// <summary>Per-county economy, indexed by county ID.</summary>
        public CountyEconomy[] Counties;

        /// <summary>Per-province economy (duke's granary), indexed by province ID.</summary>
        public ProvinceEconomy[] Provinces;

        /// <summary>Per-realm economy (king's treasury), indexed by realm ID.</summary>
        public RealmEconomy[] Realms;

        /// <summary>Median productivity across all counties, per good type (computed once at init).</summary>
        public float[] MedianProductivity;

        /// <summary>Recorded each tick for analysis. Ring buffer — oldest entries dropped when full.</summary>
        public RingBuffer<EconomySnapshot> TimeSeries = new RingBuffer<EconomySnapshot>(TimeSeriesCapacity);

        /// <summary>Current inter-realm market prices (Crowns/kg), indexed by GoodType. Set by InterRealmTradeSystem.</summary>
        public float[] MarketPrices = new float[Goods.Count];

        /// <summary>County adjacency graph. countyId → array of adjacent county IDs. Built once at init.</summary>
        public int[][] CountyAdjacency;

        /// <summary>All facility instances, set by FacilityProductionSystem.</summary>
        public Facility[] Facilities;

        /// <summary>Per-county list of facility indices into Facilities[]. Indexed by county ID.</summary>
        public System.Collections.Generic.List<int>[] CountyFacilityIndices;
    }
}
