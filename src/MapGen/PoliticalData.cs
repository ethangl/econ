namespace MapGen.Core
{
    /// <summary>
    /// Container for all per-cell political hierarchy outputs.
    /// Pipeline: landmass detection -> capitals -> realms -> provinces -> counties.
    /// Water/lake cells remain unassigned (ID = 0).
    /// </summary>
    public class PoliticalData
    {
        public CellMesh Mesh { get; }
        public int CellCount => Mesh.CellCount;

        // Landmass detection
        public int[] LandmassId;           // per-cell, -1 for water/lake/tiny islands
        public int LandmassCount;
        public int[] LandmassCellCount;    // size of each landmass
        public float[] LandmassPop;        // total population per landmass

        // Per-cell political assignment (0 = unassigned, 1-based IDs)
        public int[] RealmId;
        public int[] ProvinceId;
        public int[] CountyId;

        // Capitals: index = realmId-1, value = cell index
        public int[] Capitals;

        // County seats: index = countyId-1, value = cell index
        public int[] CountySeats;

        // Per-cell flag for fast rendering lookup
        public bool[] IsCountySeat;

        // Aggregate counts
        public int RealmCount;
        public int ProvinceCount;
        public int CountyCount;

        // Qualifying landmasses (above MinLandmassSize threshold)
        public int QualifyingLandmasses;

        public PoliticalData(CellMesh mesh)
        {
            Mesh = mesh;
            int n = mesh.CellCount;

            LandmassId = new int[n];
            for (int i = 0; i < n; i++) LandmassId[i] = -1;

            RealmId = new int[n];
            ProvinceId = new int[n];
            CountyId = new int[n];
            IsCountySeat = new bool[n];
        }
    }
}
