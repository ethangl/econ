namespace WorldGen.Core
{
    /// <summary>
    /// Configuration for spherical world generation.
    /// </summary>
    public class WorldGenConfig
    {
        /// <summary>Number of coarse tectonic cells</summary>
        public int CoarseCellCount { get; set; } = 2040;

        /// <summary>Number of dense terrain cells</summary>
        public int DenseCellCount { get; set; } = 20400;

        /// <summary>Random seed</summary>
        public int Seed { get; set; } = 42;

        /// <summary>Sphere radius (km)</summary>
        public float Radius { get; set; } = 6371f;

        /// <summary>Jitter for point distribution (0-1). Higher values give more irregular cells.</summary>
        public float Jitter { get; set; } = 0.5f;

        /// <summary>Number of major tectonic plates (seeded first, get a BFS head start)</summary>
        public int MajorPlateCount { get; set; } = 8;

        /// <summary>Number of minor tectonic plates (seeded after major plates have grown)</summary>
        public int MinorPlateCount { get; set; } = 40;

        /// <summary>BFS rounds major plates grow before minor plates are seeded</summary>
        public int MajorHeadStartRounds { get; set; } = 3;

        /// <summary>Fraction of plates that are oceanic (0-1)</summary>
        public float OceanFraction { get; set; } = 0.6f;

        /// <summary>Polar cap latitude threshold in degrees from equator. Cells above this latitude
        /// are pre-assigned to polar cap plates (north=0, south=1) before normal seeding. Set to 0 to disable.</summary>
        public float PolarCapLatitude { get; set; } = 65f;

        /// <summary>Jitter for subdivision midpoints (0-1). Breaks grid regularity in ultra-dense mesh.</summary>
        public float SubdivisionJitter { get; set; } = 0.1f;
    }
}
