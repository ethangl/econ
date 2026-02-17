using System;
using System.Collections.Generic;
using EconSim.Core.Common;
using EconSim.Core.Data;
using EconSim.Core.Simulation;

namespace EconSim.Core.Economy
{
    /// <summary>
    /// Initializes the economy from map data: resources, initial facilities, etc.
    /// </summary>
    public static class EconomyInitializer
    {
        private static Random _random = new Random(42); // Re-seeded per initialization
        // Calibrated mountain-resource thresholds as fractions of max above-sea elevation.
        // Derived from legacy anchors 40/45/50 in [20..100] => 0.25 / 0.3125 / 0.375.
        private const float Elevation40FractionAboveSea = 0.25f;
        private const float Elevation45FractionAboveSea = 0.3125f;
        private const float Elevation50FractionAboveSea = 0.375f;
        private const float BootstrapGrainReserveDays = 270f; // 9 months to avoid cold-start starvation.
        private const float BootstrapRawGrainKgPerFlourKg = 1f / 0.72f;
        private const float BootstrapSaltReserveKgPerCapita = 7.5f; // Midpoint of 5-10 kg/person reserve.
        private static readonly string[] BootstrapReserveGrainGoodIds = { "wheat", "rye", "barley", "rice_grain" };

        /// <summary>
        /// Fully initialize economy from map data.
        /// </summary>
        public static EconomyState Initialize(MapData mapData)
        {
            return Initialize(mapData, null);
        }

        /// <summary>
        /// Fully initialize economy from map data using a world-generation context.
        /// </summary>
        public static EconomyState Initialize(MapData mapData, WorldGenerationContext generationContext)
        {
            return Initialize(mapData, generationContext.EconomySeed);
        }

