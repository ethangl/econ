using System.Collections.Generic;

namespace EconSim.Core.Economy
{
    /// <summary>
    /// Initial good and facility definitions for the v1 economy.
    /// Four production chains: Food, Tools, Jewelry, Furniture.
    /// </summary>
    public static class InitialData
    {
        /// <summary>
        /// Register all v1 goods and facilities.
        /// </summary>
        public static void RegisterAll(EconomyState economy)
        {
            RegisterGoods(economy.Goods);
            RegisterFacilities(economy.FacilityDefs);
        }

        public static void RegisterGoods(GoodRegistry registry)
        {
            // =============================================
            // CHAIN 1: Food (Wheat → Flour → Bread)
            // =============================================

            registry.Register(new GoodDef
            {
                Id = "wheat",
                Name = "Wheat",
                Category = GoodCategory.Raw,
                HarvestMethod = "farming",
                TerrainAffinity = new List<string> { "Grassland", "Savanna" },
                BaseYield = 10f,
                DecayRate = 0.005f,  // 0.5% per day - grain stores well if dry
                TheftRisk = 0.3f,   // Bulk, moderate value
                BasePrice = 1.0f
            });

            registry.Register(new GoodDef
            {
                Id = "flour",
                Name = "Flour",
                Category = GoodCategory.Refined,
                Inputs = new List<GoodInput> { new GoodInput("wheat", 2) },
                FacilityType = "mill",
                ProcessingTicks = 1,
                DecayRate = 0.01f,  // 1% per day - processed, absorbs moisture
                TheftRisk = 0.4f,  // Processed, more valuable
                BasePrice = 3.0f   // 2 wheat (2.0) + processing
            });

            registry.Register(new GoodDef
            {
                Id = "bread",
                Name = "Bread",
                Category = GoodCategory.Finished,
                Inputs = new List<GoodInput> { new GoodInput("flour", 1) },
                FacilityType = "bakery",
                ProcessingTicks = 1,
                NeedCategory = NeedCategory.Basic,
                BaseConsumption = 0.01f,  // 0.01 bread per person per day
                DecayRate = 0.05f,  // 5% per day - highly perishable
                TheftRisk = 0.1f,  // Perishable, hard to fence
                BasePrice = 5.0f   // Basic staple, modest markup
            });

            // =============================================
            // CHAIN 2: Tools (Iron Ore → Iron → Tools)
            // =============================================

            registry.Register(new GoodDef
            {
                Id = "iron_ore",
                Name = "Iron Ore",
                Category = GoodCategory.Raw,
                HarvestMethod = "mining",
                TerrainAffinity = new List<string> { "Highland", "Mountain" },
                BaseYield = 5f,
                DecayRate = 0f,  // Rock doesn't decay
                TheftRisk = 0.2f, // Heavy, low value/weight
                BasePrice = 1.0f
            });

            registry.Register(new GoodDef
            {
                Id = "iron",
                Name = "Iron Ingots",
                Category = GoodCategory.Refined,
                Inputs = new List<GoodInput> { new GoodInput("iron_ore", 3) },
                FacilityType = "smelter",
                ProcessingTicks = 2,
                DecayRate = 0f,  // Metal doesn't decay
                TheftRisk = 0.5f, // Valuable, easy to move
                BasePrice = 5.0f  // 3 ore (3.0) + smelting
            });

            registry.Register(new GoodDef
            {
                Id = "tools",
                Name = "Tools",
                Category = GoodCategory.Finished,
                Inputs = new List<GoodInput> { new GoodInput("iron", 1) },
                FacilityType = "smithy",
                ProcessingTicks = 2,
                NeedCategory = NeedCategory.Comfort,
                BaseConsumption = 0.001f,  // Tools last longer, lower consumption
                DecayRate = 0f,  // Durable goods
                TheftRisk = 0.8f, // High value, high demand
                BasePrice = 15.0f // Comfort item, skilled labor premium
            });

            // =============================================
            // CHAIN 3: Jewelry (Gold Ore → Gold → Jewelry)
            // =============================================

            registry.Register(new GoodDef
            {
                Id = "gold_ore",
                Name = "Gold Ore",
                Category = GoodCategory.Raw,
                HarvestMethod = "mining",
                TerrainAffinity = new List<string> { "Highland", "Mountain" },
                BaseYield = 1f,  // Much rarer than iron (5f)
                DecayRate = 0f,
                TheftRisk = 0.7f, // High value even as ore
                BasePrice = 5.0f  // Rare precious metal
            });

            registry.Register(new GoodDef
            {
                Id = "gold",
                Name = "Gold Ingots",
                Category = GoodCategory.Refined,
                Inputs = new List<GoodInput> { new GoodInput("gold_ore", 3) },  // 3 ore per ingot (same as iron)
                FacilityType = "refinery",
                ProcessingTicks = 3,
                DecayRate = 0f,
                TheftRisk = 0.9f, // Very high value, portable
                BasePrice = 20.0f // 3 ore (15) + refining
            });

            registry.Register(new GoodDef
            {
                Id = "jewelry",
                Name = "Jewelry",
                Category = GoodCategory.Finished,
                Inputs = new List<GoodInput> { new GoodInput("gold", 1) },
                FacilityType = "jeweler",
                ProcessingTicks = 3,
                NeedCategory = NeedCategory.Luxury,
                BaseConsumption = 0.0002f,  // Very low - luxury item
                DecayRate = 0f,
                TheftRisk = 1.0f, // Maximum theft appeal
                BasePrice = 50.0f // Luxury, artisan premium
            });

            // =============================================
            // CHAIN 4: Furniture (Timber → Lumber → Furniture)
            // =============================================

            registry.Register(new GoodDef
            {
                Id = "timber",
                Name = "Timber",
                Category = GoodCategory.Raw,
                HarvestMethod = "logging",
                TerrainAffinity = new List<string> {
                    "Temperate deciduous forest",
                    "Temperate rainforest",
                    "Tropical seasonal forest",
                    "Tropical rainforest",
                    "Taiga"
                },
                BaseYield = 8f,
                DecayRate = 0.002f,  // 0.2% per day - raw wood can rot slowly
                TheftRisk = 0.1f,   // Bulky, low theft appeal
                BasePrice = 1.0f
            });

            registry.Register(new GoodDef
            {
                Id = "lumber",
                Name = "Lumber",
                Category = GoodCategory.Refined,
                Inputs = new List<GoodInput> { new GoodInput("timber", 2) },
                FacilityType = "sawmill",
                ProcessingTicks = 1,
                DecayRate = 0.002f,  // 0.2% per day - processed wood, similar to timber
                TheftRisk = 0.2f,   // Processed, more portable
                BasePrice = 3.0f    // 2 timber (2.0) + processing
            });

            registry.Register(new GoodDef
            {
                Id = "furniture",
                Name = "Furniture",
                Category = GoodCategory.Finished,
                Inputs = new List<GoodInput> { new GoodInput("lumber", 2) },
                FacilityType = "workshop",
                ProcessingTicks = 3,
                NeedCategory = NeedCategory.Luxury,
                BaseConsumption = 0.0005f,  // Luxury, low consumption
                DecayRate = 0f,  // Finished furniture is durable
                TheftRisk = 0.6f, // High value, identifiable
                BasePrice = 12.0f // 2 lumber (6.0) + crafting
            });
        }

