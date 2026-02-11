namespace MapGen.Core
{
    /// <summary>
    /// Per-cell biome, suitability, population, and geography outputs for V2 pipeline.
    /// </summary>
    public class BiomeFieldV2
    {
        public CellMesh Mesh { get; }
        public int CellCount => Mesh.CellCount;

        public bool[] IsLakeCell;
        public float[] Slope;
        public int[] CoastDistance;
        public int[] FeatureId;
        public WaterFeature[] Features;

        public BiomeId[] Biome;
        public float[] Habitability;
        public float[] MovementCost;
        public float[] Suitability;
        public float[] Population;

        public BiomeFieldV2(CellMesh mesh)
        {
            Mesh = mesh;
            int n = mesh.CellCount;

            IsLakeCell = new bool[n];
            Slope = new float[n];
            CoastDistance = new int[n];
            FeatureId = new int[n];
            Features = System.Array.Empty<WaterFeature>();

            Biome = new BiomeId[n];
            Habitability = new float[n];
            MovementCost = new float[n];
            Suitability = new float[n];
            Population = new float[n];
        }
    }
}
