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
    }

    public readonly struct RecipeInput
    {
        public readonly GoodType Good;
        public readonly float Amount;
        public RecipeInput(GoodType good, float amount) { Good = good; Amount = amount; }
    }

    /// <summary>
    /// Static definition for a facility type. Describes the production recipe,
    /// labor requirements, and placement rules.
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

        /// <summary>Good whose biome productivity determines placement. Defaults to Inputs[0].Good.</summary>
        public readonly GoodType PlacementGood;

        /// <summary>Minimum biome productivity of PlacementGood required for placement.</summary>
        public readonly float PlacementMinProductivity;

        /// <summary>Max fraction of county pop that can work in this industry.</summary>
        public readonly float MaxLaborFraction;

        /// <summary>Minimum daily output (cold start / idle production).</summary>
        public readonly float BaselineOutput;

        public FacilityDef(
            FacilityType type, string name,
            RecipeInput[] inputs,
            GoodType outputGood, float outputAmount,
            int laborPerUnit,
            float placementMinProductivity,
            float maxLaborFraction,
            float baselineOutput,
            GoodType? placementGood = null)
        {
            Type = type;
            Name = name;
            Inputs = inputs;
            OutputGood = outputGood;
            OutputAmount = outputAmount;
            LaborPerUnit = laborPerUnit;
            PlacementGood = placementGood ?? inputs[0].Good;
            PlacementMinProductivity = placementMinProductivity;
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
                    new[] { new RecipeInput(GoodType.Clay, 2.0f), new RecipeInput(GoodType.Timber, 0.5f) },
                    GoodType.Pottery, 1.0f, 3, 0.05f, 0.05f, 1.0f),
                new FacilityDef(FacilityType.Carpenter, "carpenter",
                    new[] { new RecipeInput(GoodType.Timber, 3.0f) },
                    GoodType.Furniture, 2.0f, 1, 0.2f, 0.10f, 1.0f),
                new FacilityDef(FacilityType.Smelter, "smelter",
                    new[] { new RecipeInput(GoodType.IronOre, 3.0f), new RecipeInput(GoodType.Charcoal, 0.4f) },
                    GoodType.Iron, 2.0f, 1, 0.0f, 0.05f, 1.0f),
                new FacilityDef(FacilityType.Smithy,  "smithy",
                    new[] { new RecipeInput(GoodType.Iron, 2.0f), new RecipeInput(GoodType.Charcoal, 0.2f) },
                    GoodType.Tools, 1.0f, 1, 0.0f, 0.05f, 1.0f, GoodType.IronOre),
                new FacilityDef(FacilityType.CharcoalBurner, "charcoalBurner",
                    new[] { new RecipeInput(GoodType.Timber, 5.0f) },
                    GoodType.Charcoal, 1.0f, 1, 0.1f, 0.10f, 2.0f),
                new FacilityDef(FacilityType.Weaver, "weaver",
                    new[] { new RecipeInput(GoodType.Wool, 3.0f) },
                    GoodType.Clothes, 2.0f, 2, 0.05f, 0.10f, 2.0f),
            };

            Count = Defs.Length;
        }
    }
}
