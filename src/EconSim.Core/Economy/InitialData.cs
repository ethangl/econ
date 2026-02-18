using System.Collections.Generic;

namespace EconSim.Core.Economy
{
    /// <summary>
    /// Initial good and facility definitions for the v1 economy.
    /// Twelve production chains: Food, Tools, Jewelry, Furniture, Clothing (4-tier),
    /// Leatherwork, Copperwork, Sugar, Spices, Salt, Beer, Dyed Clothes.
    /// Quantity convention: all good quantities are modeled as kilograms.
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
            // Unit convention: all BaseYield / Inputs / BaseConsumption values are kilograms.
            // =============================================
            // CHAIN 1: Food (Grain → Flour → Food)
            // Multiple grain types feed into this chain from different biomes:
            //   Grain (wheat): Grassland, Savanna
            //   Rye: Steppe, Taiga
            //   Barley: Highland, Steppe
            //   Rice: Tropical forests (skips flour, goes directly to food)
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
                BasePrice = 0.025f
            });

            registry.Register(new GoodDef
            {
                Id = "rye",
                Name = "Rye",
                Category = GoodCategory.Raw,
                HarvestMethod = "farming",
                TerrainAffinity = new List<string> { "Steppe", "Taiga" },
                BaseYield = 8f,     // Hardy but less productive than wheat
                DecayRate = 0.005f,
                TheftRisk = 0.3f,
                BasePrice = 0.018f
            });

            registry.Register(new GoodDef
            {
                Id = "barley",
                Name = "Barley",
                Category = GoodCategory.Raw,
                HarvestMethod = "farming",
                TerrainAffinity = new List<string> { "Highland", "Steppe" },
                BaseYield = 6f,     // Grows in harsh conditions, lower yield
                DecayRate = 0.005f,
                TheftRisk = 0.3f,
                BasePrice = 0.015f
            });

            registry.Register(new GoodDef
            {
                Id = "rice_grain",
                Name = "Rice",
                Category = GoodCategory.Raw,
                HarvestMethod = "farming",
                TerrainAffinity = new List<string> { "Tropical seasonal forest", "Tropical rainforest" },
                BaseYield = 12f,    // Rice paddies are very productive
                DecayRate = 0.005f,
                TheftRisk = 0.3f,
                BasePrice = 1.0f
            });

            registry.Register(new GoodDef
            {
                Id = "flour",
                Name = "Flour",
                Category = GoodCategory.Refined,
                Inputs = new List<GoodInput> { new GoodInput("wheat", 1f / 0.75f) }, // 75% yield
                InputVariants = new List<GoodInputVariant>
                {
                    new GoodInputVariant
                    {
                        Id = "wheat_preferred",
                        Inputs = new List<GoodInput> { new GoodInput("wheat", 1f / 0.75f) } // 75% yield
                    },
                    new GoodInputVariant
                    {
                        Id = "rye_fallback",
                        Inputs = new List<GoodInput> { new GoodInput("rye", 1f / 0.75f) } // 75% yield
                    },
                    new GoodInputVariant
                    {
                        Id = "barley_last_resort",
                        Inputs = new List<GoodInput> { new GoodInput("barley", 1f / 0.65f) } // 65% yield
                    }
                },
                FacilityType = "mill",
                ProcessingTicks = 1,
                NeedCategory = NeedCategory.Basic,
                BaseConsumption = 160f / 365f, // Flour-equivalent staple intake (household cooking/baking)
                DecayRate = 0.015f,  // 1.5% per day - roughly ~2 month shelf-life
                TheftRisk = 0.4f,  // Processed, more valuable
                BasePrice = 0.03f
            });

            registry.Register(new GoodDef
            {
                Id = "bread",
                Name = "Bread",
                Category = GoodCategory.Finished,
                Inputs = new List<GoodInput> { new GoodInput("flour", 1f / 1.4f) }, // 1.4 kg bread per 1 kg flour
                FacilityType = "bakery",
                ProcessingTicks = 1,
                NeedCategory = NeedCategory.Comfort,
                BaseConsumption = 40f / 365f,  // Market-purchased baked bread on top of staple flour
                DecayRate = 0.25f,  // 25% per day - stale in 3-4 days
                TheftRisk = 0f,
                BasePrice = 5.0f   // Comfort-tier prepared food markup over flour
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
            // =============================================
            // CHAIN 5: Clothing (Sheep → Wool → Cloth → Clothes)
            // =============================================

            registry.Register(new GoodDef
            {
                Id = "sheep",
                Name = "Sheep",
                Category = GoodCategory.Raw,
                HarvestMethod = "herding",
                TerrainAffinity = new List<string> { "Grassland", "Steppe", "Highland" },
                BaseYield = 6f,
                DecayRate = 0f,  // Livestock, not perishable
                TheftRisk = 0.2f, // Hard to steal a flock
                BasePrice = 1.0f
            });

            registry.Register(new GoodDef
            {
                Id = "wool",
                Name = "Wool",
                Category = GoodCategory.Refined,
                Inputs = new List<GoodInput> { new GoodInput("sheep", 2) },
                FacilityType = "shearing_shed",
                ProcessingTicks = 1,
                DecayRate = 0.002f,  // 0.2% per day - raw fleece, slight degradation
                TheftRisk = 0.3f,
                BasePrice = 3.0f  // 2 sheep (2.0) + shearing
            });

            registry.Register(new GoodDef
            {
                Id = "cloth",
                Name = "Cloth",
                Category = GoodCategory.Refined,
                Inputs = new List<GoodInput> { new GoodInput("wool", 2) },
                FacilityType = "spinning_mill",
                ProcessingTicks = 2,
                DecayRate = 0.001f,  // 0.1% per day - woven fabric, very durable
                TheftRisk = 0.4f,
                BasePrice = 8.0f  // 2 wool (6.0) + spinning/weaving
            });

            registry.Register(new GoodDef
            {
                Id = "clothes",
                Name = "Clothes",
                Category = GoodCategory.Finished,
                Inputs = new List<GoodInput> { new GoodInput("cloth", 1) },
                FacilityType = "tailor",
                ProcessingTicks = 1,
                NeedCategory = NeedCategory.Basic,
                BaseConsumption = 0.005f,  // Lower than bread - clothes last longer
                DecayRate = 0.001f,  // 0.1% per day - wears slowly
                TheftRisk = 0.5f,  // Portable, useful
                BasePrice = 12.0f  // 1 cloth (8.0) + tailoring
            });
            // =============================================
            // CHAIN 7: Leatherwork (Hides → Leather → Shoes)
            // =============================================

            registry.Register(new GoodDef
            {
                Id = "hides",
                Name = "Hides",
                Category = GoodCategory.Raw,
                HarvestMethod = "herding",
                TerrainAffinity = new List<string> { "Grassland", "Steppe", "Highland" },
                BaseYield = 4f,
                DecayRate = 0.01f,  // 1% per day - raw hides need salting/drying
                TheftRisk = 0.1f,
                BasePrice = 1.0f
            });

            registry.Register(new GoodDef
            {
                Id = "leather",
                Name = "Leather",
                Category = GoodCategory.Refined,
                Inputs = new List<GoodInput> { new GoodInput("hides", 2) },
                FacilityType = "tannery",
                ProcessingTicks = 2,  // Tanning takes time
                DecayRate = 0.001f,  // 0.1% per day - cured hide, very durable
                TheftRisk = 0.4f,
                BasePrice = 4.0f  // 2 hides (2.0) + tanning
            });

            registry.Register(new GoodDef
            {
                Id = "shoes",
                Name = "Shoes",
                Category = GoodCategory.Finished,
                Inputs = new List<GoodInput> { new GoodInput("leather", 1) },
                FacilityType = "cobbler",
                ProcessingTicks = 2,
                NeedCategory = NeedCategory.Comfort,
                BaseConsumption = 0.002f,  // Shoes wear out but last a while
                DecayRate = 0f,  // Durable
                TheftRisk = 0.5f,
                BasePrice = 10.0f  // Skilled craftsmanship
            });

            // =============================================
            // CHAIN 10: Spices (Spice Plants → Spices)
            // Two-tier chain: harvest and dry/grind
            // =============================================

            registry.Register(new GoodDef
            {
                Id = "spice_plants",
                Name = "Spice Plants",
                Category = GoodCategory.Raw,
                HarvestMethod = "farming",
                TerrainAffinity = new List<string> {
                    "Tropical seasonal forest",
                    "Tropical rainforest"
                },
                BaseYield = 3f,  // Low yield - spices are scarce
                DecayRate = 0.01f,  // 1% per day - fresh plants wilt
                TheftRisk = 0.2f,
                BasePrice = 2.0f
            });

            registry.Register(new GoodDef
            {
                Id = "spices",
                Name = "Spices",
                Category = GoodCategory.Finished,
                Inputs = new List<GoodInput> { new GoodInput("spice_plants", 2) },
                FacilityType = "spice_house",
                ProcessingTicks = 2,
                NeedCategory = NeedCategory.Luxury,
                BaseConsumption = 0.0003f,  // Very low - used sparingly
                DecayRate = 0.001f,  // 0.1% per day - dried spices keep well
                TheftRisk = 0.9f,    // Extremely high value-to-weight
                BasePrice = 25.0f    // Classic luxury trade good
            });

            // =============================================
            // CHAIN 8: Copperwork (Copper Ore → Copper → Cookware)
            // =============================================

            registry.Register(new GoodDef
            {
                Id = "copper_ore",
                Name = "Copper Ore",
                Category = GoodCategory.Raw,
                HarvestMethod = "mining",
                TerrainAffinity = new List<string> { "Highland", "Mountain" },
                BaseYield = 4f,  // Between iron (5) and gold (1)
                DecayRate = 0f,
                TheftRisk = 0.2f,
                BasePrice = 1.0f
            });

            registry.Register(new GoodDef
            {
                Id = "copper",
                Name = "Copper Ingots",
                Category = GoodCategory.Refined,
                Inputs = new List<GoodInput> { new GoodInput("copper_ore", 3) },
                FacilityType = "copper_smelter",
                ProcessingTicks = 2,
                DecayRate = 0f,
                TheftRisk = 0.4f,
                BasePrice = 5.0f  // 3 ore (3.0) + smelting
            });

            registry.Register(new GoodDef
            {
                Id = "cookware",
                Name = "Cookware",
                Category = GoodCategory.Finished,
                Inputs = new List<GoodInput> { new GoodInput("copper", 1) },
                FacilityType = "coppersmith",
                ProcessingTicks = 2,
                NeedCategory = NeedCategory.Comfort,
                BaseConsumption = 0.001f,  // Durable, similar to tools
                DecayRate = 0f,
                TheftRisk = 0.6f,
                BasePrice = 12.0f
            });

            // =============================================
            // CHAIN 9: Sugar (Sugarcane → Cane Juice → Sugar)
            // =============================================

            registry.Register(new GoodDef
            {
                Id = "sugarcane",
                Name = "Sugarcane",
                Category = GoodCategory.Raw,
                HarvestMethod = "farming",
                TerrainAffinity = new List<string> {
                    "Tropical seasonal forest",
                    "Tropical rainforest",
                    "Savanna"
                },
                BaseYield = 8f,
                DecayRate = 0.02f,  // 2% per day - cut cane dries out fast
                TheftRisk = 0.1f,   // Bulky, low value
                BasePrice = 1.0f
            });

            registry.Register(new GoodDef
            {
                Id = "cane_juice",
                Name = "Cane Juice",
                Category = GoodCategory.Refined,
                Inputs = new List<GoodInput> { new GoodInput("sugarcane", 3) },
                FacilityType = "sugar_press",
                ProcessingTicks = 1,
                // Pure intermediate — must be refined into sugar before use
                DecayRate = 0.06f,  // 6% per day - ferments quickly
                TheftRisk = 0.1f,
                BasePrice = 4.0f  // 3 sugarcane (3.0) + pressing
            });

            registry.Register(new GoodDef
            {
                Id = "sugar",
                Name = "Sugar",
                Category = GoodCategory.Finished,
                Inputs = new List<GoodInput> { new GoodInput("cane_juice", 2) },
                FacilityType = "sugar_refinery",
                ProcessingTicks = 2,
                NeedCategory = NeedCategory.Luxury,
                BaseConsumption = 0.0005f,
                DecayRate = 0.002f,  // 0.2% per day - crystallized, stores well
                TheftRisk = 0.7f,    // High value, portable
                BasePrice = 20.0f    // Luxury, long processing chain
            });

            // =============================================
            // CHAIN 11: Salt (Salt → consumed directly)
            // Single-tier: extracted from northern brine and coastal brine works
            // Universal demand, geographically constrained — drives long-distance trade
            // =============================================

            registry.Register(new GoodDef
            {
                Id = "raw_salt",
                Name = "Raw Salt",
                Category = GoodCategory.Raw,
                HarvestMethod = "brine_evaporation",
                TerrainAffinity = new List<string> { "Taiga", "Tundra", "Coastal Marsh" },
                BaseYield = 6f,
                DecayRate = 0f,  // Mineral, doesn't decay
                TheftRisk = 0.2f,  // Bulk, unprocessed
                BasePrice = 0.02f  // Keep raw below refined salt baseline (0.025 Crowns/kg)
            });

            registry.Register(new GoodDef
            {
                Id = "salt",
                Name = "Salt",
                Category = GoodCategory.Finished,
                Inputs = new List<GoodInput> { new GoodInput("raw_salt", 1.1f) }, // ~91% conversion yield
                FacilityType = "salt_warehouse",
                ProcessingTicks = 1,
                NeedCategory = NeedCategory.Basic,
                BaseConsumption = 15f / 365f,  // 15 kg per person per year
                DecayRate = 0f,  // Processed mineral, doesn't decay
                TheftRisk = 0.4f,  // Moderate value, portable
                BasePrice = 0.025f  // 0.025 Crowns/kg baseline from research
            });

            // =============================================
            // CHAIN 12: Beer (Barley → Malt → Beer)
            // Competes with barley_mill for barley supply
            // =============================================

            registry.Register(new GoodDef
            {
                Id = "malt",
                Name = "Malt",
                Category = GoodCategory.Refined,
                Inputs = new List<GoodInput> { new GoodInput("barley", 1f / 0.8f) }, // 80% conversion yield
                FacilityType = "malt_house",
                ProcessingTicks = 1,
                DecayRate = 0.004f,  // ~0.4% per day - ~9 month storage horizon
                TheftRisk = 0.2f,
                BasePrice = 0.02f  // Baseline tuned for barley at 0.015 Crowns/kg
            });

            registry.Register(new GoodDef
            {
                Id = "beer",
                Name = "Beer",
                Category = GoodCategory.Finished,
                Inputs = new List<GoodInput> { new GoodInput("malt", 1f / 3f) }, // 1 kg malt -> 3 kg beer (volume proxy)
                FacilityType = "brewery",
                ProcessingTicks = 2,
                NeedCategory = NeedCategory.Comfort,
                BaseConsumption = 1.5f,  // 1.5 kg/person/day proxy for historical small-beer intake
                DecayRate = 0.06f,  // 6% per day - beer quality collapses over ~2 weeks
                TheftRisk = 0.3f,  // Bulky liquid, moderate value
                BasePrice = 0.04f  // 0.04 Crowns/kg historical proxy pricing
            });

            // =============================================
            // CHAIN 13: Dyed Clothes (Dye Plants → Dye) + Cloth → Dyed Clothes
            // First multi-input recipe: requires tropical dye and textile-region cloth
            // Creates cross-regional trade dependency
            // =============================================

            registry.Register(new GoodDef
            {
                Id = "dye_plants",
                Name = "Dye Plants",
                Category = GoodCategory.Raw,
                HarvestMethod = "farming",
                TerrainAffinity = new List<string> {
                    "Tropical seasonal forest",
                    "Tropical rainforest",
                    "Savanna"
                },
                BaseYield = 4f,  // Moderate yield
                DecayRate = 0.02f,  // 2% per day - fresh plants wilt quickly
                TheftRisk = 0.1f,
                BasePrice = 1.0f
            });

            registry.Register(new GoodDef
            {
                Id = "dye",
                Name = "Dye",
                Category = GoodCategory.Refined,
                Inputs = new List<GoodInput> { new GoodInput("dye_plants", 3) },
                FacilityType = "dye_works",
                ProcessingTicks = 2,
                DecayRate = 0.002f,  // 0.2% per day - prepared dye keeps well
                TheftRisk = 0.6f,  // High value-to-weight
                BasePrice = 5.0f  // 3 dye plants (3.0) + extraction/preparation
            });

            registry.Register(new GoodDef
            {
                Id = "dyed_clothes",
                Name = "Dyed Clothes",
                Category = GoodCategory.Finished,
                Inputs = new List<GoodInput>
                {
                    new GoodInput("cloth", 1),
                    new GoodInput("dye", 1)
                },
                FacilityType = "dyer",
                ProcessingTicks = 2,
                NeedCategory = NeedCategory.Luxury,
                BaseConsumption = 0.001f,  // Luxury clothing, lower consumption than basic clothes
                DecayRate = 0.001f,  // 0.1% per day - fine garments, handled with care
                TheftRisk = 0.7f,  // High value, portable
                BasePrice = 20.0f  // 1 cloth (8.0) + 1 dye (5.0) + skilled dyeing
            });

        }

        /// <summary>
        /// Apply V2 economy overrides to good definitions.
        /// Called once during initialization when Economy V2 is enabled.
        /// </summary>
        public static void ApplyV2GoodOverrides(GoodRegistry registry)
        {
            // Keep only category overrides in V2.
            // Consumption-rate overrides caused demand to scale unrealistically.
            SetNeedCategory(registry, "clothes", NeedCategory.Comfort);
        }

        private static void SetNeedCategory(
            GoodRegistry registry,
            string goodId,
            NeedCategory needCategory)
        {
            var good = registry.Get(goodId);
            if (good == null)
                return;

            good.NeedCategory = needCategory;
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
                LaborRequired = 8,
                LaborType = LaborType.Unskilled,
                BaseThroughput = 140000f / 365f, // 100x baseline: 140,000 kg/year at full staffing
                IsExtraction = true,
                TerrainRequirements = new List<string> { "Grassland", "Savanna" }
            });

            registry.Register(new FacilityDef
            {
                Id = "rye_farm",
                Name = "Rye Farm",
                OutputGoodId = "rye",
                LaborRequired = 8,
                LaborType = LaborType.Unskilled,
                BaseThroughput = 150000f / 365f, // 100x baseline: 150,000 kg/year at full staffing
                IsExtraction = true,
                TerrainRequirements = new List<string> { "Steppe", "Taiga" }
            });

            registry.Register(new FacilityDef
            {
                Id = "barley_farm",
                Name = "Barley Farm",
                OutputGoodId = "barley",
                LaborRequired = 8,
                LaborType = LaborType.Unskilled,
                BaseThroughput = 160000f / 365f, // 100x baseline: 160,000 kg/year at full staffing
                IsExtraction = true,
                TerrainRequirements = new List<string> { "Highland", "Steppe" }
            });

            registry.Register(new FacilityDef
            {
                Id = "rice_paddy",
                Name = "Rice Paddy",
                OutputGoodId = "rice_grain",
                LaborRequired = 25,
                LaborType = LaborType.Unskilled,
                BaseThroughput = 2000f, // 100x baseline daily throughput
                IsExtraction = true,
                TerrainRequirements = new List<string> { "Tropical seasonal forest", "Tropical rainforest" }
            });

            registry.Register(new FacilityDef
            {
                Id = "mine",
                Name = "Iron Mine",
                OutputGoodId = "iron_ore",
                LaborRequired = 30,
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
                LaborRequired = 35,
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
                LaborRequired = 12,
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

            registry.Register(new FacilityDef
            {
                Id = "spice_farm",
                Name = "Spice Farm",
                OutputGoodId = "spice_plants",
                LaborRequired = 15,
                LaborType = LaborType.Unskilled,
                BaseThroughput = 3f,
                IsExtraction = true,
                TerrainRequirements = new List<string> {
                    "Tropical seasonal forest",
                    "Tropical rainforest"
                }
            });

            registry.Register(new FacilityDef
            {
                Id = "sugar_plantation",
                Name = "Sugar Plantation",
                OutputGoodId = "sugarcane",
                LaborRequired = 25,
                LaborType = LaborType.Unskilled,
                BaseThroughput = 8f,
                IsExtraction = true,
                TerrainRequirements = new List<string> {
                    "Tropical seasonal forest",
                    "Tropical rainforest",
                    "Savanna"
                }
            });

            registry.Register(new FacilityDef
            {
                Id = "copper_mine",
                Name = "Copper Mine",
                OutputGoodId = "copper_ore",
                LaborRequired = 25,
                LaborType = LaborType.Unskilled,
                BaseThroughput = 4f,
                IsExtraction = true,
                TerrainRequirements = new List<string> { "Highland", "Mountain" }
            });

            registry.Register(new FacilityDef
            {
                Id = "hide_farm",
                Name = "Hide Farm",
                OutputGoodId = "hides",
                LaborRequired = 10,
                LaborType = LaborType.Unskilled,
                BaseThroughput = 4f,
                IsExtraction = true,
                TerrainRequirements = new List<string> { "Grassland", "Steppe", "Highland" }
            });

            registry.Register(new FacilityDef
            {
                Id = "ranch",
                Name = "Sheep Ranch",
                OutputGoodId = "sheep",
                LaborRequired = 12,
                LaborType = LaborType.Unskilled,
                BaseThroughput = 6f,
                IsExtraction = true,
                TerrainRequirements = new List<string> { "Grassland", "Steppe", "Highland" }
            });

            registry.Register(new FacilityDef
            {
                Id = "dye_farm",
                Name = "Dye Farm",
                OutputGoodId = "dye_plants",
                LaborRequired = 15,
                LaborType = LaborType.Unskilled,
                BaseThroughput = 4f,
                IsExtraction = true,
                TerrainRequirements = new List<string> {
                    "Tropical seasonal forest",
                    "Tropical rainforest",
                    "Savanna"
                }
            });

            registry.Register(new FacilityDef
            {
                Id = "salt_works",
                Name = "Brine Salt Works",
                OutputGoodId = "raw_salt",
                LaborRequired = 15,
                LaborType = LaborType.Unskilled,
                BaseThroughput = 6f,
                IsExtraction = true,
                TerrainRequirements = new List<string> { "Taiga", "Tundra", "Coastal Marsh" }
            });

            // =============================================
            // Processing facilities (no terrain requirement)
            // =============================================

            registry.Register(new FacilityDef
            {
                Id = "mill",
                Name = "Mill",
                OutputGoodId = "flour",
                LaborRequired = 1,
                LaborType = LaborType.Skilled,
                BaseThroughput = 20000f / 365f, // 20,000 kg/year at full staffing
                IsExtraction = false
            });

            registry.Register(new FacilityDef
            {
                Id = "rye_mill",
                Name = "Rye Mill",
                OutputGoodId = "flour",
                LaborRequired = 1,
                LaborType = LaborType.Skilled,
                BaseThroughput = 20000f / 365f, // 20,000 kg/year at full staffing
                IsExtraction = false,
                InputOverrides = new List<GoodInput> { new GoodInput("rye", 1f / 0.75f) } // 75% yield
            });

            registry.Register(new FacilityDef
            {
                Id = "barley_mill",
                Name = "Barley Mill",
                OutputGoodId = "flour",
                LaborRequired = 1,
                LaborType = LaborType.Skilled,
                BaseThroughput = 20000f / 365f, // 20,000 kg/year at full staffing
                IsExtraction = false,
                InputOverrides = new List<GoodInput> { new GoodInput("barley", 1f / 0.65f) } // 65% yield
            });

            registry.Register(new FacilityDef
            {
                Id = "rice_mill",
                Name = "Rice Mill",
                OutputGoodId = "bread",
                LaborRequired = 3,
                LaborType = LaborType.Skilled,
                BaseThroughput = 10f,
                IsExtraction = false,
                InputOverrides = new List<GoodInput> { new GoodInput("rice_grain", 0.625f) }
            });

            registry.Register(new FacilityDef
            {
                Id = "bakery",
                Name = "Bakery",
                OutputGoodId = "bread",
                LaborRequired = 4,
                LaborType = LaborType.Skilled,
                BaseThroughput = 60000f / 365f, // 60,000 kg/year at full staffing
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
                Id = "spice_house",
                Name = "Spice House",
                OutputGoodId = "spices",
                LaborRequired = 2,
                LaborType = LaborType.Skilled,
                BaseThroughput = 2f,
                IsExtraction = false
            });

            registry.Register(new FacilityDef
            {
                Id = "sugar_press",
                Name = "Sugar Press",
                OutputGoodId = "cane_juice",
                LaborRequired = 4,
                LaborType = LaborType.Unskilled,
                BaseThroughput = 4f,
                IsExtraction = false
            });

            registry.Register(new FacilityDef
            {
                Id = "sugar_refinery",
                Name = "Sugar Refinery",
                OutputGoodId = "sugar",
                LaborRequired = 4,
                LaborType = LaborType.Skilled,
                BaseThroughput = 2f,
                IsExtraction = false
            });

            registry.Register(new FacilityDef
            {
                Id = "copper_smelter",
                Name = "Copper Smelter",
                OutputGoodId = "copper",
                LaborRequired = 5,
                LaborType = LaborType.Skilled,
                BaseThroughput = 3f,
                IsExtraction = false
            });

            registry.Register(new FacilityDef
            {
                Id = "coppersmith",
                Name = "Coppersmith",
                OutputGoodId = "cookware",
                LaborRequired = 3,
                LaborType = LaborType.Skilled,
                BaseThroughput = 2f,
                IsExtraction = false
            });

            registry.Register(new FacilityDef
            {
                Id = "tannery",
                Name = "Tannery",
                OutputGoodId = "leather",
                LaborRequired = 3,
                LaborType = LaborType.Skilled,
                BaseThroughput = 3f,
                IsExtraction = false
            });

            registry.Register(new FacilityDef
            {
                Id = "cobbler",
                Name = "Cobbler",
                OutputGoodId = "shoes",
                LaborRequired = 3,
                LaborType = LaborType.Skilled,
                BaseThroughput = 3f,
                IsExtraction = false
            });

            registry.Register(new FacilityDef
            {
                Id = "shearing_shed",
                Name = "Shearing Shed",
                OutputGoodId = "wool",
                LaborRequired = 2,
                LaborType = LaborType.Unskilled,
                BaseThroughput = 4f,
                IsExtraction = false
            });

            registry.Register(new FacilityDef
            {
                Id = "spinning_mill",
                Name = "Spinning Mill",
                OutputGoodId = "cloth",
                LaborRequired = 3,
                LaborType = LaborType.Skilled,
                BaseThroughput = 3f,
                IsExtraction = false
            });

            registry.Register(new FacilityDef
            {
                Id = "tailor",
                Name = "Tailor",
                OutputGoodId = "clothes",
                LaborRequired = 3,
                LaborType = LaborType.Skilled,
                BaseThroughput = 4f,
                IsExtraction = false
            });

            registry.Register(new FacilityDef
            {
                Id = "dye_works",
                Name = "Dye Works",
                OutputGoodId = "dye",
                LaborRequired = 3,
                LaborType = LaborType.Skilled,
                BaseThroughput = 3f,
                IsExtraction = false
            });

            registry.Register(new FacilityDef
            {
                Id = "dyer",
                Name = "Dyer",
                OutputGoodId = "dyed_clothes",
                LaborRequired = 4,
                LaborType = LaborType.Skilled,
                BaseThroughput = 2f,
                IsExtraction = false
            });

            registry.Register(new FacilityDef
            {
                Id = "salt_warehouse",
                Name = "Salt Warehouse",
                OutputGoodId = "salt",
                LaborRequired = 3,
                LaborType = LaborType.Unskilled,
                BaseThroughput = 4f,
                IsExtraction = false
            });

            registry.Register(new FacilityDef
            {
                Id = "malt_house",
                Name = "Malt House",
                OutputGoodId = "malt",
                LaborRequired = 4,
                LaborType = LaborType.Skilled,
                BaseThroughput = 25000f / 365f, // 25,000 kg/year at full staffing
                IsExtraction = false
            });

            registry.Register(new FacilityDef
            {
                Id = "brewery",
                Name = "Brewery",
                OutputGoodId = "beer",
                LaborRequired = 5,
                LaborType = LaborType.Skilled,
                BaseThroughput = 75000f / 365f, // 75,000 kg/year at full staffing
                IsExtraction = false
            });

            registry.Register(new FacilityDef
            {
                Id = "sawmill",
                Name = "Sawmill",
                OutputGoodId = "lumber",
                LaborRequired = 3,
                LaborType = LaborType.Unskilled,
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
