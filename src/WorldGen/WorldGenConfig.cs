namespace WorldGen.Core
{
    public enum ConvexHullAlgorithm { Quickhull, Incremental }

    /// <summary>
    /// Configuration for spherical world generation.
    /// </summary>
    public class WorldGenConfig
    {
        /// <summary>Which convex hull algorithm to use</summary>
        public ConvexHullAlgorithm HullAlgorithm { get; set; } = ConvexHullAlgorithm.Quickhull;

        /// <summary>Number of coarse tectonic cells</summary>
        public int CoarseCellCount { get; set; } = 2000;

        /// <summary>Number of dense terrain cells</summary>
        public int DenseCellCount { get; set; } = 20000;

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
    }
}
