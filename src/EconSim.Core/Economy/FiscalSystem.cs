using System;
using System.Collections.Generic;
using EconSim.Core.Data;
using EconSim.Core.Simulation;
using EconSim.Core.Simulation.Systems;

namespace EconSim.Core.Economy
{
    enum TradeScope { IntraProvince, CrossProvince, CrossRealm }

    /// <summary>
    /// Feudal redistribution with administrative consumption. Runs daily AFTER EconomySystem.
    /// All goods flow through the hierarchy independently.
    ///
    /// Phase 1: County admin consumption (building upkeep)
    /// Phase 2: Duke taxes surplus counties → provincial stockpile
    /// Phase 3: Provincial admin consumption (infrastructure)
    /// Phase 4: King taxes surplus provinces → royal stockpile
    /// Phase 5: Royal admin consumption (military upkeep)
    /// Phase 5b: Mint precious metals into Crowns (realm treasury)
    /// Phase 6: King distributes remainder → deficit provinces
    /// Phase 7: Duke distributes provincial stockpile → deficit counties
    ///
    /// Admin consumption before tax/redistribution at each tier ensures the hierarchy
    /// feeds itself first, then passes surplus up and relief down.
    /// </summary>
    public class FiscalSystem : ITickSystem
    {
        public string Name => "Fiscal";
        public int TickInterval => SimulationConfig.Intervals.Daily;

        static readonly float[] ConsumptionPerPop = Goods.ConsumptionPerPop;

        /// <summary>Fraction of surplus the duke takes from counties.</summary>
        const float DucalTaxRate = 0.20f;

        /// <summary>Fraction of provincial stockpile the king takes.</summary>
        const float RoyalTaxRate = 0.20f;

        /// <summary>Precious metals are crown property — 100% tax rate (regal right).</summary>
        const float PreciousMetalTaxRate = 1.0f;

        /// <summary>Feudal lords pay 50% of market price when taxing goods (forced requisition discount).</summary>
        const float TaxPaymentDiscount = 0.50f;


        /// <summary>Buyers pay 5% toll on cross-province trade (paid to own province treasury).</summary>
        const float CrossProvTollRate = 0.05f;

        /// <summary>Buyers pay 10% tariff on cross-realm trade (paid to own realm treasury).</summary>
        const float CrossRealmTariffRate = 0.10f;

        /// <summary>Buyers pay 2% market fee on all trade (paid to market county treasury).</summary>
        const float MarketFeeRate = 0.02f;

        /// <summary>Counties with BasicSatisfaction below this threshold receive feudal relief.</summary>
        const float ReliefSatisfactionThreshold = 0.70f;


        /// <summary>Province ID → array of county IDs.</summary>
        int[][] _provinceCounties;

        /// <summary>Province IDs that exist.</summary>
        int[] _provinceIds;

        /// <summary>Realm ID → array of province IDs.</summary>
        int[][] _realmProvinces;

        /// <summary>Realm IDs that exist.</summary>
        int[] _realmIds;

        /// <summary>Max province count across all realms (for stackalloc sizing).</summary>
        int _maxProvincesPerRealm;

        /// <summary>Realm ID → array of all county IDs in that realm.</summary>
        int[][] _realmCounties;

        /// <summary>All county IDs (for cross-realm trade global pool).</summary>
        int[] _allCountyIds;

        /// <summary>Total county count.</summary>
        int _totalCountyCount;

        /// <summary>Reusable surplus buffer for trade passes. Sized to _totalCountyCount.</summary>
        float[] _surplusBuf;

        /// <summary>County ID → Province ID (for routing tolls to buyer's province).</summary>
        int[] _countyToProvince;

        /// <summary>County ID → Realm ID (for deficit ledger).</summary>
        int[] _countyToRealm;

        /// <summary>County ID hosting the market (receives market fees).</summary>
        int _marketCountyId = -1;

        /// <summary>Province ID → Realm ID (for deficit ledger).</summary>
        int[] _provinceToRealm;

        /// <summary>Per-county retain deficit scratch for the current good. Indexed by county ID.</summary>
        float[] _countyRetainDeficit;

        /// <summary>Per-province total retain deficit scratch for the current good. Indexed by province ID.</summary>
        float[] _provinceRetainDeficit;

        /// <summary>Per-county retain target scratch for the current good. Indexed by county ID.</summary>
        float[] _countyRetainTarget;

