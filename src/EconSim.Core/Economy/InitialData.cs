using System.Collections.Generic;

namespace EconSim.Core.Economy
{
    /// <summary>
    /// Initial good and facility definitions for the v1 economy.
    /// Three production chains: Food, Tools, Furniture.
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
                BaseYield = 10f
            });

            registry.Register(new GoodDef
            {
                Id = "flour",
                Name = "Flour",
                Category = GoodCategory.Refined,
                Inputs = new List<GoodInput> { new GoodInput("wheat", 2) },
                FacilityType = "mill",
                ProcessingTicks = 1
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
                BaseConsumption = 0.01f  // 0.01 bread per person per day
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
                BaseYield = 5f
            });

            registry.Register(new GoodDef
            {
                Id = "iron",
                Name = "Iron Ingots",
                Category = GoodCategory.Refined,
                Inputs = new List<GoodInput> { new GoodInput("iron_ore", 3) },
                FacilityType = "smelter",
                ProcessingTicks = 2
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
                BaseConsumption = 0.001f  // Tools last longer, lower consumption
            });

            // =============================================
            // CHAIN 3: Furniture (Timber → Lumber → Furniture)
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
                BaseYield = 8f
            });

            registry.Register(new GoodDef
            {
                Id = "lumber",
                Name = "Lumber",
                Category = GoodCategory.Refined,
                Inputs = new List<GoodInput> { new GoodInput("timber", 2) },
                FacilityType = "sawmill",
                ProcessingTicks = 1
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
                BaseConsumption = 0.0005f  // Luxury, low consumption
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
