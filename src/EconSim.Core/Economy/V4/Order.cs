namespace EconSim.Core.Economy.V4
{
    public enum OrderSide : byte
    {
        Buy,
        Sell,
    }

    /// <summary>
    /// Identifies who placed an order, for coin routing after market resolution.
    /// </summary>
    public enum OrderSource : byte
    {
        /// <summary>Peasant surplus sell order — revenue to upper noble treasury.</summary>
        PeasantSurplus,
        /// <summary>Facility sell order — revenue to upper commoner coin (M).</summary>
        Facility,
        /// <summary>Upper nobility buy order — funded from upper noble treasury.</summary>
        UpperNobility,
        /// <summary>Lower nobility buy order — funded from lower noble treasury.</summary>
        LowerNobility,
        /// <summary>Upper clergy buy order — funded from upper clergy treasury.</summary>
        UpperClergy,
        /// <summary>Lower clergy buy order — funded from lower clergy coin.</summary>
        LowerClergy,
        /// <summary>Upper commoner buy/sell order — funded from / revenue to upper commoner coin (M).</summary>
        UpperCommoner,
        /// <summary>Cross-market trade order.</summary>
        Trade,
    }

    public struct Order
    {
        /// <summary>County that placed this order.</summary>
        public int CountyId;

        /// <summary>Good being bought or sold.</summary>
        public int GoodId;

        /// <summary>Buy or Sell.</summary>
        public OrderSide Side;

        /// <summary>Who placed the order (for coin routing).</summary>
        public OrderSource Source;

        /// <summary>Quantity in kg.</summary>
        public float Quantity;

        /// <summary>Maximum price per unit the buyer will pay. Ignored for sell orders.</summary>
        public float MaxBid;

        /// <summary>How much of this order was filled during market resolution.</summary>
        public float FilledQuantity;
    }
}
