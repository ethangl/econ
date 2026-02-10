using System;
using System.Collections.Generic;
using EconSim.Core.Diagnostics;

namespace EconSim.Core.Common
{
    /// <summary>
    /// Simple logging for simulation debugging.
    /// Unity can hook into this by setting the LogAction.
    /// </summary>
    public static class SimLog
    {
        private static readonly Dictionary<string, LogDomain> CategoryDomains =
            new Dictionary<string, LogDomain>(StringComparer.OrdinalIgnoreCase)
            {
                ["mapgen"] = LogDomain.MapGen,
                ["heightmapdsl"] = LogDomain.HeightmapDsl,
                ["climate"] = LogDomain.Climate,
                ["rivers"] = LogDomain.Rivers,
                ["biomes"] = LogDomain.Biomes,
                ["population"] = LogDomain.Population,
                ["political"] = LogDomain.Political,
                ["economy"] = LogDomain.Economy,
                ["market"] = LogDomain.Economy,
                ["production"] = LogDomain.Economy,
                ["consumption"] = LogDomain.Economy,
                ["trade"] = LogDomain.Economy,
                ["theft"] = LogDomain.Economy,
                ["transport"] = LogDomain.Transport,
                ["roads"] = LogDomain.Roads,
                ["renderer"] = LogDomain.Renderer,
                ["shaders"] = LogDomain.Shaders,
                ["overlay"] = LogDomain.Overlay,
                ["selection"] = LogDomain.Selection,
                ["ui"] = LogDomain.UI,
                ["camera"] = LogDomain.Camera,
                ["simulation"] = LogDomain.Simulation,
                ["profiler"] = LogDomain.Bootstrap,
                ["bootstrap"] = LogDomain.Bootstrap,
                ["io"] = LogDomain.IO
            };

        /// <summary>
        /// Set this to route logs to Unity's Debug.Log or elsewhere.
        /// Default: Console.WriteLine
        /// </summary>
        public static Action<string> LogAction { get; set; } = Console.WriteLine;

        public static void Log(string message)
        {
            DomainLog.Info(LogDomain.Simulation, message);

            // Fallback for tools/tests that have not installed DomainLog sinks.
            if (!DomainLog.HasSinks)
            {
                LogAction?.Invoke(message);
            }
        }

        public static void Log(string category, string message)
        {
            LogDomain domain = ResolveDomain(category);
            DomainLog.Info(domain, message, category);

            // Fallback for tools/tests that have not installed DomainLog sinks.
            if (!DomainLog.HasSinks)
            {
                LogAction?.Invoke($"[{category}] {message}");
            }
        }

        private static LogDomain ResolveDomain(string category)
        {
            if (string.IsNullOrWhiteSpace(category))
            {
                return LogDomain.Simulation;
            }

            if (CategoryDomains.TryGetValue(category.Trim(), out var domain))
            {
                return domain;
            }

            return LogDomain.Simulation;
        }
    }
}