        /// <summary>Per-good stock presence across county/province/realm stores for this tick.</summary>
        bool[] _hasStockByGood;

        public void Initialize(SimulationState state, MapData mapData)
        {
            BuildMappings(state, mapData);
        }

        public void Tick(SimulationState state, MapData mapData)
        {
            var counties = state.Economy.Counties;
            var provinces = state.Economy.Provinces;
            var realms = state.Economy.Realms;
            var prices = state.Economy.MarketPrices;

            // Shared population caches (updated by PopulationSystem monthly)
            var _provincePop = state.Economy.ProvincePop;
            var _realmPop = state.Economy.RealmPop;

            // Reset per-tick accumulators
            ResetAccumulators(counties, provinces, realms);
            BuildHasStockByGood(counties, provinces, realms);

            // ── TAXATION PASS (Phases 1-5) ──────────────────────────────
            for (int g = 0; g < Goods.Count; g++)
            {
                float retainPerPop = Goods.DurableRetainPerPop[g] > 0f
                    ? Goods.DurableRetainPerPop[g]
                    : Goods.Defs[g].Need == NeedCategory.Staple
                        ? Goods.StapleIdealPerPop[g]
                        : ConsumptionPerPop[g];
                float countyAdmin = Goods.CountyAdminPerPop[g];
                float provAdmin = Goods.ProvinceAdminPerPop[g];
                float realmAdmin = Goods.RealmAdminPerPop[g];

                if (!_hasStockByGood[g] && retainPerPop <= 0f
                    && countyAdmin <= 0f && provAdmin <= 0f && realmAdmin <= 0f)
                    continue;

                // Phase 1: County administrative consumption (building upkeep)
                if (countyAdmin > 0f)
                {
                    for (int i = 0; i < counties.Length; i++)
                    {
                        var ce = counties[i];
                        if (ce == null) continue;
                        float need = ce.Population * countyAdmin;
                        float consumed = Math.Min(ce.Stock[g], need);
                        ce.Stock[g] -= consumed;
                        if (consumed < need)
                            realms[_countyToRealm[i]].Deficit[g] += need - consumed;
                    }
                }

                // Phase 2: Duke taxes surplus counties → provincial stockpile
                // Durables (TargetStockPerPop > 0) are excluded — they flow via trade, not taxation.
                // Taxing them creates a black hole (relief only returns staples).
                bool taxable = (Goods.HasDirectDemand[g] && Goods.TargetStockPerPop[g] <= 0f)
                             || Goods.IsPreciousMetal(g);
                if (taxable)
                {
                    float taxRate = Goods.IsPreciousMetal(g) ? PreciousMetalTaxRate : DucalTaxRate;
                    for (int p = 0; p < _provinceIds.Length; p++)
                    {
                        int provId = _provinceIds[p];
                        var pe = provinces[provId];
                        var countyIds = _provinceCounties[provId];

                        for (int c = 0; c < countyIds.Length; c++)
                        {
                            int countyId = countyIds[c];
                            var ce = counties[countyId];
                            float retain = ce.Population * retainPerPop;
                            _countyRetainTarget[countyId] = retain;
                            float surplus = ce.Stock[g] - retain;

                            if (surplus > 0f)
                            {
                                float tax = taxRate * surplus;
                                ce.Stock[g] -= tax;
                                ce.TaxPaid[g] += tax;
                                pe.Stockpile[g] += tax;
                                pe.TaxCollected[g] += tax;

                                if (!Goods.IsPreciousMetal(g))
                                {
                                    float owed = tax * prices[g] * TaxPaymentDiscount;
                                    float paid = Math.Min(owed, pe.Treasury);
                                    pe.Treasury -= paid;
                                    pe.TaxCrownsPaid += paid;
                                    ce.Treasury += paid;
                                    ce.TaxCrownsReceived += paid;
                                }
                            }
                        }
                    }
                }

                // Phase 3: Provincial administrative consumption (infrastructure)
                if (provAdmin > 0f)
                {
                    for (int p = 0; p < _provinceIds.Length; p++)
                    {
                        int provId = _provinceIds[p];
                        var pe = provinces[provId];
                        float need = _provincePop[provId] * provAdmin;
                        float consumed = Math.Min(pe.Stockpile[g], need);
                        pe.Stockpile[g] -= consumed;
                        if (consumed < need)
                            realms[_provinceToRealm[provId]].Deficit[g] += need - consumed;
                    }
                }

                // Phase 4: King taxes surplus provincial stockpiles → royal stockpile
                if (taxable)
                {
                    float royalRate = Goods.IsPreciousMetal(g) ? PreciousMetalTaxRate : RoyalTaxRate;
                    for (int r = 0; r < _realmIds.Length; r++)
                    {
                        int realmId = _realmIds[r];
                        var re = realms[realmId];
                        var provIds = _realmProvinces[realmId];

                        for (int p = 0; p < provIds.Length; p++)
                        {
                            var pe = provinces[provIds[p]];
                            if (pe.Stockpile[g] <= 0f) continue;

                            float tax = royalRate * pe.Stockpile[g];
                            pe.Stockpile[g] -= tax;
                            re.Stockpile[g] += tax;
                            re.TaxCollected[g] += tax;

                            if (!Goods.IsPreciousMetal(g))
                            {
                                float owed = tax * prices[g] * TaxPaymentDiscount;
                                float paid = Math.Min(owed, re.Treasury);
                                re.Treasury -= paid;
                                re.TaxCrownsPaid += paid;
                                pe.Treasury += paid;
                                pe.RoyalTaxCrownsReceived += paid;
                            }
                        }
                    }
                }

                // Phase 5: Royal administrative consumption (military upkeep)
                if (realmAdmin > 0f)
                {
                    for (int r = 0; r < _realmIds.Length; r++)
                    {
                        int realmId = _realmIds[r];
                        var re = realms[realmId];
                        float need = _realmPop[realmId] * realmAdmin;
                        float consumed = Math.Min(re.Stockpile[g], need);
                        re.Stockpile[g] -= consumed;
                        if (consumed < need)
                            re.Deficit[g] += need - consumed;
                    }
                }
            }

            // ── MINTING ─────────────────────────────────────────────────
            for (int r = 0; r < _realmIds.Length; r++)
            {
                int realmId = _realmIds[r];
                var re = realms[realmId];

                float gold = re.Stockpile[(int)GoodType.GoldOre];
                float silver = re.Stockpile[(int)GoodType.SilverOre];
                re.Stockpile[(int)GoodType.GoldOre] = 0f;
                re.Stockpile[(int)GoodType.SilverOre] = 0f;

                float crowns = gold * Goods.GoldSmeltingYield * Goods.CrownsPerKgGold
                             + silver * Goods.SilverSmeltingYield * Goods.CrownsPerKgSilver;
                re.Treasury += crowns;
                re.GoldMinted = gold;
                re.SilverMinted = silver;
                re.CrownsMinted = crowns;
            }

            // ── TRADE PASSES ──────────────────────────────────────────
            // Cascading scope: local trade runs first, consuming surplus before wider passes.
            // Intra-province (market fee only) → cross-province (+toll) → cross-realm (+tariff).

            // Intra-province: counties within the same province
            for (int p = 0; p < _provinceIds.Length; p++)
            {
                var ids = _provinceCounties[_provinceIds[p]];
                if (ids.Length <= 1) continue;
                ExecuteTradePass(counties, ids, _surplusBuf, prices,
                    0f, 0f, provinces, realms, TradeScope.IntraProvince);
            }

            // Cross-province: counties across provinces within the same realm
            for (int r = 0; r < _realmIds.Length; r++)
            {
                if (_realmProvinces[_realmIds[r]].Length <= 1) continue;
                ExecuteTradePass(counties, _realmCounties[_realmIds[r]], _surplusBuf, prices,
                    CrossProvTollRate, 0f, provinces, realms, TradeScope.CrossProvince);
            }

            // Cross-realm: all counties globally
            if (_realmIds.Length > 1)
                ExecuteTradePass(counties, _allCountyIds, _surplusBuf, prices,
                    CrossProvTollRate, CrossRealmTariffRate, provinces, realms, TradeScope.CrossRealm);

            // ── RELIEF PASS ──────────────────────────────────────────
            // Emergency backstop AFTER trade: staple goods only, distressed counties only.
            // Trade handles routine redistribution; relief prevents famine.
            var stapleGoods = Goods.StapleGoods;
            for (int si = 0; si < stapleGoods.Length; si++)
            {
                int g = stapleGoods[si];
                float retainPerPop = Goods.StapleIdealPerPop[g];

                if (!_hasStockByGood[g] && retainPerPop <= 0f)
                    continue;

                BuildRetainDeficitsForGood(g, retainPerPop, counties, retainTargetsReady: false);

                // King distributes royal stockpile to deficit provinces
                Span<float> provDeficits = stackalloc float[_maxProvincesPerRealm];
                for (int r = 0; r < _realmIds.Length; r++)
                {
                    int realmId = _realmIds[r];
                    var re = realms[realmId];
                    if (re.Stockpile[g] <= 0f) continue;

                    var provIds = _realmProvinces[realmId];

                    float totalDeficit = 0f;
                    for (int pi = 0; pi < provIds.Length; pi++)
                    {
                        int provId = provIds[pi];
                        float d = _provinceRetainDeficit[provId] - provinces[provId].Stockpile[g];
                        if (d < 0f) d = 0f;
                        provDeficits[pi] = d;
                        if (d > 0f)
                            totalDeficit += d;
                    }

                    if (totalDeficit <= 0f) continue;

                    float available = re.Stockpile[g];
                    for (int pi = 0; pi < provIds.Length; pi++)
                    {
                        if (provDeficits[pi] <= 0f) continue;

                        float share = provDeficits[pi] / totalDeficit;
                        float relief = Math.Min(share * available, provDeficits[pi]);
                        var pe = provinces[provIds[pi]];
                        pe.Stockpile[g] += relief;
                        re.Stockpile[g] -= relief;
                        re.ReliefGiven[g] += relief;
                    }
                }

                // Duke distributes provincial stockpile to deficit counties
                for (int p = 0; p < _provinceIds.Length; p++)
                {
                    int provId = _provinceIds[p];
                    var pe = provinces[provId];
                    if (pe.Stockpile[g] <= 0f) continue;

                    var countyIds = _provinceCounties[provId];
                    float totalDeficit = _provinceRetainDeficit[provId];

                    if (totalDeficit <= 0f) continue;

                    float available = pe.Stockpile[g];
                    for (int c = 0; c < countyIds.Length; c++)
                    {
                        int countyId = countyIds[c];
                        float deficit = _countyRetainDeficit[countyId];
                        if (deficit <= 0f) continue;
                        var ce = counties[countyId];
                        if (ce == null) continue;

                        float share = deficit / totalDeficit;
                        float relief = Math.Min(share * available, deficit);
                        ce.Stock[g] += relief;
                        ce.Relief[g] += relief;
                        pe.Stockpile[g] -= relief;
                        pe.ReliefGiven[g] += relief;
                    }
                }
            }

            // Phase 8 (deficit scan) moved to InterRealmTradeSystem.
        }

