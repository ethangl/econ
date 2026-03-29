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

        /// <summary>Per-cell crust type. In multi-step mode, plate ownership can change
        /// via boundary migration but crust type is immutable — an oceanic cell absorbed
        /// by a continental plate remains oceanic crust. In single-step mode, matches
        /// PlateIsOceanic[CellPlate[c]]. Use this instead of PlateIsOceanic for per-cell
        /// continental/oceanic classification.</summary>
        public bool[] CellCrustOceanic;

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

        // --- Seafloor age ---

        /// <summary>BFS hop distance from nearest divergent boundary per oceanic cell.
        /// -1 for continental cells. 0 = at the ridge, higher = older crust.</summary>
        public int[] CellSeafloorAge;

        // --- Hotspot data ---

        /// <summary>Per-cell hotspot intensity (0 = none, 1 = directly on hotspot source,
        /// decaying along trail). Used for elevation bumps and downstream rendering.</summary>
        public float[] CellHotspotIntensity;

        /// <summary>Hotspot trail data for rendering (cone stamping at heightmap resolution).</summary>
        public HotspotData[] Hotspots;

        // --- Volcanic arc data ---

        /// <summary>Per-cell volcanic arc intensity (0 = none, >0 = arc cell).
        /// Used for elevation bumps and downstream rendering.</summary>
        public float[] CellVolcanicArcIntensity;

        /// <summary>Volcanic arc segment data for rendering (cone stamping at heightmap resolution).</summary>
        public VolcanicArcData[] VolcanicArcs;

        // --- Craton / Shield data ---

        /// <summary>Per-cell craton strength (0 = not a craton, 1 = deep interior craton).
        /// Used to dampen fractal noise in DenseTerrainOps and for debug visualization.</summary>
        public float[] CellCratonStrength;

        // --- Sedimentary Basin data ---

        /// <summary>Per-cell basin ID (0 = not in a basin, 1..N = basin assignment).
        /// Used for elevation flattening and debug visualization.</summary>
        public int[] CellBasinId;

        /// <summary>Number of sedimentary basins found.</summary>
        public int BasinCount;

        // --- Seamount / Abyssal Hill data ---

        /// <summary>Per-cell seamount density (0 = none, >0 = tagged for seamount placement).
        /// Derived from hotspot trails on oceanic crust and young crust near ridges.</summary>
        public float[] CellSeamountDensity;

        /// <summary>Scattered seamount peak positions for cone-stamping at dense terrain resolution.</summary>
        public SeamountPeakData[] Seamounts;
    }

    /// <summary>
    /// A single volcanic hotspot: fixed mantle position with a trail of affected cells
    /// projected opposite to the owning plate's drift vector.
    /// </summary>
    public class HotspotData
    {
        /// <summary>Fixed position on sphere surface (mantle plume location).</summary>
        public Vec3 Position;

        /// <summary>Cell index at the hotspot source (youngest volcanism).</summary>
        public int SourceCell;

        /// <summary>Trail cell indices from source (youngest) to tail (oldest).</summary>
        public int[] TrailCells;

        /// <summary>Intensity per trail cell (1.0 at source, decaying toward tail).</summary>
        public float[] TrailIntensity;
    }

    /// <summary>
    /// A contiguous volcanic arc segment along a convergent ocean-continent boundary.
    /// </summary>
    public class VolcanicArcData
    {
        /// <summary>Continental (overriding) plate cells directly at the boundary.</summary>
        public int[] BoundaryCells;

        /// <summary>Arc cells offset inland on the overriding plate.</summary>
        public int[] ArcCells;

        /// <summary>Individual volcano peaks along this arc.</summary>
        public VolcanoPeakData[] Peaks;

        /// <summary>Plate index of the overriding (continental) plate.</summary>
        public int OverridingPlate;
    }

    /// <summary>
    /// A single stratovolcano peak within a volcanic arc.
    /// </summary>
    public class VolcanoPeakData
    {
        /// <summary>Coarse cell index where the peak is located.</summary>
        public int Cell;

        /// <summary>3D position on the sphere surface (for heightmap stamping).</summary>
        public Vec3 Position;

        /// <summary>Peak intensity (0-1). Higher = taller cone.</summary>
        public float Intensity;
    }

    /// <summary>
    /// A single seamount or abyssal hill peak on the ocean floor.
    /// Position is scattered within a tagged coarse cell; elevation bump
    /// is applied at dense terrain resolution via radial cone falloff.
    /// </summary>
    public class SeamountPeakData
    {
        /// <summary>Coarse cell index containing this peak.</summary>
        public int Cell;

        /// <summary>3D position on the sphere surface.</summary>
        public Vec3 Position;

        /// <summary>Peak elevation bump (added at apex, falls off with distance).</summary>
        public float Height;
    }
}
