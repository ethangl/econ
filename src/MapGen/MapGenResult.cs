namespace MapGen.Core
{
    /// <summary>
    /// Map generation output for the world-unit pipeline.
    /// </summary>
    public class MapGenResult
    {
        public CellMesh Mesh;
        public ElevationField Elevation;
        public ClimateField Climate;
        public RiverField Rivers;
        public BiomeField Biomes;
        public PoliticalField Political;
        public WorldMetadata World;

        // Legacy alias kept for call sites that still read `result.Heights`.
        public ElevationField Heights => Elevation;
    }
}
