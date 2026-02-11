namespace MapGen.Core
{
    /// <summary>
    /// Height values for each cell in a CellMesh.
    /// Heights are in 0-100 range (Azgaar convention), with 20 = sea level.
    /// </summary>
    public class HeightGrid
    {
        public const float SeaLevel = 20f;
        public const float MinHeight = 0f;
        public const float MaxHeight = 100f;

        /// <summary>Height value for each cell</summary>
        public float[] Heights;

        /// <summary>Reference to the underlying cell mesh</summary>
        public CellMesh Mesh { get; }

        /// <summary>Number of cells</summary>
        public int CellCount => Mesh.CellCount;

        public HeightGrid(CellMesh mesh)
        {
            Mesh = mesh;
            Heights = new float[mesh.CellCount];
            // Initialize to 0 (deep water) - land builds up from here
        }

        /// <summary>Get height at cell, clamped to valid range.</summary>
        public float this[int cellIndex]
        {
            get => Heights[cellIndex];
            set => Heights[cellIndex] = Clamp(value);
        }

        /// <summary>Is this cell above sea level?</summary>
        public bool IsLand(int cellIndex) => Heights[cellIndex] > SeaLevel;

        /// <summary>Is this cell at or below sea level?</summary>
        public bool IsWater(int cellIndex) => Heights[cellIndex] <= SeaLevel;

        /// <summary>Clamp height to valid range.</summary>
        public static float Clamp(float h) =>
            h < MinHeight ? MinHeight : (h > MaxHeight ? MaxHeight : h);

        /// <summary>Clamp all heights to valid range.</summary>
        public void ClampAll()
        {
            for (int i = 0; i < Heights.Length; i++)
                Heights[i] = Clamp(Heights[i]);
        }

        /// <summary>Get the neighbors of a cell (from CellMesh adjacency).</summary>
        public int[] GetNeighbors(int cellIndex) => Mesh.CellNeighbors[cellIndex];

        /// <summary>Count land and water cells.</summary>
        public (int land, int water) CountLandWater()
        {
            int land = 0, water = 0;
            for (int i = 0; i < Heights.Length; i++)
            {
                if (IsLand(i)) land++;
                else water++;
            }
            return (land, water);
        }

        /// <summary>Get land ratio (0-1).</summary>
        public float LandRatio()
        {
            var (land, _) = CountLandWater();
            return (float)land / CellCount;
        }
    }
}
