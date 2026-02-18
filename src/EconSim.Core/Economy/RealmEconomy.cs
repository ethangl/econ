namespace EconSim.Core.Economy
{
    /// <summary>
    /// Per-realm runtime economic state. The king's treasury.
    /// All per-good arrays indexed by (int)GoodType.
    /// </summary>
    public class RealmEconomy
    {
        /// <summary>Goods held in the royal stockpile, per good type.</summary>
        public float[] Stockpile = new float[Goods.Count];

        /// <summary>Tax collected from surplus provinces this tick, per good type.</summary>
        public float[] TaxCollected = new float[Goods.Count];

        /// <summary>Relief distributed to deficit provinces this tick, per good type.</summary>
        public float[] ReliefGiven = new float[Goods.Count];
    }
}
