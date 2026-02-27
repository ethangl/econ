namespace WorldGen.Core
{
    public enum BoundaryType : byte
    {
        None,
        Convergent,
        Divergent,
        Transform
    }

    /// <summary>
    /// Tectonic plate assignment and boundary classification for a SphereMesh.
    /// </summary>
    public class TectonicData
    {
        /// <summary>Plate ID per cell (0-based)</summary>
        public int[] CellPlate;

        /// <summary>Number of tectonic plates</summary>
        public int PlateCount;

        /// <summary>Seed cell index per plate</summary>
        public int[] PlateSeeds;

        /// <summary>Tangent drift vector per plate (on sphere surface)</summary>
        public Vec3[] PlateDrift;

        /// <summary>Boundary classification per SphereMesh edge</summary>
        public BoundaryType[] EdgeBoundary;

        /// <summary>Signed convergence scalar per edge (positive=convergent, negative=divergent)</summary>
        public float[] EdgeConvergence;
    }
}
