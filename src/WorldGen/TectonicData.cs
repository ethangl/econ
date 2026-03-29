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

        /// <summary>Per plate: true=major, false=minor</summary>
        public bool[] PlateIsMajor;

        /// <summary>Per plate: true=oceanic, false=continental</summary>
        public bool[] PlateIsOceanic;

        /// <summary>Number of polar cap plates (0..PolarPlateCount-1 are polar caps)</summary>
        public int PolarPlateCount;

        /// <summary>Normalized elevation per cell (0-1). Sea level ~0.4.</summary>
        public float[] CellElevation;

        // --- Multi-step history (populated when TectonicSteps > 1) ---

        /// <summary>How many steps this cell was adjacent to a plate boundary.
        /// High = geologically active zone.</summary>
        public int[] CellBoundaryExposure;

        /// <summary>How many steps this cell stayed in the same plate.
        /// High = stable craton candidate.</summary>
        public int[] CellPlateContinuity;

        /// <summary>Last step index (0-based) when this cell was near a boundary.
        /// Recent = fresh terrain, ancient = eroded remnant.</summary>
        public int[] CellLastBoundaryStep;
    }
}
