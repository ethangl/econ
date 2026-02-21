using System;
using System.Collections.Generic;
using EconSim.Core.Data;
using EconSim.Core.Simulation;
using EconSim.Core.Simulation.Systems;

namespace EconSim.Core.Economy
{
    enum TradeScope { IntraProvince, CrossProvince, CrossRealm }

    /// <summary>
    /// Monetary taxation with ducal granary. Runs daily AFTER EconomySystem.
    ///
    /// Phase 1: County admin consumption (building upkeep — unchanged)
    /// Phase 2: Precious metal confiscation (100% county gold/silver → realm)
    /// Phase 3: Monetary taxation (county → province production tax, province → realm revenue share)
    /// Phase 4: Admin wages (province + realm spend on admin, wages flow to counties)
    /// Phase 5: Minting (precious metals → crowns)
    /// Phase 6: Trade passes (intra-prov, cross-prov, cross-realm with fees)
    /// Phase 7: Ducal granary requisition (duke buys preserved staples from surplus counties)
    /// Phase 8: Emergency relief (duke distributes granary to distressed counties)
    /// </summary>
    public class FiscalSystem : ITickSystem
    {
        public string Name => "Fiscal";
        public int TickInterval => SimulationConfig.Intervals.Daily;

        static readonly float[] ConsumptionPerPop = Goods.ConsumptionPerPop;

        /// <summary>Fraction of daily production value paid to province (tithe-style).</summary>
        const float DucalProductionTaxRate = 0.013f;

        /// <summary>Fraction of province's collected tax revenue passed up to realm.</summary>
        const float RoyalRevenueShareRate = 0.40f;

        /// <summary>Precious metals are crown property — 100% tax rate (regal right).</summary>
        const float PreciousMetalTaxRate = 1.0f;

        /// <summary>Days of food reserve the duke aims to maintain.</summary>
        const float GranaryDaysBuffer = 7f;

        /// <summary>Duke pays 60% of market price when requisitioning goods.</summary>
        const float GranaryRequisitionDiscount = 0.60f;

        /// <summary>Fraction of gap filled per day (gradual fill).</summary>
        const float GranaryFillRate = 0.05f;


        /// <summary>Buyers pay 5% toll on cross-province trade (paid to own province treasury).</summary>
        const float CrossProvTollRate = 0.05f;

        /// <summary>Buyers pay 10% tariff on cross-realm trade (paid to own realm treasury).</summary>
        const float CrossRealmTariffRate = 0.10f;

        /// <summary>Buyers pay 2% market fee on all trade (paid to market county treasury).</summary>
        const float MarketFeeRate = 0.02f;

        /// <summary>Counties with BasicSatisfaction below this threshold receive feudal relief.</summary>
        const float ReliefSatisfactionThreshold = 0.70f;

        /// <summary>Province admin cost in crowns per capita per day (computed from per-good rates × base prices).</summary>
        static readonly float ProvinceAdminCostPerPop;

        /// <summary>Realm admin cost in crowns per capita per day (computed from per-good rates × base prices).</summary>
        static readonly float RealmAdminCostPerPop;

        static FiscalSystem()
        {
            float provCost = 0f;
            float realmCost = 0f;
            for (int g = 0; g < Goods.Count; g++)
            {
                provCost += Goods.ProvinceAdminPerPop[g] * Goods.BasePrice[g];
                realmCost += Goods.RealmAdminPerPop[g] * Goods.BasePrice[g];
            }
            ProvinceAdminCostPerPop = provCost;
            RealmAdminCostPerPop = realmCost;
        }


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

