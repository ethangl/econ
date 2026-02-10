using System;

namespace EconSim.Core.Diagnostics
{
    public readonly struct DomainLogEvent
    {
        public readonly DateTime TimestampUtc;
        public readonly LogDomain Domain;
        public readonly LogLevel Level;
        public readonly string Message;
        public readonly string Context;

        public DomainLogEvent(DateTime timestampUtc, LogDomain domain, LogLevel level, string message, string context)
        {
            TimestampUtc = timestampUtc;
            Domain = domain;
            Level = level;
            Message = message;
            Context = context;
        }

        public string FormatForConsole()
        {
            string ts = TimestampUtc.ToString("HH:mm:ss.fff");
            if (!string.IsNullOrEmpty(Context))
            {
                return $"[{ts}] [{Level}] [{Domain}] [{Context}] {Message}";
            }

            return $"[{ts}] [{Level}] [{Domain}] {Message}";
        }
    }
}
