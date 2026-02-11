using System;

namespace MapGen.Core
{
    /// <summary>
    /// Explicit world-scale metadata emitted by map generation.
    /// Values are deterministic for a given map config + mesh output.
    /// </summary>
    [Serializable]
    public class WorldMetadata
    {
        // Physical map scale.
        public float CellSizeKm;
        public float MapWidthKm;
        public float MapHeightKm;
        public float MapAreaKm2;

        // Geospatial placement.
        public float LatitudeSouth;
        public float LatitudeNorth;

        // Elevation domains.
        public float MinHeight;
        public float SeaLevelHeight;
        public float MaxHeight;
        public float MaxElevationMeters;
        public float MaxSeaDepthMeters;
    }
}
