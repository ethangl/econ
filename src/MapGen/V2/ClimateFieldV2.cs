namespace MapGen.Core
{
    /// <summary>
    /// Per-cell climate in explicit world units.
    /// </summary>
    public class ClimateFieldV2
    {
        /// <summary>Air temperature at cell center in degrees Celsius.</summary>
        public float[] TemperatureC;

        /// <summary>Annual precipitation in millimeters.</summary>
        public float[] PrecipitationMmYear;

        public CellMesh Mesh { get; }
        public int CellCount => Mesh.CellCount;

        public ClimateFieldV2(CellMesh mesh)
        {
            Mesh = mesh;
            TemperatureC = new float[mesh.CellCount];
            PrecipitationMmYear = new float[mesh.CellCount];
        }
    }
}
