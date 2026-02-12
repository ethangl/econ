namespace MapGen.Core
{
    /// <summary>
    /// Minimal MapGen V2 output for phase-A scaffolding.
    /// </summary>
    public class MapGenV2Result
    {
        public CellMesh Mesh;
        public ElevationField Elevation;
        public ClimateField Climate;
        public RiverField Rivers;
        public BiomeField Biomes;
        public PoliticalField Political;
        public WorldMetadata World;
    }
}
