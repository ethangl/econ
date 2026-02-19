namespace EconSim.Core.Economy
{
    public enum GoodType
    {
        Food = 0,
        Timber = 1,
        IronOre = 2,
        GoldOre = 3,
        Salt = 4,
        Wool = 5,
    }

    public static class Goods
    {
        public const int Count = 6;

        /// <summary>Daily consumption per person (kg/day), indexed by GoodType.</summary>
        public static readonly float[] ConsumptionPerPop =
        {
            1.0f,   // Food (staple)
            0.2f,   // Timber
            0.01f,  // IronOre
            0.0f,   // GoldOre (not consumed, minted)
            0.05f,  // Salt
            0.1f,   // Wool
        };

        /// <summary>Serialization names, indexed by GoodType.</summary>
        public static readonly string[] Names =
        {
            "food", "timber", "ironOre", "goldOre", "salt", "wool",
        };
    }
}
