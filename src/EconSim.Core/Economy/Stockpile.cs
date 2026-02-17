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
        private readonly Dictionary<int, float> _goodsByRuntimeId = new Dictionary<int, float>();
        private readonly Dictionary<string, float> _goodsByStringId = new Dictionary<string, float>();
        [NonSerialized] private GoodRegistry _goodsRegistry;

        /// <summary>
        /// Bind this stockpile to the active good registry so string operations can use dense runtime IDs.
        /// Safe to call multiple times.
        /// </summary>
        public void BindGoods(GoodRegistry goodsRegistry)
        {
            _goodsRegistry = goodsRegistry;

            if (_goodsRegistry == null || _goodsByStringId.Count == 0)
                return;

            var migrated = new List<string>();
            foreach (var kvp in _goodsByStringId)
            {
                if (!TryResolveRuntimeId(kvp.Key, out int runtimeId))
                    continue;

                Add(runtimeId, kvp.Value);
                migrated.Add(kvp.Key);
            }

            for (int i = 0; i < migrated.Count; i++)
                _goodsByStringId.Remove(migrated[i]);
        }

        /// <summary>Get the quantity of a good (0 if not present).</summary>
        public float Get(string goodId)
        {
            if (TryResolveRuntimeId(goodId, out int runtimeId))
                return Get(runtimeId);

            return _goodsByStringId.TryGetValue(goodId, out var qty) ? qty : 0f;
        }

        /// <summary>Get the quantity of a good by dense runtime ID (0 if not present).</summary>
        public float Get(int runtimeId)
        {
            return _goodsByRuntimeId.TryGetValue(runtimeId, out var qty) ? qty : 0f;
        }

        /// <summary>Set the quantity of a good directly.</summary>
        public void Set(string goodId, float quantity)
        {
            if (TryResolveRuntimeId(goodId, out int runtimeId))
            {
                Set(runtimeId, quantity);
                return;
            }

            if (quantity <= 0f)
                _goodsByStringId.Remove(goodId);
            else
                _goodsByStringId[goodId] = quantity;
        }

        /// <summary>Set the quantity of a good by dense runtime ID.</summary>
        public void Set(int runtimeId, float quantity)
        {
            if (runtimeId < 0)
                return;

            if (quantity <= 0f)
                _goodsByRuntimeId.Remove(runtimeId);
            else
                _goodsByRuntimeId[runtimeId] = quantity;
        }

        /// <summary>Add to the quantity of a good.</summary>
        public void Add(string goodId, float amount)
        {
            if (amount <= 0f) return;

            if (TryResolveRuntimeId(goodId, out int runtimeId))
            {
                Add(runtimeId, amount);
                return;
            }

            _goodsByStringId[goodId] = Get(goodId) + amount;
        }

        /// <summary>Add to the quantity of a good by dense runtime ID.</summary>
        public void Add(int runtimeId, float amount)
        {
            if (runtimeId < 0 || amount <= 0f)
                return;

            _goodsByRuntimeId[runtimeId] = Get(runtimeId) + amount;
        }

        /// <summary>
        /// Remove from the quantity of a good.
        /// Returns the amount actually removed (may be less if insufficient).
        /// </summary>
        public float Remove(string goodId, float amount)
        {
            if (amount <= 0f) return 0f;

            if (TryResolveRuntimeId(goodId, out int runtimeId))
                return Remove(runtimeId, amount);

            float current = Get(goodId);
            float removed = Math.Min(current, amount);

            if (removed >= current)
                _goodsByStringId.Remove(goodId);
            else
                _goodsByStringId[goodId] = current - removed;

            return removed;
        }

        /// <summary>
        /// Remove from the quantity of a good by dense runtime ID.
        /// Returns the amount actually removed (may be less if insufficient).
        /// </summary>
        public float Remove(int runtimeId, float amount)
        {
            if (runtimeId < 0 || amount <= 0f)
                return 0f;

            float current = Get(runtimeId);
            float removed = Math.Min(current, amount);

            if (removed >= current)
                _goodsByRuntimeId.Remove(runtimeId);
            else
                _goodsByRuntimeId[runtimeId] = current - removed;

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

        /// <summary>
        /// Try to remove an exact amount by dense runtime ID. Returns false if insufficient.
        /// </summary>
        public bool TryRemove(int runtimeId, float amount)
        {
            if (Get(runtimeId) < amount) return false;
            Remove(runtimeId, amount);
            return true;
        }

        /// <summary>Check if we have at least the specified amount.</summary>
        public bool Has(string goodId, float amount)
        {
            return Get(goodId) >= amount;
        }

        /// <summary>Check if we have at least the specified amount by dense runtime ID.</summary>
        public bool Has(int runtimeId, float amount)
        {
            return Get(runtimeId) >= amount;
        }

        /// <summary>Check if we have all the required inputs.</summary>
        public bool HasAll(IEnumerable<GoodInput> inputs)
        {
            foreach (var input in inputs)
            {
                if (!Has(input.GoodId, input.QuantityKg))
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
                Remove(input.GoodId, input.QuantityKg);
            }
            return true;
        }

        /// <summary>Get all goods with non-zero quantities keyed by dense runtime ID.</summary>
        public IEnumerable<KeyValuePair<int, float>> AllRuntime => _goodsByRuntimeId;

        /// <summary>Get all goods with non-zero quantities keyed by string ID.</summary>
        public IEnumerable<KeyValuePair<string, float>> All
        {
            get
            {
                foreach (var kvp in _goodsByRuntimeId)
                {
                    string goodId = ResolveGoodId(kvp.Key);
                    yield return new KeyValuePair<string, float>(goodId, kvp.Value);
                }

                foreach (var kvp in _goodsByStringId)
                    yield return kvp;
            }
        }

        /// <summary>Check if the stockpile is empty.</summary>
        public bool IsEmpty => _goodsByRuntimeId.Count == 0 && _goodsByStringId.Count == 0;

        /// <summary>Clear all goods.</summary>
        public void Clear()
        {
            _goodsByRuntimeId.Clear();
            _goodsByStringId.Clear();
        }

        /// <summary>Total number of distinct good types stored.</summary>
        public int Count => _goodsByRuntimeId.Count + _goodsByStringId.Count;

        private bool TryResolveRuntimeId(string goodId, out int runtimeId)
        {
            if (_goodsRegistry != null && _goodsRegistry.TryGetRuntimeId(goodId, out runtimeId))
                return true;

            runtimeId = -1;
            return false;
        }

        private string ResolveGoodId(int runtimeId)
        {
            var good = _goodsRegistry?.GetByRuntimeId(runtimeId);
            return good?.Id ?? runtimeId.ToString();
        }
    }
}
