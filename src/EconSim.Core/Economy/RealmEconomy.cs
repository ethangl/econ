namespace EconSim.Core.Economy
{
    /// <summary>
    /// Per-realm runtime economic state. The king's treasury.
    /// All per-good arrays indexed by (int)GoodType.
    /// </summary>
    public class RealmEconomy
    {
        /// <summary>Goods held in the royal stockpile (precious metals only), per good type.</summary>
        public float[] Stockpile = new float[Goods.Count];

        /// <summary>Crowns held by the realm (minted from precious metals + revenue share from provinces).</summary>
        public float Treasury;

        /// <summary>Kg of gold ore minted this tick (reset daily).</summary>
        public float GoldMinted;

        /// <summary>Kg of silver ore minted this tick (reset daily).</summary>
        public float SilverMinted;

        /// <summary>Crowns generated this tick (reset daily).</summary>
        public float CrownsMinted;

        /// <summary>Revenue share collected from provinces this tick (reset daily).</summary>
        public float MonetaryTaxCollected;

        /// <summary>Admin crown cost deducted this tick (reset daily).</summary>
        public float AdminCrownsCost;

        // ── Inter-realm trade fields (reset daily) ──────────────

        /// <summary>Total unmet demand per good (set by FiscalSystem admin + InterRealmTradeSystem deficit scan).</summary>
        public float[] Deficit = new float[Goods.Count];

        /// <summary>Quantities imported from the inter-realm market this tick, per good.</summary>
        public float[] TradeImports = new float[Goods.Count];

        /// <summary>Quantities exported to the inter-realm market this tick, per good.</summary>
        public float[] TradeExports = new float[Goods.Count];

        /// <summary>Crowns spent on market purchases this tick.</summary>
        public float TradeSpending;

        /// <summary>Crowns earned from market sales this tick.</summary>
        public float TradeRevenue;

        /// <summary>Tariff crowns collected from cross-realm imports this tick (reset daily).</summary>
        public float TradeTariffsCollected;
    }
}
