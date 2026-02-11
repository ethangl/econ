namespace MapGen.Core
{
    /// <summary>
    /// Minimal MapGen V2 output for phase-A scaffolding.
    /// </summary>
    public class MapGenV2Result
    {
        public CellMesh Mesh;
        public ElevationFieldV2 Elevation;
        public ClimateFieldV2 Climate;
        public RiverFieldV2 Rivers;
        public WorldMetadata World;
    }
}
