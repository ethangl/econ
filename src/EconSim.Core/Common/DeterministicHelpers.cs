using System;
using System.Collections.Generic;

namespace EconSim.Core.Common
{
    /// <summary>
    /// Deterministic utility helpers for seeded simulation behavior.
    /// </summary>
    public static class DeterministicHelpers
    {
        /// <summary>
        /// Fisher-Yates shuffle (in place) using an explicit RNG source.
        /// </summary>
        public static void ShuffleInPlace<T>(IList<T> items, Random rng)
        {
            if (items == null || rng == null)
                return;

            for (int i = items.Count - 1; i > 0; i--)
            {
                int swapIndex = rng.Next(i + 1);
                (items[i], items[swapIndex]) = (items[swapIndex], items[i]);
            }
        }

        /// <summary>
        /// Returns a float in [0, 1) from the provided RNG source.
        /// </summary>
        public static float NextFloat(Random rng)
        {
            if (rng == null)
                return 0f;

            return (float)rng.NextDouble();
        }

        /// <summary>
        /// Returns a float in [min, max) from the provided RNG source.
        /// </summary>
        public static float NextFloat(Random rng, float min, float max)
        {
            if (rng == null)
                return min;

            if (max <= min)
                return min;

            return min + (float)rng.NextDouble() * (max - min);
        }

        /// <summary>
        /// Wraps a comparison with a deterministic tie-break based on a stable key selector.
        /// </summary>
        public static Comparison<T> WithStableTieBreak<T, TKey>(
            Comparison<T> primary,
            Func<T, TKey> tieBreakSelector,
            IComparer<TKey> tieBreakComparer = null)
        {
            if (primary == null)
                throw new ArgumentNullException(nameof(primary));
            if (tieBreakSelector == null)
                throw new ArgumentNullException(nameof(tieBreakSelector));

            tieBreakComparer ??= Comparer<TKey>.Default;

            return (a, b) =>
            {
                int result = primary(a, b);
                if (result != 0)
                    return result;

                return tieBreakComparer.Compare(tieBreakSelector(a), tieBreakSelector(b));
            };
        }
    }
}
