using EconSim.Core.Economy;
using NUnit.Framework;

namespace EconSim.Tests
{
    [TestFixture]
    public class GoodRegistryRuntimeIdTests
    {
        [Test]
        public void Register_AssignsDenseRuntimeIds_AndSupportsLookups()
        {
            var registry = new GoodRegistry();
            registry.Register(new GoodDef { Id = "wheat", Name = "Wheat" });
            registry.Register(new GoodDef { Id = "bread", Name = "Bread" });

            Assert.That(registry.RuntimeCount, Is.EqualTo(2));

            Assert.That(registry.TryGetRuntimeId("wheat", out int wheatId), Is.True);
            Assert.That(registry.TryGetRuntimeId("bread", out int breadId), Is.True);
            Assert.That(wheatId, Is.EqualTo(0));
            Assert.That(breadId, Is.EqualTo(1));

            Assert.That(registry.GetByRuntimeId(wheatId)?.Id, Is.EqualTo("wheat"));
            Assert.That(registry.GetByRuntimeId(breadId)?.Id, Is.EqualTo("bread"));
            Assert.That(registry.GetByRuntimeId(99), Is.Null);
        }

        [Test]
        public void Register_ReplacingExistingGood_PreservesRuntimeId()
        {
            var registry = new GoodRegistry();
            registry.Register(new GoodDef { Id = "wheat", Name = "Wheat" });
            Assert.That(registry.TryGetRuntimeId("wheat", out int initialId), Is.True);
            Assert.That(initialId, Is.EqualTo(0));

            var replacement = new GoodDef { Id = "wheat", Name = "Wheat Override" };
            registry.Register(replacement);

            Assert.That(registry.RuntimeCount, Is.EqualTo(1));
            Assert.That(registry.TryGetRuntimeId("wheat", out int replacedId), Is.True);
            Assert.That(replacedId, Is.EqualTo(initialId));
            Assert.That(replacement.RuntimeId, Is.EqualTo(initialId));
            Assert.That(registry.Get("wheat")?.Name, Is.EqualTo("Wheat Override"));
            Assert.That(registry.GetByRuntimeId(initialId)?.Name, Is.EqualTo("Wheat Override"));
        }
    }
}
