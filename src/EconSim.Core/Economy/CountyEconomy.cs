using System;
using System.Collections.Generic;

namespace EconSim.Core.Economy
{
    /// <summary>
    /// Economic state for a single county.
    /// This is the runtime data that changes each tick.
    /// A county contains one or more cells grouped by CountyGrouper.
    /// </summary>
    [Serializable]
    public class CountyEconomy
    {
        /// <summary>County ID this economy belongs to (from MapData.Counties).</summary>
        public int CountyId;

        /// <summary>Population cohorts and employment state.</summary>
        public CountyPopulation Population;

        /// <summary>Local stockpile of goods.</summary>
        public Stockpile Stockpile;

        /// <summary>Facilities located in this county.</summary>
        public List<int> FacilityIds;

        /// <summary>
        /// Natural resources available in this county.
        /// Maps good ID → abundance (0-1 multiplier on harvest yield).
        /// </summary>
        public Dictionary<string, float> Resources;

        /// <summary>Unmet demand from last consumption tick (for tracking/effects).</summary>
        public Dictionary<string, float> UnmetDemand;

        public CountyEconomy(int countyId)
        {
            CountyId = countyId;
            Population = new CountyPopulation();
            Stockpile = new Stockpile();
            FacilityIds = new List<int>();
            Resources = new Dictionary<string, float>();
            UnmetDemand = new Dictionary<string, float>();
        }

        /// <summary>Check if this county has a particular natural resource.</summary>
        public bool HasResource(string goodId)
        {
            return Resources.ContainsKey(goodId) && Resources[goodId] > 0;
        }

        /// <summary>Get resource abundance (0 if not present).</summary>
        public float GetResourceAbundance(string goodId)
        {
            return Resources.TryGetValue(goodId, out var abundance) ? abundance : 0f;
        }
    }
}
