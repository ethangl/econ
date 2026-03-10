namespace EconSim.Core.Economy.V4
{
    /// <summary>
    /// Per-county economic state for economy v4.
    /// </summary>
    public class CountyEconomyV4
    {
        // ── Population by class ──

        /// <summary>Lower commoner (serf) population.</summary>
        public float LowerCommonerPop;

        /// <summary>Upper commoner (artisan/merchant) population.</summary>
        public float UpperCommonerPop;

        /// <summary>Lower nobility population.</summary>
        public float LowerNobilityPop;

        /// <summary>Upper nobility population.</summary>
        public float UpperNobilityPop;

        /// <summary>Lower clergy population.</summary>
        public float LowerClergyPop;

        /// <summary>Upper clergy population.</summary>
        public float UpperClergyPop;

        public float TotalPopulation =>
            LowerCommonerPop + UpperCommonerPop +
            LowerNobilityPop + UpperNobilityPop +
            LowerClergyPop + UpperClergyPop;

        // ── Five coin pools ──

        /// <summary>Upper noble treasury (funded by surplus sales, tax, tariffs).</summary>
        public float UpperNobleTreasury;

        /// <summary>Lower noble treasury (funded by stipend from upper noble).</summary>
        public float LowerNobleTreasury;

        /// <summary>Upper clergy treasury (funded by tithe on upper commoner buys).</summary>
        public float UpperClergyTreasury;

        /// <summary>Lower clergy coin balance (funded by wages from upper clergy).</summary>
        public float LowerClergyCoin;

        /// <summary>Upper commoner coin balance. Part of M (money in circulation).</summary>
        public float UpperCommonerCoin;

        /// <summary>M = upper commoner coin + lower clergy coin. Money in circulation for this county.</summary>
        public float MoneySupply => UpperCommonerCoin + LowerClergyCoin;

        // ── Biome productivity (computed once at init) ──

        /// <summary>Average biome yield per good, indexed by GoodTypeV4. kg/person/day.</summary>
        public float[] Productivity;

        // ── Per-class satisfaction (computed each tick in Phase 5) ──

        public float LowerCommonerSatisfaction;
        public float UpperCommonerSatisfaction;
        public float LowerNobilitySatisfaction;
        public float UpperNobilitySatisfaction;
        public float LowerClergySatisfaction;
        public float UpperClergySatisfaction;

        public CountyEconomyV4()
        {
            Productivity = new float[GoodsV4.Count];
        }
    }
}
