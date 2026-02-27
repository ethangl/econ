using System;
using System.Collections.Generic;

namespace EconSim.Core.Economy
{
    /// <summary>
    /// State for the virtual overseas market — an abstract foreign trade partner
    /// representing Silk Road, spice trade, salt routes, etc. Counties can import
    /// from and export to it, paying transport costs based on coast proximity.
    /// </summary>
    public class VirtualMarketState
    {
        /// <summary>Good indices that the VM trades (e.g. Salt, Spices).</summary>
        public HashSet<int> TradedGoods = new HashSet<int>();

        /// <summary>Current inventory per good (kg).</summary>
        public float[] Stock;

        /// <summary>Import price: VM sells to county at this price (Cr/kg).</summary>
        public float[] SellPrice;

        /// <summary>Export price: county sells to VM at this price (Cr/kg).</summary>
        public float[] BuyPrice;

        /// <summary>Price anchor — price equals base when stock equals target.</summary>
        public float[] TargetStock;

        /// <summary>Daily stock regeneration (kg/day).</summary>
        public float[] ReplenishRate;

        /// <summary>Maximum inventory capacity per good (kg).</summary>
        public float[] MaxStock;

        /// <summary>Precomputed transport cost (Cr/kg) per county to nearest port. Indexed by county ID.</summary>
        public float[] CountyPortCost;

        /// <summary>Flat surcharge on all overseas transport (Cr/kg).</summary>
        public float OverseasSurcharge;

        // ── Per-tick accumulators (reset daily) ──

        /// <summary>Total kg imported from VM this tick, per good.</summary>
        public float[] TotalImported;

        /// <summary>Total kg exported to VM this tick, per good.</summary>
        public float[] TotalExported;

        /// <summary>Total crowns spent on imports this tick.</summary>
        public float TotalImportSpending;

        /// <summary>Total crowns earned from exports this tick.</summary>
        public float TotalExportRevenue;

        /// <summary>Total tariff crowns collected on imports this tick.</summary>
        public float TotalTariffCollected;

        public VirtualMarketState(int goodCount, int maxCountyId)
        {
            Stock = new float[goodCount];
            SellPrice = new float[goodCount];
            BuyPrice = new float[goodCount];
            TargetStock = new float[goodCount];
            ReplenishRate = new float[goodCount];
            MaxStock = new float[goodCount];
            CountyPortCost = new float[maxCountyId + 1];
            TotalImported = new float[goodCount];
            TotalExported = new float[goodCount];

            // Default all port costs to unreachable
            for (int i = 0; i <= maxCountyId; i++)
                CountyPortCost[i] = float.MaxValue;
        }

        /// <summary>Reset per-tick accumulators.</summary>
        public void ResetAccumulators()
        {
            Array.Clear(TotalImported, 0, TotalImported.Length);
            Array.Clear(TotalExported, 0, TotalExported.Length);
            TotalImportSpending = 0f;
            TotalExportRevenue = 0f;
            TotalTariffCollected = 0f;
        }
    }
}
