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
        public int Seed { get; set; } = 7;

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
        public float OceanFraction { get; set; } = 0.8f;

        /// <summary>Number of tectonic time steps. Each step rotates plate seeds,
        /// re-grows plates, reclassifies boundaries, and applies elevation deltas.
        /// 1 = original single-shot behavior.</summary>
        public int TectonicSteps { get; set; } = 15;

        /// <summary>Inter-step erosion factor (0-1). Each step pulls elevation
        /// toward base plate elevation by this fraction, simulating geological aging.</summary>
        public float InterStepErosion { get; set; } = 0.15f;

        /// <summary>Number of volcanic hotspots (fixed mantle plumes). Trails are projected
        /// opposite to plate drift, length scales with TectonicSteps.</summary>
        public int HotspotCount { get; set; } = 20;

        /// <summary>Maximum trail length in cell hops per hotspot.</summary>
        public int HotspotTrailLength { get; set; } = 8;

        /// <summary>Elevation bump at the hotspot source (decays along trail).</summary>
        public float HotspotElevation { get; set; } = 0.5f;

        /// <summary>BFS hops from convergent boundary to arc position on overriding plate.</summary>
        public int VolcanicArcOffset { get; set; } = 2;

        /// <summary>Elevation bump at arc volcano peaks.</summary>
        public float VolcanicArcElevation { get; set; } = 0.25f;

        /// <summary>Minimum boundary edge count to form an arc segment.</summary>
        public int VolcanicArcMinEdges { get; set; } = 3;

        /// <summary>Polar cap latitude threshold in degrees from equator. Cells above this latitude
        /// are pre-assigned to polar cap plates (north=0, south=1) before normal seeding. Set to 0 to disable.</summary>
        public float PolarCapLatitude { get; set; } = 0f;

        /// <summary>Jitter for subdivision midpoints (0-1). Breaks grid regularity in ultra-dense mesh.</summary>
        public float SubdivisionJitter { get; set; } = 0.1f;

        /// <summary>Enable ultra-dense mesh via midpoint subdivision (~4x dense cell count).</summary>
        public bool EnableUltraDense { get; set; } = false;

        // --- Cratons / Shields ---

        /// <summary>Minimum BFS hops from any plate boundary for a cell to be craton.</summary>
        public int CratonMinBoundaryDistance { get; set; } = 3;

        /// <summary>BFS hops over which craton strength ramps from 0 to 1.</summary>
        public int CratonRampWidth { get; set; } = 3;

        /// <summary>How much to flatten craton cells toward ContinentalBase (0-1).</summary>
        public float CratonFlattenStrength { get; set; } = 0.6f;

        /// <summary>Noise amplitude multiplier for craton cells in DenseTerrainOps (0-1).</summary>
        public float CratonNoiseMultiplier { get; set; } = 0.25f;

        // --- Sedimentary Basins ---

        /// <summary>Elevation below ContinentalBase to qualify as basin candidate.</summary>
        public float BasinElevationThreshold { get; set; } = 0.04f;

        /// <summary>Minimum cell count for a basin to be kept.</summary>
        public int BasinMinCells { get; set; } = 3;

        /// <summary>How much to flatten basin cells toward the basin floor (0-1).</summary>
        public float BasinFlattenStrength { get; set; } = 0.5f;

        /// <summary>Elevation offset above the lowest cell to set basin floor.</summary>
        public float BasinFloorOffset { get; set; } = 0.02f;

        // --- Seamounts / Abyssal Hills ---

        /// <summary>Maximum seafloor age (BFS hops from ridge) for young-crust abyssal hills.</summary>
        public int SeamountYoungCrustMaxAge { get; set; } = 3;

        /// <summary>Average seamount peaks scattered per tagged coarse cell (at full density).</summary>
        public float SeamountPeaksPerCell { get; set; } = 2f;

        /// <summary>Maximum elevation bump at a seamount apex (normalized 0-1).</summary>
        public float SeamountMaxElevation { get; set; } = 0.08f;

        /// <summary>Cone falloff radius in km. Peaks taper to zero over this distance.</summary>
        public float SeamountRadius { get; set; } = 200f;

        // --- Isostatic Adjustment ---

        /// <summary>How strongly to adjust elevation toward isostatic equilibrium (0=disabled, 1=full).
        /// Mountains sink slightly, continental interiors with cratonic roots rise slightly.</summary>
        public float IsostaticStrength { get; set; } = 0.4f;

        // --- Site selection ---

        /// <summary>Minimum degrees from equator for site candidates</summary>
        public float SiteLatitudeMin { get; set; } = 15f;

        /// <summary>Maximum degrees from equator for site candidates</summary>
        public float SiteLatitudeMax { get; set; } = 45f;

        /// <summary>Minimum BFS hops from coast for site candidates</summary>
        public int SiteCoastDistMin { get; set; } = 2;

        /// <summary>Maximum BFS hops from coast for site candidates</summary>
        public int SiteCoastDistMax { get; set; } = 5;
    }
}
