namespace MapGen.Core
{
    /// <summary>
    /// Per-cell signed elevation values in meters relative to sea level (0m).
    /// </summary>
    public class ElevationField
    {
        public float[] ElevationMetersSigned;
        public float[] Heights => ElevationMetersSigned;

        public CellMesh Mesh { get; }
        public int CellCount => Mesh.CellCount;

        public float SeaLevelMeters => 0f;
        public float MaxElevationMeters { get; }
        public float MaxSeaDepthMeters { get; }

        public ElevationField(CellMesh mesh, float maxSeaDepthMeters, float maxElevationMeters)
        {
            Mesh = mesh;
            MaxSeaDepthMeters = maxSeaDepthMeters;
            MaxElevationMeters = maxElevationMeters;
            ElevationMetersSigned = new float[mesh.CellCount];
            for (int i = 0; i < ElevationMetersSigned.Length; i++)
                ElevationMetersSigned[i] = -MaxSeaDepthMeters;
        }

        public float this[int cellIndex]
        {
            get => ElevationMetersSigned[cellIndex];
            set => ElevationMetersSigned[cellIndex] = Clamp(value);
        }

        public bool IsLand(int cellIndex) => ElevationMetersSigned[cellIndex] > SeaLevelMeters;

        public bool IsWater(int cellIndex) => ElevationMetersSigned[cellIndex] <= SeaLevelMeters;

        public float Clamp(float elevationMeters) =>
            elevationMeters < -MaxSeaDepthMeters ? -MaxSeaDepthMeters :
            (elevationMeters > MaxElevationMeters ? MaxElevationMeters : elevationMeters);

        public void ClampAll()
        {
            for (int i = 0; i < ElevationMetersSigned.Length; i++)
                ElevationMetersSigned[i] = Clamp(ElevationMetersSigned[i]);
        }

        public (int land, int water) CountLandWater()
        {
            int land = 0;
            int water = 0;
            for (int i = 0; i < ElevationMetersSigned.Length; i++)
            {
                if (IsLand(i)) land++;
                else water++;
            }

            return (land, water);
        }

        public float LandRatio()
        {
            if (CellCount <= 0)
                return 0f;

            var (land, _) = CountLandWater();
            return (float)land / CellCount;
        }
    }
}