        void ExecuteTradePass(
            CountyEconomy[] counties, int[] countyIds, float[] surplusBuf,
            float[] prices, float tollRate, float tariffRate,
            ProvinceEconomy[] provinces, RealmEconomy[] realms,
            TradeScope scope)
        {
            var buyPriority = Goods.BuyPriority;

            for (int bp = 0; bp < buyPriority.Length; bp++)
            {
                int g = buyPriority[bp];
                float price = prices[g];
                if (price <= 0f) continue;

                float retainPerPop = Goods.DurableRetainPerPop[g] > 0f
                    ? Goods.DurableRetainPerPop[g]
                    : Goods.Defs[g].Need == NeedCategory.Staple
                        ? Goods.StapleIdealPerPop[g]
                        : ConsumptionPerPop[g];

                float costPerUnit = price * (1f + tollRate + tariffRate + MarketFeeRate);

                float totalSupply = 0f;
                float totalDemand = 0f;

                for (int c = 0; c < countyIds.Length; c++)
                {
                    var ce = counties[countyIds[c]];
                    float surplus = ce.Stock[g] - ce.Population * retainPerPop - ce.FacilityInputNeed[g];
                    surplusBuf[c] = surplus;
                    if (surplus > 0f)
                        totalSupply += surplus;
                    else if (surplus < 0f)
                    {
                        float rawDemand = -surplus;
                        float affordable = ce.Treasury / costPerUnit;
                        float demand = Math.Min(rawDemand, affordable);
                        surplusBuf[c] = -demand;
                        totalDemand += demand;
                    }
                }

                if (totalSupply <= 0f || totalDemand <= 0f) continue;

                float fillRatio = Math.Min(1f, totalSupply / totalDemand);
                float sellRatio = Math.Min(1f, totalDemand / totalSupply);

                for (int c = 0; c < countyIds.Length; c++)
                {
                    float s = surplusBuf[c];
                    int countyId = countyIds[c];
                    var ce = counties[countyId];

                    if (s > 0f)
                    {
                        float sold = s * sellRatio;
                        ce.Stock[g] -= sold;
                        float earned = sold * price;
                        ce.Treasury += earned;

                        switch (scope)
                        {
                            case TradeScope.IntraProvince:
                                ce.TradeSold[g] += sold;
                                ce.TradeCrownsEarned += earned;
                                break;
                            case TradeScope.CrossProvince:
                                ce.CrossProvTradeSold[g] += sold;
                                ce.CrossProvTradeCrownsEarned += earned;
                                break;
                            case TradeScope.CrossRealm:
                                ce.CrossRealmTradeSold[g] += sold;
                                ce.CrossRealmTradeCrownsEarned += earned;
                                int sellerRealmId = _countyToRealm[countyId];
                                realms[sellerRealmId].TradeExports[g] += sold;
                                realms[sellerRealmId].TradeRevenue += earned;
                                break;
                        }
                    }
                    else if (s < 0f)
                    {
                        float bought = (-s) * fillRatio;
                        ce.Stock[g] += bought;
                        float goodsCost = bought * price;
                        float toll = tollRate > 0f ? bought * price * tollRate : 0f;
                        float tariff = tariffRate > 0f ? bought * price * tariffRate : 0f;
                        float marketFee = bought * price * MarketFeeRate;
                        ce.Treasury -= goodsCost + toll + tariff + marketFee;

                        switch (scope)
                        {
                            case TradeScope.IntraProvince:
                                ce.TradeBought[g] += bought;
                                ce.TradeCrownsSpent += goodsCost;
                                break;
                            case TradeScope.CrossProvince:
                                ce.CrossProvTradeBought[g] += bought;
                                ce.CrossProvTradeCrownsSpent += goodsCost;
                                ce.TradeTollsPaid += toll;
                                provinces[_countyToProvince[countyId]].TradeTollsCollected += toll;
                                provinces[_countyToProvince[countyId]].Treasury += toll;
                                break;
                            case TradeScope.CrossRealm:
                                ce.CrossRealmTradeBought[g] += bought;
                                ce.CrossRealmTradeCrownsSpent += goodsCost;
                                ce.CrossRealmTollsPaid += toll;
                                ce.CrossRealmTariffsPaid += tariff;
                                int provId = _countyToProvince[countyId];
                                provinces[provId].TradeTollsCollected += toll;
                                provinces[provId].Treasury += toll;
                                int realmId = _countyToRealm[countyId];
                                realms[realmId].TradeTariffsCollected += tariff;
                                realms[realmId].Treasury += tariff;
                                realms[realmId].TradeImports[g] += bought;
                                realms[realmId].TradeSpending += goodsCost;
                                break;
                        }

                        if (_marketCountyId >= 0)
                        {
                            counties[_marketCountyId].Treasury += marketFee;
                            counties[_marketCountyId].MarketFeesReceived += marketFee;
                        }
                    }
                }
            }
        }

