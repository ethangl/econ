namespace EconSim.Core.Economy.V4
{
    public enum FacilityTypeV4
    {
        Bakery = 0,
        Brewery = 1,
        Smithy = 2,
        Weaver = 3,
        Carpenter = 4,
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
            };

            Count = Defs.Length;
        }
    }
}
