using System;
using System.Collections.Generic;
using EconSim.Core.Common;
using EconSim.Core.Data;

namespace EconSim.Core.Economy
{
    /// <summary>
    /// Initializes the economy from map data: resources, initial facilities, etc.
    /// </summary>
    public static class EconomyInitializer
    {
        private static Random _random = new Random(42); // Deterministic for now

        /// <summary>
        /// Fully initialize economy from map data.
        /// </summary>
        public static EconomyState Initialize(MapData mapData)
        {
            var economy = new EconomyState();

            // Register good and facility definitions
            InitialData.RegisterAll(economy);

            // Initialize county economies from map
            economy.InitializeFromMap(mapData);
            SimLog.Log("Economy", $"Initialized {economy.Counties.Count} counties");

            // Assign natural resources based on biomes
            AssignResources(economy, mapData);

            // Place initial facilities
            PlaceInitialFacilities(economy, mapData);

            return economy;
        }

        /// <summary>
        /// Assign natural resources to counties based on cell biomes.
        /// A county's resources are the union of resources from all its cells.
        /// </summary>
        private static void AssignResources(EconomyState economy, MapData mapData)
        {
            // Build biome name lookup
            var biomeNames = new Dictionary<int, string>();
            foreach (var biome in mapData.Biomes)
            {
                biomeNames[biome.Id] = biome.Name;
            }

            SimLog.Log("Economy", $"Biomes available: {string.Join(", ", biomeNames.Values)}");

            // Debug: log height distribution for mining resources
            int cellsAbove40 = 0, cellsAbove45 = 0, cellsAbove50 = 0;
            float maxHeight = 0;
            foreach (var cell in mapData.Cells)
            {
                if (!cell.IsLand) continue;
                if (cell.Height > maxHeight) maxHeight = cell.Height;
                if (cell.Height > 40) cellsAbove40++;
                if (cell.Height > 45) cellsAbove45++;
                if (cell.Height > 50) cellsAbove50++;
            }
            SimLog.Log("Economy", $"Height distribution: max={maxHeight:F1}, >40={cellsAbove40}, >45={cellsAbove45}, >50={cellsAbove50}");

            var resourceCounts = new Dictionary<string, int>();

            // First, determine which cells have which resources
            var cellResources = new Dictionary<int, Dictionary<string, float>>();
            foreach (var cell in mapData.Cells)
            {
                if (!cell.IsLand) continue;
                if (!biomeNames.TryGetValue(cell.BiomeId, out var biomeName))
                    continue;

                var resources = new Dictionary<string, float>();

                // Check each raw good's terrain affinity
                foreach (var good in economy.Goods.ByCategory(GoodCategory.Raw))
                {
                    if (good.TerrainAffinity == null) continue;

                    bool matches = false;

                    // Special case: iron_ore uses height (mountains) not biome
                    if (good.Id == "iron_ore")
                    {
                        matches = cell.Height > 50;
                    }
                    // Special case: gold_ore uses high terrain with rare probability
                    else if (good.Id == "gold_ore")
                    {
                        matches = cell.Height > 45 && _random.NextDouble() < 0.25;
                    }
                    else
                    {
                        // Normal biome matching
                        foreach (var terrain in good.TerrainAffinity)
                        {
                            if (biomeName.Contains(terrain) || terrain.Contains(biomeName))
                            {
                                matches = true;
                                break;
                            }
                        }
                    }

                    if (matches)
                    {
                        float abundance = 0.5f + (float)_random.NextDouble() * 0.5f;
                        resources[good.Id] = abundance;
                    }
                }

                if (resources.Count > 0)
                {
                    cellResources[cell.Id] = resources;
                }
            }

            // Now aggregate resources at county level
            foreach (var countyData in mapData.Counties)
            {
                var countyEcon = economy.GetCounty(countyData.Id);

                // Union of all cell resources in this county (take max abundance)
                foreach (int cellId in countyData.CellIds)
                {
                    if (cellResources.TryGetValue(cellId, out var resources))
                    {
                        foreach (var kvp in resources)
                        {
                            if (!countyEcon.Resources.ContainsKey(kvp.Key) ||
                                countyEcon.Resources[kvp.Key] < kvp.Value)
                            {
                                countyEcon.Resources[kvp.Key] = kvp.Value;
                            }
                        }
                    }
                }

                // Count counties with each resource
                foreach (var res in countyEcon.Resources.Keys)
                {
                    if (!resourceCounts.ContainsKey(res))
                        resourceCounts[res] = 0;
                    resourceCounts[res]++;
                }
            }

            SimLog.Log("Economy", "Resources assigned:");
            foreach (var kvp in resourceCounts)
            {
                SimLog.Log("Economy", $"  {kvp.Key}: {kvp.Value} counties");
            }
        }

