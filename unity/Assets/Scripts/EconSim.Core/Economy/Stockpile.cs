using System;
using System.Collections.Generic;

namespace EconSim.Core.Economy
{
    /// <summary>
    /// Inventory of goods. Used for county stockpiles, facility buffers, etc.
    /// </summary>
    [Serializable]
    public class Stockpile
    {
        private Dictionary<string, float> _goods = new Dictionary<string, float>();

        /// <summary>Get the quantity of a good (0 if not present).</summary>
        public float Get(string goodId)
        {
            return _goods.TryGetValue(goodId, out var qty) ? qty : 0f;
        }

        /// <summary>Set the quantity of a good directly.</summary>
        public void Set(string goodId, float quantity)
        {
            if (quantity <= 0f)
                _goods.Remove(goodId);
            else
                _goods[goodId] = quantity;
        }

        /// <summary>Add to the quantity of a good.</summary>
        public void Add(string goodId, float amount)
        {
            if (amount <= 0f) return;
            _goods[goodId] = Get(goodId) + amount;
        }

        /// <summary>
        /// Remove from the quantity of a good.
        /// Returns the amount actually removed (may be less if insufficient).
        /// </summary>
        public float Remove(string goodId, float amount)
        {
            if (amount <= 0f) return 0f;

            float current = Get(goodId);
            float removed = Math.Min(current, amount);

            if (removed >= current)
                _goods.Remove(goodId);
            else
                _goods[goodId] = current - removed;

            return removed;
        }

        /// <summary>
        /// Try to remove an exact amount. Returns false if insufficient.
        /// </summary>
        public bool TryRemove(string goodId, float amount)
        {
            if (Get(goodId) < amount) return false;
            Remove(goodId, amount);
            return true;
        }

        /// <summary>Check if we have at least the specified amount.</summary>
        public bool Has(string goodId, float amount)
        {
            return Get(goodId) >= amount;
        }

        /// <summary>Check if we have all the required inputs.</summary>
        public bool HasAll(IEnumerable<GoodInput> inputs)
        {
            foreach (var input in inputs)
            {
                if (!Has(input.GoodId, input.Quantity))
                    return false;
            }
            return true;
        }

        /// <summary>Remove all the required inputs. Returns false if insufficient.</summary>
        public bool TryRemoveAll(IEnumerable<GoodInput> inputs)
        {
            if (!HasAll(inputs)) return false;

            foreach (var input in inputs)
            {
                Remove(input.GoodId, input.Quantity);
            }
            return true;
        }

        /// <summary>Get all goods with non-zero quantities.</summary>
        public IEnumerable<KeyValuePair<string, float>> All => _goods;

        /// <summary>Check if the stockpile is empty.</summary>
        public bool IsEmpty => _goods.Count == 0;

        /// <summary>Clear all goods.</summary>
        public void Clear() => _goods.Clear();

        /// <summary>Total number of distinct good types stored.</summary>
        public int Count => _goods.Count;
    }
}
