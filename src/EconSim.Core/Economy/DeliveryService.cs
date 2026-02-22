namespace EconSim.Core.Economy
{
    /// <summary>
    /// Centralizes all inter-entity goods transfers (trade, granary, relief).
    /// v1: instant transfer (behaviorally identical to inline Stock manipulation).
    /// v2: transport cost accounting.
    /// v3: transit time with per-tick arrival processing.
    /// </summary>
    public class DeliveryService
    {
        const float TransportRatePerKg = 0.007f;
        const float CrossRealmMultiplier = 3f;

        internal float GetTransportCost(TradeScope scope)
        {
            switch (scope)
            {
                case TradeScope.CrossProvince: return TransportRatePerKg;
                case TradeScope.CrossRealm:   return TransportRatePerKg * CrossRealmMultiplier;
                default:                       return 0f;
            }
        }

        /// <summary>Pooled trade: seller dispatches goods (removes from stock).</summary>
        public void Dispatch(CountyEconomy county, int good, float amount)
        {
            county.Stock[good] -= amount;
        }

        /// <summary>Pooled trade: buyer receives goods (adds to stock).</summary>
        public void Receive(CountyEconomy county, int good, float amount)
        {
            county.Stock[good] += amount;
        }

        /// <summary>Direct transfer: county → provincial granary.</summary>
        public void ShipToGranary(CountyEconomy from, ProvinceEconomy to, int good, float amount)
        {
            from.Stock[good] -= amount;
            to.Stockpile[good] += amount;
        }

        /// <summary>Direct transfer: provincial granary → county.</summary>
        public void ShipFromGranary(ProvinceEconomy from, CountyEconomy to, int good, float amount)
        {
            from.Stockpile[good] -= amount;
            to.Stock[good] += amount;
        }
    }
}
