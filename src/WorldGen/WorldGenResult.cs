namespace WorldGen.Core
{
    /// <summary>
    /// Dense terrain mesh with elevation derived from coarse tectonics + fractal noise.
    /// </summary>
    public class DenseTerrainData
    {
        /// <summary>High-resolution Voronoi mesh</summary>
        public SphereMesh Mesh;

        /// <summary>Maps each dense cell index to its nearest coarse cell index</summary>
        public int[] DenseToCoarse;

        /// <summary>Elevation per dense cell (0-1, sea level ~0.4)</summary>
        public float[] CellElevation;
    }

    /// <summary>
    /// Composite result from the world generation pipeline.
    /// </summary>
    public class WorldGenResult
    {
        public SphereMesh Mesh;
        public TectonicData Tectonics;
        public DenseTerrainData DenseTerrain;
    }
}
