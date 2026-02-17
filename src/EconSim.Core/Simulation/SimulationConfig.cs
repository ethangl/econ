namespace EconSim.Core.Simulation
{
    /// <summary>
    /// Configuration for simulation speed and timing.
    /// </summary>
    public static class SimulationConfig
    {
        /// <summary>
        /// Optional explicit economy seed override.
        /// Values greater than zero take precedence over map-derived seeds.
        /// </summary>
        public static int EconomySeedOverride = 0;

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
        /// Temporary economy tuning controls used while balancing production chains.
        /// </summary>
        public static class Economy
        {
            /// <summary>
            /// When enabled, facilities receive an operating subsidy to prevent liquidity-driven collapse.
            /// </summary>
            public static readonly bool EnableFacilitySubsidies = true;

            /// <summary>
            /// Keep each active facility at least this many subsistence-wage days liquid.
            /// </summary>
            public const float FacilityTreasuryFloorDays = 14f;

            /// <summary>
            /// Number of wage-debt days forgiven per tick while subsidies are enabled.
            /// </summary>
            public const int FacilityWageDebtReliefPerDay = 14;
        }

        /// <summary>
        /// Maximum simulation days processed in a single frame update.
        /// Caps catch-up work to avoid frame stalls when under load.
        /// </summary>
        public const int MaxTicksPerFrame = 4;

        /// <summary>
        /// Configuration for static transport backbone generation.
        /// </summary>
        public static class Roads
        {
            /// <summary>
            /// Build a static path network once at simulation initialization from major-county routes.
            /// </summary>
            public static readonly bool BuildStaticNetworkAtInit = true;

            /// <summary>
            /// Share of top-population counties considered "major".
            /// </summary>
            public const float MajorCountyTopPercent = 0.25f;

            /// <summary>
            /// Minimum number of major counties to include.
            /// </summary>
            public const int MajorCountyMinCount = 20;

            /// <summary>
            /// Hard cap on major counties to keep startup bounded.
            /// </summary>
            public const int MajorCountyMaxCount = 160;

            /// <summary>
            /// Number of nearest major-county connections traced per major county.
            /// </summary>
            public const int ConnectionsPerMajorCounty = 3;

            /// <summary>
            /// Path tier threshold percentile over positive edge usage values (0-1).
            /// </summary>
            public const float PathTierPercentile = 0.40f;

            /// <summary>
            /// Road tier threshold percentile over positive edge usage values (0-1).
            /// </summary>
            public const float RoadTierPercentile = 0.80f;

            /// <summary>
            /// Scale factor for per-route weight from county population.
            /// </summary>
            public const float RoutePopulationScale = 1500f;

            /// <summary>
            /// Floor for per-route weight to ensure sparse regions still draw visible paths.
            /// </summary>
            public const float MinRouteWeight = 1f;
        }
    }
}
