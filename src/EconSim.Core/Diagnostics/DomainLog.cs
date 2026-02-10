using System;
using System.Collections.Generic;

namespace EconSim.Core.Diagnostics
{
    public static class DomainLog
    {
        private static readonly object SinkGate = new object();
        private static readonly List<IDomainLogSink> Sinks = new List<IDomainLogSink>();

        private static LogDomain _enabledDomains = LogDomain.All;
        private static LogLevel _minimumLevel = LogLevel.Info;

        public static LogDomain EnabledDomains
        {
            get => _enabledDomains;
            set => _enabledDomains = value;
        }

        public static LogLevel MinimumLevel
        {
            get => _minimumLevel;
            set => _minimumLevel = value;
        }

        public static bool HasSinks
        {
            get
            {
                lock (SinkGate)
                {
                    return Sinks.Count > 0;
                }
            }
        }

        public static bool IsEnabled(LogDomain domain, LogLevel level)
        {
            if (level < _minimumLevel)
            {
                return false;
            }

            if (domain == LogDomain.None)
            {
                return true;
            }

            return (_enabledDomains & domain) != 0;
        }

        public static void SetFilter(LogDomain domains, LogLevel minimumLevel)
        {
            _enabledDomains = domains;
            _minimumLevel = minimumLevel;
        }

        public static void AddSink(IDomainLogSink sink)
        {
            if (sink == null)
            {
                throw new ArgumentNullException(nameof(sink));
            }

            lock (SinkGate)
            {
                Sinks.Add(sink);
            }
        }

        public static void RemoveSink(IDomainLogSink sink)
        {
            if (sink == null)
            {
                return;
            }

            lock (SinkGate)
            {
                Sinks.Remove(sink);
            }
        }

        public static void ClearSinks()
        {
            lock (SinkGate)
            {
                Sinks.Clear();
            }
        }

        public static void Trace(LogDomain domain, string message, string context = null)
        {
            Log(domain, LogLevel.Trace, message, context);
        }

        public static void Debug(LogDomain domain, string message, string context = null)
        {
            Log(domain, LogLevel.Debug, message, context);
        }

        public static void Info(LogDomain domain, string message, string context = null)
        {
            Log(domain, LogLevel.Info, message, context);
        }

        public static void Warn(LogDomain domain, string message, string context = null)
        {
            Log(domain, LogLevel.Warn, message, context);
        }

        public static void Error(LogDomain domain, string message, string context = null)
        {
            Log(domain, LogLevel.Error, message, context);
        }

        public static void Log(LogDomain domain, LogLevel level, string message, string context = null)
        {
            if (!IsEnabled(domain, level))
            {
                return;
            }

            if (string.IsNullOrEmpty(message))
            {
                return;
            }

            var entry = new DomainLogEvent(DateTime.UtcNow, domain, level, message, context);
            IDomainLogSink[] snapshot;
            lock (SinkGate)
            {
                if (Sinks.Count == 0)
                {
                    return;
                }

                snapshot = Sinks.ToArray();
            }

            for (int i = 0; i < snapshot.Length; i++)
            {
                snapshot[i].Write(entry);
            }
        }
    }
}