        void BuildHasStockByGood(
            CountyEconomy[] counties, ProvinceEconomy[] provinces, RealmEconomy[] realms)
        {
            if (_hasStockByGood == null || _hasStockByGood.Length != Goods.Count)
                _hasStockByGood = new bool[Goods.Count];
            else
                Array.Clear(_hasStockByGood, 0, _hasStockByGood.Length);

            for (int i = 0; i < counties.Length; i++)
            {
                var ce = counties[i];
                if (ce == null) continue;
                for (int g = 0; g < Goods.Count; g++)
                    if (ce.Stock[g] > 0f) _hasStockByGood[g] = true;
            }

            for (int p = 0; p < _provinceIds.Length; p++)
            {
                var pe = provinces[_provinceIds[p]];
                for (int g = 0; g < Goods.Count; g++)
                    if (pe.Stockpile[g] > 0f) _hasStockByGood[g] = true;
            }

            for (int r = 0; r < _realmIds.Length; r++)
            {
                var re = realms[_realmIds[r]];
                for (int g = 0; g < Goods.Count; g++)
                    if (re.Stockpile[g] > 0f) _hasStockByGood[g] = true;
            }
        }

        void BuildRetainDeficitsForGood(
            int goodIdx, float retainPerPop, CountyEconomy[] counties, bool retainTargetsReady)
        {
            for (int p = 0; p < _provinceIds.Length; p++)
            {
                int provId = _provinceIds[p];
                var countyIds = _provinceCounties[provId];
                float totalDeficit = 0f;

                for (int c = 0; c < countyIds.Length; c++)
                {
                    int countyId = countyIds[c];
                    var ce = counties[countyId];
                    float retain = _countyRetainTarget[countyId];
                    float deficit = 0f;
                    if (ce != null)
                    {
                        if (!retainTargetsReady)
                        {
                            retain = ce.Population * retainPerPop;
                            _countyRetainTarget[countyId] = retain;
                        }

                        // Only distressed counties qualify for relief
                        if (ce.BasicSatisfaction < ReliefSatisfactionThreshold)
                        {
                            deficit = retain - ce.Stock[goodIdx];
                            if (deficit < 0f) deficit = 0f;
                        }
                    }

                    _countyRetainDeficit[countyId] = deficit;
                    totalDeficit += deficit;
                }

                _provinceRetainDeficit[provId] = totalDeficit;
            }
        }

