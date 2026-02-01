using System;

namespace EconSim.Core.Common
{
    /// <summary>
    /// Simple logging for simulation debugging.
    /// Unity can hook into this by setting the LogAction.
    /// </summary>
    public static class SimLog
    {
        /// <summary>
        /// Set this to route logs to Unity's Debug.Log or elsewhere.
        /// Default: Console.WriteLine
        /// </summary>
        public static Action<string> LogAction { get; set; } = Console.WriteLine;

        public static void Log(string message)
        {
            LogAction?.Invoke(message);
        }

        public static void Log(string category, string message)
        {
            LogAction?.Invoke($"[{category}] {message}");
        }
    }
}
