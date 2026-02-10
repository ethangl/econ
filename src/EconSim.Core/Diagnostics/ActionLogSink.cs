using System;

namespace EconSim.Core.Diagnostics
{
    public sealed class ActionLogSink : IDomainLogSink
    {
        private readonly Action<string> _log;
        private readonly Action<string> _warn;
        private readonly Action<string> _error;

        public ActionLogSink(Action<string> log, Action<string> warn = null, Action<string> error = null)
        {
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _warn = warn ?? log;
            _error = error ?? _warn;
        }

        public void Write(DomainLogEvent entry)
        {
            string text = entry.FormatForConsole();
            switch (entry.Level)
            {
                case LogLevel.Error:
                    _error(text);
                    break;
                case LogLevel.Warn:
                    _warn(text);
                    break;
                default:
                    _log(text);
                    break;
            }
        }
    }
}
