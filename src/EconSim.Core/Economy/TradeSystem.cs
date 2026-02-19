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

        public void Initialize(SimulationState state, MapData mapData)
        {
            BuildMappings(state, mapData);
        }

        public void Tick(SimulationState state, MapData mapData)
        {
            var counties = state.Economy.Counties;
            var provinces = state.Economy.Provinces;
            var realms = state.Economy.Realms;

            // Reset per-tick accumulators
            ResetAccumulators(counties, provinces, realms);

            for (int g = 0; g < Goods.Count; g++)
            {
                float consumeRate = ConsumptionPerPop[g];

                // Phase 1: County administrative consumption (building upkeep)
                float countyAdmin = Goods.CountyAdminPerPop[g];
                if (countyAdmin > 0f)
                {
                    for (int i = 0; i < counties.Length; i++)
                    {
                        var ce = counties[i];
                        if (ce == null) continue;
                        float need = ce.Population * countyAdmin;
                        float consumed = Math.Min(ce.Stock[g], need);
                        ce.Stock[g] -= consumed;
                    }
                }

                // Phase 2: Duke taxes surplus counties → provincial stockpile
                float taxRate = Goods.IsPreciousMetal(g) ? PreciousMetalTaxRate : DucalTaxRate;
                for (int p = 0; p < _provinceIds.Length; p++)
                {
                    int provId = _provinceIds[p];
                    var pe = provinces[provId];
                    var countyIds = _provinceCounties[provId];

                    for (int c = 0; c < countyIds.Length; c++)
                    {
                        var ce = counties[countyIds[c]];
                        float dailyNeed = ce.Population * consumeRate;
                        float surplus = ce.Stock[g] - dailyNeed;

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

                // Phase 3: Provincial administrative consumption (infrastructure)
                float provAdmin = Goods.ProvinceAdminPerPop[g];
                if (provAdmin > 0f)
                {
                    for (int p = 0; p < _provinceIds.Length; p++)
                    {
                        int provId = _provinceIds[p];
                        var pe = provinces[provId];
                        float need = _provincePop[provId] * provAdmin;
                        float consumed = Math.Min(pe.Stockpile[g], need);
                        pe.Stockpile[g] -= consumed;
                    }
                }

                // Phase 4: King taxes surplus provincial stockpiles → royal stockpile
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

                // Phase 5: Royal administrative consumption (military upkeep)
                float realmAdmin = Goods.RealmAdminPerPop[g];
                if (realmAdmin > 0f)
                {
                    for (int r = 0; r < _realmIds.Length; r++)
                    {
                        int realmId = _realmIds[r];
                        var re = realms[realmId];
                        float need = _realmPop[realmId] * realmAdmin;
                        float consumed = Math.Min(re.Stockpile[g], need);
                        re.Stockpile[g] -= consumed;
                    }
                }

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
                        float d = ComputeProvinceDeficit(
                            g, consumeRate, provinces[provIds[p]], counties, _provinceCounties[provIds[p]]);
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

                    float totalDeficit = 0f;
                    for (int c = 0; c < countyIds.Length; c++)
                    {
                        var ce = counties[countyIds[c]];
                        float deficit = ce.Population * consumeRate - ce.Stock[g];
                        if (deficit > 0f)
                            totalDeficit += deficit;
                    }

                    if (totalDeficit <= 0f) continue;

                    float available = pe.Stockpile[g];
                    for (int c = 0; c < countyIds.Length; c++)
                    {
                        var ce = counties[countyIds[c]];
                        float deficit = ce.Population * consumeRate - ce.Stock[g];
                        if (deficit <= 0f) continue;

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

        /// <summary>
        /// Sum of (dailyNeed - stock) across deficit counties in a province for a single good.
        /// Accounts for what the provincial stockpile could cover locally.
        /// </summary>
        static float ComputeProvinceDeficit(
            int g, float consumeRate, ProvinceEconomy pe, CountyEconomy[] counties, int[] countyIds)
        {
            float totalDeficit = 0f;
            for (int c = 0; c < countyIds.Length; c++)
            {
                var ce = counties[countyIds[c]];
                float deficit = ce.Population * consumeRate - ce.Stock[g];
                if (deficit > 0f)
                    totalDeficit += deficit;
            }

            // Province can cover some of the deficit locally
            return Math.Max(0f, totalDeficit - pe.Stockpile[g]);
        }

        void ResetAccumulators(
            CountyEconomy[] counties, ProvinceEconomy[] provinces, RealmEconomy[] realms)
        {
            for (int p = 0; p < _provinceIds.Length; p++)
            {
                int provId = _provinceIds[p];
                var pe = provinces[provId];
                for (int g = 0; g < Goods.Count; g++)
                {
                    pe.TaxCollected[g] = 0f;
                    pe.ReliefGiven[g] = 0f;
                }

                var countyIds = _provinceCounties[provId];
                for (int c = 0; c < countyIds.Length; c++)
                {
                    var ce = counties[countyIds[c]];
                    for (int g = 0; g < Goods.Count; g++)
                    {
                        ce.TaxPaid[g] = 0f;
                        ce.Relief[g] = 0f;
                    }
                }
            }

            for (int r = 0; r < _realmIds.Length; r++)
            {
                var re = realms[_realmIds[r]];
                for (int g = 0; g < Goods.Count; g++)
                {
                    re.TaxCollected[g] = 0f;
                    re.ReliefGiven[g] = 0f;
                }
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
        }
    }
}
