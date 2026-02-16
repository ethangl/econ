using System;

namespace EconSim.Core.Economy
{
    [Serializable]
    public struct BuyOrder
    {
        // Facility IDs are positive. Population buyers are encoded as negative county IDs.
        public int BuyerId;
        public string GoodId;
        public float Quantity;
        public float MaxSpend;
        public float TransportCost;
        public int DayPosted;
    }

    [Serializable]
    public struct ConsignmentLot
    {
        // Facility IDs are positive. Synthetic sellers use reserved negative IDs.
        public int SellerId;
        public string GoodId;
        public float Quantity;
        public int DayListed;
    }

    public static class MarketOrderIds
    {
        public const int SeedSellerBase = -100000;
        public const int OffMapSellerBase = -200000;

        public static int MakePopulationBuyerId(int countyId) => -Math.Abs(countyId);
        public static bool TryGetPopulationCountyId(int buyerId, out int countyId)
        {
            countyId = buyerId < 0 ? -buyerId : 0;
            return countyId > 0;
        }

        public static int MakeSeedSellerId(int marketId) => SeedSellerBase - Math.Abs(marketId);
        public static int MakeOffMapSellerId(int marketId) => OffMapSellerBase - Math.Abs(marketId);

        public static bool IsSyntheticSeller(int sellerId) => sellerId <= SeedSellerBase;
    }
}
