using System;
using System.Collections.Generic;

namespace EconSim.Core.Simulation
{
    /// <summary>
    /// Runtime timing stats for one tick system.
    /// </summary>
    [Serializable]
    public class TickSystemPerformanceStats
    {
        public string Name;
        public int TickInterval;
        public int InvocationCount;
        public float TotalMs;
        public float MaxMs;
        public float LastMs;

        public float AvgMs => InvocationCount > 0 ? TotalMs / InvocationCount : 0f;
    }

    /// <summary>
    /// Runtime timing stats for simulation ticks and all registered systems.
    /// </summary>
    [Serializable]
    public class SimulationPerformanceStats
    {
        public int TickSamples;
        public float TotalTickMs;
        public float MaxTickMs;
        public float LastTickMs;
        public Dictionary<string, TickSystemPerformanceStats> Systems = new Dictionary<string, TickSystemPerformanceStats>();

        public float AvgTickMs => TickSamples > 0 ? TotalTickMs / TickSamples : 0f;

        public void RecordTick(float tickMs)
        {
            if (tickMs < 0f)
                tickMs = 0f;

            TickSamples++;
            TotalTickMs += tickMs;
            LastTickMs = tickMs;
            if (tickMs > MaxTickMs)
                MaxTickMs = tickMs;
        }

        public void RecordSystem(string systemName, int tickInterval, float elapsedMs)
        {
            if (string.IsNullOrWhiteSpace(systemName))
                systemName = "Unknown";
            if (elapsedMs < 0f)
                elapsedMs = 0f;

            if (!Systems.TryGetValue(systemName, out var stats))
            {
                stats = new TickSystemPerformanceStats
                {
                    Name = systemName,
                    TickInterval = tickInterval
                };
                Systems[systemName] = stats;
            }

            stats.TickInterval = tickInterval;
            stats.InvocationCount++;
            stats.TotalMs += elapsedMs;
            stats.LastMs = elapsedMs;
            if (elapsedMs > stats.MaxMs)
                stats.MaxMs = elapsedMs;
        }
    }
}
