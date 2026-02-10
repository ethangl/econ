using EconSim.Core.Diagnostics;

namespace EconSim.Core
{
    public static class DomainLoggingBootstrap
    {
        private static UnityConsoleLogSink _unitySink;
        private static RingBufferLogSink _ringBufferSink;

        public static RingBufferLogSink RingBufferSink => _ringBufferSink;

        public static void Initialize(int ringBufferCapacity = 2000)
        {
            if (_unitySink == null)
            {
                _unitySink = new UnityConsoleLogSink();
                DomainLog.AddSink(_unitySink);
            }

            if (_ringBufferSink == null || _ringBufferSink.Capacity != ringBufferCapacity)
            {
                if (_ringBufferSink != null)
                {
                    DomainLog.RemoveSink(_ringBufferSink);
                }

                _ringBufferSink = new RingBufferLogSink(ringBufferCapacity);
                DomainLog.AddSink(_ringBufferSink);
            }

            // Default runtime filter: all domains, info and above.
            DomainLog.SetFilter(LogDomain.All, LogLevel.Info);
        }
    }
}
