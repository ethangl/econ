using System;
using System.Collections.Generic;
using EconSim.Core.Common;

namespace EconSim.Core.Simulation
{
    /// <summary>
    /// Deterministic helper methods for stable seeded ordering.
    /// </summary>
    public static class DeterministicOrder
    {
        public static void ShuffleInPlace<T>(IList<T> items, Random rng)
        {
            DeterministicHelpers.ShuffleInPlace(items, rng);
        }

        public static float NextFloat(Random rng)
        {
            return DeterministicHelpers.NextFloat(rng);
        }

        public static float NextFloat(Random rng, float min, float max)
        {
            return DeterministicHelpers.NextFloat(rng, min, max);
        }

        public static Comparison<T> WithStableTieBreak<T, TKey>(
            Comparison<T> primary,
            Func<T, TKey> tieBreakSelector,
            IComparer<TKey> tieBreakComparer = null)
        {
            return DeterministicHelpers.WithStableTieBreak(primary, tieBreakSelector, tieBreakComparer);
        }
    }
}
