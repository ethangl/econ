namespace EconSim.Core.Economy
{
    /// <summary>
    /// Single time series data point, aggregated across all counties.
    /// Scalar fields are food values for backward compatibility.
    /// </summary>
    public class EconomySnapshot
    {
        public int Day;

        // Per-good aggregates (Layer 3)
        public float[] TotalStockByGood;
        public float[] TotalProductionByGood;
        public float[] TotalConsumptionByGood;
        public float[] TotalUnmetNeedByGood;

        // Backward-compat scalars = food values
        public float TotalStock;
        public float TotalProduction;
        public float TotalConsumption;
        public float TotalUnmetNeed;
        public int SurplusCounties;
        public int DeficitCounties;
        public int ShortfallCounties;
        public float MinStock;
        public float MaxStock;
        public float MedianProductivity;

        // Feudal redistribution — ducal relief (provincial granary → counties)
        public float[] TotalDucalReliefByGood;
        public float[] TotalProvincialStockpileByGood;
        public float[] TotalRoyalStockpileByGood;

        // Monetary taxation
        public float TotalMonetaryTaxToProvince;   // county → province production tax
        public float TotalMonetaryTaxToRealm;      // province → realm revenue share
        public float TotalProvinceAdminCost;        // province admin crown cost
        public float TotalRealmAdminCost;           // realm admin crown cost

        // Granary requisition
        public float[] TotalGranaryRequisitionedByGood;
        public float TotalGranaryRequisitionCrowns;

        // Treasury / minting (Layer 4 Phase B)
        public float TotalTreasury;         // realm treasuries only (inter-realm trade)
        public float TotalCountyTreasury;
        public float TotalProvinceTreasury;
        public float TotalDomesticTreasury; // county + province + realm combined
        public float TotalGoldMinted;
        public float TotalSilverMinted;
        public float TotalCrownsMinted;

        // Intra-province trade (Layer 9 Phase A)
        public float[] TotalIntraProvTradeBoughtByGood;
        public float[] TotalIntraProvTradeSoldByGood;
        public float TotalIntraProvTradeSpending;
        public float TotalIntraProvTradeRevenue;

        // Cross-province trade (Layer 9 Phase B)
        public float[] TotalCrossProvTradeBoughtByGood;
        public float[] TotalCrossProvTradeSoldByGood;
        public float TotalCrossProvTradeSpending;
        public float TotalCrossProvTradeRevenue;
        public float TotalTradeTollsPaid;
        public float TotalTradeTollsCollected;

        // Cross-realm trade (Layer 9 Phase C)
        public float[] TotalCrossRealmTradeBoughtByGood;
        public float[] TotalCrossRealmTradeSoldByGood;
        public float TotalCrossRealmTradeSpending;
        public float TotalCrossRealmTradeRevenue;
        public float TotalCrossRealmTollsPaid;
        public float TotalCrossRealmTariffsPaid;
        public float TotalCrossRealmTariffsCollected;

        // Market fees (Layer 10)
        public float TotalMarketFeesCollected;

        // Inter-realm trade (Layer 4 Phase C)
        public float[] MarketPrices;
        public float[] TotalTradeImportsByGood;
        public float[] TotalTradeExportsByGood;
        public float[] TotalRealmDeficitByGood;
        public float TotalTradeSpending;
        public float TotalTradeRevenue;

        // Population dynamics (Layer 5)
        public float TotalPopulation;
        public float TotalBirths;
        public float TotalDeaths;
        public float AvgBasicSatisfaction;   // population-weighted
        public float MinBasicSatisfaction;
        public float MaxBasicSatisfaction;
        public int CountiesInDistress;      // satisfaction < 0.5

        // Backward-compat scalars = food values
        public float TotalDucalRelief;
        public float TotalProvincialStockpile;
        public float TotalRoyalStockpile;
    }
}
