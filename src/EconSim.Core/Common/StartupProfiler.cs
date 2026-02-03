using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace EconSim.Core.Common
{
    /// <summary>
    /// Simple startup profiler for identifying performance bottlenecks.
    /// Logs timing data to console with hierarchical indentation.
    /// </summary>
    public static class StartupProfiler
    {
        private static readonly Stack<(string name, Stopwatch sw)> _stack = new Stack<(string, Stopwatch)>();
        private static readonly List<(string name, long ms, int depth)> _results = new List<(string, long, int)>();
        private static bool _enabled = true;

        public static bool Enabled
        {
            get => _enabled;
            set => _enabled = value;
        }

        /// <summary>
        /// Begin timing a named section. Call End() when done.
        /// </summary>
        public static void Begin(string name)
        {
            if (!_enabled) return;

            var sw = Stopwatch.StartNew();
            _stack.Push((name, sw));
        }

        /// <summary>
        /// End timing the current section and record the result.
        /// </summary>
        public static void End()
        {
            if (!_enabled) return;
            if (_stack.Count == 0) return;

            var (name, sw) = _stack.Pop();
            sw.Stop();
            _results.Add((name, sw.ElapsedMilliseconds, _stack.Count));
        }

        /// <summary>
        /// Time a single action and record the result.
        /// </summary>
        public static void Time(string name, Action action)
        {
            if (!_enabled)
            {
                action();
                return;
            }

            Begin(name);
            try
            {
                action();
            }
            finally
            {
                End();
            }
        }

        /// <summary>
        /// Log all recorded timings to the console.
        /// </summary>
        public static void LogResults()
        {
            if (!_enabled || _results.Count == 0) return;

            SimLog.Log("Profiler", "=== STARTUP TIMING ===");

            long totalMs = 0;
            foreach (var (name, ms, depth) in _results)
            {
                string indent = new string(' ', depth * 2);
                string timeStr = ms >= 1000 ? $"{ms / 1000.0:F2}s" : $"{ms}ms";
                SimLog.Log("Profiler", $"{indent}{name}: {timeStr}");

                // Only count top-level for total
                if (depth == 0) totalMs += ms;
            }

            string totalStr = totalMs >= 1000 ? $"{totalMs / 1000.0:F2}s" : $"{totalMs}ms";
            SimLog.Log("Profiler", $"=== TOTAL: {totalStr} ===");
        }

        /// <summary>
        /// Clear all recorded results for a fresh run.
        /// </summary>
        public static void Reset()
        {
            _stack.Clear();
            _results.Clear();
        }
    }
}
