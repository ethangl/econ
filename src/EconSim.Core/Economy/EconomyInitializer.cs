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
        /// Assign natural resources to counties based on biome.
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

            foreach (var cell in mapData.Cells)
            {
                if (!cell.IsLand) continue;

                var county = economy.GetCounty(cell.Id);
                if (!biomeNames.TryGetValue(cell.BiomeId, out var biomeName))
                    continue;

                // Check each raw good's terrain affinity
                foreach (var good in economy.Goods.ByCategory(GoodCategory.Raw))
                {
                    if (good.TerrainAffinity == null) continue;

                    bool matches = false;

                    // Special case: iron_ore uses height (mountains) not biome
                    if (good.Id == "iron_ore")
                    {
                        // Height > 50 = mountainous (sea level is 20, max is 100)
                        matches = cell.Height > 50;
                    }
                    // Special case: gold_ore uses high terrain with rare probability
                    else if (good.Id == "gold_ore")
                    {
                        // Height > 45 (slightly lower than iron's 50), with 25% chance - rarer due to probability
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
                        // Abundance varies 0.5 - 1.0
                        float abundance = 0.5f + (float)_random.NextDouble() * 0.5f;
                        county.Resources[good.Id] = abundance;

                        if (!resourceCounts.ContainsKey(good.Id))
                            resourceCounts[good.Id] = 0;
                        resourceCounts[good.Id]++;
                    }
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
        /// - Extraction: place in counties with matching resources
        /// - Processing: place near population centers
        /// </summary>
        private static void PlaceInitialFacilities(EconomyState economy, MapData mapData)
        {
            // Gather counties with resources for each extraction type
            var resourceCounties = new Dictionary<string, List<int>>();
            foreach (var county in economy.Counties.Values)
            {
                foreach (var kvp in county.Resources)
                {
                    if (!resourceCounties.ContainsKey(kvp.Key))
                        resourceCounties[kvp.Key] = new List<int>();
                    resourceCounties[kvp.Key].Add(county.CellId);
                }
            }

            SimLog.Log("Economy", "Placing extraction facilities:");

            // Track which counties have extraction facilities (for co-locating processing)
            var extractionCounties = new Dictionary<string, List<int>>(); // output good -> cell IDs with facilities

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
                for (int i = 0; i < toPlace && i < candidates.Count; i++)
                {
                    int cellId = candidates[_random.Next(candidates.Count)];
                    economy.CreateFacility(facilityDef.Id, cellId);
                    extractionCounties[facilityDef.OutputGoodId].Add(cellId);
                    candidates.Remove(cellId); // Don't double-place
                    placed++;
                }
                SimLog.Log("Economy", $"  {facilityDef.Id}: placed {placed} (from {candidates.Count + placed} candidates)");
            }

            // Place processing facilities in stages to ensure proper co-location
            // Stage 1: Primary processors (need raw materials) - place in extraction counties
            // Stage 2: Secondary processors (need refined materials) - place where stage 1 facilities are
            SimLog.Log("Economy", "Placing processing facilities:");

            // Track where each facility type is placed
            var facilityCounties = new Dictionary<string, List<int>>();

            // Stage 1: Primary processors - place in extraction counties
            var primaryProcessors = new Dictionary<string, string>
            {
                { "mill", "wheat" },       // mill needs wheat (from farms)
                { "smelter", "iron_ore" }, // smelter needs iron_ore (from mines)
                { "refinery", "gold_ore" }, // refinery needs gold_ore (from gold mines)
                { "sawmill", "timber" }    // sawmill needs timber (from lumber camps)
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
                    int cellId = candidates[_random.Next(candidates.Count)];
                    economy.CreateFacility(facilityId, cellId);
                    facilityCounties[facilityId].Add(cellId);
                }
                SimLog.Log("Economy", $"  {facilityId}: placed {toPlace} in {sourceGood} counties");
            }

            // Stage 2: Secondary processors - place where primary processors are
            var secondaryProcessors = new Dictionary<string, string>
            {
                { "bakery", "mill" },      // bakery needs flour (from mills)
                { "smithy", "smelter" },   // smithy needs iron (from smelters)
                { "jeweler", "refinery" }, // jeweler needs gold (from refineries)
                { "workshop", "sawmill" }  // workshop needs lumber (from sawmills)
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

                // Place in ALL counties that have the upstream processor (ensures production chain works)
                foreach (var cellId in candidates)
                {
                    economy.CreateFacility(facilityId, cellId);
                }
                SimLog.Log("Economy", $"  {facilityId}: placed {candidates.Count} (co-located with {upstreamFacility})");
            }

            SimLog.Log("Economy", $"Total facilities: {economy.Facilities.Count}");
        }
    }
}
