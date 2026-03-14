using System.Collections.Generic;

namespace EconSim.Core.Economy
{
    /// <summary>
    /// Per-market state for economy v4. Each market has an order book and price data.
    /// </summary>
    public class MarketState
    {
        /// <summary>Market ID (1-based, matches v3 MarketInfo.Id).</summary>
        public int Id;

        /// <summary>Hub county ID (market center for transport cost calculations).</summary>
        public int HubCountyId;

        /// <summary>Hub cell ID (seat cell of hub county).</summary>
        public int HubCellId;

        /// <summary>Realm ID that owns this market.</summary>
        public int HubRealmId;

        /// <summary>Current price level from quantity theory: max((M*V)/Q, 1.0).</summary>
        public float PriceLevel = 1.0f;

        /// <summary>Last clearing price per good, indexed by GoodType.</summary>
        public float[] ClearingPrice;

        /// <summary>Total M across all counties in this market.</summary>
        public float TotalMoneySupply;

        /// <summary>Total real output Q = sum(sell_g * value_g) from last tick.</summary>
        public float TotalRealOutput;

        /// <summary>Orders posted to this market for the current tick. Cleared each tick.</summary>
        public List<Order> Orders;

        /// <summary>County IDs belonging to this market.</summary>
        public List<int> CountyIds;

        /// <summary>Unsold domestic supply per good from last tick (sell posted - sell filled).</summary>
        public float[] LastSurplus;

        /// <summary>Unmet domestic demand per good from last tick (buy posted - buy filled).</summary>
        public float[] LastDeficit;

        public MarketState(int id)
        {
            Id = id;
            int gc = Goods.Count;
            ClearingPrice = new float[gc];
            LastSurplus = new float[gc];
            LastDeficit = new float[gc];
            Orders = new List<Order>();
            CountyIds = new List<int>();

            // Seed clearing prices at base value
            for (int g = 0; g < gc; g++)
                ClearingPrice[g] = Goods.Value[g];
        }
    }
}
