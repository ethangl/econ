using System;
using System.Collections.Generic;

namespace EconSim.Core.Economy
{
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
        public float Quantity;

        public GoodInput(string goodId, float quantity)
        {
            GoodId = goodId;
            Quantity = quantity;
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
        public string Name;
        public GoodCategory Category;

        // === Raw resources only ===
        /// <summary>How this resource is harvested (logging, mining, farming, etc.).</summary>
        public string HarvestMethod;
        /// <summary>Terrain/biome types where this resource can be found.</summary>
        public List<string> TerrainAffinity;
        /// <summary>Base units harvested per day per facility.</summary>
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
        /// <summary>Base consumption rate per capita per day.</summary>
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
    }

    /// <summary>
    /// Registry of all good definitions. Singleton-ish, initialized at startup.
    /// </summary>
    public class GoodRegistry
    {
        private readonly Dictionary<string, GoodDef> _goods = new Dictionary<string, GoodDef>();

        public void Register(GoodDef good)
        {
            _goods[good.Id] = good;
        }

        public GoodDef Get(string id)
        {
            return _goods.TryGetValue(id, out var good) ? good : null;
        }

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
