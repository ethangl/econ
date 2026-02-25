namespace EconSim.Core.Economy
{
    public enum FacilityType
    {
        Kiln = 0,
        Carpenter = 1,
        Smelter = 2,
        Smithy = 3,
        CharcoalBurner = 4,
        Weaver = 5,
        Butcher = 6,
        Smokehouse = 7,
        Cheesemaker = 8,
        Salter = 9,
        DryingRack = 10,
        Bakery = 11,
        Brewery = 12,
        GoldJeweler = 13,
        SilverJeweler = 14,
        Winery = 15,
    }

    public readonly struct RecipeInput
    {
        public readonly GoodType Good;
        public readonly float Amount;
        public RecipeInput(GoodType good, float amount) { Good = good; Amount = amount; }
    }

    /// <summary>
    /// Static definition for a facility type. Describes the production recipe
    /// and labor requirements.
    /// </summary>
    public readonly struct FacilityDef
    {
        public readonly FacilityType Type;
        public readonly string Name;

        // Recipe: input goods + amounts → output good + amount (per labor-day)
        public readonly RecipeInput[] Inputs;
        public readonly GoodType OutputGood;
        public readonly float OutputAmount;

        /// <summary>Workers needed to produce OutputAmount per day at full capacity.</summary>
        public readonly int LaborPerUnit;

        /// <summary>Max fraction of county pop that can work in this industry.</summary>
        public readonly float MaxLaborFraction;

        /// <summary>Minimum daily output (cold start / idle production).</summary>
        public readonly float BaselineOutput;

        public FacilityDef(
            FacilityType type, string name,
            RecipeInput[] inputs,
            GoodType outputGood, float outputAmount,
            int laborPerUnit,
            float maxLaborFraction,
            float baselineOutput)
        {
            Type = type;
            Name = name;
            Inputs = inputs;
            OutputGood = outputGood;
            OutputAmount = outputAmount;
            LaborPerUnit = laborPerUnit;
            MaxLaborFraction = maxLaborFraction;
            BaselineOutput = baselineOutput;
        }
    }

    public static class Facilities
    {
        public static readonly FacilityDef[] Defs;
        public static readonly int Count;

        static Facilities()
        {
            Defs = new[]
            {
                new FacilityDef(FacilityType.Kiln,    "kiln",
                    new[] { new RecipeInput(GoodType.Clay, 2.0f) },
                    GoodType.Pottery, 1.0f, 3, 0.05f, 1.0f),
                new FacilityDef(FacilityType.Carpenter, "carpenter",
                    new[] { new RecipeInput(GoodType.Timber, 15.0f) },
                    GoodType.Furniture, 1.0f, 1, 0.10f, 0.5f),
                new FacilityDef(FacilityType.Smelter, "smelter",
                    new[] { new RecipeInput(GoodType.IronOre, 3.0f), new RecipeInput(GoodType.Charcoal, 0.4f) },
                    GoodType.Iron, 2.0f, 1, 0.05f, 1.0f),
                new FacilityDef(FacilityType.Smithy,  "smithy",
                    new[] { new RecipeInput(GoodType.Iron, 5.0f), new RecipeInput(GoodType.Charcoal, 0.5f) },
                    GoodType.Tools, 1.0f, 1, 0.05f, 0.25f),
                new FacilityDef(FacilityType.CharcoalBurner, "charcoalBurner",
                    new[] { new RecipeInput(GoodType.Timber, 5.0f) },
                    GoodType.Charcoal, 2.0f, 1, 0.10f, 2.0f),
                new FacilityDef(FacilityType.Weaver, "weaver",
                    new[] { new RecipeInput(GoodType.Wool, 4.0f) },
                    GoodType.Clothes, 1.0f, 2, 0.10f, 0.667f),
                new FacilityDef(FacilityType.Butcher, "butcher",
                    new[] { new RecipeInput(GoodType.Pork, 1.0f), new RecipeInput(GoodType.Salt, 0.2f) },
                    GoodType.Sausage, 3.0f, 1, 0.15f, 3.0f),
                new FacilityDef(FacilityType.Smokehouse, "smokehouse",
                    new[] { new RecipeInput(GoodType.Pork, 2.0f) },
                    GoodType.Bacon, 3.0f, 1, 0.15f, 3.0f),
                new FacilityDef(FacilityType.Cheesemaker, "cheesemaker",
                    new[] { new RecipeInput(GoodType.Milk, 3.0f), new RecipeInput(GoodType.Salt, 0.3f) },
                    GoodType.Cheese, 1.5f, 1, 0.15f, 1.5f),
                new FacilityDef(FacilityType.Salter, "salter",
                    new[] { new RecipeInput(GoodType.Fish, 1.0f), new RecipeInput(GoodType.Salt, 0.5f) },
                    GoodType.SaltedFish, 3.0f, 1, 0.15f, 3.0f),
                new FacilityDef(FacilityType.DryingRack, "dryingRack",
                    new[] { new RecipeInput(GoodType.Fish, 2.0f) },
                    GoodType.Stockfish, 1.5f, 1, 0.10f, 1.5f),
                new FacilityDef(FacilityType.Bakery, "bakery",
                    new[] { new RecipeInput(GoodType.Wheat, 2.0f), new RecipeInput(GoodType.Salt, 0.03f) },
                    GoodType.Bread, 2.8f, 1, 0.15f, 2.8f),
                new FacilityDef(FacilityType.Brewery, "brewery",
                    new[] { new RecipeInput(GoodType.Barley, 2.0f) },
                    GoodType.Ale, 4.0f, 1, 0.15f, 4.0f),
                new FacilityDef(FacilityType.GoldJeweler, "goldJeweler",
                    new[] { new RecipeInput(GoodType.Gold, 0.01f) },
                    GoodType.GoldJewelry, 1.0f, 1, 0.02f, 0.1f),
                new FacilityDef(FacilityType.SilverJeweler, "silverJeweler",
                    new[] { new RecipeInput(GoodType.Silver, 0.05f) },
                    GoodType.SilverJewelry, 1.0f, 1, 0.02f, 0.1f),
                new FacilityDef(FacilityType.Winery, "winery",
                    new[] { new RecipeInput(GoodType.Grapes, 2.0f) },
                    GoodType.Wine, 1.5f, 1, 0.10f, 1.5f),
            };

            Count = Defs.Length;
        }
    }
}