        /// <summary>
        /// Place initial facilities. Simple strategy:
        /// - Extraction: place in counties with matching resources (at a cell with the resource)
        /// - Processing: place near population centers
        /// </summary>
        private static void PlaceInitialFacilities(EconomyState economy, MapData mapData)
        {
            // Build cell resource lookup for facility placement
            var cellResources = BuildCellResourceLookup(economy, mapData);

            // Gather counties with resources for each extraction type
            var resourceCounties = new Dictionary<string, List<int>>(); // goodId -> countyIds
            foreach (var county in economy.Counties.Values)
            {
                foreach (var kvp in county.Resources)
                {
                    if (!resourceCounties.ContainsKey(kvp.Key))
                        resourceCounties[kvp.Key] = new List<int>();
                    resourceCounties[kvp.Key].Add(county.CountyId);
                }
            }

            SimLog.Log("Economy", "Placing extraction facilities:");

            // Track which counties have extraction facilities (for co-locating processing)
            var extractionCounties = new Dictionary<string, List<int>>(); // output good -> countyIds with facilities

            // Place extraction facilities (one per ~10 resource counties)
            foreach (var facilityDef in economy.FacilityDefs.ExtractionFacilities)
            {
                if (!resourceCounties.TryGetValue(facilityDef.OutputGoodId, out var candidates))
                {
                    SimLog.Log("Economy", $"  {facilityDef.Id}: NO candidates (no counties with {facilityDef.OutputGoodId})");
                    continue;
                }

                extractionCounties[facilityDef.OutputGoodId] = new List<int>();

                int toPlace = Math.Max(1, candidates.Count / 10);
                int placed = 0;
                var candidatesCopy = new List<int>(candidates);

                for (int i = 0; i < toPlace && candidatesCopy.Count > 0; i++)
                {
                    int countyId = candidatesCopy[_random.Next(candidatesCopy.Count)];

                    // Find a cell in this county that has the resource
                    int cellId = FindCellWithResource(countyId, facilityDef.OutputGoodId, mapData, cellResources);
                    if (cellId < 0)
                    {
                        candidatesCopy.Remove(countyId);
                        continue;
                    }

                    economy.CreateFacility(facilityDef.Id, cellId);
                    extractionCounties[facilityDef.OutputGoodId].Add(countyId);
                    candidatesCopy.Remove(countyId);
                    placed++;
                }
                SimLog.Log("Economy", $"  {facilityDef.Id}: placed {placed} (from {candidates.Count} candidates)");
            }

            // Place processing facilities in stages
            SimLog.Log("Economy", "Placing processing facilities:");

            // Track where each facility type is placed (by countyId)
            var facilityCounties = new Dictionary<string, List<int>>();

            // Stage 1: Primary processors - place in extraction counties
            var primaryProcessors = new Dictionary<string, string>
            {
                { "mill", "wheat" },
                { "smelter", "iron_ore" },
                { "refinery", "gold_ore" },
                { "sawmill", "timber" }
            };

            foreach (var kvp in primaryProcessors)
            {
                var facilityId = kvp.Key;
                var sourceGood = kvp.Value;
                var facilityDef = economy.FacilityDefs.Get(facilityId);
                if (facilityDef == null) continue;

                if (!extractionCounties.TryGetValue(sourceGood, out var candidates) || candidates.Count == 0)
                {
                    SimLog.Log("Economy", $"  {facilityId}: no extraction counties for {sourceGood}");
                    continue;
                }

                facilityCounties[facilityId] = new List<int>();
                int toPlace = Math.Max(1, candidates.Count / 10);
                for (int i = 0; i < toPlace; i++)
                {
                    int countyId = candidates[_random.Next(candidates.Count)];
                    int cellId = GetCountySeatCell(countyId, mapData);
                    if (cellId < 0) continue;

                    economy.CreateFacility(facilityId, cellId);
                    facilityCounties[facilityId].Add(countyId);
                }
                SimLog.Log("Economy", $"  {facilityId}: placed {toPlace} in {sourceGood} counties");
            }

            // Stage 2: Secondary processors - place where primary processors are
            var secondaryProcessors = new Dictionary<string, string>
            {
                { "bakery", "mill" },
                { "smithy", "smelter" },
                { "jeweler", "refinery" },
                { "workshop", "sawmill" }
            };

            foreach (var kvp in secondaryProcessors)
            {
                var facilityId = kvp.Key;
                var upstreamFacility = kvp.Value;
                var facilityDef = economy.FacilityDefs.Get(facilityId);
                if (facilityDef == null) continue;

                if (!facilityCounties.TryGetValue(upstreamFacility, out var candidates) || candidates.Count == 0)
                {
                    SimLog.Log("Economy", $"  {facilityId}: no counties with {upstreamFacility}");
                    continue;
                }

                // Place in ALL counties that have the upstream processor
                foreach (var countyId in candidates)
                {
                    int cellId = GetCountySeatCell(countyId, mapData);
                    if (cellId < 0) continue;
                    economy.CreateFacility(facilityId, cellId);
                }
                SimLog.Log("Economy", $"  {facilityId}: placed {candidates.Count} (co-located with {upstreamFacility})");
            }

            SimLog.Log("Economy", $"Total facilities: {economy.Facilities.Count}");
        }

