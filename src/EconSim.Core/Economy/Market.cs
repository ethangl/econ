using System;
using System.Collections.Generic;

namespace EconSim.Core.Economy
{
    /// <summary>
    /// Type of market - affects behavior and accessibility.
    /// </summary>
    public enum MarketType
    {
        /// <summary>Normal market with physical location and zone.</summary>
        Legitimate = 0,
        // Value 1 is reserved for the removed legacy black-market type.
        /// <summary>Off-map virtual market - represents trade with the outside world via edge access point.</summary>
        OffMap = 2
    }

    /// <summary>
    /// A market is a physical trading location in a county.
    /// Nearby counties can buy/sell goods through the market.
    /// </summary>
    [Serializable]
    public class Market
    {
        /// <summary>
        /// Type of market (Legitimate or OffMap).
        /// </summary>
        public MarketType Type { get; set; } = MarketType.Legitimate;
        /// <summary>
        /// Unique market identifier.
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// Cell ID where the market is physically located.
        /// </summary>
        public int LocationCellId { get; set; }

        /// <summary>
        /// Name of the market (often the burg name).
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Set of cell IDs that can access this market.
        /// Computed from transport accessibility.
        /// </summary>
        public HashSet<int> ZoneCellIds { get; set; } = new HashSet<int>();

        /// <summary>
        /// Transport cost from the market to each cell in its zone.
        /// Used to determine which market a cell should belong to when zones overlap.
        /// </summary>
        public Dictionary<int, float> ZoneCellCosts { get; set; } = new Dictionary<int, float>();

        /// <summary>
        /// Current market state for each good: supply, demand, price.
        /// </summary>
        public Dictionary<string, MarketGoodState> Goods { get; set; } = new Dictionary<string, MarketGoodState>();

        /// <summary>
        /// Pending buy orders posted by facilities and county populations.
        /// </summary>
        public List<BuyOrder> PendingBuyOrders { get; set; } = new List<BuyOrder>();

        /// <summary>
        /// Consignment inventory available for clearing.
        /// </summary>
        public List<ConsignmentLot> Inventory { get; set; } = new List<ConsignmentLot>();

        /// <summary>
        /// For OffMap markets: good IDs that this market supplies from off-map.
        /// Null/empty for non-OffMap markets.
        /// </summary>
        public HashSet<string> OffMapGoodIds { get; set; }

        /// <summary>
        /// For OffMap markets: the price multiplier applied to off-map goods.
        /// </summary>
        public float OffMapPriceMultiplier { get; set; } = 1f;
    }

    /// <summary>
    /// Market state for a single good type.
    /// </summary>
    [Serializable]
    public class MarketGoodState
    {
        /// <summary>
        /// Good type ID.
        /// </summary>
        public string GoodId { get; set; }

        /// <summary>
        /// Total quantity available for purchase (remaining after trades).
        /// Accumulated from county surpluses, reduced by purchases.
        /// </summary>
        public float Supply { get; set; }

        /// <summary>
        /// Total quantity offered this tick (before purchases).
        /// Use this for UI display.
        /// </summary>
        public float SupplyOffered { get; set; }

        /// <summary>
        /// Total quantity wanted.
        /// Accumulated from county deficits.
        /// </summary>
        public float Demand { get; set; }

        /// <summary>
        /// Current price per unit.
        /// Adjusts based on supply/demand ratio.
        /// </summary>
        public float Price { get; set; } = 1.0f;

        /// <summary>
        /// Base price (price when supply equals demand).
        /// </summary>
        public float BasePrice { get; set; } = 1.0f;

        /// <summary>
        /// Quantity traded in the last market tick.
        /// </summary>
        public float LastTradeVolume { get; set; }

        /// <summary>
        /// Total gold paid to sellers during the last clearing pass.
        /// </summary>
        public float Revenue { get; set; }
    }
}
