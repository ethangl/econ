namespace MapGen.Core
{
    /// <summary>
    /// Container for per-cell climate data: temperature and precipitation.
    /// Temperature is in °C. Precipitation is relative (0-100 range, normalized).
    /// </summary>
    public class ClimateData
    {
        /// <summary>Temperature in °C for each cell</summary>
        public float[] Temperature;

        /// <summary>Precipitation (relative 0-100) for each cell</summary>
        public float[] Precipitation;

        /// <summary>Reference to the underlying cell mesh</summary>
        public CellMesh Mesh { get; }

        public int CellCount => Mesh.CellCount;

        public ClimateData(CellMesh mesh)
        {
            Mesh = mesh;
            Temperature = new float[mesh.CellCount];
            Precipitation = new float[mesh.CellCount];
        }

        /// <summary>Get min/max temperature.</summary>
        public (float min, float max) TemperatureRange()
        {
            float min = float.MaxValue, max = float.MinValue;
            for (int i = 0; i < Temperature.Length; i++)
            {
                if (Temperature[i] < min) min = Temperature[i];
                if (Temperature[i] > max) max = Temperature[i];
            }
            return (min, max);
        }

        /// <summary>Get min/max precipitation.</summary>
        public (float min, float max) PrecipitationRange()
        {
            float min = float.MaxValue, max = float.MinValue;
            for (int i = 0; i < Precipitation.Length; i++)
            {
                if (Precipitation[i] < min) min = Precipitation[i];
                if (Precipitation[i] > max) max = Precipitation[i];
            }
            return (min, max);
        }
    }
}
