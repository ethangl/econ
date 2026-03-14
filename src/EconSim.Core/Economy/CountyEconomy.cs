namespace EconSim.Core.Economy
{
    /// <summary>
    /// Per-county economic state for economy v4.
    /// </summary>
    public class CountyEconomy
    {
        // ── Population by class ──

        /// <summary>Lower commoner (serf) population.</summary>
        public float LowerCommonerPop;

        /// <summary>Upper commoner (artisan/merchant) population.</summary>
        public float UpperCommonerPop;

        /// <summary>Lower nobility population.</summary>
        public float LowerNobilityPop;

        /// <summary>Upper nobility population.</summary>
        public float UpperNobilityPop;

        /// <summary>Lower clergy population.</summary>
        public float LowerClergyPop;

        /// <summary>Upper clergy population.</summary>
        public float UpperClergyPop;

        public float TotalPopulation =>
            LowerCommonerPop + UpperCommonerPop +
            LowerNobilityPop + UpperNobilityPop +
            LowerClergyPop + UpperClergyPop;

        // ── Five coin pools ──

        /// <summary>Upper noble treasury (funded by surplus sales, tax, tariffs).</summary>
        public float UpperNobleTreasury;

        /// <summary>Lower noble treasury (funded by stipend from upper noble).</summary>
        public float LowerNobleTreasury;

        /// <summary>Upper clergy treasury (funded by tithe on upper commoner buys).</summary>
        public float UpperClergyTreasury;

        /// <summary>Lower clergy coin balance (funded by wages from upper clergy).</summary>
        public float LowerClergyCoin;

        /// <summary>Upper commoner coin balance. Part of M (money in circulation).</summary>
        public float UpperCommonerCoin;

        /// <summary>M = upper commoner coin + lower clergy coin. Money in circulation for this county.</summary>
        public float MoneySupply => UpperCommonerCoin + LowerClergyCoin;

        // ── Biome productivity (computed once at init) ──

        /// <summary>Average biome yield per good, indexed by GoodType. kg/person/day.</summary>
        public float[] Productivity;

        // ── Per-class satisfaction (weighted composite, computed each tick) ──

        public float LowerCommonerSatisfaction;
        public float UpperCommonerSatisfaction;
        public float LowerNobilitySatisfaction;
        public float UpperNobilitySatisfaction;
        public float LowerClergySatisfaction;
        public float UpperClergySatisfaction;

        // ── Phase 5: satisfaction breakdown (diagnostic) ──

        /// <summary>LC survival component (local food / staple need).</summary>
        public float SurvivalSatisfaction;

        /// <summary>Shared religion component (clergy worship goods fulfillment).</summary>
        public float ReligionSatisfaction;

        /// <summary>UC economic component (comfort+luxury buy order fulfillment).</summary>
        public float EconomicSatisfaction;

        // ── Phase 5: population change (reset each tick) ──

        /// <summary>Total births this tick across all classes.</summary>
        public float Births;

        /// <summary>Total deaths this tick across all classes.</summary>
        public float Deaths;

        /// <summary>Net migration this tick (positive = immigration).</summary>
        public float NetMigration;

        // ── Phase 1: daily production / consumption / surplus ──

        /// <summary>Raw daily production per good (kg/day). LowerCommonerPop × Productivity[g].</summary>
        public float[] Production;

        /// <summary>Subsistence consumption per good (kg/day).</summary>
        public float[] Consumption;

        /// <summary>Production − consumption per good (kg/day). Available for sell orders.</summary>
        public float[] Surplus;

        /// <summary>True if local staple production &lt; staple need.</summary>
        public bool FoodDeficit;

        // ── Phase 2: per-tick tracking (reset each tick) ──

        /// <summary>Food (kg) bought by lord and given to serfs this tick.</summary>
        public float SerfFoodProvided;

        /// <summary>Total coin spent by upper noble this tick (buys + stipend).</summary>
        public float UpperNobleSpend;

        /// <summary>Total coin received by upper noble this tick (surplus sales + minting + tax).</summary>
        public float UpperNobleIncome;

        /// <summary>Total coin spent by lower noble this tick.</summary>
        public float LowerNobleSpend;

        // ── Phase 3: facility + commoner + clergy state ──

        /// <summary>Per-good fill rate for facility input buy orders from last tick. Used to compute next tick's output.</summary>
        public float[] FacilityInputGoodFill;

        /// <summary>Per-good sell fill rate for facility output from last tick. Throttles production to match demand.</summary>
        public float[] FacilityOutputGoodFill;

        /// <summary>Total coin earned by upper commoners this tick (facility sales).</summary>
        public float UpperCommonerIncome;

        /// <summary>Total coin spent by upper commoners this tick (goods + facility inputs).</summary>
        public float UpperCommonerSpend;

        /// <summary>Tax revenue collected from upper commoner purchases this tick.</summary>
        public float TaxRevenue;

        /// <summary>Tithe revenue collected from upper commoner purchases this tick.</summary>
        public float TitheRevenue;

        /// <summary>Tariff revenue from cross-market trade this tick.</summary>
        public float TariffRevenue;

        /// <summary>Total coin spent by upper clergy this tick.</summary>
        public float UpperClergySpend;

        /// <summary>Total coin received by upper clergy this tick (tithe).</summary>
        public float UpperClergyIncome;

        /// <summary>Total coin spent by lower clergy this tick.</summary>
        public float LowerClergySpend;

        /// <summary>Total coin received by lower clergy this tick (wages).</summary>
        public float LowerClergyIncome;

        public CountyEconomy()
        {
            int n = Goods.Count;
            Productivity = new float[n];
            Production = new float[n];
            Consumption = new float[n];
            Surplus = new float[n];
            FacilityInputGoodFill = new float[n];
            FacilityOutputGoodFill = new float[n];
        }
    }
}
