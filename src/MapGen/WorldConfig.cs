using System;

namespace MapGen.Core
{
    /// <summary>
    /// Wind band definition: latitude range and wind travel direction.
    /// Compass bearing is direction wind travels (not source): 0=N, 90=E, 180=S, 270=W.
    /// </summary>
    public struct WindBand
    {
        public float LatMin;
        public float LatMax;
        public float CompassDegrees;

        public WindBand(float latMin, float latMax, float compassDegrees)
        {
            LatMin = latMin;
            LatMax = latMax;
            CompassDegrees = compassDegrees;
        }

        /// <summary>
        /// Unit vector in map space (X=east, Y=north).
        /// </summary>
        public Vec2 WindVector
        {
            get
            {
                float rad = CompassDegrees * (float)Math.PI / 180f;
                return new Vec2((float)Math.Sin(rad), (float)Math.Cos(rad));
            }
        }
    }

    /// <summary>
    /// Earth-like planet parameters + map positioning.
    /// </summary>
    public class WorldConfig
    {
        // Planet temperature
        public float EquatorTemp = 29f;
        public float NorthPoleTemp = -15f;
        public float SouthPoleTemp = -25f;
        public float LapseRate = 6.5f; // °C per 1000m

        // Scale (1 mesh unit = 1 km)
        public float CellSizeKm = 2.5f;
        public float MaxElevationMeters = 5000f;

        // Position (latitude in degrees, negative = south)
        public float LatitudeSouth = 30f;
        public float LatitudeNorth; // derived from mesh

        // Wind bands (Earth defaults)
        public WindBand[] WindBands = new WindBand[]
        {
            new WindBand(-90, -60, 315), // SH Polar easterlies → NW
            new WindBand(-60, -30, 135), // SH Westerlies → SE
            new WindBand(-30,   0, 315), // SH Trade winds → NW
            new WindBand(  0,  30, 225), // NH Trade winds → SW
            new WindBand( 30,  60,  45), // NH Westerlies → NE
            new WindBand( 60,  90, 225), // NH Polar easterlies → SW
        };

        /// <summary>
        /// Convert Azgaar height (0-100, sea=20) to meters above sea level.
        /// Returns 0 for water cells.
        /// </summary>
        public float HeightToMeters(float height)
        {
            if (height <= HeightGrid.SeaLevel) return 0f;
            return ((height - HeightGrid.SeaLevel) / (HeightGrid.MaxHeight - HeightGrid.SeaLevel)) * MaxElevationMeters;
        }

        /// <summary>
        /// Auto-derive latitude span from mesh dimensions.
        /// Sets LatitudeNorth based on LatitudeSouth + map height in degrees.
        /// 1 mesh unit = 1 km, so mesh.Height is directly in km.
        /// </summary>
        public void AutoLatitudeSpan(CellMesh mesh)
        {
            float mapHeightKm = mesh.Height; // 1 mesh unit = 1 km
            float latSpan = mapHeightKm / 111f; // ~111 km per degree latitude
            LatitudeNorth = LatitudeSouth + latSpan;
        }

        /// <summary>
        /// Build explicit world-scale metadata for downstream systems.
        /// </summary>
        public WorldMetadata BuildMetadata(CellMesh mesh)
        {
            float seaLevel = HeightGrid.SeaLevel;
            float landRange = Math.Max(1f, HeightGrid.MaxHeight - seaLevel);
            float waterRange = Math.Max(1f, seaLevel - HeightGrid.MinHeight);
            float maxSeaDepthMeters = MaxElevationMeters * (waterRange / landRange);

            return new WorldMetadata
            {
                CellSizeKm = CellSizeKm,
                MapWidthKm = mesh.Width,
                MapHeightKm = mesh.Height,
                MapAreaKm2 = mesh.Width * mesh.Height,
                LatitudeSouth = LatitudeSouth,
                LatitudeNorth = LatitudeNorth,
                MinHeight = HeightGrid.MinHeight,
                SeaLevelHeight = seaLevel,
                MaxHeight = HeightGrid.MaxHeight,
                MaxElevationMeters = MaxElevationMeters,
                MaxSeaDepthMeters = maxSeaDepthMeters
            };
        }
    }
}
