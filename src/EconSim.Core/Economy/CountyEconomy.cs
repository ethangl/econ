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
    }
}
