using System;
using System.Collections.Generic;
using EconSim.Core.Data;
using EconSim.Core.Simulation;
using EconSim.Core.Simulation.Systems;

namespace EconSim.Core.Economy
{
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
    public class TradeSystem : ITickSystem
    {
        public string Name => "Trade";
        public int TickInterval => SimulationConfig.Intervals.Daily;

        static readonly float[] ConsumptionPerPop = Goods.ConsumptionPerPop;

        /// <summary>Fraction of surplus the duke takes from counties.</summary>
        const float DucalTaxRate = 0.20f;

        /// <summary>Fraction of provincial stockpile the king takes.</summary>
        const float RoyalTaxRate = 0.20f;

        /// <summary>Precious metals are crown property — 100% tax rate (regal right).</summary>
        const float PreciousMetalTaxRate = 1.0f;

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

        /// <summary>Population per province (cached at init).</summary>
        float[] _provincePop;

        /// <summary>Population per realm (cached at init).</summary>
        float[] _realmPop;

        /// <summary>County ID → Realm ID (for deficit ledger).</summary>
        int[] _countyToRealm;

        /// <summary>County ID → Province ID (for provincial facility quotas).</summary>
        int[] _countyToProvince;

        /// <summary>Province ID → Realm ID (for deficit ledger).</summary>
        int[] _provinceToRealm;

        /// <summary>Per-realm, per-output-good → list of county IDs with that facility.</summary>
        int[][][] _realmFacilityCounties;

        /// <summary>Per-realm, per-output-good → total pop of producing counties.</summary>
        float[][] _realmFacilityPop;

        /// <summary>Per-province, per-output-good → list of county IDs with that facility.</summary>
        int[][][] _provinceFacilityCounties;

        /// <summary>Per-province, per-output-good → total pop of producing counties.</summary>
        float[][] _provinceFacilityPop;

        /// <summary>True if this good is produced as output by any facility type.</summary>
        bool[] _isFacilityOutput;

        /// <summary>Effective demand per pop per day — ConsumptionPerPop for consumables, aggregate demand/pop for durables.</summary>
        float[] _effectiveDemandPerPop;

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

            // Precompute effective demand per pop for durable goods
            if (_effectiveDemandPerPop == null)
                _effectiveDemandPerPop = new float[Goods.Count];
            float totalPop = 0f;
            for (int i = 0; i < counties.Length; i++)
                if (counties[i] != null) totalPop += counties[i].Population;
            for (int g = 0; g < Goods.Count; g++)
            {
                if (Goods.TargetStockPerPop[g] > 0f && totalPop > 0f)
                {
                    float totalDemand = 0f;
                    for (int i = 0; i < counties.Length; i++)
                    {
                        var ce = counties[i];
                        if (ce == null) continue;
                        totalDemand += ce.Consumption[g] + ce.UnmetNeed[g];
                    }
                    _effectiveDemandPerPop[g] = totalDemand / totalPop;
                }
                else
                {
                    _effectiveDemandPerPop[g] = Goods.Defs[g].Need == NeedCategory.Staple
                        ? Goods.StapleIdealPerPop[g]
                        : ConsumptionPerPop[g];
                }
            }

            // Reset per-tick accumulators
            ResetAccumulators(counties, provinces, realms);
            BuildHasStockByGood(counties, provinces, realms);