            // ── PHASE 1: County admin consumption ───────────────────
            for (int g = 0; g < Goods.Count; g++)
            {
                float countyAdmin = Goods.CountyAdminPerPop[g];
                if (countyAdmin <= 0f) continue;

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

            // ── PHASE 2: Precious metal confiscation ────────────────
            // 100% of county gold/silver ore → realm stockpile (simplified from 2-tier)
            for (int r = 0; r < _realmIds.Length; r++)
            {
                int realmId = _realmIds[r];
                var re = realms[realmId];
                var countyIds = _realmCounties[realmId];

                for (int c = 0; c < countyIds.Length; c++)
                {
                    var ce = counties[countyIds[c]];
                    if (ce == null) continue;

                    for (int g = 0; g < Goods.Count; g++)
                    {
                        if (!Goods.IsPreciousMetal(g)) continue;
                        float amount = ce.Stock[g];
                        if (amount > 0f)
                        {
                            ce.Stock[g] = 0f;
                            re.Stockpile[g] += amount;
                        }
                    }
                }
            }

            // ── PHASE 3: Monetary taxation ──────────────────────────
            // Counties pay production-value tax to province (tithe-style)
            for (int p = 0; p < _provinceIds.Length; p++)
            {
                int provId = _provinceIds[p];
                var pe = provinces[provId];
                var countyIds = _provinceCounties[provId];

                for (int c = 0; c < countyIds.Length; c++)
                {
                    var ce = counties[countyIds[c]];
                    if (ce == null) continue;

                    float productionValue = 0f;
                    for (int g = 0; g < Goods.Count; g++)
                        productionValue += ce.Production[g] * prices[g];

                    float tax = Math.Min(productionValue * DucalProductionTaxRate, ce.Treasury);
                    if (tax > 0f)
                    {
                        ce.Treasury -= tax;
                        ce.MonetaryTaxPaid += tax;
                        pe.Treasury += tax;
                        pe.MonetaryTaxCollected += tax;
                    }
                }
            }

            // Provinces pay revenue share to realm
            for (int p = 0; p < _provinceIds.Length; p++)
            {
                int provId = _provinceIds[p];
                var pe = provinces[provId];
                int realmId = _provinceToRealm[provId];
                var re = realms[realmId];

                float tax = Math.Min(pe.MonetaryTaxCollected * RoyalRevenueShareRate, pe.Treasury);
                if (tax > 0f)
                {
                    pe.Treasury -= tax;
                    pe.MonetaryTaxPaidToRealm += tax;
                    re.Treasury += tax;
                    re.MonetaryTaxCollected += tax;
                }
            }

            // ── PHASE 4: Admin wages ──────────────────────────────────
            // Admin costs are wages paid to county workers (not destroyed).
            // Province deducts from treasury, distributes to counties proportional to pop.
            for (int p = 0; p < _provinceIds.Length; p++)
            {
                int provId = _provinceIds[p];
                var pe = provinces[provId];
                float provPop = _provincePop[provId];
                float cost = Math.Min(provPop * ProvinceAdminCostPerPop, pe.Treasury);
                pe.Treasury -= cost;
                pe.AdminCrownsCost += cost;

                if (cost > 0f && provPop > 0f)
                {
                    var cIds = _provinceCounties[provId];
                    for (int c = 0; c < cIds.Length; c++)
                    {
                        var ce = counties[cIds[c]];
                        if (ce == null) continue;
                        ce.Treasury += cost * (ce.Population / provPop);
                    }
                }
            }

            // Realm admin wages distributed to all counties in realm.
            for (int r = 0; r < _realmIds.Length; r++)
            {
                int realmId = _realmIds[r];
                var re = realms[realmId];
                float rPop = _realmPop[realmId];
                float cost = Math.Min(rPop * RealmAdminCostPerPop, re.Treasury);
                re.Treasury -= cost;
                re.AdminCrownsCost += cost;

                if (cost > 0f && rPop > 0f)
                {
                    var cIds = _realmCounties[realmId];
                    for (int c = 0; c < cIds.Length; c++)
                    {
                        var ce = counties[cIds[c]];
                        if (ce == null) continue;
                        ce.Treasury += cost * (ce.Population / rPop);
                    }
                }
            }

            // ── PHASE 5: Minting ────────────────────────────────────
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

            // ── PHASE 6: Trade passes ───────────────────────────────
            // Cascading scope: local trade runs first, consuming surplus before wider passes.

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

            // ── PHASE 7: Ducal granary requisition ──────────────────
            // Duke buys preserved staples from surplus counties at a discount.
            var stapleGoods = Goods.StapleGoods;
            for (int p = 0; p < _provinceIds.Length; p++)
            {
                int provId = _provinceIds[p];
                var pe = provinces[provId];
                var countyIds = _provinceCounties[provId];
                float provPop = _provincePop[provId];

                for (int si = 0; si < stapleGoods.Length; si++)
                {
                    int g = stapleGoods[si];
                    float target = GranaryDaysBuffer * provPop * Goods.StapleIdealPerPop[g];
                    float gap = target - pe.Stockpile[g];
                    if (gap <= 0f) continue;

                    float fillAmount = gap * GranaryFillRate;
                    float unitCost = prices[g] * GranaryRequisitionDiscount;
                    float maxAffordable = unitCost > 0f ? pe.Treasury / unitCost : float.MaxValue;
                    fillAmount = Math.Min(fillAmount, maxAffordable);
                    if (fillAmount <= 0f) continue;

                    // Collect proportionally from surplus counties
                    float totalSurplus = 0f;
                    float retainPerPop = Goods.StapleIdealPerPop[g];
                    for (int c = 0; c < countyIds.Length; c++)
                    {
                        var ce = counties[countyIds[c]];
                        if (ce == null) continue;
                        float surplus = ce.Stock[g] - ce.Population * retainPerPop;
                        if (surplus > 0f) totalSurplus += surplus;
                    }

                    if (totalSurplus <= 0f) continue;
                    float collectRatio = Math.Min(1f, fillAmount / totalSurplus);

                    float totalCollected = 0f;
                    for (int c = 0; c < countyIds.Length; c++)
                    {
                        int countyId = countyIds[c];
                        var ce = counties[countyId];
                        if (ce == null) continue;
                        float surplus = ce.Stock[g] - ce.Population * retainPerPop;
                        if (surplus <= 0f) continue;

                        float take = surplus * collectRatio;
                        ce.Stock[g] -= take;
                        ce.GranaryRequisitioned[g] += take;
                        totalCollected += take;

                        // Pay county at discount
                        float payment = take * unitCost;
                        ce.Treasury += payment;
                        ce.GranaryRequisitionCrownsReceived += payment;
                    }

                    if (totalCollected > 0f)
                    {
                        float totalCost = totalCollected * unitCost;
                        pe.Treasury -= totalCost;
                        pe.GranaryRequisitionCrownsSpent += totalCost;
                        pe.Stockpile[g] += totalCollected;
                        pe.GranaryRequisitioned[g] += totalCollected;
                    }
                }
            }

            // ── PHASE 8: Relief pass ────────────────────────────────
            // Emergency backstop AFTER trade: staple goods only, distressed counties only.
            for (int si = 0; si < stapleGoods.Length; si++)
            {
                int g = stapleGoods[si];
                float retainPerPop = Goods.StapleIdealPerPop[g];

                BuildRetainDeficitsForGood(g, retainPerPop, counties, retainTargetsReady: false);

                // Duke distributes provincial granary to deficit counties
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
                Array.Clear(pe.ReliefGiven, 0, pe.ReliefGiven.Length);
                Array.Clear(pe.GranaryRequisitioned, 0, pe.GranaryRequisitioned.Length);

                pe.MonetaryTaxCollected = 0f;
                pe.MonetaryTaxPaidToRealm = 0f;
                pe.AdminCrownsCost = 0f;
                pe.GranaryRequisitionCrownsSpent = 0f;
                pe.TradeTollsCollected = 0f;

                var countyIds = _provinceCounties[provId];
                for (int c = 0; c < countyIds.Length; c++)
                {
                    var ce = counties[countyIds[c]];
                    Array.Clear(ce.Relief, 0, ce.Relief.Length);
                    Array.Clear(ce.TradeBought, 0, ce.TradeBought.Length);
                    Array.Clear(ce.TradeSold, 0, ce.TradeSold.Length);
                    Array.Clear(ce.CrossProvTradeBought, 0, ce.CrossProvTradeBought.Length);
                    Array.Clear(ce.CrossProvTradeSold, 0, ce.CrossProvTradeSold.Length);
                    Array.Clear(ce.GranaryRequisitioned, 0, ce.GranaryRequisitioned.Length);
                    ce.MonetaryTaxPaid = 0f;
                    ce.GranaryRequisitionCrownsReceived = 0f;
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
                re.GoldMinted = 0f;
                re.SilverMinted = 0f;
                re.CrownsMinted = 0f;
                re.MonetaryTaxCollected = 0f;
                re.AdminCrownsCost = 0f;
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
