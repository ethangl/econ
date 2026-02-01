using System;
using System.Collections.Generic;
using EconSim.Core.Data;

namespace EconSim.Core.Economy
{
    /// <summary>
    /// Global economic state for the simulation.
    /// Contains registries (static definitions) and runtime state (counties, facilities).
    /// </summary>
    [Serializable]
    public class EconomyState
    {
        // === Static definitions (loaded at startup) ===
        public GoodRegistry Goods;
        public FacilityRegistry FacilityDefs;

        // === Runtime state ===
        /// <summary>Economic state per county, keyed by cell ID.</summary>
        public Dictionary<int, CountyEconomy> Counties;

        /// <summary>All facility instances, keyed by facility ID.</summary>
        public Dictionary<int, Facility> Facilities;

        /// <summary>Next facility ID to assign.</summary>
        public int NextFacilityId;

        public EconomyState()
        {
            Goods = new GoodRegistry();
            FacilityDefs = new FacilityRegistry();
            Counties = new Dictionary<int, CountyEconomy>();
            Facilities = new Dictionary<int, Facility>();
            NextFacilityId = 1;
        }

        /// <summary>
        /// Initialize county economies from map data.
        /// </summary>
        public void InitializeFromMap(MapData mapData)
        {
            foreach (var cell in mapData.Cells)
            {
                if (!cell.IsLand) continue;

                var county = new CountyEconomy(cell.Id);
                county.Population = CountyPopulation.FromTotal(cell.Population);
                Counties[cell.Id] = county;
            }
        }

        /// <summary>
        /// Get or create county economy for a cell.
        /// </summary>
        public CountyEconomy GetCounty(int cellId)
        {
            if (!Counties.TryGetValue(cellId, out var county))
            {
                county = new CountyEconomy(cellId);
                Counties[cellId] = county;
            }
            return county;
        }

        /// <summary>
        /// Create a new facility instance.
        /// </summary>
        public Facility CreateFacility(string typeId, int cellId)
        {
            var facility = new Facility
            {
                Id = NextFacilityId++,
                TypeId = typeId,
                CellId = cellId
            };

            Facilities[facility.Id] = facility;

            var county = GetCounty(cellId);
            county.FacilityIds.Add(facility.Id);

            return facility;
        }

        /// <summary>
        /// Get a facility by ID.
        /// </summary>
        public Facility GetFacility(int facilityId)
        {
            return Facilities.TryGetValue(facilityId, out var f) ? f : null;
        }

        /// <summary>
        /// Get all facilities of a given type.
        /// </summary>
        public IEnumerable<Facility> GetFacilitiesByType(string typeId)
        {
            foreach (var f in Facilities.Values)
            {
                if (f.TypeId == typeId)
                    yield return f;
            }
        }

        /// <summary>
        /// Get all facilities in a county.
        /// </summary>
        public IEnumerable<Facility> GetFacilitiesInCounty(int cellId)
        {
            if (!Counties.TryGetValue(cellId, out var county))
                yield break;

            foreach (var fid in county.FacilityIds)
            {
                if (Facilities.TryGetValue(fid, out var f))
                    yield return f;
            }
        }
    }
}
