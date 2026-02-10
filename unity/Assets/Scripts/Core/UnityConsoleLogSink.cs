using EconSim.Core.Diagnostics;
using UnityEngine;

namespace EconSim.Core
{
    public sealed class UnityConsoleLogSink : IDomainLogSink
    {
        public void Write(DomainLogEvent entry)
        {
            string text = entry.FormatForConsole();
            switch (entry.Level)
            {
                case LogLevel.Error:
                    Debug.LogError(text);
                    break;
                case LogLevel.Warn:
                    Debug.LogWarning(text);
                    break;
                default:
                    Debug.Log(text);
                    break;
            }
        }
    }
}
