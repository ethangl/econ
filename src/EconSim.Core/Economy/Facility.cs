namespace EconSim.Core.Economy
{
    /// <summary>
    /// Runtime state for a single facility instance in a county.
    /// </summary>
    public class Facility
    {
        public readonly FacilityType Type;
        public readonly int CountyId;
        public readonly int CellId;

        /// <summary>Current number of workers assigned to this facility.</summary>
        public int Workforce;

        public Facility(FacilityType type, int countyId, int cellId)
        {
            Type = type;
            CountyId = countyId;
            CellId = cellId;
        }

        public FacilityDef Def => Facilities.Defs[(int)Type];
    }
}
