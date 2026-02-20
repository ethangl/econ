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

        /// <summary>Realm-distributed production target per good, indexed by GoodType. Set by TradeSystem Phase 9.</summary>
        public float[] FacilityQuota = new float[Goods.Count];
    }
}
