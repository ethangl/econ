namespace EconSim.Core.Economy
{
    public enum GoodCategory { Raw, Refined, Finished }
    public enum NeedCategory { None, Basic, Comfort, Luxury }

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

        public GoodDef(
            GoodType type, string name,
            GoodCategory category, NeedCategory need,
            float consumptionPerPop,
            float countyAdminPerPop, float provinceAdminPerPop, float realmAdminPerPop,
            float basePrice, float minPrice, float maxPrice,
            bool isTradeable, bool isPreciousMetal,
            float spoilageRate = 0f, float targetStockPerPop = 0f)
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
        }
    }
}