        /// <summary>
        /// Fully initialize economy from map data using an optional explicit seed.
        /// </summary>
        public static EconomyState Initialize(MapData mapData, int? explicitSeed)
        {
            // Use explicit economy seed when provided, otherwise derive from map metadata.
            _random = new Random(ResolveInitializationSeed(mapData, explicitSeed));

            var economy = new EconomyState();

            // Register good and facility definitions
            InitialData.RegisterAll(economy);
            InitialData.ApplyV2GoodOverrides(economy.Goods);

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
        /// Seed V2 economy treasuries, inventories, and day-0 orders after markets are available.
        /// </summary>
        public static void BootstrapV2(SimulationState state, MapData mapData)
        {
            var economy = state.Economy;
            if (economy == null)
                return;

            float basicBasket = 0f;
            foreach (var good in economy.Goods.ConsumerGoods)
            {
                if (!IsBasicNeed(good))
                    continue;

                basicBasket += good.BasePrice * good.BaseConsumptionKgPerCapitaPerDay;
            }

            state.SmoothedBasketCost = basicBasket;
            state.SubsistenceWage = basicBasket * 1.2f;

            foreach (var county in economy.Counties.Values)
            {
                if (county.Population.Total <= 0)
                {
                    county.Population.Treasury = 0f;
                    continue;
                }

                float dailyBasicCost = 0f;
                foreach (var good in economy.Goods.ConsumerGoods)
                {
                    if (!IsBasicNeed(good))
                        continue;

                    dailyBasicCost += good.BasePrice * good.BaseConsumptionKgPerCapitaPerDay * county.Population.Total;
                }

                county.Population.Treasury = dailyBasicCost * 30f;
            }

            SeedCountyGrainReserves(economy);
            SeedCountySaltReserves(economy);

            foreach (var facility in economy.Facilities.Values)
            {
                var def = economy.FacilityDefs.Get(facility.TypeId);
                if (def == null)
                    continue;
                if (!SimulationConfig.Economy.IsFacilityEnabled(facility.TypeId)
                    || !SimulationConfig.Economy.IsGoodEnabled(def.OutputGoodId))
                {
                    facility.IsActive = false;
                    facility.AssignedWorkers = 0;
                    facility.Treasury = 0f;
                    continue;
                }

                var outputGood = economy.Goods.Get(def.OutputGoodId);
                var inputs = SelectInputsForFacilityByBaseCost(economy, def, outputGood);

                float weeklyInputCost = 0f;
                if (inputs != null)
                {
                    float nominalThroughput = facility.GetNominalThroughput(def);
                    foreach (var input in inputs)
                    {
                        var inputGood = economy.Goods.Get(input.GoodId);
                        if (inputGood == null)
                            continue;
                        weeklyInputCost += inputGood.BasePrice * input.QuantityKg * nominalThroughput * 7f;
                    }
                }

                float weeklyWage = facility.GetRequiredLabor(def) * state.SubsistenceWage * 7f;
                facility.Treasury = weeklyInputCost + weeklyWage;
                facility.WageRate = state.SubsistenceWage;
                facility.IsActive = true;
                facility.GraceDaysRemaining = 14;
            }

            // Seed processing input buffers (3 days).
            foreach (var facility in economy.Facilities.Values)
            {
                var def = economy.FacilityDefs.Get(facility.TypeId);
                if (def == null || def.IsExtraction)
                    continue;
                if (!SimulationConfig.Economy.IsFacilityEnabled(facility.TypeId)
                    || !SimulationConfig.Economy.IsGoodEnabled(def.OutputGoodId))
                    continue;

                var outputGood = economy.Goods.Get(def.OutputGoodId);
                var inputs = SelectInputsForFacilityByBaseCost(economy, def, outputGood);
                if (inputs == null)
                    continue;

                float nominalThroughput = facility.GetNominalThroughput(def);
                foreach (var input in inputs)
                {
                    facility.InputBuffer.Add(input.GoodId, input.QuantityKg * nominalThroughput * 3f);
                }
            }

            // Seed market inventory and day-0 buy orders.
            foreach (var market in economy.Markets.Values)
            {
                if (market.Type != MarketType.Legitimate)
                    continue;

                int seedSellerId = MarketOrderIds.MakeSeedSellerId(market.Id);
                var localOutputs = new HashSet<string>();
                foreach (var kvp in economy.CountyToMarket)
                {
                    if (kvp.Value != market.Id || !economy.Counties.ContainsKey(kvp.Key))
                        continue;

                    var county = economy.Counties[kvp.Key];
                    foreach (int facilityId in county.FacilityIds)
                    {
                        if (economy.Facilities.TryGetValue(facilityId, out var facility))
                        {
                            var def = economy.FacilityDefs.Get(facility.TypeId);
                            if (def != null)
                                localOutputs.Add(def.OutputGoodId);
                        }
                    }
                }

                foreach (string goodId in localOutputs)
                {
                    if (!SimulationConfig.Economy.IsGoodEnabled(goodId))
                        continue;
                    var good = economy.Goods.Get(goodId);
                    if (good == null)
                        continue;

                    float weeklyDemand = 0f;
                    foreach (var kvp in economy.CountyToMarket)
                    {
                        if (kvp.Value != market.Id || !economy.Counties.TryGetValue(kvp.Key, out var county))
                            continue;

                        weeklyDemand += good.BaseConsumptionKgPerCapitaPerDay * county.Population.Total * 7f;
                    }

                    if (weeklyDemand <= 0f)
                        continue;

                    market.AddInventoryLot(new ConsignmentLot
                    {
                        SellerId = seedSellerId,
                        GoodId = goodId,
                        GoodRuntimeId = good.RuntimeId,
                        Quantity = weeklyDemand * 2f,
                        DayListed = 0
                    });
                }
            }

            // Prefill day-0 orders for processing facilities and basic population demand.
            foreach (var county in economy.Counties.Values)
            {
                if (!economy.CountyToMarket.TryGetValue(county.CountyId, out int marketId))
                    continue;
                if (!economy.Markets.TryGetValue(marketId, out var market))
                    continue;

                float transportCost = economy.GetCountyTransportCost(county.CountyId);
                int populationBuyerId = MarketOrderIds.MakePopulationBuyerId(county.CountyId);

                foreach (var good in economy.Goods.ConsumerGoods)
                {
                    if (!IsBasicNeed(good))
                        continue;
                    if (!SimulationConfig.Economy.IsGoodEnabled(good.Id))
                        continue;
                    if (!market.Goods.TryGetValue(good.Id, out var marketGood))
                        continue;

                    float quantity = good.BaseConsumptionKgPerCapitaPerDay * county.Population.Total;
                    if (quantity <= 0f)
                        continue;

                    float effectivePrice = marketGood.Price * (1f + transportCost * 0.005f);
                    float maxSpend = quantity * effectivePrice;
                    if (maxSpend <= 0f)
                        continue;

                    market.AddPendingBuyOrder(new BuyOrder
                    {
                        BuyerId = populationBuyerId,
                        GoodId = good.Id,
                        GoodRuntimeId = good.RuntimeId,
                        Quantity = quantity,
                        MaxSpend = Math.Min(maxSpend, county.Population.Treasury),
                        TransportCost = transportCost,
                        DayPosted = 0
                    });
                }

                foreach (int facilityId in county.FacilityIds)
                {
                    if (!economy.Facilities.TryGetValue(facilityId, out var facility) || !facility.IsActive)
                        continue;

                    var def = economy.FacilityDefs.Get(facility.TypeId);
                    if (def == null || def.IsExtraction)
                        continue;
                    if (!SimulationConfig.Economy.IsFacilityEnabled(facility.TypeId)
                        || !SimulationConfig.Economy.IsGoodEnabled(def.OutputGoodId))
                        continue;

                    var outputGood = economy.Goods.Get(def.OutputGoodId);
                    var inputs = SelectInputsForFacilityByBaseCost(economy, def, outputGood);
                    if (inputs == null)
                        continue;

                    float remainingTreasury = facility.Treasury;
                    foreach (var input in inputs)
                    {
                        if (!SimulationConfig.Economy.IsGoodEnabled(input.GoodId))
                            continue;
                        if (!market.Goods.TryGetValue(input.GoodId, out var marketGood))
                            continue;
                        if (!economy.Goods.TryGetRuntimeId(input.GoodId, out int inputRuntimeId))
                            continue;

                        float needed = input.QuantityKg * facility.GetNominalThroughput(def);
                        float have = facility.InputBuffer.Get(inputRuntimeId);
                        float toBuy = Math.Max(0f, needed - have);
                        if (toBuy <= 0f)
                            continue;

                        float effectivePrice = marketGood.Price * (1f + transportCost * 0.005f);
                        float maxSpend = Math.Min(remainingTreasury, toBuy * effectivePrice);
                        float quantity = effectivePrice > 0f ? maxSpend / effectivePrice : 0f;
                        if (quantity <= 0f || maxSpend <= 0f)
                            continue;

                        market.AddPendingBuyOrder(new BuyOrder
                        {
                            BuyerId = facility.Id,
                            GoodId = input.GoodId,
                            GoodRuntimeId = inputRuntimeId,
                            Quantity = quantity,
                            MaxSpend = maxSpend,
                            TransportCost = transportCost,
                            DayPosted = 0
                        });

                        remainingTreasury -= maxSpend;
                        if (remainingTreasury <= 0f)
                            break;
                    }
                }
            }
        }

        private static int ResolveInitializationSeed(MapData mapData, int? explicitSeed)
        {
            if (explicitSeed.HasValue)
            {
                return explicitSeed.Value;
            }

            if (SimulationConfig.EconomySeedOverride > 0)
            {
                return SimulationConfig.EconomySeedOverride;
            }

            if (mapData?.Info != null)
            {
                if (mapData.Info.EconomySeed > 0)
                    return mapData.Info.EconomySeed;

                if (mapData.Info.RootSeed > 0)
                    return WorldSeeds.FromRoot(mapData.Info.RootSeed).EconomySeed;

                if (!string.IsNullOrWhiteSpace(mapData.Info.Seed) && int.TryParse(mapData.Info.Seed, out int parsed))
                    return WorldSeeds.FromRoot(parsed).EconomySeed;
            }
            return 42;
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

            float elevation40Meters = ResolveThresholdMetersAboveSeaLevel(mapData.Info, Elevation40FractionAboveSea);
            float elevation45Meters = ResolveThresholdMetersAboveSeaLevel(mapData.Info, Elevation45FractionAboveSea);
            float elevation50Meters = ResolveThresholdMetersAboveSeaLevel(mapData.Info, Elevation50FractionAboveSea);

            // Debug: log elevation distribution for mining resources.
            int cellsAbove40 = 0, cellsAbove45 = 0, cellsAbove50 = 0;
            float maxHeightAbsolute = 0f;
            float maxHeightMeters = 0f;
            foreach (var cell in mapData.Cells)
            {
                if (!cell.IsLand) continue;
                float absoluteElevation = Elevation.GetAbsoluteHeight(cell, mapData.Info);
                float elevationMetersAboveSeaLevel = Elevation.GetMetersAboveSeaLevel(cell, mapData.Info);
                if (absoluteElevation > maxHeightAbsolute) maxHeightAbsolute = absoluteElevation;
                if (elevationMetersAboveSeaLevel > maxHeightMeters) maxHeightMeters = elevationMetersAboveSeaLevel;
                if (elevationMetersAboveSeaLevel > elevation40Meters) cellsAbove40++;
                if (elevationMetersAboveSeaLevel > elevation45Meters) cellsAbove45++;
                if (elevationMetersAboveSeaLevel > elevation50Meters) cellsAbove50++;
            }
            SimLog.Log(
                "Economy",
                $"Elevation distribution: maxAbs={maxHeightAbsolute:F1}, maxAboveSea={maxHeightMeters:F0}m, >{elevation40Meters:F0}m={cellsAbove40}, >{elevation45Meters:F0}m={cellsAbove45}, >{elevation50Meters:F0}m={cellsAbove50}");

            var resourceCounts = new Dictionary<string, int>();

            // First, determine which cells have which resources
            var cellResources = new Dictionary<int, Dictionary<string, float>>();
            foreach (var cell in mapData.Cells)
            {
                if (!cell.IsLand) continue;
                if (!biomeNames.TryGetValue(cell.BiomeId, out var biomeName))
                    continue;

                var resources = new Dictionary<string, float>();
                float elevationMetersAboveSeaLevel = Elevation.GetMetersAboveSeaLevel(cell, mapData.Info);

                // Check each raw good's terrain affinity
                foreach (var good in economy.Goods.ByCategory(GoodCategory.Raw))
                {
                    if (good.TerrainAffinity == null) continue;

                    bool matches = false;

                    // Special case: iron_ore uses height (mountains) not biome
                    if (good.Id == "iron_ore")
                    {
                        matches = elevationMetersAboveSeaLevel > elevation50Meters;
                    }
                    // Special case: copper_ore uses lower elevation (foothills)
                    else if (good.Id == "copper_ore")
                    {
                        matches = elevationMetersAboveSeaLevel > elevation40Meters;
                    }
                    // Special case: gold_ore uses high terrain with rare probability
                    else if (good.Id == "gold_ore")
                    {
                        matches = elevationMetersAboveSeaLevel > elevation45Meters && _random.NextDouble() < 0.25;
                    }
                    else
                    {
                        // Normal biome matching
                        foreach (var terrain in good.TerrainAffinity)
                        {
                            if (TerrainAffinityMatcher.MatchesBiome(terrain, biomeName))
                            {
                                matches = true;
                                break;
                            }
                        }
                    }

                    if (matches)
                    {
                        float abundance = DeterministicHelpers.NextFloat(_random, 0.5f, 1f);
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

            // Place extraction facilities (multiple per eligible county)
            foreach (var facilityDef in economy.FacilityDefs.ExtractionFacilities)
            {
                if (!resourceCounties.TryGetValue(facilityDef.OutputGoodId, out var candidates))
                {
                    SimLog.Log("Economy", $"  {facilityDef.Id}: NO candidates (no counties with {facilityDef.OutputGoodId})");
                    continue;
                }

                int placed = 0;

                foreach (var countyId in candidates)
                {
                    int cellId = FindCellWithResource(countyId, facilityDef.OutputGoodId, mapData, cellResources);
                    if (cellId < 0) continue;

                    int count = ComputeFacilityCount(economy.GetCounty(countyId).Population, facilityDef, minPerCounty: 1);
                    if (count <= 0)
                        continue;

                    economy.CreateFacility(facilityDef.Id, cellId, count);
                    placed += count;
                }
                SimLog.Log("Economy", $"  {facilityDef.Id}: placed {placed} in {candidates.Count} candidates");
            }

            // Place non-harvesting facilities in every county.
            // This keeps processing/manufacturing universally available while extraction stays resource-gated.
            SimLog.Log("Economy", "Placing non-harvesting facilities in all counties:");
            foreach (var facilityDef in economy.FacilityDefs.All)
            {
                if (facilityDef == null || facilityDef.IsExtraction)
                    continue;
                if (!SimulationConfig.Economy.IsFacilityEnabled(facilityDef.Id)
                    || !SimulationConfig.Economy.IsGoodEnabled(facilityDef.OutputGoodId))
                    continue;

                int placed = 0;
                int countyCount = 0;
                foreach (var county in economy.Counties.Values)
                {
                    int cellId = GetCountySeatCell(county.CountyId, mapData);
                    if (cellId < 0)
                        continue;

                    int count = ComputeFacilityCount(county.Population, facilityDef, minPerCounty: 1);
                    if (count <= 0)
                        continue;

                    economy.CreateFacility(facilityDef.Id, cellId, count);
                    placed += count;
                    countyCount++;
                }

                SimLog.Log("Economy", $"  {facilityDef.Id}: placed {placed} across {countyCount} counties");
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

            float elevation40Meters = ResolveThresholdMetersAboveSeaLevel(mapData.Info, Elevation40FractionAboveSea);
            float elevation45Meters = ResolveThresholdMetersAboveSeaLevel(mapData.Info, Elevation45FractionAboveSea);
            float elevation50Meters = ResolveThresholdMetersAboveSeaLevel(mapData.Info, Elevation50FractionAboveSea);

            foreach (var cell in mapData.Cells)
            {
                if (!cell.IsLand) continue;
                if (!biomeNames.TryGetValue(cell.BiomeId, out var biomeName)) continue;

                var resources = new HashSet<string>();
                float elevationMetersAboveSeaLevel = Elevation.GetMetersAboveSeaLevel(cell, mapData.Info);

                foreach (var good in economy.Goods.ByCategory(GoodCategory.Raw))
                {
                    if (good.TerrainAffinity == null) continue;

                    bool matches = false;
                    if (good.Id == "iron_ore")
                    {
                        matches = elevationMetersAboveSeaLevel > elevation50Meters;
                    }
                    else if (good.Id == "copper_ore")
                    {
                        matches = elevationMetersAboveSeaLevel > elevation40Meters;
                    }
                    else if (good.Id == "gold_ore")
                    {
                        matches = elevationMetersAboveSeaLevel > elevation45Meters;
                    }
                    else
                    {
                        foreach (var terrain in good.TerrainAffinity)
                        {
                            if (TerrainAffinityMatcher.MatchesBiome(terrain, biomeName))
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
        /// Compute how many facilities of a given type to place in a county,
        /// scaling with the relevant worker pool (unskilled or skilled).
        /// Formula: 1 facility per (ScaleFactor * LaborRequired) workers of the matching type.
        /// A median ~200-pop county gets ~3 farms (labor=20), matching prior defaults.
        /// </summary>
        private static int ComputeFacilityCount(CountyPopulation pop, FacilityDef def, int minPerCounty = 0)
        {
            const float ExtractionScaleFactor = 3f;
            const float ProcessingUnskilledScaleFactor = 4f;
            const float ProcessingSkilledScaleFactor = 5f;
            const int MaxPerType = 50;

            int workerPool = def.LaborType == LaborType.Unskilled
                ? pop.TotalUnskilled
                : pop.TotalSkilled;

            float scaleFactor = def.IsExtraction
                ? ExtractionScaleFactor
                : (def.LaborType == LaborType.Skilled
                    ? ProcessingSkilledScaleFactor
                    : ProcessingUnskilledScaleFactor);

            int count = (int)(workerPool / (def.LaborRequired * scaleFactor));
            return Math.Max(minPerCounty, Math.Min(MaxPerType, count));
        }

        private static int ComputeInputConstrainedFacilityCount(
            EconomyState economy,
            int countyId,
            FacilityDef targetDef,
            int laborLimitedCount,
            HashSet<string> requiredLocalInputGoodIds = null)
        {
            if (laborLimitedCount <= 0)
                return 0;

            var outputGood = economy.Goods.Get(targetDef.OutputGoodId);
            var inputs = SelectInputsForFacilityByBaseCost(economy, targetDef, outputGood);
            if (inputs == null || inputs.Count == 0)
                return laborLimitedCount;

            var county = economy.GetCounty(countyId);
            if (county == null)
                return laborLimitedCount;

            float maxThroughputFromInputs = float.MaxValue;
            bool hasInputConstraint = false;

            foreach (var input in inputs)
            {
                if (input.QuantityKg <= 0f)
                    continue;
                if (requiredLocalInputGoodIds != null &&
                    requiredLocalInputGoodIds.Count > 0 &&
                    !requiredLocalInputGoodIds.Contains(input.GoodId))
                {
                    continue;
                }

                float localInputPerDay = 0f;
                foreach (int facilityId in county.FacilityIds)
                {
                    if (!economy.Facilities.TryGetValue(facilityId, out var facility))
                        continue;

                    var producerDef = economy.FacilityDefs.Get(facility.TypeId);
                    if (producerDef == null || producerDef.OutputGoodId != input.GoodId)
                        continue;

                    localInputPerDay += facility.GetNominalThroughput(producerDef);
                }

                hasInputConstraint = true;
                if (localInputPerDay <= 0f)
                    return 0;

                float inputLimitedThroughput = localInputPerDay / input.QuantityKg;
                if (inputLimitedThroughput < maxThroughputFromInputs)
                    maxThroughputFromInputs = inputLimitedThroughput;
            }

            if (!hasInputConstraint || maxThroughputFromInputs == float.MaxValue)
                return laborLimitedCount;

            float targetThroughput = Math.Max(0.0001f, targetDef.BaseThroughputKgPerDay);
            int inputLimitedCount = (int)Math.Ceiling(maxThroughputFromInputs / targetThroughput);
            if (inputLimitedCount <= 0)
                return 0;

            return Math.Max(1, Math.Min(laborLimitedCount, inputLimitedCount));
        }

        private static List<GoodInput> SelectInputsForFacilityByBaseCost(
            EconomyState economy,
            FacilityDef def,
            GoodDef outputGood)
        {
            if (def?.InputOverrides != null && def.InputOverrides.Count > 0)
                return def.InputOverrides;

            if (outputGood?.InputVariants != null && outputGood.InputVariants.Count > 0)
            {
                List<GoodInput> bestInputs = null;
                float bestCost = float.MaxValue;
                for (int i = 0; i < outputGood.InputVariants.Count; i++)
                {
                    var variant = outputGood.InputVariants[i];
                    var variantInputs = variant?.Inputs;
                    if (variantInputs == null || variantInputs.Count == 0)
                        continue;

                    float cost = 0f;
                    bool valid = true;
                    for (int j = 0; j < variantInputs.Count; j++)
                    {
                        var input = variantInputs[j];
                        if (input.QuantityKg <= 0f)
                        {
                            valid = false;
                            break;
                        }

                        var inputGood = economy.Goods.Get(input.GoodId);
                        if (inputGood == null)
                        {
                            valid = false;
                            break;
                        }

                        cost += inputGood.BasePrice * input.QuantityKg;
                    }

                    if (!valid || cost >= bestCost)
                        continue;

                    bestCost = cost;
                    bestInputs = variantInputs;
                }

                if (bestInputs != null)
                    return bestInputs;
            }

            return outputGood?.Inputs;
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

        private static float ResolveThresholdMetersAboveSeaLevel(MapInfo info, float aboveSeaFraction)
        {
            if (aboveSeaFraction <= 0f)
                return 0f;
            if (aboveSeaFraction >= 1f)
                return Elevation.ResolveMaxElevationMeters(info);

            return aboveSeaFraction * Elevation.ResolveMaxElevationMeters(info);
        }

        private static bool IsBasicNeed(GoodDef good)
        {
            return good != null
                && good.NeedCategory == NeedCategory.Basic
                && SimulationConfig.Economy.IsGoodEnabled(good.Id);
        }

        private static void SeedCountyGrainReserves(EconomyState economy)
        {
            if (economy == null)
                return;

            float stapleFlourPerCapitaPerDay = 160f / 365f;
            var flour = economy.Goods.Get("flour");
            if (flour != null
                && flour.NeedCategory == NeedCategory.Basic
                && flour.BaseConsumptionKgPerCapitaPerDay > 0f)
            {
                stapleFlourPerCapitaPerDay = flour.BaseConsumptionKgPerCapitaPerDay;
            }

            float totalSeededKg = 0f;
            int seededCounties = 0;
            foreach (var county in economy.Counties.Values)
            {
                int population = county.Population.Total;
                if (population <= 0)
                    continue;

                float dailyRawNeed = population * stapleFlourPerCapitaPerDay * BootstrapRawGrainKgPerFlourKg;
                float seedTargetKg = Math.Max(0f, dailyRawNeed * BootstrapGrainReserveDays);
                if (seedTargetKg <= 0f)
                    continue;

                float[] weights = new float[BootstrapReserveGrainGoodIds.Length];
                float weightSum = 0f;
                for (int i = 0; i < BootstrapReserveGrainGoodIds.Length; i++)
                {
                    string goodId = BootstrapReserveGrainGoodIds[i];
                    float weight = 0f;
                    if (county.Resources != null
                        && county.Resources.TryGetValue(goodId, out float localAbundance)
                        && localAbundance > 0f)
                    {
                        weight = localAbundance;
                    }

                    weights[i] = weight;
                    weightSum += weight;
                }

                // Fallback cereal mix if county has no explicit grain resource profile.
                if (weightSum <= 0f)
                {
                    for (int i = 0; i < BootstrapReserveGrainGoodIds.Length; i++)
                        weights[i] = 0f;

                    int wheatIndex = Array.IndexOf(BootstrapReserveGrainGoodIds, "wheat");
                    int ryeIndex = Array.IndexOf(BootstrapReserveGrainGoodIds, "rye");
                    int barleyIndex = Array.IndexOf(BootstrapReserveGrainGoodIds, "barley");

                    if (wheatIndex >= 0) weights[wheatIndex] = 1f;
                    if (ryeIndex >= 0) weights[ryeIndex] = 1f;
                    if (barleyIndex >= 0) weights[barleyIndex] = 1f;
                    weightSum = 3f;
                }

                if (weightSum <= 0f)
                    continue;

                float seededHere = 0f;
                for (int i = 0; i < BootstrapReserveGrainGoodIds.Length; i++)
                {
                    if (weights[i] <= 0f)
                        continue;

                    string goodId = BootstrapReserveGrainGoodIds[i];
                    if (!economy.Goods.TryGetRuntimeId(goodId, out int runtimeId) || runtimeId < 0)
                        continue;

                    float quantity = seedTargetKg * (weights[i] / weightSum);
                    if (quantity <= 0f)
                        continue;

                    county.Stockpile.Add(runtimeId, quantity);
                    seededHere += quantity;
                }

                if (seededHere > 0f)
                {
                    seededCounties++;
                    totalSeededKg += seededHere;
                }
            }

            SimLog.Log(
                "Economy",
                $"Seeded county grain reserves: counties={seededCounties}, total={totalSeededKg:F0} kg, targetDays={BootstrapGrainReserveDays:F0}");
        }

        private static void SeedCountySaltReserves(EconomyState economy)
        {
            if (economy == null)
                return;
            if (!economy.Goods.TryGetRuntimeId("salt", out int saltRuntimeId) || saltRuntimeId < 0)
                return;

            float totalSeededKg = 0f;
            int seededCounties = 0;
            foreach (var county in economy.Counties.Values)
            {
                int population = county.Population.Total;
                if (population <= 0)
                    continue;

                float quantity = population * BootstrapSaltReserveKgPerCapita;
                if (quantity <= 0f)
                    continue;

                county.Stockpile.Add(saltRuntimeId, quantity);
                seededCounties++;
                totalSeededKg += quantity;
            }

            SimLog.Log(
                "Economy",
                $"Seeded county salt reserves: counties={seededCounties}, total={totalSeededKg:F0} kg, perCapita={BootstrapSaltReserveKgPerCapita:F1} kg");
        }

    }
}
