namespace MapGen.Core
{
    /// <summary>
    /// Per-cell political hierarchy for V2 (0 = unassigned/water).
    /// </summary>
    public class PoliticalField
    {
        public CellMesh Mesh { get; }
        public int CellCount => Mesh.CellCount;

        public int[] LandmassId; // -1 for water
        public int LandmassCount;

        public int[] RealmId;
        public int[] ProvinceId;
        public int[] CountyId;

        public int RealmCount;
        public int ProvinceCount;
        public int CountyCount;

        public int[] Capitals;
        public int[] CountySeats;

        public PoliticalField(CellMesh mesh)
        {
            Mesh = mesh;
            int n = mesh.CellCount;

            LandmassId = new int[n];
            RealmId = new int[n];
            ProvinceId = new int[n];
            CountyId = new int[n];

            for (int i = 0; i < n; i++)
                LandmassId[i] = -1;

            Capitals = System.Array.Empty<int>();
            CountySeats = System.Array.Empty<int>();
        }
    }

    [System.Obsolete("Use PoliticalField.")]
    public class PoliticalFieldV2 : PoliticalField
    {
        public PoliticalFieldV2(CellMesh mesh) : base(mesh)
        {
        }
    }
}
