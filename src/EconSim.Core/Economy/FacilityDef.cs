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
        Meadery = 16,
        Churn = 17,
        SpiceBlender = 18,
        AmberCarver = 19,
        SilkWeaver = 20,
        Tanner = 21,
        Cobbler = 22,
        LinenWeaver = 23,
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

        /// <summary>Max fraction of county pop that can work in this industry.</summary>
        public readonly float MaxLaborFraction;

        public FacilityDef(
            FacilityType type, string name,
            RecipeInput[] inputs,
            GoodType outputGood, float outputAmount,
            float maxLaborFraction)
        {
            Type = type;
            Name = name;
            Inputs = inputs;
            OutputGood = outputGood;
            OutputAmount = outputAmount;
            MaxLaborFraction = maxLaborFraction;
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
                    new[] { new RecipeInput(GoodType.Clay, 4.0f) },
                    GoodType.Pottery, 2.0f, 0.017f),
                new FacilityDef(FacilityType.Carpenter, "carpenter",
                    new[] { new RecipeInput(GoodType.Timber, 30.0f) },
                    GoodType.Furniture, 2.0f, 0.10f),
                new FacilityDef(FacilityType.Smelter, "smelter",
                    new[] { new RecipeInput(GoodType.IronOre, 6.0f), new RecipeInput(GoodType.Charcoal, 0.8f) },
                    GoodType.Iron, 4.0f, 0.05f),
                new FacilityDef(FacilityType.Smithy,  "smithy",
                    new[] { new RecipeInput(GoodType.Iron, 10.0f), new RecipeInput(GoodType.Charcoal, 1.0f) },
                    GoodType.Tools, 2.0f, 0.05f),
                new FacilityDef(FacilityType.CharcoalBurner, "charcoalBurner",
                    new[] { new RecipeInput(GoodType.Timber, 10.0f) },
                    GoodType.Charcoal, 4.0f, 0.10f),
                new FacilityDef(FacilityType.Weaver, "weaver",
                    new[] { new RecipeInput(GoodType.Wool, 8.0f) },
                    GoodType.WoolClothes, 2.0f, 0.05f),
                new FacilityDef(FacilityType.Butcher, "butcher",
                    new[] { new RecipeInput(GoodType.Pork, 2.0f), new RecipeInput(GoodType.Salt, 0.4f) },
                    GoodType.Sausage, 6.0f, 0.15f),
                new FacilityDef(FacilityType.Smokehouse, "smokehouse",
                    new[] { new RecipeInput(GoodType.Pork, 4.0f) },
                    GoodType.Bacon, 6.0f, 0.15f),
                new FacilityDef(FacilityType.Cheesemaker, "cheesemaker",
                    new[] { new RecipeInput(GoodType.Milk, 6.0f), new RecipeInput(GoodType.Salt, 0.6f) },
                    GoodType.Cheese, 3.0f, 0.15f),
                new FacilityDef(FacilityType.Salter, "salter",
                    new[] { new RecipeInput(GoodType.Fish, 2.0f), new RecipeInput(GoodType.Salt, 1.0f) },
                    GoodType.SaltedFish, 6.0f, 0.15f),
                new FacilityDef(FacilityType.DryingRack, "dryingRack",
                    new[] { new RecipeInput(GoodType.Fish, 4.0f) },
                    GoodType.Stockfish, 3.0f, 0.10f),
                new FacilityDef(FacilityType.Bakery, "bakery",
                    new[] { new RecipeInput(GoodType.Wheat, 8.0f), new RecipeInput(GoodType.Salt, 0.12f) },
                    GoodType.Bread, 11.2f, 0.15f),
                new FacilityDef(FacilityType.Brewery, "brewery",
                    new[] { new RecipeInput(GoodType.Barley, 4.0f) },
                    GoodType.Ale, 8.0f, 0.15f),
                new FacilityDef(FacilityType.GoldJeweler, "goldJeweler",
                    new[] { new RecipeInput(GoodType.Gold, 0.02f) },
                    GoodType.GoldJewelry, 2.0f, 0.02f),
                new FacilityDef(FacilityType.SilverJeweler, "silverJeweler",
                    new[] { new RecipeInput(GoodType.Silver, 0.1f) },
                    GoodType.SilverJewelry, 2.0f, 0.02f),
                new FacilityDef(FacilityType.Winery, "winery",
                    new[] { new RecipeInput(GoodType.Grapes, 4.0f) },
                    GoodType.Wine, 3.0f, 0.10f),
                new FacilityDef(FacilityType.Meadery, "meadery",
                    new[] { new RecipeInput(GoodType.Honey, 4.0f) },
                    GoodType.Mead, 4.0f, 0.10f),
                new FacilityDef(FacilityType.Churn, "churn",
                    new[] { new RecipeInput(GoodType.Milk, 6.0f) },
                    GoodType.Butter, 2.0f, 0.10f),
                new FacilityDef(FacilityType.SpiceBlender, "spiceBlender",
                    new[] { new RecipeInput(GoodType.Wine, 4.0f), new RecipeInput(GoodType.Spices, 0.2f) },
                    GoodType.SpicedWine, 4.0f, 0.05f),
                new FacilityDef(FacilityType.AmberCarver, "amberCarver",
                    new[] { new RecipeInput(GoodType.Amber, 1.0f) },
                    GoodType.AmberJewelry, 2.0f, 0.02f),
                new FacilityDef(FacilityType.SilkWeaver, "silkWeaver",
                    new[] { new RecipeInput(GoodType.Silk, 6.0f) },
                    GoodType.SilkClothes, 2.0f, 0.025f),
                new FacilityDef(FacilityType.Tanner, "tanner",
                    new[] { new RecipeInput(GoodType.Hides, 3.0f) },
                    GoodType.Leather, 3.0f, 0.15f),
                new FacilityDef(FacilityType.Cobbler, "cobbler",
                    new[] { new RecipeInput(GoodType.Leather, 4.0f) },
                    GoodType.Shoes, 4.0f, 0.12f),
                new FacilityDef(FacilityType.LinenWeaver, "linenWeaver",
                    new[] { new RecipeInput(GoodType.Flax, 8.0f) },
                    GoodType.LinenClothes, 2.0f, 0.05f),
            };

            Count = Defs.Length;
        }
    }
}