            for (int g = 0; g < Goods.Count; g++)
            {
                // For durables, retain target stock level; for staples, retain ideal share; for others, retain one day's consumption
                float retainPerPop = Goods.TargetStockPerPop[g] > 0f
                    ? Goods.TargetStockPerPop[g]
                    : Goods.Defs[g].Need == NeedCategory.Staple
                        ? Goods.StapleIdealPerPop[g]
                        : ConsumptionPerPop[g];
                float countyAdmin = Goods.CountyAdminPerPop[g];
                float provAdmin = Goods.ProvinceAdminPerPop[g];
                float realmAdmin = Goods.RealmAdminPerPop[g];

                // Skip inert goods: no stock anywhere and no demand drivers this tick.
                // This avoids full phase passes for dormant intermediate goods.
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
                // Pure facility inputs (no direct demand, not precious) skip taxation
                bool taxable = Goods.HasDirectDemand[g] || Goods.IsPreciousMetal(g);
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

                // Precompute county/province retain deficits once per good (used by Phases 6 and 7).
                BuildRetainDeficitsForGood(g, retainPerPop, counties, taxable);

                // Phase 6: King distributes remainder to deficit provinces
                Span<float> provDeficits = stackalloc float[_maxProvincesPerRealm];
                for (int r = 0; r < _realmIds.Length; r++)
                {
                    int realmId = _realmIds[r];
                    var re = realms[realmId];
                    if (re.Stockpile[g] <= 0f) continue;

                    var provIds = _realmProvinces[realmId];

                    float totalDeficit = 0f;
                    for (int p = 0; p < provIds.Length; p++)
                    {
                        int provId = provIds[p];
                        float d = _provinceRetainDeficit[provId] - provinces[provId].Stockpile[g];
                        if (d < 0f) d = 0f;
                        provDeficits[p] = d;
                        if (d > 0f)
                            totalDeficit += d;
                    }

                    if (totalDeficit <= 0f) continue;

                    float available = re.Stockpile[g];
                    for (int p = 0; p < provIds.Length; p++)
                    {
                        if (provDeficits[p] <= 0f) continue;

                        float share = provDeficits[p] / totalDeficit;
                        float relief = Math.Min(share * available, provDeficits[p]);
                        provinces[provIds[p]].Stockpile[g] += relief;
                        re.Stockpile[g] -= relief;
                        re.ReliefGiven[g] += relief;
                    }
                }

                // Phase 7: Duke distributes provincial stockpile to deficit counties
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

            // Phase 8: Post-relief county deficit scan — record remaining unmet pop consumption per realm
            for (int g = 0; g < Goods.Count; g++)
            {
                float retainRate = Goods.TargetStockPerPop[g] > 0f
                    ? Goods.TargetStockPerPop[g]
                    : ConsumptionPerPop[g];
                if (retainRate <= 0f) continue;

                for (int i = 0; i < counties.Length; i++)
                {
                    var ce = counties[i];
                    if (ce == null) continue;
                    float shortfall = ce.Population * retainRate - ce.Stock[g];
                    if (shortfall > 0f)
                        realms[_countyToRealm[i]].Deficit[g] += shortfall;
                }
            }

            // Phase 5b: Mint precious metals into Crowns (realm treasury)
            // Runs after all per-good phases so gold/silver have been fully taxed up.
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

            // Phase 9a: Provincial facility quotas
            // Duke computes province need (consumption + county/province admin)
            // and distributes to local producing counties
            if (_provinceFacilityCounties != null)
            {
                for (int p = 0; p < _provinceIds.Length; p++)
                {
                    int provId = _provinceIds[p];
                    float provPopulation = _provincePop[provId];

                    for (int g = 0; g < Goods.Count; g++)
                    {
                        var producingCounties = _provinceFacilityCounties[provId][g];
                        if (producingCounties == null || producingCounties.Length == 0) continue;

                        float totalProducingPop = _provinceFacilityPop[provId][g];
                        if (totalProducingPop <= 0f) continue;

                        // Provincial need = pop consumption + county admin + province admin
                        float perCapNeed = _effectiveDemandPerPop[g]
                                         + Goods.CountyAdminPerPop[g]
                                         + Goods.ProvinceAdminPerPop[g];
                        float provinceTotalNeed = provPopulation * perCapNeed;
                        if (provinceTotalNeed <= 0f) continue;

                        for (int c = 0; c < producingCounties.Length; c++)
                        {
                            int countyId = producingCounties[c];
                            var ce = counties[countyId];
                            float share = ce.Population / totalProducingPop;
                            ce.FacilityQuota[g] += provinceTotalNeed * share;
                        }
                    }
                }
            }

            // Phase 9b: Realm facility quotas
            // King computes realm-specific needs (realm admin + provinces without local facilities)
            // and distributes to producing counties across the realm, additive to provincial quotas
            if (_realmFacilityCounties != null)
            {
                for (int r = 0; r < _realmIds.Length; r++)
                {
                    int realmId = _realmIds[r];
                    float realmPopulation = _realmPop[realmId];

                    for (int g = 0; g < Goods.Count; g++)
                    {
                        var producingCounties = _realmFacilityCounties[realmId][g];
                        if (producingCounties == null || producingCounties.Length == 0) continue;

                        float totalProducingPop = _realmFacilityPop[realmId][g];
                        if (totalProducingPop <= 0f) continue;

                        // Realm admin need
                        float realmQuota = realmPopulation * Goods.RealmAdminPerPop[g];

                        // Provinces without local production can't self-supply —
                        // king requisitions on their behalf
                        var provIds = _realmProvinces[realmId];
                        for (int pi = 0; pi < provIds.Length; pi++)
                        {
                            int provId = provIds[pi];
                            if (_provinceFacilityCounties != null
                                && provId < _provinceFacilityCounties.Length
                                && _provinceFacilityCounties[provId] != null
                                && _provinceFacilityCounties[provId][g] != null
                                && _provinceFacilityCounties[provId][g].Length > 0)
                                continue; // province handles its own need

                            float perCapNeed = _effectiveDemandPerPop[g]
                                             + Goods.CountyAdminPerPop[g]
                                             + Goods.ProvinceAdminPerPop[g];
                            realmQuota += _provincePop[provId] * perCapNeed;
                        }

                        if (realmQuota <= 0f) continue;

                        for (int c = 0; c < producingCounties.Length; c++)
                        {
                            int countyId = producingCounties[c];
                            var ce = counties[countyId];
                            float share = ce.Population / totalProducingPop;
                            ce.FacilityQuota[g] += realmQuota * share;
                        }
                    }
                }
            }

            // Phase 9c: Upstream quota propagation
            // If a facility's input is itself a facility output, propagate demand backward.
            // e.g. Smithy quota for Tools → Iron demand → FacilityQuota[Iron] → drives Smelter
            if (_isFacilityOutput != null && state.Economy.Facilities != null)
            {
                var facIndices = state.Economy.CountyFacilityIndices;
                var facilities = state.Economy.Facilities;
                for (int i = 0; i < counties.Length; i++)
                {
                    var ce = counties[i];
                    if (ce == null) continue;
                    if (i >= facIndices.Length || facIndices[i] == null || facIndices[i].Count == 0) continue;

                    var indices = facIndices[i];
                    for (int fi = 0; fi < indices.Count; fi++)
                    {
                        var def = facilities[indices[fi]].Def;
                        int outputGood = (int)def.OutputGood;
                        for (int ii = 0; ii < def.Inputs.Length; ii++)
                        {
                            int inputGood = (int)def.Inputs[ii].Good;
                            if (!_isFacilityOutput[inputGood]) continue;
                            float inputNeeded = ce.FacilityQuota[outputGood] * def.Inputs[ii].Amount / def.OutputAmount;
                            ce.FacilityQuota[inputGood] += inputNeeded;
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

                        deficit = retain - ce.Stock[goodIdx];
                        if (deficit < 0f) deficit = 0f;
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

                var countyIds = _provinceCounties[provId];
                for (int c = 0; c < countyIds.Length; c++)
                {
                    var ce = counties[countyIds[c]];
                    Array.Clear(ce.TaxPaid, 0, ce.TaxPaid.Length);
                    Array.Clear(ce.Relief, 0, ce.Relief.Length);
                    Array.Clear(ce.FacilityQuota, 0, ce.FacilityQuota.Length);
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
                re.TradeSpending = 0f;
                re.TradeRevenue = 0f;
                Array.Clear(re.Deficit, 0, re.Deficit.Length);
                Array.Clear(re.TradeImports, 0, re.TradeImports.Length);
                Array.Clear(re.TradeExports, 0, re.TradeExports.Length);
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

            // County → realm and Province → realm lookups
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
                _countyToRealm[county.Id] = provId >= 0 && provId < _provinceToRealm.Length
                    ? _provinceToRealm[provId]
                    : 0;
                _countyToProvince[county.Id] = provId;
            }

            // Initialize economy arrays
            var econ = state.Economy;
            econ.Provinces = new ProvinceEconomy[maxProvId + 1];
            foreach (var prov in mapData.Provinces)
                econ.Provinces[prov.Id] = new ProvinceEconomy();

            econ.Realms = new RealmEconomy[maxRealmId + 1];
            foreach (var realm in mapData.Realms)
                econ.Realms[realm.Id] = new RealmEconomy();

            // Cache population per province and realm for admin consumption
            _provincePop = new float[maxProvId + 1];
            _realmPop = new float[maxRealmId + 1];

            foreach (var county in mapData.Counties)
            {
                var ce = econ.Counties[county.Id];
                if (ce == null) continue;
                int provId = county.ProvinceId;
                if (provId >= 0 && provId < _provincePop.Length)
                    _provincePop[provId] += ce.Population;
            }

            foreach (var prov in mapData.Provinces)
            {
                int realmId = prov.RealmId;
                if (realmId >= 0 && realmId < _realmPop.Length)
                    _realmPop[realmId] += _provincePop[prov.Id];
            }

            // Pre-compute per-realm and per-province facility county lists for quota distribution
            BuildFacilityMappings(econ, maxRealmId, maxProvId);
        }

        void BuildFacilityMappings(EconomyState econ, int maxRealmId, int maxProvId)
        {
            // Precompute which goods are facility outputs (for Phase 9c upstream propagation)
            _isFacilityOutput = new bool[Goods.Count];
            for (int f = 0; f < Facilities.Count; f++)
                _isFacilityOutput[(int)Facilities.Defs[f].OutputGood] = true;

            var facIndices = econ.CountyFacilityIndices;
            if (facIndices == null || econ.Facilities == null)
            {
                _realmFacilityCounties = null;
                _realmFacilityPop = null;
                _provinceFacilityCounties = null;
                _provinceFacilityPop = null;
                return;
            }

            // Build lists: realmId/provId → goodIndex → list of county IDs
            var realmLists = new List<int>[maxRealmId + 1][];
            var realmPops = new float[maxRealmId + 1][];
            for (int r = 0; r <= maxRealmId; r++)
            {
                realmLists[r] = new List<int>[Goods.Count];
                realmPops[r] = new float[Goods.Count];
            }

            var provLists = new List<int>[maxProvId + 1][];
            var provPops = new float[maxProvId + 1][];
            for (int p = 0; p <= maxProvId; p++)
            {
                provLists[p] = new List<int>[Goods.Count];
                provPops[p] = new float[Goods.Count];
            }

            for (int i = 0; i < econ.Counties.Length; i++)
            {
                var ce = econ.Counties[i];
                if (ce == null) continue;
                if (i >= facIndices.Length || facIndices[i] == null || facIndices[i].Count == 0) continue;
                if (i >= _countyToRealm.Length) continue;

                int realmId = _countyToRealm[i];
                if (realmId < 0 || realmId > maxRealmId) continue;

                int provId = _countyToProvince[i];

                var indices = facIndices[i];
                for (int fi = 0; fi < indices.Count; fi++)
                {
                    var fac = econ.Facilities[indices[fi]];
                    int outputGood = (int)fac.Def.OutputGood;

                    // Realm level
                    if (realmLists[realmId][outputGood] == null)
                        realmLists[realmId][outputGood] = new List<int>();
                    realmLists[realmId][outputGood].Add(i);
                    realmPops[realmId][outputGood] += ce.Population;

                    // Province level
                    if (provId >= 0 && provId <= maxProvId)
                    {
                        if (provLists[provId][outputGood] == null)
                            provLists[provId][outputGood] = new List<int>();
                        provLists[provId][outputGood].Add(i);
                        provPops[provId][outputGood] += ce.Population;
                    }
                }
            }

            // Convert realm to arrays
            _realmFacilityCounties = new int[maxRealmId + 1][][];
            _realmFacilityPop = new float[maxRealmId + 1][];
            for (int r = 0; r <= maxRealmId; r++)
            {
                _realmFacilityCounties[r] = new int[Goods.Count][];
                _realmFacilityPop[r] = realmPops[r];
                for (int g = 0; g < Goods.Count; g++)
                    _realmFacilityCounties[r][g] = realmLists[r][g]?.ToArray();
            }

            // Convert province to arrays
            _provinceFacilityCounties = new int[maxProvId + 1][][];
            _provinceFacilityPop = new float[maxProvId + 1][];
            for (int p = 0; p <= maxProvId; p++)
            {
                _provinceFacilityCounties[p] = new int[Goods.Count][];
                _provinceFacilityPop[p] = provPops[p];
                for (int g = 0; g < Goods.Count; g++)
                    _provinceFacilityCounties[p][g] = provLists[p][g]?.ToArray();
            }
        }
    }
}
