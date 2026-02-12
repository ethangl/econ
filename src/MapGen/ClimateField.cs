namespace MapGen.Core
{
    /// <summary>
    /// Per-cell climate in explicit world units.
    /// </summary>
    public class ClimateField
    {
        /// <summary>Air temperature at cell center in degrees Celsius.</summary>
        public float[] TemperatureC;
        public float[] Temperature => TemperatureC;

        /// <summary>Annual precipitation in millimeters.</summary>
        public float[] PrecipitationMmYear;
        public float[] Precipitation => PrecipitationMmYear;

        public CellMesh Mesh { get; }
        public int CellCount => Mesh.CellCount;

        public ClimateField(CellMesh mesh)
        {
            Mesh = mesh;
            TemperatureC = new float[mesh.CellCount];
            PrecipitationMmYear = new float[mesh.CellCount];
        }
    }
}
