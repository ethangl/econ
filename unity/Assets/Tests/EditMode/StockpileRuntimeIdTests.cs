using EconSim.Core.Economy;
using NUnit.Framework;

namespace EconSim.Tests
{
    [TestFixture]
    public class StockpileRuntimeIdTests
    {
        [Test]
        public void BoundStockpile_StringAndRuntimePathsStayInSync()
        {
            var registry = new GoodRegistry();
            registry.Register(new GoodDef { Id = "wheat", Name = "Wheat" });
            Assert.That(registry.TryGetRuntimeId("wheat", out int wheatId), Is.True);

            var stockpile = new Stockpile();
            stockpile.BindGoods(registry);

            stockpile.Add("wheat", 5f);
            Assert.That(stockpile.Get("wheat"), Is.EqualTo(5f));
            Assert.That(stockpile.Get(wheatId), Is.EqualTo(5f));

            stockpile.Add(wheatId, 2f);
            Assert.That(stockpile.Get("wheat"), Is.EqualTo(7f));
            Assert.That(stockpile.Get(wheatId), Is.EqualTo(7f));

            float removed = stockpile.Remove("wheat", 3f);
            Assert.That(removed, Is.EqualTo(3f));
            Assert.That(stockpile.Get(wheatId), Is.EqualTo(4f));
        }

        [Test]
        public void BindGoods_MigratesPreexistingStringEntries()
        {
            var registry = new GoodRegistry();
            registry.Register(new GoodDef { Id = "bread", Name = "Bread" });
            Assert.That(registry.TryGetRuntimeId("bread", out int breadId), Is.True);

            var stockpile = new Stockpile();
            stockpile.Add("bread", 3f);
            Assert.That(stockpile.Get("bread"), Is.EqualTo(3f));

            stockpile.BindGoods(registry);

            Assert.That(stockpile.Get("bread"), Is.EqualTo(3f));
            Assert.That(stockpile.Get(breadId), Is.EqualTo(3f));
            Assert.That(stockpile.Count, Is.EqualTo(1));
        }
    }
}
