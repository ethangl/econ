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
        /// <summary>Economic state per county, keyed by county ID.</summary>
        public Dictionary<int, CountyEconomy> Counties;

        /// <summary>All facility instances, keyed by facility ID.</summary>
        public Dictionary<int, Facility> Facilities;

        /// <summary>All markets, keyed by market ID.</summary>
        public Dictionary<int, Market> Markets;

        /// <summary>Road network state (traffic accumulation, road tiers).</summary>
        public RoadState Roads;

        /// <summary>Next facility ID to assign.</summary>
        public int NextFacilityId;

        /// <summary>Lookup: cell ID → county ID (from MapData).</summary>
        [NonSerialized]
        public Dictionary<int, int> CellToCounty;

        /// <summary>Lookup: county ID → market ID that serves it.</summary>
        [NonSerialized]
        public Dictionary<int, int> CountyToMarket;

        /// <summary>Lookup: cell ID → market ID that serves it (computed from county assignments).</summary>
        [NonSerialized]
        public Dictionary<int, int> CellToMarket;

        public EconomyState()
        {
            Goods = new GoodRegistry();
            FacilityDefs = new FacilityRegistry();
            Counties = new Dictionary<int, CountyEconomy>();
            Facilities = new Dictionary<int, Facility>();
            Markets = new Dictionary<int, Market>();
            Roads = new RoadState();
            NextFacilityId = 1;
            CellToCounty = new Dictionary<int, int>();
            CountyToMarket = new Dictionary<int, int>();
            CellToMarket = new Dictionary<int, int>();
        }

        /// <summary>
        /// Initialize county economies from map data.
        /// Creates one CountyEconomy per County (not per Cell).
        /// </summary>
        public void InitializeFromMap(MapData mapData)
        {
            // Build cell-to-county lookup
            CellToCounty.Clear();
            foreach (var cell in mapData.Cells)
            {
                if (cell.IsLand && cell.CountyId > 0)
                {
                    CellToCounty[cell.Id] = cell.CountyId;
                }
            }

            // Create economy for each county
            if (mapData.Counties == null) return;

            foreach (var countyData in mapData.Counties)
            {
                var countyEcon = new CountyEconomy(countyData.Id);
                countyEcon.Population = CountyPopulation.FromTotal(countyData.TotalPopulation);
                countyEcon.Stockpile.BindGoods(Goods);
                Counties[countyData.Id] = countyEcon;
            }
        }

        /// <summary>
        /// Get county economy by county ID.
        /// </summary>
        public CountyEconomy GetCounty(int countyId)
        {
            if (!Counties.TryGetValue(countyId, out var county))
            {
                county = new CountyEconomy(countyId);
                Counties[countyId] = county;
            }

            county.Stockpile.BindGoods(Goods);
            return county;
        }

        /// <summary>
        /// Get county economy for a cell (uses CellToCounty lookup).
        /// </summary>
        public CountyEconomy GetCountyForCell(int cellId)
        {
            if (CellToCounty.TryGetValue(cellId, out int countyId))
            {
                return GetCounty(countyId);
            }
            return null;
        }

        /// <summary>
        /// Create a new facility instance at a cell.
        /// The facility is physically located in the cell but economically owned by its county.
        /// </summary>
        public Facility CreateFacility(string typeId, int cellId, int unitCount = 1)
        {
            // Look up the county that owns this cell
            int countyId = CellToCounty.TryGetValue(cellId, out int cId) ? cId : 0;

            var facility = new Facility
            {
                Id = NextFacilityId++,
                TypeId = typeId,
                CellId = cellId,
                CountyId = countyId,
                UnitCount = Math.Max(1, unitCount)
            };
            facility.BindGoods(Goods);

            Facilities[facility.Id] = facility;

            // Add to county's facility list
            if (countyId > 0)
            {
                var county = GetCounty(countyId);
                county.FacilityIds.Add(facility.Id);
            }

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
        /// Get all facilities in a county by county ID.
        /// </summary>
        public IEnumerable<Facility> GetFacilitiesInCounty(int countyId)
        {
            if (!Counties.TryGetValue(countyId, out var county))
                yield break;

            foreach (var fid in county.FacilityIds)
            {
                if (Facilities.TryGetValue(fid, out var f))
                    yield return f;
            }
        }

        /// <summary>
        /// Get market by ID.
        /// </summary>
        public Market GetMarket(int marketId)
        {
            return Markets.TryGetValue(marketId, out var m) ? m : null;
        }

        /// <summary>
        /// Get the market that serves a given cell (if any).
        /// </summary>
        public Market GetMarketForCell(int cellId)
        {
            if (CellToMarket.TryGetValue(cellId, out var marketId))
                return GetMarket(marketId);
            return null;
        }

        /// <summary>
        /// Rebuild the cell-to-market and county-to-market lookups from market zones.
        /// Assigns each cell to the nearest market by transport cost.
        /// Counties are assigned based on their seat cell's assignment.
        /// </summary>
        public void RebuildCellToMarketLookup()
        {
            CellToMarket.Clear();
            CountyToMarket.Clear();
            var cellToCost = new Dictionary<int, float>();
            var countyCells = new Dictionary<int, List<int>>();

            // Build county -> cells index once (avoids O(counties * cells) scans below).
            foreach (var kvp in CellToCounty)
            {
                int cellId = kvp.Key;
                int countyId = kvp.Value;
                if (!countyCells.TryGetValue(countyId, out var list))
                {
                    list = new List<int>();
                    countyCells[countyId] = list;
                }
                list.Add(cellId);
            }

            // First pass: assign cells to primary (legitimate) markets only.
            // Off-map markets are supplemental import channels and should not replace county assignments.
            foreach (var market in Markets.Values)
            {
                if (market.Type != MarketType.Legitimate)
                    continue;

                foreach (var cellId in market.ZoneCellIds)
                {
                    float cost = market.ZoneCellCosts.TryGetValue(cellId, out var c) ? c : float.MaxValue;

                    // Assign to this market if it's closer than any previous assignment
                    if (!CellToMarket.ContainsKey(cellId) || cost < cellToCost[cellId])
                    {
                        CellToMarket[cellId] = market.Id;
                        cellToCost[cellId] = cost;
                    }
                }
            }

            // Second pass: assign counties based on best cell access
            // For each county, use the cell with lowest transport cost to any market
            foreach (var countyEcon in Counties.Values)
            {
                int bestMarketId = -1;
                float bestCost = float.MaxValue;

                if (!countyCells.TryGetValue(countyEcon.CountyId, out var cellsInCounty))
                    continue;

                // Check all cells in this county and pick the lowest-cost market access.
                foreach (int cellId in cellsInCounty)
                {
                    if (CellToMarket.TryGetValue(cellId, out int marketId) &&
                        cellToCost.TryGetValue(cellId, out float cost))
                    {
                        if (cost < bestCost)
                        {
                            bestCost = cost;
                            bestMarketId = marketId;
                        }
                    }
                }

                if (bestMarketId > 0)
                {
                    CountyToMarket[countyEcon.CountyId] = bestMarketId;
                }
            }
        }

        /// <summary>
        /// Rebuild only the cell-to-market lookup from existing county assignments.
        /// Use this when county-level market mapping is already known (e.g., loaded from cache).
        /// </summary>
        public void RebuildCellToMarketFromCountyLookup()
        {
            CellToMarket.Clear();
            foreach (var kvp in CellToCounty)
            {
                int cellId = kvp.Key;
                int countyId = kvp.Value;
                if (CountyToMarket.TryGetValue(countyId, out int marketId) && marketId > 0)
                {
                    CellToMarket[cellId] = marketId;
                }
            }
        }

        /// <summary>
        /// Get the market that serves a given county.
        /// </summary>
        public Market GetMarketForCounty(int countyId)
        {
            if (CountyToMarket.TryGetValue(countyId, out var marketId))
                return GetMarket(marketId);
            return null;
        }
    }
}
