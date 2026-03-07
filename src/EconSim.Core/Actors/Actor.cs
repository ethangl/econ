namespace EconSim.Core.Actors
{
    public class Actor
    {
        public int Id;             // 1-based
        public string Name;        // Full display name, e.g. "Erik Halvorsen"
        public string GivenName;   // First name only
        public int BirthDay;       // Simulation day (for age calculation)
        public bool IsFemale;
        public Estate Estate;      // Nobility, Clergy
        public int CultureId;
        public int ReligionId;
        public int TitleId;        // Primary (highest-rank) title held (0 = unlanded)
        public int LiegeActorId;   // Direct superior (0 = sovereign / none)
        public int CountyId;       // Home county
    }
}
