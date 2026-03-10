namespace EconSim.Core.Economy.V4
{
    public enum FacilityTypeV4
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

    public readonly struct FacilityInputV4
    {
        public readonly GoodTypeV4 Good;
        public readonly float Ratio;

        public FacilityInputV4(GoodTypeV4 good, float ratio)
        {
            Good = good;
            Ratio = ratio;
        }
    }

    public readonly struct FacilityDefV4
    {
        public readonly FacilityTypeV4 Type;
        public readonly string Name;
        public readonly FacilityInputV4[] Inputs;
        public readonly GoodTypeV4 Output;

        /// <summary>Output units per upper commoner per day at full capacity.</summary>
        public readonly float ThroughputPerCapita;

        public FacilityDefV4(FacilityTypeV4 type, string name, FacilityInputV4[] inputs,
            GoodTypeV4 output, float throughputPerCapita)
        {
            Type = type;
            Name = name;
            Inputs = inputs;
            Output = output;
            ThroughputPerCapita = throughputPerCapita;
        }
    }

    public static class FacilitiesV4
    {
        public static readonly FacilityDefV4[] Defs;
        public static readonly int Count;

        static FacilitiesV4()
        {
            Defs = new[]
            {
                // ── Comfort facilities ──
                new FacilityDefV4(FacilityTypeV4.Bakery, "bakery",
                    new[] { new FacilityInputV4(GoodTypeV4.Wheat, 2.0f) },
                    GoodTypeV4.Bread, 0.10f),

                new FacilityDefV4(FacilityTypeV4.Brewery, "brewery",
                    new[] { new FacilityInputV4(GoodTypeV4.Barley, 2.0f) },
                    GoodTypeV4.Ale, 0.10f),

                new FacilityDefV4(FacilityTypeV4.Smithy, "smithy",
                    new[] {
                        new FacilityInputV4(GoodTypeV4.Iron, 1.0f),
                        new FacilityInputV4(GoodTypeV4.Timber, 0.5f),
                    },
                    GoodTypeV4.Tools, 0.05f),

                new FacilityDefV4(FacilityTypeV4.Weaver, "weaver",
                    new[] { new FacilityInputV4(GoodTypeV4.Wool, 2.0f) },
                    GoodTypeV4.Clothes, 0.08f),

                new FacilityDefV4(FacilityTypeV4.Carpenter, "carpenter",
                    new[] { new FacilityInputV4(GoodTypeV4.Timber, 3.0f) },
                    GoodTypeV4.Furniture, 0.04f),

                // ── Luxury facilities ──
                new FacilityDefV4(FacilityTypeV4.Kitchen, "kitchen",
                    new[] {
                        new FacilityInputV4(GoodTypeV4.Spices, 0.5f),
                        new FacilityInputV4(GoodTypeV4.Meat, 2.0f),
                    },
                    GoodTypeV4.Feast, 0.06f),

                new FacilityDefV4(FacilityTypeV4.Tailor, "tailor",
                    new[] { new FacilityInputV4(GoodTypeV4.Silk, 2.0f) },
                    GoodTypeV4.FineClothes, 0.05f),

                new FacilityDefV4(FacilityTypeV4.Jeweler, "jeweler",
                    new[] { new FacilityInputV4(GoodTypeV4.Gold, 1.0f) },
                    GoodTypeV4.Jewelry, 0.02f),

                new FacilityDefV4(FacilityTypeV4.FineCarpenter, "fine carpenter",
                    new[] {
                        new FacilityInputV4(GoodTypeV4.Timber, 3.0f),
                        new FacilityInputV4(GoodTypeV4.Silk, 0.5f),
                    },
                    GoodTypeV4.FineFurniture, 0.03f),

                new FacilityDefV4(FacilityTypeV4.Vintner, "vintner",
                    new[] { new FacilityInputV4(GoodTypeV4.Grapes, 3.0f) },
                    GoodTypeV4.Wine, 0.08f),
            };

            Count = Defs.Length;
        }
    }
}
