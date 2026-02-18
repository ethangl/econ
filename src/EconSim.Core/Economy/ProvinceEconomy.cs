namespace EconSim.Core.Economy
{
    /// <summary>
    /// Per-province runtime economic state. The duke's granary.
    /// </summary>
    public class ProvinceEconomy
    {
        /// <summary>Goods held in the provincial stockpile.</summary>
        public float Stockpile;

        /// <summary>Tax collected from surplus counties this tick.</summary>
        public float TaxCollected;

        /// <summary>Relief distributed to deficit counties this tick.</summary>
        public float ReliefGiven;
    }
}
