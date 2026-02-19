namespace EconSim.Core.Economy
{
    public enum FacilityType
    {
        Kiln = 0,
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

        /// <summary>Minimum biome productivity of InputGood required for placement.</summary>
        public readonly float PlacementMinProductivity;

        public FacilityDef(
            FacilityType type, string name,
            GoodType inputGood, float inputAmount,
            GoodType outputGood, float outputAmount,
            int laborPerUnit,
            float placementMinProductivity)
        {
            Type = type;
            Name = name;
            InputGood = inputGood;
            InputAmount = inputAmount;
            OutputGood = outputGood;
            OutputAmount = outputAmount;
            LaborPerUnit = laborPerUnit;
            PlacementMinProductivity = placementMinProductivity;
        }
    }

    public static class Facilities
    {
        public static readonly FacilityDef[] Defs;
        public static readonly int Count;

        static Facilities()
        {
            //                         Type              Name    Input          InAmt  Output            OutAmt Labor  MinProd
            Defs = new[]
            {
                new FacilityDef(FacilityType.Kiln, "kiln", GoodType.Clay, 2.0f, GoodType.Pottery, 1.0f, 3, 0.05f),
            };

            Count = Defs.Length;
        }
    }
}
