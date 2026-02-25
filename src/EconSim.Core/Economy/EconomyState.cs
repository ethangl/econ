using System.Collections.Generic;
using EconSim.Core.Common;

namespace EconSim.Core.Economy
{
    public struct MarketInfo
    {
        public int Id;            // 1-based, used as palette index
        public int HubCountyId;   // County hosting the market
        public int HubCellId;     // Seat cell of hub county (transport cost origin)
        public int HubRealmId;    // Realm owning hub county (palette color source)
    }

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

        /// <summary>Population-weighted average market prices (Crowns/kg), indexed by GoodType. Set by InterRealmTradeSystem.</summary>
        public float[] MarketPrices = new float[Goods.Count];

        /// <summary>Per-market prices (Crowns/kg). [marketId][goodId], 1-indexed (slot 0 unused). Set by InterRealmTradeSystem.</summary>
        public float[][] PerMarketPrices;

        /// <summary>Transport cost between market hubs. [marketId][marketId]. Computed once at init.</summary>
        public float[][] HubToHubCost;

        /// <summary>Per-market embargo lists. [marketId] → set of blocked realm IDs. Empty by default.</summary>
        public HashSet<int>[] MarketEmbargoes;

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

        /// <summary>Total production capacity per good (kg/day). Extraction (pop * Productivity) + facility labor capacity. Updated by EconomySystem each tick.</summary>
        public float[] ProductionCapacity = new float[Goods.Count];

        /// <summary>Per-county sabbath day (0=Monday..6=Sunday), indexed by county ID. Derived from seat cell's religion.</summary>
        public int[] CountySabbathDay;

        /// <summary>Market definitions, indexed by market ID (slot 0 unused).</summary>
        public MarketInfo[] Markets;

        /// <summary>County ID → market ID lookup.</summary>
        public int[] CountyToMarket;
    }
}
