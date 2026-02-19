namespace EconSim.Core.Economy
{
    public enum FacilityType
    {
        Kiln = 0,
        Sawmill = 1,
        Smelter = 2,
        Smithy = 3,
    }

    /// <summary>
    /// Static definition for a facility type. Describes the production recipe,
    /// labor requirements, and placement rules.
    /// </summary>
    public readonly struct FacilityDef
    {
        public readonly FacilityType Type;
        public readonly string Name;

        // Recipe: input good + amount → output good + amount (per labor-day)
        public readonly GoodType InputGood;
        public readonly float InputAmount;
        public readonly GoodType OutputGood;
        public readonly float OutputAmount;

        /// <summary>Workers needed to produce OutputAmount per day at full capacity.</summary>
        public readonly int LaborPerUnit;

        /// <summary>Good whose biome productivity determines placement. Defaults to InputGood.</summary>
        public readonly GoodType PlacementGood;

        /// <summary>Minimum biome productivity of PlacementGood required for placement.</summary>
        public readonly float PlacementMinProductivity;

        /// <summary>Max fraction of county pop that can work in this industry.</summary>
        public readonly float MaxLaborFraction;

        /// <summary>Minimum daily output (cold start / idle production).</summary>
        public readonly float BaselineOutput;

        public FacilityDef(
            FacilityType type, string name,
            GoodType inputGood, float inputAmount,
            GoodType outputGood, float outputAmount,
            int laborPerUnit,
            float placementMinProductivity,
            float maxLaborFraction,
            float baselineOutput,
            GoodType? placementGood = null)
        {
            Type = type;
            Name = name;
            InputGood = inputGood;
            InputAmount = inputAmount;
            OutputGood = outputGood;
            OutputAmount = outputAmount;
            LaborPerUnit = laborPerUnit;
            PlacementGood = placementGood ?? inputGood;
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
            //                         Type              Name      Input            InAmt  Output            OutAmt Labor  MinProd MaxLabor Baseline [PlacementGood]
            Defs = new[]
            {
                new FacilityDef(FacilityType.Kiln,     "kiln",    GoodType.Clay,    2.0f, GoodType.Pottery, 1.0f, 3, 0.05f, 0.05f, 1.0f),
                new FacilityDef(FacilityType.Sawmill,  "sawmill", GoodType.Timber,  3.0f, GoodType.Lumber,  2.0f, 1, 0.2f,  0.10f, 5.0f),
                new FacilityDef(FacilityType.Smelter,  "smelter", GoodType.IronOre, 3.0f, GoodType.Iron,    2.0f, 1, 0.0f,  0.05f, 1.0f),
                new FacilityDef(FacilityType.Smithy,   "smithy",  GoodType.Iron,    2.0f, GoodType.Tools,   1.0f, 1, 0.0f,  0.05f, 1.0f, GoodType.IronOre),
            };

            Count = Defs.Length;
        }
    }
}
