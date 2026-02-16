using System;
using System.Collections.Generic;

namespace EconSim.Core.Simulation
{
    /// <summary>
    /// Deterministic helper methods for stable seeded ordering.
    /// </summary>
    public static class DeterministicOrder
    {
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
    }
}
