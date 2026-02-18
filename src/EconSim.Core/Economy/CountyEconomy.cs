namespace EconSim.Core.Economy
{
    /// <summary>
    /// Per-county runtime economic state.
    /// </summary>
    public class CountyEconomy
    {
        /// <summary>Goods on hand.</summary>
        public float Stock;

        /// <summary>County population (cached at init).</summary>
        public float Population;

        /// <summary>Goods produced per person per day (set at init from biome).</summary>
        public float Productivity;

        /// <summary>Last tick's production output.</summary>
        public float Production;

        /// <summary>Last tick's consumption.</summary>
        public float Consumption;

        /// <summary>Shortfall when stock hits zero.</summary>
        public float UnmetNeed;

        /// <summary>Tax paid to provincial stockpile this tick.</summary>
        public float TaxPaid;

        /// <summary>Relief received from provincial stockpile this tick.</summary>
        public float Relief;
    }
}
