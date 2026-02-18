namespace EconSim.Core.Economy
{
    /// <summary>
    /// Per-realm runtime economic state. The king's treasury.
    /// </summary>
    public class RealmEconomy
    {
        /// <summary>Goods held in the royal stockpile.</summary>
        public float Stockpile;

        /// <summary>Tax collected from surplus provinces this tick.</summary>
        public float TaxCollected;

        /// <summary>Relief distributed to deficit provinces this tick.</summary>
        public float ReliefGiven;
    }
}
