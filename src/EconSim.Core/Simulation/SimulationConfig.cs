using System.Collections.Generic;

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
            /// Canonical money unit used across treasury, wages, and prices.
            /// One Crown is defined as one gram of gold (0.001 kg).
            /// </summary>
            public static class Money
            {
                public const string UnitName = "Crown";
                public const string UnitNamePlural = "Crowns";
                public const float KgGoldPerCrown = 0.001f;
                public const float CrownsPerKgGold = 1000f;
                public const string PricePerKgLabel = "Crowns/kg";
            }

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

            /// <summary>
            /// Temporary chain-isolation mode for focused tuning.
            /// When enabled, only explicitly listed goods/facilities participate.
            /// </summary>
            public static readonly bool EnableChainIsolation = true;

            /// <summary>
            /// When enabled, subsistence wage is pegged directly to the raw salt base price
            /// (Crowns/kg), instead of the basic basket model.
            /// </summary>
            public static readonly bool PegSubsistenceWageToRawSaltPrice = true;

            /// <summary>
            /// Share of staple flour demand covered by local subsistence stockpile before
            /// remaining demand is routed to markets.
            /// 0.99 means 99% subsistence / 1% market.
            /// </summary>
            public const float BreadSubsistenceShare = 0.99f;

            /// <summary>
            /// Share of beer demand covered by local home brewing from county grain stockpiles
            /// before remaining demand is routed to markets.
            /// 0.50 means 50% home-brew / 50% market.
            /// </summary>
            public const float BeerSubsistenceShare = 0.50f;

            /// <summary>
            /// Flat hauling fee in Crowns per kilogram per transport-cost unit.
            /// Applied uniformly across all goods.
            /// Tuned to be microscopic at short distances.
            /// </summary>
            public const float FlatHaulingFeePerKgPerTransportCostUnit = 0.00002f;

            /// <summary>
            /// Grain-to-flour conversion baseline used across reserve and subsistence logic.
            /// </summary>
            public const float RawGrainKgPerFlourKg = 1f / 0.72f;
            public const float FlourKgPerRawGrainKg = 1f / RawGrainKgPerFlourKg;

            /// <summary>
            /// Barley/malt/beer conversion baselines used in home-brew subsistence logic.
            /// </summary>
            public const float RawBarleyKgPerMaltKg = 1f / 0.8f;
            public const float MaltKgPerRawBarleyKg = 1f / RawBarleyKgPerMaltKg;
            public const float BeerKgPerMaltKg = 3f;
            public const float BeerKgPerRawBarleyKg = MaltKgPerRawBarleyKg * BeerKgPerMaltKg;
            public const float RawBarleyKgPerBeerKg = 1f / BeerKgPerRawBarleyKg;

            private static readonly HashSet<string> EnabledGoods = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase)
            {
                "raw_salt",
                "salt",
                "wheat",
                "rye",
                "barley",
                "flour",
                "bread",
                "malt",
                "beer"
            };

            private static readonly HashSet<string> EnabledFacilities = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase)
            {
                "salt_works",
                "salt_warehouse",
                "farm",
                "rye_farm",
                "barley_farm",
                "mill",
                "bakery",
                "malt_house",
                "brewery"
            };

            public static bool IsGoodEnabled(string goodId)
            {
                if (!EnableChainIsolation)
                    return true;
                if (string.IsNullOrWhiteSpace(goodId))
                    return false;
                return EnabledGoods.Contains(goodId);
            }

            public static bool IsFacilityEnabled(string facilityTypeId)
            {
                if (!EnableChainIsolation)
                    return true;
                if (string.IsNullOrWhiteSpace(facilityTypeId))
                    return false;
                return EnabledFacilities.Contains(facilityTypeId);
            }
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
