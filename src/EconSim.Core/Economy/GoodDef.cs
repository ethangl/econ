using System.Collections.Generic;

namespace EconSim.Core.Economy
{
    public enum GoodCategory { Raw, Refined, Finished }
    public enum NeedCategory { None, Staple, Basic, Comfort, Luxury }
    public enum ComfortCategory { None, Alcohol, PreparedFood, Pottery, Furniture, Tools, Clothing, Jewelry, Pantry, Footwear }

    public readonly struct ComfortCategoryDef
    {
        public readonly ComfortCategory Category;
        public readonly float TargetPerPop;
        public readonly bool IsDurable;

        public ComfortCategoryDef(ComfortCategory category, float targetPerPop, bool isDurable)
        {
            Category = category;
            TargetPerPop = targetPerPop;
            IsDurable = isDurable;
        }
    }

    public readonly struct GoodDef
    {
        public readonly GoodType Type;
        public readonly string Name;
        public readonly GoodCategory Category;
        public readonly NeedCategory Need;
        public readonly float ConsumptionPerPop;
        public readonly float CountyAdminPerPop;
        public readonly float ProvinceAdminPerPop;
        public readonly float RealmAdminPerPop;
        public readonly float BasePrice;
        public readonly float MinPrice;
        public readonly float MaxPrice;
        public readonly bool IsTradeable;
        public readonly bool IsPreciousMetal;
        public readonly float SpoilageRate;
        public readonly float TargetStockPerPop;

        /// <summary>Weight in kg per unit. 1.0 for bulk goods (unit = kg). >1 for durables (unit = item).</summary>
        public readonly float UnitWeight;

        /// <summary>Comfort category for substitute grouping (None = not a comfort good).</summary>
        public readonly ComfortCategory Comfort;

        /// <summary>Sensitivity to seasonal extraction penalty (0 = unaffected, 1 = fully seasonal).</summary>
        public readonly float SeasonalSensitivity;

        /// <summary>Minimum cell temperature (°C) for extraction. float.NegativeInfinity = no lower bound.</summary>
        public readonly float MinTemperature;

        /// <summary>Maximum cell temperature (°C) for extraction. float.PositiveInfinity = no upper bound.</summary>
        public readonly float MaxTemperature;

        public readonly Dictionary<int, float> BiomeYields;

        public GoodDef(
            GoodType type, string name,
            GoodCategory category, NeedCategory need,
            float consumptionPerPop,
            float countyAdminPerPop, float provinceAdminPerPop, float realmAdminPerPop,
            float basePrice, float minPrice, float maxPrice,
            bool isTradeable, bool isPreciousMetal,
            float spoilageRate = 0f, float targetStockPerPop = 0f,
            float unitWeight = 1f, ComfortCategory comfortCategory = ComfortCategory.None,
            float seasonalSensitivity = 0f,
            float minTemperature = float.NegativeInfinity, float maxTemperature = float.PositiveInfinity,
            Dictionary<int, float> biomeYields = null)
        {
            Type = type;
            Name = name;
            Category = category;
            Need = need;
            ConsumptionPerPop = consumptionPerPop;
            CountyAdminPerPop = countyAdminPerPop;
            ProvinceAdminPerPop = provinceAdminPerPop;
            RealmAdminPerPop = realmAdminPerPop;
            BasePrice = basePrice;
            MinPrice = minPrice;
            MaxPrice = maxPrice;
            IsTradeable = isTradeable;
            IsPreciousMetal = isPreciousMetal;
            SpoilageRate = spoilageRate;
            TargetStockPerPop = targetStockPerPop;
            UnitWeight = unitWeight;
            Comfort = comfortCategory;
            SeasonalSensitivity = seasonalSensitivity;
            MinTemperature = minTemperature;
            MaxTemperature = maxTemperature;
            BiomeYields = biomeYields;
        }
    }
}
