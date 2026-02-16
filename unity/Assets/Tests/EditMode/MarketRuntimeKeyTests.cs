using EconSim.Core.Economy;
using NUnit.Framework;

namespace EconSim.Tests
{
    [TestFixture]
    public class MarketRuntimeKeyTests
    {
        [Test]
        public void BoundMarket_StoresBooksByRuntimeId_AndStringQueriesStillWork()
        {
            var goods = new GoodRegistry();
            goods.Register(new GoodDef { Id = "wheat", Name = "Wheat" });
            Assert.That(goods.TryGetRuntimeId("wheat", out int wheatRuntimeId), Is.True);

            var market = new Market();
            market.BindGoods(goods);

            market.AddPendingBuyOrder(new BuyOrder
            {
                BuyerId = 10,
                GoodId = "wheat",
                Quantity = 5f,
                MaxSpend = 10f,
                DayPosted = 0
            });

            market.AddInventoryLot(new ConsignmentLot
            {
                SellerId = 20,
                GoodId = "wheat",
                Quantity = 7f,
                DayListed = 0
            });

            Assert.That(market.PendingBuyOrdersByGood.ContainsKey(wheatRuntimeId), Is.True);
            Assert.That(market.PendingInventoryByGood.ContainsKey(wheatRuntimeId), Is.True);

            market.PromotePendingBooks(currentDay: 1);

            Assert.That(market.TryGetTradableOrders("wheat", out var ordersByString), Is.True);
            Assert.That(market.TryGetTradableOrders(wheatRuntimeId, out var ordersByRuntime), Is.True);
            Assert.That(ordersByString.Count, Is.EqualTo(1));
            Assert.That(ordersByRuntime.Count, Is.EqualTo(1));

            Assert.That(market.GetTradableSupply("wheat"), Is.EqualTo(7f));
            Assert.That(market.GetTradableSupply(wheatRuntimeId), Is.EqualTo(7f));
        }

        [Test]
        public void UnboundMarket_RejectsUnknownStringGoods_ButAcceptsExplicitRuntimeIds()
        {
            var market = new Market();
            market.AddPendingBuyOrder(new BuyOrder
            {
                BuyerId = 1,
                GoodId = "mystery_good",
                Quantity = 2f,
                MaxSpend = 3f,
                DayPosted = 0
            });

            Assert.That(market.ResolveGoodRuntimeId("mystery_good"), Is.EqualTo(-1));
            Assert.That(market.PendingBuyOrdersByGood.Count, Is.EqualTo(0));

            market.AddPendingBuyOrder(new BuyOrder
            {
                BuyerId = 2,
                GoodId = "mystery_good",
                GoodRuntimeId = 42,
                Quantity = 4f,
                MaxSpend = 7f,
                DayPosted = 0
            });

            Assert.That(market.PendingBuyOrdersByGood.ContainsKey(42), Is.True);
        }
    }
}