        public static void RegisterFacilities(FacilityRegistry registry)
        {
            // =============================================
            // Extraction facilities (need terrain match)
            // =============================================

            registry.Register(new FacilityDef
            {
                Id = "farm",
                Name = "Farm",
                OutputGoodId = "wheat",
                LaborRequired = 5,
                LaborType = LaborType.Unskilled,
                BaseThroughput = 10f,
                IsExtraction = true,
                TerrainRequirements = new List<string> { "Grassland", "Savanna" }
            });

            registry.Register(new FacilityDef
            {
                Id = "mine",
                Name = "Iron Mine",
                OutputGoodId = "iron_ore",
                LaborRequired = 8,
                LaborType = LaborType.Unskilled,
                BaseThroughput = 5f,
                IsExtraction = true,
                TerrainRequirements = new List<string> { "Highland", "Mountain" }
            });

            registry.Register(new FacilityDef
            {
                Id = "gold_mine",
                Name = "Gold Mine",
                OutputGoodId = "gold_ore",
                LaborRequired = 10,
                LaborType = LaborType.Unskilled,
                BaseThroughput = 3f,  // Lower than iron (5f) but enough to supply refinery
                IsExtraction = true,
                TerrainRequirements = new List<string> { "Highland", "Mountain" }
            });

            registry.Register(new FacilityDef
            {
                Id = "lumber_camp",
                Name = "Lumber Camp",
                OutputGoodId = "timber",
                LaborRequired = 4,
                LaborType = LaborType.Unskilled,
                BaseThroughput = 8f,
                IsExtraction = true,
                TerrainRequirements = new List<string> {
                    "Temperate deciduous forest",
                    "Temperate rainforest",
                    "Tropical seasonal forest",
                    "Tropical rainforest",
                    "Taiga"
                }
            });

            // =============================================
            // Processing facilities (no terrain requirement)
            // =============================================

            registry.Register(new FacilityDef
            {
                Id = "mill",
                Name = "Mill",
                OutputGoodId = "flour",
                LaborRequired = 3,
                LaborType = LaborType.Skilled,
                BaseThroughput = 5f,
                IsExtraction = false
            });

            registry.Register(new FacilityDef
            {
                Id = "bakery",
                Name = "Bakery",
                OutputGoodId = "bread",
                LaborRequired = 2,
                LaborType = LaborType.Skilled,
                BaseThroughput = 5f,
                IsExtraction = false
            });

            registry.Register(new FacilityDef
            {
                Id = "smelter",
                Name = "Smelter",
                OutputGoodId = "iron",
                LaborRequired = 6,
                LaborType = LaborType.Skilled,
                BaseThroughput = 3f,
                IsExtraction = false
            });

            registry.Register(new FacilityDef
            {
                Id = "smithy",
                Name = "Smithy",
                OutputGoodId = "tools",
                LaborRequired = 4,
                LaborType = LaborType.Skilled,
                BaseThroughput = 2f,
                IsExtraction = false
            });

            registry.Register(new FacilityDef
            {
                Id = "refinery",
                Name = "Gold Refinery",
                OutputGoodId = "gold",
                LaborRequired = 4,
                LaborType = LaborType.Skilled,
                BaseThroughput = 2f,  // Needs >= 2 to handle understaffing (int truncation)
                IsExtraction = false
            });

            registry.Register(new FacilityDef
            {
                Id = "jeweler",
                Name = "Jeweler",
                OutputGoodId = "jewelry",
                LaborRequired = 2,
                LaborType = LaborType.Skilled,
                BaseThroughput = 2f,  // Needs >= 2 to handle understaffing (int truncation)
                IsExtraction = false
            });

            registry.Register(new FacilityDef
            {
                Id = "sawmill",
                Name = "Sawmill",
                OutputGoodId = "lumber",
                LaborRequired = 3,
                LaborType = LaborType.Skilled,
                BaseThroughput = 4f,
                IsExtraction = false
            });

            registry.Register(new FacilityDef
            {
                Id = "workshop",
                Name = "Workshop",
                OutputGoodId = "furniture",
                LaborRequired = 4,
                LaborType = LaborType.Skilled,
                BaseThroughput = 2f,
                IsExtraction = false
            });
        }
    }
}
