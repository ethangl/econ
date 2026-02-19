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

        /// <summary>Crowns held by the realm (minted from precious metals).</summary>
        public float Treasury;

        /// <summary>Kg of gold ore minted this tick (reset daily).</summary>
        public float GoldMinted;

        /// <summary>Kg of silver ore minted this tick (reset daily).</summary>
        public float SilverMinted;

        /// <summary>Crowns generated this tick (reset daily).</summary>
        public float CrownsMinted;
    }
}
