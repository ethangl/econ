using System;
using System.Collections.Generic;

namespace EconSim.Core.Economy
{
    /// <summary>
    /// Canonical quantity unit for economy goods.
    /// </summary>
    public enum GoodQuantityUnit
    {
        Kilogram
    }

    /// <summary>
    /// Category in the production chain.
    /// </summary>
    public enum GoodCategory
    {
        Raw,        // Harvested from terrain (wheat, iron ore, timber)
        Refined,    // Processed from raw (flour, iron ingots, lumber)
        Finished    // Manufactured for consumption (bread, tools, furniture)
    }

    /// <summary>
    /// Consumer need hierarchy - determines priority and effects of unmet demand.
    /// </summary>
    public enum NeedCategory
    {
        Basic,      // Must have - unmet causes population decline, unrest
        Comfort,    // Want - unmet causes slower growth, mild unrest
        Luxury      // Premium - unmet only means missed economic activity
    }

    /// <summary>
    /// Input requirement for a recipe.
    /// </summary>
    [Serializable]
    public struct GoodInput
    {
        public string GoodId;
        /// <summary>
        /// Input amount in kilograms required per one kilogram of output.
        /// </summary>
        public float Quantity;

        public GoodInput(string goodId, float quantity)
        {
            GoodId = goodId;
            Quantity = quantity;
        }

        /// <summary>
        /// Alias for <see cref="Quantity"/> with explicit units.
        /// </summary>
        public float QuantityKg
        {
            get => Quantity;
            set => Quantity = value;
        }
    }

    /// <summary>
    /// Static definition of a good (raw resource, refined material, or finished product).
    /// Loaded from data files, immutable at runtime.
    /// </summary>
    [Serializable]
    public class GoodDef
    {
        public string Id;
        /// <summary>
        /// Dense runtime identifier assigned by <see cref="GoodRegistry"/>.
        /// Stable for a given initialization sequence and used by int-keyed hot paths.
        /// </summary>
        public int RuntimeId = -1;
        public string Name;
        public GoodCategory Category;
        /// <summary>
        /// Canonical unit for all quantities involving this good.
        /// For now, all goods are modeled in kilograms.
        /// </summary>
        public GoodQuantityUnit QuantityUnit = GoodQuantityUnit.Kilogram;

        // === Raw resources only ===
        /// <summary>How this resource is harvested (logging, mining, farming, etc.).</summary>
        public string HarvestMethod;
        /// <summary>Terrain/biome types where this resource can be found.</summary>
        public List<string> TerrainAffinity;
        /// <summary>Base kilograms harvested per day per facility at full staffing.</summary>
        public float BaseYield;

        // === Refined and Finished goods ===
        /// <summary>Input goods required to produce one unit.</summary>
        public List<GoodInput> Inputs;
        /// <summary>Facility type required for production.</summary>
        public string FacilityType;
        /// <summary>Ticks to produce one batch.</summary>
        public int ProcessingTicks;

        // === Finished goods only (consumer demand) ===
        /// <summary>What need this good satisfies (null for non-consumer goods).</summary>
        public NeedCategory? NeedCategory;
        /// <summary>Base consumption rate in kilograms per capita per day.</summary>
        public float BaseConsumption;

        // === Storage properties ===
        /// <summary>
        /// Fraction of stockpile lost per day due to spoilage/decay.
        /// 0 = imperishable, 0.05 = 5% lost per day.
        /// </summary>
        public float DecayRate;

        // === Theft properties ===
        /// <summary>
        /// Legacy theft-risk tuning coefficient.
        /// 0 = no theft appeal, 1 = all losses are stolen.
        /// High-value portable goods have higher theft risk.
        /// </summary>
        public float TheftRisk;

        // === Pricing ===
        /// <summary>
        /// Base market price when supply equals demand.
        /// Raw goods ~1, refined ~2-5, finished basics ~5-10, luxury ~50+.
        /// </summary>
        public float BasePrice = 1.0f;

        public bool IsRaw => Category == GoodCategory.Raw;
        public bool IsRefined => Category == GoodCategory.Refined;
        public bool IsFinished => Category == GoodCategory.Finished;
        public bool IsConsumerGood => NeedCategory.HasValue;

