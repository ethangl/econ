namespace EconSim.Core.Economy
{
    /// <summary>
    /// Per-province runtime economic state. The duke's granary and treasury.
    /// All per-good arrays indexed by (int)GoodType.
    /// </summary>
    public class ProvinceEconomy
    {
        /// <summary>Goods held in the ducal granary (staples) and precious metals in transit, per good type.</summary>
        public float[] Stockpile = new float[Goods.Count];

        /// <summary>Relief distributed to deficit counties this tick, per good type.</summary>
        public float[] ReliefGiven = new float[Goods.Count];

        /// <summary>Crowns held by the province (from production tax and tolls, spent on admin and granary).</summary>
        public float Treasury;

        /// <summary>Monetary production tax collected from counties this tick (reset daily).</summary>
        public float MonetaryTaxCollected;

        /// <summary>Revenue share paid up to realm this tick (reset daily).</summary>
        public float MonetaryTaxPaidToRealm;

        /// <summary>Admin crown cost deducted this tick (reset daily).</summary>
        public float AdminCrownsCost;

        /// <summary>Goods requisitioned from counties for granary this tick, per good type (reset daily).</summary>
        public float[] GranaryRequisitioned = new float[Goods.Count];

        /// <summary>Crowns spent on granary requisition this tick (reset daily).</summary>
        public float GranaryRequisitionCrownsSpent;

        /// <summary>Cross-province trade: toll revenue received from buying counties this tick (reset daily).</summary>
        public float TradeTollsCollected;
    }
}
