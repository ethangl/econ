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

            // Place processing facilities CO-LOCATED with extraction (same counties)
            // This creates integrated production chains until we have transport
            SimLog.Log("Economy", "Placing processing facilities (co-located with extraction):");

            // Map processing output to its input source
            // flour needs wheat -> place mills in wheat counties (farms)
            // bread needs flour -> place bakeries in wheat counties (with mills)
            // iron needs iron_ore -> place smelters in iron_ore counties (mines)
            // tools needs iron -> place smithies in iron_ore counties (with smelters)
            // lumber needs timber -> place sawmills in timber counties (lumber camps)
            // furniture needs lumber -> place workshops in timber counties (with sawmills)

            var processingChains = new Dictionary<string, string>
            {
                { "mill", "wheat" },       // mill processes wheat
                { "bakery", "wheat" },     // bakery in same area as mills
                { "smelter", "iron_ore" }, // smelter processes iron_ore
                { "smithy", "iron_ore" },  // smithy in same area as smelters
                { "sawmill", "timber" },   // sawmill processes timber
                { "workshop", "timber" }   // workshop in same area as sawmills
            };

            foreach (var facilityDef in economy.FacilityDefs.ProcessingFacilities)
            {
                if (!processingChains.TryGetValue(facilityDef.Id, out var sourceGood))
                {
                    SimLog.Log("Economy", $"  {facilityDef.Id}: no chain mapping, skipping");
                    continue;
                }

                if (!extractionCounties.TryGetValue(sourceGood, out var candidates) || candidates.Count == 0)
                {
                    SimLog.Log("Economy", $"  {facilityDef.Id}: no extraction counties for {sourceGood}");
                    continue;
                }

                // Place in ~10% of extraction counties
                int toPlace = Math.Max(1, candidates.Count / 10);
                for (int i = 0; i < toPlace; i++)
                {
                    int cellId = candidates[_random.Next(candidates.Count)];
                    economy.CreateFacility(facilityDef.Id, cellId);
                }
                SimLog.Log("Economy", $"  {facilityDef.Id}: placed {toPlace} in {sourceGood} counties");
            }

            SimLog.Log("Economy", $"Total facilities: {economy.Facilities.Count}");
        }
    }
}