        void ResetAccumulators(
            CountyEconomy[] counties, ProvinceEconomy[] provinces, RealmEconomy[] realms)
        {
            for (int p = 0; p < _provinceIds.Length; p++)
            {
                int provId = _provinceIds[p];
                var pe = provinces[provId];
                Array.Clear(pe.TaxCollected, 0, pe.TaxCollected.Length);
                Array.Clear(pe.ReliefGiven, 0, pe.ReliefGiven.Length);

                pe.TaxCrownsPaid = 0f;
                pe.ReliefCrownsReceived = 0f;
                pe.RoyalTaxCrownsReceived = 0f;
                pe.RoyalReliefCrownsPaid = 0f;
                pe.TradeTollsCollected = 0f;

                var countyIds = _provinceCounties[provId];
                for (int c = 0; c < countyIds.Length; c++)
                {
                    var ce = counties[countyIds[c]];
                    Array.Clear(ce.TaxPaid, 0, ce.TaxPaid.Length);
                    Array.Clear(ce.Relief, 0, ce.Relief.Length);
                    Array.Clear(ce.TradeBought, 0, ce.TradeBought.Length);
                    Array.Clear(ce.TradeSold, 0, ce.TradeSold.Length);
                    Array.Clear(ce.CrossProvTradeBought, 0, ce.CrossProvTradeBought.Length);
                    Array.Clear(ce.CrossProvTradeSold, 0, ce.CrossProvTradeSold.Length);
                    ce.TaxCrownsReceived = 0f;
                    ce.ReliefCrownsPaid = 0f;
                    ce.TradeCrownsSpent = 0f;
                    ce.TradeCrownsEarned = 0f;
                    ce.CrossProvTradeCrownsSpent = 0f;
                    ce.CrossProvTradeCrownsEarned = 0f;
                    ce.TradeTollsPaid = 0f;
                    Array.Clear(ce.CrossRealmTradeBought, 0, ce.CrossRealmTradeBought.Length);
                    Array.Clear(ce.CrossRealmTradeSold, 0, ce.CrossRealmTradeSold.Length);
                    ce.CrossRealmTradeCrownsSpent = 0f;
                    ce.CrossRealmTradeCrownsEarned = 0f;
                    ce.CrossRealmTollsPaid = 0f;
                    ce.CrossRealmTariffsPaid = 0f;
                    ce.MarketFeesReceived = 0f;
                }
            }

            for (int r = 0; r < _realmIds.Length; r++)
            {
                var re = realms[_realmIds[r]];
                Array.Clear(re.TaxCollected, 0, re.TaxCollected.Length);
                Array.Clear(re.ReliefGiven, 0, re.ReliefGiven.Length);
                re.GoldMinted = 0f;
                re.SilverMinted = 0f;
                re.CrownsMinted = 0f;
                re.TaxCrownsPaid = 0f;
                re.ReliefCrownsReceived = 0f;
                re.TradeSpending = 0f;
                re.TradeRevenue = 0f;
                Array.Clear(re.TradeImports, 0, re.TradeImports.Length);
                Array.Clear(re.TradeExports, 0, re.TradeExports.Length);
                re.TradeTariffsCollected = 0f;
                Array.Clear(re.Deficit, 0, re.Deficit.Length);
            }
        }

