using System;
using System.Collections.Generic;
using System.Linq;
using EconSim.Core.Common;
using EconSim.Core.Data;

namespace EconSim.Core.Import
{
    /// <summary>
    /// Groups cells into counties based on population density.
    /// High-density areas (cities) become single-cell counties.
    /// Sparse areas consolidate into larger multi-cell counties.
    /// </summary>
    public static class CountyGrouper
    {
        // Tuning parameters
        private const float HighDensityThreshold = 500f;  // Cells above this pop become instant single-cell counties
        private const float TargetPopulation = 200f;      // Target population per county (rural areas)
        private const int MaxCellsPerCounty = 64;         // Maximum cells in a rural county

        /// <summary>
        /// Group cells into counties. Modifies mapData in place:
        /// - Sets Cell.CountyId for each land cell
        /// - Populates MapData.Counties list
        /// </summary>
        public static void GroupCells(MapData mapData)
        {
            var counties = new List<County>();
            var assigned = new HashSet<int>();  // Cell IDs already assigned to a county
            int nextCountyId = 1;

            // Build burg lookup for naming
            var burgByCellId = new Dictionary<int, Burg>();
            if (mapData.Burgs != null)
            {
                foreach (var burg in mapData.Burgs)
                {
                    if (burg.CellId > 0)
                    {
                        burgByCellId[burg.CellId] = burg;
                    }
                }
            }

            // Group cells by province for province boundary constraint
            var cellsByProvince = new Dictionary<int, List<Cell>>();
            foreach (var cell in mapData.Cells)
            {
                if (!cell.IsLand) continue;

                int provId = cell.ProvinceId > 0 ? cell.ProvinceId : 0;
                if (!cellsByProvince.ContainsKey(provId))
                {
                    cellsByProvince[provId] = new List<Cell>();
                }
                cellsByProvince[provId].Add(cell);
            }

            // Process each province separately
            foreach (var kvp in cellsByProvince)
            {
                int provinceId = kvp.Key;
                var provinceCells = kvp.Value;
                var provinceAssigned = new HashSet<int>();

                // Step 1: High-density cells become instant single-cell counties
                var highDensityCells = provinceCells
                    .Where(c => c.Population >= HighDensityThreshold)
                    .OrderByDescending(c => c.Population)
                    .ToList();

                foreach (var cell in highDensityCells)
                {
                    var county = CreateSingleCellCounty(cell, nextCountyId++, mapData, burgByCellId);
                    counties.Add(county);
                    assigned.Add(cell.Id);
                    provinceAssigned.Add(cell.Id);
                }

                // Step 2: Burg cells become seeds (largest population first)
                var burgSeeds = provinceCells
                    .Where(c => c.HasBurg && !assigned.Contains(c.Id))
                    .OrderByDescending(c => c.Population)
                    .ToList();

                foreach (var seed in burgSeeds)
                {
                    if (assigned.Contains(seed.Id)) continue;

                    var county = GrowCountyFromSeed(
                        seed, nextCountyId++, mapData, burgByCellId,
                        assigned, provinceAssigned, provinceId);
                    counties.Add(county);
                }

                // Step 3: Orphan cells form rural counties
                var orphans = provinceCells
                    .Where(c => !assigned.Contains(c.Id))
                    .OrderByDescending(c => c.Population)
                    .ToList();

                while (orphans.Count > 0)
                {
                    // Find highest-pop orphan as seed
                    var seed = orphans[0];
                    orphans.RemoveAt(0);

                    if (assigned.Contains(seed.Id)) continue;

                    var county = GrowCountyFromSeed(
                        seed, nextCountyId++, mapData, burgByCellId,
                        assigned, provinceAssigned, provinceId);
                    counties.Add(county);

                    // Update orphans list
                    orphans = orphans.Where(c => !assigned.Contains(c.Id)).ToList();
                }
            }

            // Update mapData
            mapData.Counties = counties;

            // Rebuild lookups to include counties
            mapData.CountyById = new Dictionary<int, County>();
            foreach (var county in counties)
            {
                mapData.CountyById[county.Id] = county;
            }

            SimLog.Log("CountyGrouper", $"Created {counties.Count} counties from {mapData.Cells.Count(c => c.IsLand)} land cells");

            // Log distribution
            int singleCell = counties.Count(c => c.CellCount == 1);
            int multi = counties.Count(c => c.CellCount > 1);
            float avgCells = counties.Count > 0 ? (float)counties.Sum(c => c.CellCount) / counties.Count : 0;
            SimLog.Log("CountyGrouper", $"  Single-cell: {singleCell}, Multi-cell: {multi}, Avg cells/county: {avgCells:F1}");
        }

