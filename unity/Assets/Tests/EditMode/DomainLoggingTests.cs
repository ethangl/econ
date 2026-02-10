using EconSim.Core.Common;
using EconSim.Core.Diagnostics;
using NUnit.Framework;

namespace EconSim.Tests
{
    public class DomainLoggingTests
    {
        [SetUp]
        public void SetUp()
        {
            DomainLog.ClearSinks();
            DomainLog.SetFilter(LogDomain.All, LogLevel.Trace);
            SimLog.LogAction = _ => { };
        }

        [TearDown]
        public void TearDown()
        {
            DomainLog.ClearSinks();
            DomainLog.SetFilter(LogDomain.All, LogLevel.Info);
        }

        [Test]
        public void DomainFilter_Blocks_DisabledDomains_And_LowerSeverity()
        {
            var sink = new RingBufferLogSink(8);
            DomainLog.AddSink(sink);
            DomainLog.SetFilter(LogDomain.Economy, LogLevel.Warn);

            DomainLog.Info(LogDomain.Economy, "info-econ");
            DomainLog.Warn(LogDomain.Economy, "warn-econ");
            DomainLog.Error(LogDomain.Renderer, "error-renderer");

            var entries = sink.Snapshot(8);
            Assert.That(entries.Count, Is.EqualTo(1));
            Assert.That(entries[0].Level, Is.EqualTo(LogLevel.Warn));
            Assert.That(entries[0].Domain, Is.EqualTo(LogDomain.Economy));
            Assert.That(entries[0].Message, Is.EqualTo("warn-econ"));
        }

        [Test]
        public void SimLog_CategoryRouting_Uses_DomainMapping()
        {
            var sink = new RingBufferLogSink(8);
            DomainLog.AddSink(sink);
            DomainLog.SetFilter(LogDomain.Economy, LogLevel.Info);

            SimLog.Log("Economy", "allowed");
            SimLog.Log("Renderer", "blocked");

            var entries = sink.Snapshot(8);
            Assert.That(entries.Count, Is.EqualTo(1));
            Assert.That(entries[0].Domain, Is.EqualTo(LogDomain.Economy));
            Assert.That(entries[0].Context, Is.EqualTo("Economy"));
            Assert.That(entries[0].Message, Is.EqualTo("allowed"));
        }
    }
}
