namespace EconSim.Core.Economy
{
    public enum Estate
    {
        LowerCommoner = 0,
        UpperCommoner = 1,
        LowerNobility = 2,
        UpperNobility = 3,
        LowerClergy = 4,
        UpperClergy = 5,
    }

    public static class Estates
    {
        public const int Count = 6;
        const int NeedCategoryCount = 5; // None, Staple, Basic, Comfort, Luxury

        /// <summary>Population fraction per estate. Sum = 1.0.</summary>
        public static readonly float[] DefaultShare = new float[Count]
        {
            0.81f,  // LowerCommoner — peasants, villeins; extraction labor
            0.15f,  // UpperCommoner — freemen, burghers; facility labor
            0.018f, // LowerNobility
            0.002f, // UpperNobility
            0.018f, // LowerClergy
            0.002f, // UpperClergy
        };

        /// <summary>
        /// Per-estate demand multiplier indexed by [estate, needCategory].
        /// Multiplies effective population for consumption in that need category.
        /// </summary>
        static readonly float[,] NeedMultiplier = new float[Count, NeedCategoryCount]
        {
            // None  Staple Basic  Comfort Luxury
            { 1f,   1.0f,  1.0f,  0.5f,   0.0f }, // LowerCommoner
            { 1f,   1.0f,  1.0f,  2.0f,   0.5f }, // UpperCommoner
            { 1f,   1.0f,  1.0f,  3.0f,   1.0f }, // LowerNobility
            { 1f,   1.0f,  1.0f,  5.0f,   3.0f }, // UpperNobility
            { 1f,   1.0f,  1.0f,  1.0f,   0.0f }, // LowerClergy
            { 1f,   1.0f,  1.0f,  3.0f,   1.0f }, // UpperClergy
        };

        /// <summary>Fills estatePop from total population using default shares.</summary>
        public static void ComputeEstatePop(float totalPop, float[] estatePop)
        {
            for (int e = 0; e < Count; e++)
                estatePop[e] = totalPop * DefaultShare[e];
        }

        /// <summary>Computes effective population per NeedCategory from estate populations.</summary>
        public static void ComputeEffectivePop(float[] estatePop, float[] effPop)
        {
            for (int n = 0; n < NeedCategoryCount; n++)
            {
                float sum = 0f;
                for (int e = 0; e < Count; e++)
                    sum += estatePop[e] * NeedMultiplier[e, n];
                effPop[n] = sum;
            }
        }
    }
}