        private static County CreateSingleCellCounty(
            Cell cell, int countyId, MapData mapData,
            Dictionary<int, Burg> burgByCellId)
        {
            var county = new County(countyId);
            county.SeatCellId = cell.Id;
            county.CellIds.Add(cell.Id);
            county.ProvinceId = cell.ProvinceId;
            county.StateId = cell.StateId;
            county.TotalPopulation = cell.Population;
            county.Centroid = cell.Center;

            // Name from burg or cell ID
            if (burgByCellId.TryGetValue(cell.Id, out var burg) && !string.IsNullOrEmpty(burg.Name))
            {
                county.Name = burg.Name;
            }
            else
            {
                county.Name = $"County {countyId}";
            }

            // Update cell reference
            cell.CountyId = countyId;

            return county;
        }

        private static County GrowCountyFromSeed(
            Cell seed, int countyId, MapData mapData,
            Dictionary<int, Burg> burgByCellId,
            HashSet<int> globalAssigned, HashSet<int> provinceAssigned,
            int provinceId)
        {
            var county = new County(countyId);
            county.SeatCellId = seed.Id;
            county.ProvinceId = seed.ProvinceId;
            county.StateId = seed.StateId;

            // Name from seed burg
            if (burgByCellId.TryGetValue(seed.Id, out var burg) && !string.IsNullOrEmpty(burg.Name))
            {
                county.Name = burg.Name;
            }
            else
            {
                county.Name = $"County {countyId}";
            }

            // Start with seed cell
            county.CellIds.Add(seed.Id);
            globalAssigned.Add(seed.Id);
            provinceAssigned.Add(seed.Id);
            seed.CountyId = countyId;
            float totalPop = seed.Population;

            // Flood fill to grow county
            var frontier = new List<int>(seed.NeighborIds);

            while (totalPop < TargetPopulation && county.CellCount < MaxCellsPerCounty && frontier.Count > 0)
            {
                // Score candidates: prefer sparse neighbors (easier to merge)
                int bestIdx = -1;
                float bestScore = float.MinValue;

                for (int i = 0; i < frontier.Count; i++)
                {
                    int neighborId = frontier[i];
                    if (globalAssigned.Contains(neighborId)) continue;

                    if (!mapData.CellById.TryGetValue(neighborId, out var neighbor))
                        continue;

                    // Skip if not in same province (or no province constraint)
                    if (neighbor.ProvinceId != provinceId && provinceId > 0)
                        continue;

                    if (!neighbor.IsLand) continue;

                    // Score: prefer low population neighbors (easier to absorb)
                    // But also prefer cells that are already adjacent to existing county cells
                    int adjacentCount = neighbor.NeighborIds.Count(n => county.CellIds.Contains(n));
                    float score = -neighbor.Population + adjacentCount * 100;

                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestIdx = i;
                    }
                }

                if (bestIdx < 0)
                {
                    // No valid candidates, remove assigned/invalid from frontier and continue
                    frontier = frontier.Where(id =>
                    {
                        if (globalAssigned.Contains(id)) return false;
                        if (!mapData.CellById.TryGetValue(id, out var c)) return false;
                        if (!c.IsLand) return false;
                        if (c.ProvinceId != provinceId && provinceId > 0) return false;
                        return true;
                    }).ToList();

                    if (frontier.Count == 0) break;
                    continue;
                }

                // Add best neighbor to county
                int cellId = frontier[bestIdx];
                frontier.RemoveAt(bestIdx);

                var cell = mapData.CellById[cellId];
                county.CellIds.Add(cellId);
                globalAssigned.Add(cellId);
                provinceAssigned.Add(cellId);
                cell.CountyId = countyId;
                totalPop += cell.Population;

                // Add this cell's neighbors to frontier
                foreach (int n in cell.NeighborIds)
                {
                    if (!globalAssigned.Contains(n) && !frontier.Contains(n))
                    {
                        frontier.Add(n);
                    }
                }
            }

            // Calculate county stats
            county.TotalPopulation = totalPop;
            county.Centroid = CalculateCentroid(county, mapData);

            return county;
        }

        private static Vec2 CalculateCentroid(County county, MapData mapData)
        {
            if (county.CellIds.Count == 0)
                return new Vec2(0, 0);

            // Population-weighted centroid
            float sumX = 0, sumY = 0, sumPop = 0;
            foreach (int cellId in county.CellIds)
            {
                if (mapData.CellById.TryGetValue(cellId, out var cell))
                {
                    float weight = cell.Population > 0 ? cell.Population : 1;
                    sumX += cell.Center.X * weight;
                    sumY += cell.Center.Y * weight;
                    sumPop += weight;
                }
            }

            if (sumPop > 0)
            {
                return new Vec2(sumX / sumPop, sumY / sumPop);
            }

            // Fallback to geometric center
            sumX = 0; sumY = 0;
            foreach (int cellId in county.CellIds)
            {
                if (mapData.CellById.TryGetValue(cellId, out var cell))
                {
                    sumX += cell.Center.X;
                    sumY += cell.Center.Y;
                }
            }
            return new Vec2(sumX / county.CellIds.Count, sumY / county.CellIds.Count);
        }
    }
}
