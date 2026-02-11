using System;

namespace MapGen.Core
{
    /// <summary>
    /// Configuration for MapGen V2 world-unit generation.
    /// Elevation values are signed meters relative to sea level.
    /// </summary>
    public class MapGenV2Config
    {
        public int Seed = 12345;
        public int CellCount = 10000;
        public float AspectRatio = 16f / 9f;
        public float CellSizeKm = 2.5f;
        public HeightmapTemplateType Template = HeightmapTemplateType.LowIsland;
        public float LatitudeSouth = 30f;

        // Elevation envelope in signed meters (sea level = 0).
        public float MaxElevationMeters = 5000f;
        public float MaxSeaDepthMeters = 1250f;

        // Climate defaults.
        public float EquatorTempC = 29f;
        public float NorthPoleTempC = -15f;
        public float SouthPoleTempC = -25f;
        public float LapseRateCPerKm = 6.5f;
        public float MaxAnnualPrecipitationMm = 3000f;
        public WindBand[] WindBands = new WindBand[]
        {
            new WindBand(-90, -60, 315),
            new WindBand(-60, -30, 135),
            new WindBand(-30,   0, 315),
            new WindBand(  0,  30, 225),
            new WindBand( 30,  60,  45),
            new WindBand( 60,  90, 225),
        };

        // River extraction thresholds in normalized flux space.
        public float RiverThreshold = 240f;
        public float RiverTraceThreshold = 12f;
        public int MinRiverVertices = 12;

        // Optional per-template tuning override used by analysis/sweeps.
        public HeightmapTemplateTuningProfile TemplateTuningOverride;

        // Sub-seed constants (golden-ratio hashing for decorrelation).
        const uint MeshXor = 0x9E3779B9;
        const uint ElevationXor = 0xA54FF53A;
        const uint ClimateXor = 0x63D83595;
        const uint RiverXor = 0x7B9D14E1;

        public int MeshSeed => (int)((uint)Seed ^ MeshXor);
        public int ElevationSeed => (int)((uint)Seed ^ ElevationXor);
        public int ClimateSeed => (int)((uint)Seed ^ ClimateXor);
        public int RiverSeed => (int)((uint)Seed ^ RiverXor);

        public void Validate()
        {
            if (CellCount <= 0) throw new ArgumentOutOfRangeException(nameof(CellCount), "CellCount must be positive.");
            if (AspectRatio <= 0f) throw new ArgumentOutOfRangeException(nameof(AspectRatio), "AspectRatio must be positive.");
            if (CellSizeKm <= 0f) throw new ArgumentOutOfRangeException(nameof(CellSizeKm), "CellSizeKm must be positive.");
            if (MaxElevationMeters <= 0f) throw new ArgumentOutOfRangeException(nameof(MaxElevationMeters), "MaxElevationMeters must be positive.");
            if (MaxSeaDepthMeters <= 0f) throw new ArgumentOutOfRangeException(nameof(MaxSeaDepthMeters), "MaxSeaDepthMeters must be positive.");
            if (LapseRateCPerKm <= 0f) throw new ArgumentOutOfRangeException(nameof(LapseRateCPerKm), "LapseRateCPerKm must be positive.");
            if (MaxAnnualPrecipitationMm <= 0f) throw new ArgumentOutOfRangeException(nameof(MaxAnnualPrecipitationMm), "MaxAnnualPrecipitationMm must be positive.");
            if (RiverThreshold <= 0f) throw new ArgumentOutOfRangeException(nameof(RiverThreshold), "RiverThreshold must be positive.");
            if (RiverTraceThreshold <= 0f) throw new ArgumentOutOfRangeException(nameof(RiverTraceThreshold), "RiverTraceThreshold must be positive.");
            if (MinRiverVertices <= 0) throw new ArgumentOutOfRangeException(nameof(MinRiverVertices), "MinRiverVertices must be positive.");
            if (WindBands == null || WindBands.Length == 0) throw new ArgumentException("WindBands must be configured.", nameof(WindBands));
        }
    }
}
