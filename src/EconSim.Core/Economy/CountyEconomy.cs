namespace EconSim.Core.Economy
{
    /// <summary>
    /// Per-county runtime economic state. All per-good arrays indexed by (int)GoodType.
    /// </summary>
    public class CountyEconomy
    {
        /// <summary>Goods on hand, per good type (kg).</summary>
        public float[] Stock = new float[Goods.Count];

        /// <summary>County population (cached at init).</summary>
        public float Population;

        /// <summary>Goods produced per person per day, per good type (set at init from biome).</summary>
        public float[] Productivity = new float[Goods.Count];

        /// <summary>Last tick's production output, per good type.</summary>
        public float[] Production = new float[Goods.Count];

        /// <summary>Last tick's consumption, per good type.</summary>
        public float[] Consumption = new float[Goods.Count];

        /// <summary>Shortfall when stock hits zero, per good type.</summary>
        public float[] UnmetNeed = new float[Goods.Count];

        /// <summary>Tax paid to provincial stockpile this tick, per good type.</summary>
        public float[] TaxPaid = new float[Goods.Count];

        /// <summary>Relief received from provincial stockpile this tick, per good type.</summary>
        public float[] Relief = new float[Goods.Count];

        /// <summary>Exponential moving average of daily basic-needs satisfaction (0=starving, 1=fully supplied). ~30-day window.
        /// Weighted average of all NeedCategory.Basic goods (food, ale, salt) by consumption rate.</summary>
        public float BasicSatisfaction = 1f;

        /// <summary>Births this month (reset monthly by PopulationSystem).</summary>
        public float BirthsThisMonth;

        /// <summary>Deaths this month (reset monthly by PopulationSystem).</summary>
        public float DeathsThisMonth;

        /// <summary>Net migration this month (reset monthly by PopulationSystem). Positive = inflow.</summary>
        public float NetMigrationThisMonth;

        /// <summary>Total workers assigned to facilities in this county. Updated each tick by EconomySystem.</summary>
        public float FacilityWorkers;

        /// <summary>Realm-distributed production target per good, indexed by GoodType. Set by FacilityQuotaSystem.</summary>
        public float[] FacilityQuota = new float[Goods.Count];

        /// <summary>Daily facility input demand per good (kg/day). Computed by EconomySystem, read by FiscalSystem for trade retain.</summary>
        public float[] FacilityInputNeed = new float[Goods.Count];

        /// <summary>Crowns held by the county (received from tax payments, spent on relief).</summary>
        public float Treasury;

        /// <summary>Crowns received from province for taxed goods this tick (reset daily).</summary>
        public float TaxCrownsReceived;

        /// <summary>Crowns paid to province for relief goods this tick (reset daily).</summary>
        public float ReliefCrownsPaid;

        /// <summary>Intra-province trade: kg bought per good this tick (reset daily).</summary>
        public float[] TradeBought = new float[Goods.Count];

        /// <summary>Intra-province trade: kg sold per good this tick (reset daily).</summary>
        public float[] TradeSold = new float[Goods.Count];

        /// <summary>Intra-province trade: total crowns spent buying this tick (reset daily).</summary>
        public float TradeCrownsSpent;

        /// <summary>Intra-province trade: total crowns earned selling this tick (reset daily).</summary>
        public float TradeCrownsEarned;

        /// <summary>Cross-province trade: kg bought per good this tick (reset daily).</summary>
        public float[] CrossProvTradeBought = new float[Goods.Count];

        /// <summary>Cross-province trade: kg sold per good this tick (reset daily).</summary>
        public float[] CrossProvTradeSold = new float[Goods.Count];

        /// <summary>Cross-province trade: total crowns spent buying this tick (reset daily).</summary>
        public float CrossProvTradeCrownsSpent;

        /// <summary>Cross-province trade: total crowns earned selling this tick (reset daily).</summary>
        public float CrossProvTradeCrownsEarned;

        /// <summary>Cross-province trade: toll crowns paid to own province this tick (reset daily).</summary>
        public float TradeTollsPaid;

        /// <summary>Cross-realm trade: kg bought per good this tick (reset daily).</summary>
        public float[] CrossRealmTradeBought = new float[Goods.Count];

        /// <summary>Cross-realm trade: kg sold per good this tick (reset daily).</summary>
        public float[] CrossRealmTradeSold = new float[Goods.Count];

        /// <summary>Cross-realm trade: total crowns spent buying this tick (reset daily).</summary>
        public float CrossRealmTradeCrownsSpent;

        /// <summary>Cross-realm trade: total crowns earned selling this tick (reset daily).</summary>
        public float CrossRealmTradeCrownsEarned;

        /// <summary>Cross-realm trade: toll crowns paid to own province this tick (reset daily).</summary>
        public float CrossRealmTollsPaid;

        /// <summary>Cross-realm trade: tariff crowns paid to own realm this tick (reset daily).</summary>
        public float CrossRealmTariffsPaid;

        /// <summary>Market fees received this tick (reset daily). Only nonzero for market county.</summary>
        public float MarketFeesReceived;
    }
}
