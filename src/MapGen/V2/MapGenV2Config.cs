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

        // Sub-seed constants (golden-ratio hashing for decorrelation).
        const uint MeshXor = 0x9E3779B9;
        const uint ElevationXor = 0xA54FF53A;

        public int MeshSeed => (int)((uint)Seed ^ MeshXor);
        public int ElevationSeed => (int)((uint)Seed ^ ElevationXor);

        public void Validate()
        {
            if (CellCount <= 0) throw new ArgumentOutOfRangeException(nameof(CellCount), "CellCount must be positive.");
            if (AspectRatio <= 0f) throw new ArgumentOutOfRangeException(nameof(AspectRatio), "AspectRatio must be positive.");
            if (CellSizeKm <= 0f) throw new ArgumentOutOfRangeException(nameof(CellSizeKm), "CellSizeKm must be positive.");
            if (MaxElevationMeters <= 0f) throw new ArgumentOutOfRangeException(nameof(MaxElevationMeters), "MaxElevationMeters must be positive.");
            if (MaxSeaDepthMeters <= 0f) throw new ArgumentOutOfRangeException(nameof(MaxSeaDepthMeters), "MaxSeaDepthMeters must be positive.");
        }
    }
}
