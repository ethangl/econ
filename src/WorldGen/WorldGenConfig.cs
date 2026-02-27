namespace WorldGen.Core
{
    /// <summary>
    /// Configuration for spherical world generation.
    /// </summary>
    public class WorldGenConfig
    {
        /// <summary>Number of Voronoi cells on the sphere</summary>
        public int CellCount { get; set; } = 10000;

        /// <summary>Random seed</summary>
        public int Seed { get; set; } = 42;

        /// <summary>Sphere radius (km)</summary>
        public float Radius { get; set; } = 6371f;

        /// <summary>Jitter for point distribution (0-1). Higher values give more irregular cells.</summary>
        public float Jitter { get; set; } = 0f;

        /// <summary>Number of tectonic plates</summary>
        public int PlateCount { get; set; } = 20;

        /// <summary>Fraction of plates that are oceanic (0-1)</summary>
        public float OceanFraction { get; set; } = 0.6f;
    }
}
