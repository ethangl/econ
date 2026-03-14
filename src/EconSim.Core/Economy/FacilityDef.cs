namespace EconSim.Core.Economy
{
    public enum FacilityType
    {
        // Comfort facilities
        Bakery = 0,
        Brewery = 1,
        Smithy = 2,
        Weaver = 3,
        Carpenter = 4,

        // Luxury facilities
        Kitchen = 5,
        Tailor = 6,
        Jeweler = 7,
        FineCarpenter = 8,
        Vintner = 9,
    }

    public readonly struct FacilityInput
    {
        public readonly GoodType Good;
        public readonly float Ratio;

        public FacilityInput(GoodType good, float ratio)
        {
            Good = good;
            Ratio = ratio;
        }
    }

    public readonly struct FacilityDef
    {
        public readonly FacilityType Type;
        public readonly string Name;
        public readonly FacilityInput[] Inputs;
        public readonly GoodType Output;

        /// <summary>Output units per upper commoner per day at full capacity.</summary>
        public readonly float ThroughputPerCapita;

        public FacilityDef(FacilityType type, string name, FacilityInput[] inputs,
            GoodType output, float throughputPerCapita)
        {
            Type = type;
            Name = name;
            Inputs = inputs;
            Output = output;
            ThroughputPerCapita = throughputPerCapita;
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
                // ── Comfort facilities ──
                new FacilityDef(FacilityType.Bakery, "bakery",
                    new[] { new FacilityInput(GoodType.Wheat, 2.0f) },
                    GoodType.Bread, 0.10f),

                new FacilityDef(FacilityType.Brewery, "brewery",
                    new[] { new FacilityInput(GoodType.Barley, 2.0f) },
                    GoodType.Ale, 0.10f),

                new FacilityDef(FacilityType.Smithy, "smithy",
                    new[] {
                        new FacilityInput(GoodType.Iron, 1.0f),
                        new FacilityInput(GoodType.Timber, 0.5f),
                    },
                    GoodType.Tools, 0.05f),

                new FacilityDef(FacilityType.Weaver, "weaver",
                    new[] { new FacilityInput(GoodType.Wool, 2.0f) },
                    GoodType.Clothes, 0.08f),

                new FacilityDef(FacilityType.Carpenter, "carpenter",
                    new[] { new FacilityInput(GoodType.Timber, 3.0f) },
                    GoodType.Furniture, 0.04f),

                // ── Luxury facilities ──
                new FacilityDef(FacilityType.Kitchen, "kitchen",
                    new[] {
                        new FacilityInput(GoodType.Spices, 0.5f),
                        new FacilityInput(GoodType.Meat, 2.0f),
                    },
                    GoodType.Feast, 0.06f),

                new FacilityDef(FacilityType.Tailor, "tailor",
                    new[] { new FacilityInput(GoodType.Silk, 2.0f) },
                    GoodType.FineClothes, 0.05f),

                new FacilityDef(FacilityType.Jeweler, "jeweler",
                    new[] { new FacilityInput(GoodType.Gold, 1.0f) },
                    GoodType.Jewelry, 0.02f),

                new FacilityDef(FacilityType.FineCarpenter, "fine carpenter",
                    new[] {
                        new FacilityInput(GoodType.Timber, 3.0f),
                        new FacilityInput(GoodType.Silk, 0.5f),
                    },
                    GoodType.FineFurniture, 0.03f),

                new FacilityDef(FacilityType.Vintner, "vintner",
                    new[] { new FacilityInput(GoodType.Grapes, 3.0f) },
                    GoodType.Wine, 0.08f),
            };

            Count = Defs.Length;
        }
    }
}
