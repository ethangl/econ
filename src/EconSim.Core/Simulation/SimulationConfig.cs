namespace EconSim.Core.Simulation
{
    /// <summary>
    /// Configuration for simulation speed and timing.
    /// </summary>
    public static class SimulationConfig
    {
        /// <summary>
        /// Speed presets in ticks (days) per second.
        /// </summary>
        public static class Speed
        {
            public const float Slow = 0.5f;    // 0.5 days/sec
            public const float Normal = 1f;    // 1 day/sec
            public const float Fast = 5f;      // 5 days/sec
            public const float Ultra = 20f;    // 20 days/sec
            public const float Hyper = 60f;    // 60 days/sec
        }

        /// <summary>
        /// Standard tick intervals for systems.
        /// </summary>
        public static class Intervals
        {
            public const int Daily = 1;
            public const int Weekly = 7;
            public const int Monthly = 30;
            public const int Yearly = 365;
        }

        /// <summary>
        /// Configuration for emergent road/path evolution.
        /// </summary>
        public static class Roads
        {
            /// <summary>
            /// When false, trade does not record route traffic and roads do not evolve over time.
            /// Keep disabled for now to avoid simulation hitches while we redesign infrastructure growth.
            /// </summary>
            public const bool DynamicEvolutionEnabled = false;
        }
    }
}
