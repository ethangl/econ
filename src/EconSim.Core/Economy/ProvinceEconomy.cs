namespace EconSim.Core.Economy
{
    /// <summary>
    /// Per-province runtime economic state. The duke's granary.
    /// All per-good arrays indexed by (int)GoodType.
    /// </summary>
    public class ProvinceEconomy
    {
        /// <summary>Goods held in the provincial stockpile, per good type.</summary>
        public float[] Stockpile = new float[Goods.Count];

        /// <summary>Tax collected from surplus counties this tick, per good type.</summary>
        public float[] TaxCollected = new float[Goods.Count];

        /// <summary>Relief distributed to deficit counties this tick, per good type.</summary>
        public float[] ReliefGiven = new float[Goods.Count];
    }
}
