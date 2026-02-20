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

        /// <summary>When true, EconomySystem records daily snapshots into TimeSeries.</summary>
        public bool CaptureSnapshots;

        /// <summary>Current inter-realm market prices (Crowns/kg), indexed by GoodType. Set by InterRealmTradeSystem.</summary>
        public float[] MarketPrices = new float[Goods.Count];

        /// <summary>County adjacency graph. countyId → array of adjacent county IDs. Built once at init.</summary>
        public int[][] CountyAdjacency;

        /// <summary>All facility instances, set during EconomySystem init.</summary>
        public Facility[] Facilities;

        /// <summary>Per-county list of facility indices into Facilities[]. Indexed by county ID.</summary>
        public System.Collections.Generic.List<int>[] CountyFacilityIndices;

        /// <summary>Population per province, indexed by province ID. Updated by PopulationSystem monthly.</summary>
        public float[] ProvincePop;

        /// <summary>Population per realm, indexed by realm ID. Updated by PopulationSystem monthly.</summary>
        public float[] RealmPop;

        /// <summary>Effective demand per pop per day, indexed by GoodType. Updated by EconomySystem each tick.</summary>
        public float[] EffectiveDemandPerPop;

        /// <summary>Total extraction capacity per good (kg/day). sum(pop * Productivity[g]) across all counties. Updated by EconomySystem each tick.</summary>
        public float[] ExtractionCapacity = new float[Goods.Count];

        /// <summary>County ID hosting the central market. Receives market fees from all trade transactions.</summary>
        public int MarketCountyId = -1;
    }
}
