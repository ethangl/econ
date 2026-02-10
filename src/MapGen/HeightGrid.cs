namespace MapGen.Core
{
    /// <summary>
    /// Height values for each cell in a CellMesh.
    /// DSL generation starts in 0-100 (sea=20), but the grid can be rescaled
    /// to other domains (e.g. 0-255 simulation) after map shaping.
    /// </summary>
    public class HeightGrid
    {
        // Legacy DSL constants retained for DSL operations that intentionally
        // stay in Azgaar's domain during migration.
        public const float SeaLevel = 20f;
        public const float MinHeight = 0f;
        public const float MaxHeight = 100f;

        /// <summary>Height value for each cell</summary>
        public float[] Heights;

        /// <summary>Reference to the underlying cell mesh</summary>
        public CellMesh Mesh { get; }

        /// <summary>Active elevation domain for this grid.</summary>
        public ElevationDomain Domain { get; private set; }

        public float DomainMin => Domain.Min;
        public float DomainMax => Domain.Max;
        public float DomainSeaLevel => Domain.SeaLevel;
        public float DomainLandRange => Domain.LandRange;

        /// <summary>Number of cells</summary>
        public int CellCount => Mesh.CellCount;

        public HeightGrid(CellMesh mesh, ElevationDomain? domain = null)
        {
            Mesh = mesh;
            Domain = domain ?? ElevationDomains.Dsl;
            Heights = new float[mesh.CellCount];
            // Initialize to 0 (deep water) - land builds up from here
        }

        /// <summary>Get height at cell, clamped to valid range.</summary>
        public float this[int cellIndex]
        {
            get => Heights[cellIndex];
            set => Heights[cellIndex] = ClampToDomain(value);
        }

        /// <summary>Is this cell above sea level?</summary>
        public bool IsLand(int cellIndex) => Heights[cellIndex] > DomainSeaLevel;

        /// <summary>Is this cell at or below sea level?</summary>
        public bool IsWater(int cellIndex) => Heights[cellIndex] <= DomainSeaLevel;

        /// <summary>Clamp height to valid range.</summary>
        public static float Clamp(float h) =>
            h < MinHeight ? MinHeight : (h > MaxHeight ? MaxHeight : h);

        /// <summary>Clamp height to this grid's active domain.</summary>
        public float ClampToDomain(float h) => Domain.Clamp(h);

        /// <summary>
        /// Rescale all heights from current domain into target domain.
        /// </summary>
        public void RescaleTo(ElevationDomain targetDomain)
        {
            if (Domain.Min == targetDomain.Min &&
                Domain.Max == targetDomain.Max &&
                Domain.SeaLevel == targetDomain.SeaLevel)
            {
                return;
            }

            var sourceDomain = Domain;
            for (int i = 0; i < Heights.Length; i++)
                Heights[i] = targetDomain.RescaleFrom(Heights[i], sourceDomain);

            Domain = targetDomain;
        }

        /// <summary>Clamp all heights to valid range.</summary>
        public void ClampAll()
        {
            for (int i = 0; i < Heights.Length; i++)
                Heights[i] = ClampToDomain(Heights[i]);
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
