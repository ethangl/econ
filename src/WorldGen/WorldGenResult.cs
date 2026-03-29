namespace WorldGen.Core
{
    public class DenseTerrainTimingData
    {
        public double DensePointsSeconds;
        public double DenseHullSeconds;
        public double DenseVoronoiSeconds;
        public double DenseAreaSeconds;
        public double DenseMappingSeconds;
        public double DenseElevationSeconds;
        public double UltraSubdivisionSeconds;
        public double UltraSubdivisionSetupSeconds;
        public double UltraSubdivisionRestoreSeconds;
        public double UltraVoronoiSeconds;
        public double UltraAreaSeconds;
        public double UltraMappingSeconds;
        public double UltraElevationSeconds;
        public double TotalSeconds;
    }

    /// <summary>
    /// Dense terrain mesh with elevation derived from coarse tectonics + fractal noise.
    /// </summary>
    public class DenseTerrainData
    {
        /// <summary>High-resolution Voronoi mesh</summary>
        public SphereMesh Mesh;

        /// <summary>Maps each dense cell index to its nearest coarse cell index</summary>
        public int[] DenseToCoarse;

        /// <summary>Elevation per dense cell (0-1, sea level 0.5)</summary>
        public float[] CellElevation;

        /// <summary>Ultra-dense Voronoi mesh from tessellation (~4x dense cell count)</summary>
        public SphereMesh UltraDenseMesh;

        /// <summary>Maps each ultra-dense cell to its nearest coarse cell</summary>
        public int[] UltraDenseToCoarse;

        /// <summary>Elevation per ultra-dense cell (0-1, sea level 0.5)</summary>
        public float[] UltraDenseCellElevation;

        /// <summary>Generation timings for dense and ultra-dense terrain stages.</summary>
        public DenseTerrainTimingData Timings;
    }

    public class WorldGenTimingData
    {
        public double CoarsePointsSeconds;
        public double CoarseHullSeconds;
        public double CoarseVoronoiSeconds;
        public double CoarseAreaSeconds;
        public double TectonicsSeconds;
        public double ElevationSeconds;
        public double HotspotsSeconds;
        public double VolcanicArcsSeconds;
        public double CratonsSeconds;
        public double BasinsSeconds;
        public double SeamountsSeconds;
        public double IsostasySeconds;
        public double WindSeconds;
        public double PrecipitationSeconds;
        public double DenseTerrainSeconds;
        public double SiteSelectionSeconds;
        public double TotalSeconds;
    }

    /// <summary>
    /// Composite result from the world generation pipeline.
    /// </summary>
    public class WorldGenResult
    {
        public SphereMesh Mesh;
        public TectonicData Tectonics;
        public DenseTerrainData DenseTerrain;

        /// <summary>Selected site for flat map generation (null if selection failed)</summary>
        public SiteContext Site;

        /// <summary>Ranked candidate sites (best first), for UI cycling</summary>
        public System.Collections.Generic.List<SiteContext> Sites;

        /// <summary>Generation timings for the major globe pipeline stages.</summary>
        public WorldGenTimingData Timings;
    }
}