        /// <summary>
        /// Explicit-unit alias for <see cref="BaseYield"/>.
        /// </summary>
        public float BaseYieldKgPerDay
        {
            get => BaseYield;
            set => BaseYield = value;
        }

        /// <summary>
        /// Explicit-unit alias for <see cref="BaseConsumption"/>.
        /// </summary>
        public float BaseConsumptionKgPerCapitaPerDay
        {
            get => BaseConsumption;
            set => BaseConsumption = value;
        }
    }

    /// <summary>
    /// Registry of all good definitions. Singleton-ish, initialized at startup.
    /// </summary>
    public class GoodRegistry
    {
        private readonly Dictionary<string, GoodDef> _goods = new Dictionary<string, GoodDef>();
        private readonly Dictionary<string, int> _runtimeIdByGoodId = new Dictionary<string, int>();
        private readonly List<GoodDef> _goodsByRuntimeId = new List<GoodDef>();

        public void Register(GoodDef good)
        {
            if (good == null || string.IsNullOrWhiteSpace(good.Id))
                throw new ArgumentException("GoodDef and GoodDef.Id are required for registration.");

            ValidateQuantities(good);

            if (_runtimeIdByGoodId.TryGetValue(good.Id, out int existingRuntimeId))
            {
                good.RuntimeId = existingRuntimeId;
                _goods[good.Id] = good;
                _goodsByRuntimeId[existingRuntimeId] = good;
                return;
            }

            int runtimeId = _goodsByRuntimeId.Count;
            good.RuntimeId = runtimeId;
            _runtimeIdByGoodId[good.Id] = runtimeId;
            _goodsByRuntimeId.Add(good);
            _goods[good.Id] = good;
        }

        private static void ValidateQuantities(GoodDef good)
        {
            if (!IsFinite(good.BaseYield) || good.BaseYield < 0f)
                throw new ArgumentException($"GoodDef {good.Id} has invalid BaseYield: {good.BaseYield}");

            if (!IsFinite(good.BaseConsumption) || good.BaseConsumption < 0f)
                throw new ArgumentException($"GoodDef {good.Id} has invalid BaseConsumption: {good.BaseConsumption}");

            if (good.Inputs == null)
                return;

            for (int i = 0; i < good.Inputs.Count; i++)
            {
                var input = good.Inputs[i];
                if (!IsFinite(input.Quantity) || input.Quantity <= 0f)
                {
                    throw new ArgumentException(
                        $"GoodDef {good.Id} has invalid input quantity for {input.GoodId}: {input.Quantity}");
                }
            }
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        public GoodDef Get(string id)
        {
            return _goods.TryGetValue(id, out var good) ? good : null;
        }

        public bool TryGetRuntimeId(string id, out int runtimeId)
        {
            return _runtimeIdByGoodId.TryGetValue(id, out runtimeId);
        }

        public GoodDef GetByRuntimeId(int runtimeId)
        {
            return runtimeId >= 0 && runtimeId < _goodsByRuntimeId.Count
                ? _goodsByRuntimeId[runtimeId]
                : null;
        }

        public bool TryGetByRuntimeId(int runtimeId, out GoodDef good)
        {
            if (runtimeId >= 0 && runtimeId < _goodsByRuntimeId.Count)
            {
                good = _goodsByRuntimeId[runtimeId];
                return true;
            }

            good = null;
            return false;
        }

        public IReadOnlyList<GoodDef> Dense => _goodsByRuntimeId;

        public int RuntimeCount => _goodsByRuntimeId.Count;

        public IEnumerable<GoodDef> All => _goods.Values;

        public IEnumerable<GoodDef> ByCategory(GoodCategory category)
        {
            foreach (var good in _goods.Values)
            {
                if (good.Category == category)
                    yield return good;
            }
        }

        public IEnumerable<GoodDef> ConsumerGoods
        {
            get
            {
                foreach (var good in _goods.Values)
                {
                    if (good.IsConsumerGood)
                        yield return good;
                }
            }
        }
    }
}