        /// <summary>
        /// Build a lookup of which cells have which resources.
        /// </summary>
        private static Dictionary<int, HashSet<string>> BuildCellResourceLookup(EconomyState economy, MapData mapData)
        {
            var result = new Dictionary<int, HashSet<string>>();
            var biomeNames = new Dictionary<int, string>();
            foreach (var biome in mapData.Biomes)
            {
                biomeNames[biome.Id] = biome.Name;
            }

            foreach (var cell in mapData.Cells)
            {
                if (!cell.IsLand) continue;
                if (!biomeNames.TryGetValue(cell.BiomeId, out var biomeName)) continue;

                var resources = new HashSet<string>();

                foreach (var good in economy.Goods.ByCategory(GoodCategory.Raw))
                {
                    if (good.TerrainAffinity == null) continue;

                    bool matches = false;
                    if (good.Id == "iron_ore")
                    {
                        matches = cell.Height > 50;
                    }
                    else if (good.Id == "gold_ore")
                    {
                        matches = cell.Height > 45;
                    }
                    else
                    {
                        foreach (var terrain in good.TerrainAffinity)
                        {
                            if (biomeName.Contains(terrain) || terrain.Contains(biomeName))
                            {
                                matches = true;
                                break;
                            }
                        }
                    }

                    if (matches)
                    {
                        resources.Add(good.Id);
                    }
                }

                if (resources.Count > 0)
                {
                    result[cell.Id] = resources;
                }
            }
            return result;
        }

        /// <summary>
        /// Find a cell within a county that has a specific resource.
        /// </summary>
        private static int FindCellWithResource(int countyId, string resourceId, MapData mapData, Dictionary<int, HashSet<string>> cellResources)
        {
            if (!mapData.CountyById.TryGetValue(countyId, out var county))
                return -1;

            foreach (int cellId in county.CellIds)
            {
                if (cellResources.TryGetValue(cellId, out var resources) && resources.Contains(resourceId))
                {
                    return cellId;
                }
            }
            return -1;
        }

        /// <summary>
        /// Get the seat cell for a county (used for processing facility placement).
        /// </summary>
        private static int GetCountySeatCell(int countyId, MapData mapData)
        {
            if (mapData.CountyById.TryGetValue(countyId, out var county))
            {
                return county.SeatCellId;
            }
            return -1;
        }
    }
}
