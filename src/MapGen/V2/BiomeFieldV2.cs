namespace MapGen.Core
{
    /// <summary>
    /// Per-cell biome, suitability, population, and geography outputs for V2 pipeline.
    /// </summary>
    public class BiomeField
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

        // Debug tuning overlays (per-cell, land cells only).
        public float[] DebugCellFlux;
        public bool[] DebugSalineCandidate;
        public bool[] DebugAlluvialCandidate;
        public bool[] DebugLithosolCandidate;
        public bool[] DebugWetlandCandidate;

        public BiomeField(CellMesh mesh)
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

            DebugCellFlux = new float[n];
            DebugSalineCandidate = new bool[n];
            DebugAlluvialCandidate = new bool[n];
            DebugLithosolCandidate = new bool[n];
            DebugWetlandCandidate = new bool[n];
        }
    }

    [System.Obsolete("Use BiomeField.")]
    public class BiomeFieldV2 : BiomeField
    {
        public BiomeFieldV2(CellMesh mesh) : base(mesh)
        {
        }
    }
}