        void BuildMappings(SimulationState state, MapData mapData)
        {
            // Province → county
            int maxProvId = 0;
            foreach (var prov in mapData.Provinces)
                if (prov.Id > maxProvId) maxProvId = prov.Id;

            var provCountyLists = new List<int>[maxProvId + 1];
            var provIdList = new List<int>(mapData.Provinces.Count);

            foreach (var prov in mapData.Provinces)
            {
                provCountyLists[prov.Id] = new List<int>();
                provIdList.Add(prov.Id);
            }

            foreach (var county in mapData.Counties)
            {
                int provId = county.ProvinceId;
                if (provId >= 0 && provId < provCountyLists.Length &&
                    provCountyLists[provId] != null)
                {
                    provCountyLists[provId].Add(county.Id);
                }
            }

            _provinceCounties = new int[maxProvId + 1][];
            foreach (var prov in mapData.Provinces)
                _provinceCounties[prov.Id] = provCountyLists[prov.Id].ToArray();
            _provinceIds = provIdList.ToArray();

            // Realm → province
            int maxRealmId = 0;
            foreach (var realm in mapData.Realms)
                if (realm.Id > maxRealmId) maxRealmId = realm.Id;

            var realmProvLists = new List<int>[maxRealmId + 1];
            var realmIdList = new List<int>(mapData.Realms.Count);

            foreach (var realm in mapData.Realms)
            {
                realmProvLists[realm.Id] = new List<int>();
                realmIdList.Add(realm.Id);
            }

            foreach (var prov in mapData.Provinces)
            {
                int realmId = prov.RealmId;
                if (realmId >= 0 && realmId < realmProvLists.Length &&
                    realmProvLists[realmId] != null)
                {
                    realmProvLists[realmId].Add(prov.Id);
                }
            }

            _realmProvinces = new int[maxRealmId + 1][];
            _maxProvincesPerRealm = 0;
            foreach (var realm in mapData.Realms)
            {
                var arr = realmProvLists[realm.Id].ToArray();
                _realmProvinces[realm.Id] = arr;
                if (arr.Length > _maxProvincesPerRealm)
                    _maxProvincesPerRealm = arr.Length;
            }
            _realmIds = realmIdList.ToArray();

            // County → realm, County → province, and Province → realm lookups
            _provinceToRealm = new int[maxProvId + 1];
            foreach (var prov in mapData.Provinces)
                _provinceToRealm[prov.Id] = prov.RealmId;

            int maxCountyId = 0;
            foreach (var county in mapData.Counties)
                if (county.Id > maxCountyId) maxCountyId = county.Id;

            _countyToRealm = new int[maxCountyId + 1];
            _countyToProvince = new int[maxCountyId + 1];
            _countyRetainDeficit = new float[maxCountyId + 1];
            _countyRetainTarget = new float[maxCountyId + 1];
            _provinceRetainDeficit = new float[maxProvId + 1];
            foreach (var county in mapData.Counties)
            {
                int provId = county.ProvinceId;
                _countyToProvince[county.Id] = provId;
                _countyToRealm[county.Id] = provId >= 0 && provId < _provinceToRealm.Length
                    ? _provinceToRealm[provId]
                    : 0;
            }

            // Realm → counties (flattened from realm → provinces → counties)
            _realmCounties = new int[maxRealmId + 1][];
            foreach (var realm in mapData.Realms)
            {
                var allCounties = new List<int>();
                var provIds = _realmProvinces[realm.Id];
                for (int p = 0; p < provIds.Length; p++)
                {
                    var cIds = _provinceCounties[provIds[p]];
                    for (int c = 0; c < cIds.Length; c++)
                        allCounties.Add(cIds[c]);
                }
                _realmCounties[realm.Id] = allCounties.ToArray();
            }

            // All county IDs for cross-realm trade
            var allCountyList = new List<int>(mapData.Counties.Count);
            foreach (var county in mapData.Counties)
                allCountyList.Add(county.Id);
            _allCountyIds = allCountyList.ToArray();
            _totalCountyCount = _allCountyIds.Length;
            _surplusBuf = new float[_totalCountyCount];

            _marketCountyId = state.Economy.MarketCountyId;
        }
    }
}
