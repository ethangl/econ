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

        /// <summary>Crowns held by the province (received from tax payments, spent on relief).</summary>
        public float Treasury;

        /// <summary>Crowns paid to counties for ducal tax this tick (reset daily).</summary>
        public float TaxCrownsPaid;

        /// <summary>Crowns received from counties for relief goods this tick (reset daily).</summary>
        public float ReliefCrownsReceived;

        /// <summary>Crowns received from realm for royal tax this tick (reset daily).</summary>
        public float RoyalTaxCrownsReceived;

        /// <summary>Crowns paid to realm for relief received this tick (reset daily).</summary>
        public float RoyalReliefCrownsPaid;

        /// <summary>Cross-province trade: toll revenue received from buying counties this tick (reset daily).</summary>
        public float TradeTollsCollected;
    }
}
