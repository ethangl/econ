using System;
using System.Collections.Generic;
using EconSim.Core.Data;
using EconSim.Core.Simulation;
using EconSim.Core.Simulation.Systems;

namespace EconSim.Core.Economy
{
    /// <summary>
    /// Two-tier feudal redistribution. Runs daily AFTER EconomySystem.
    ///
    /// Phase 1: Duke taxes surplus counties → provincial stockpile
    /// Phase 2: King taxes surplus provincial stockpiles → royal stockpile
    /// Phase 3: King distributes royal stockpile → deficit provinces
    /// Phase 4: Duke distributes provincial stockpile → deficit counties
    ///
    /// Ordering lets goods flow county→province→realm→province→county in one tick.
    /// </summary>
    public class TradeSystem : ITickSystem
    {
        public string Name => "Trade";
        public int TickInterval => SimulationConfig.Intervals.Daily;

        /// <summary>Fraction of surplus the duke takes from counties.</summary>
        const float DucalTaxRate = 0.20f;

        /// <summary>Fraction of provincial stockpile the king takes.</summary>
        const float RoyalTaxRate = 0.20f;

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

            // Phase 1: Duke taxes surplus counties
            for (int p = 0; p < _provinceIds.Length; p++)
            {
                int provId = _provinceIds[p];
                var pe = provinces[provId];
                var countyIds = _provinceCounties[provId];

                for (int c = 0; c < countyIds.Length; c++)
                {
                    var ce = counties[countyIds[c]];
                    float dailyNeed = ce.Population;
                    float surplus = ce.Stock - dailyNeed;

                    if (surplus > 0f)
                    {
                        float tax = DucalTaxRate * surplus;
                        ce.Stock -= tax;
                        ce.TaxPaid = tax;
                        pe.Stockpile += tax;
                        pe.TaxCollected += tax;
                    }
                }
            }

            // Phase 2: King taxes surplus provincial stockpiles
            for (int r = 0; r < _realmIds.Length; r++)
            {
                int realmId = _realmIds[r];
                var re = realms[realmId];
                var provIds = _realmProvinces[realmId];

                for (int p = 0; p < provIds.Length; p++)
                {
                    var pe = provinces[provIds[p]];
                    if (pe.Stockpile <= 0f) continue;

                    float tax = RoyalTaxRate * pe.Stockpile;
                    pe.Stockpile -= tax;
                    re.Stockpile += tax;
                    re.TaxCollected += tax;
                }
            }

            // Phase 3: King distributes to deficit provinces
            // Cache per-province deficit values to avoid double iteration
            Span<float> provDeficits = stackalloc float[_maxProvincesPerRealm];
            for (int r = 0; r < _realmIds.Length; r++)
            {
                int realmId = _realmIds[r];
                var re = realms[realmId];
                if (re.Stockpile <= 0f) continue;

                var provIds = _realmProvinces[realmId];

                float totalDeficit = 0f;
                for (int p = 0; p < provIds.Length; p++)
                {
                    float d = ComputeProvinceDeficit(
                        provinces[provIds[p]], counties, _provinceCounties[provIds[p]]);
                    provDeficits[p] = d;
                    if (d > 0f)
                        totalDeficit += d;
                }

                if (totalDeficit <= 0f) continue;

                float available = re.Stockpile;
                for (int p = 0; p < provIds.Length; p++)
                {
                    if (provDeficits[p] <= 0f) continue;

                    float share = provDeficits[p] / totalDeficit;
                    float relief = Math.Min(share * available, provDeficits[p]);
                    provinces[provIds[p]].Stockpile += relief;
                    re.Stockpile -= relief;
                    re.ReliefGiven += relief;
                }
            }

            // Phase 4: Duke distributes provincial stockpile to deficit counties
            for (int p = 0; p < _provinceIds.Length; p++)
            {
                int provId = _provinceIds[p];
                var pe = provinces[provId];
                if (pe.Stockpile <= 0f) continue;

                var countyIds = _provinceCounties[provId];

                float totalDeficit = 0f;
                for (int c = 0; c < countyIds.Length; c++)
                {
                    var ce = counties[countyIds[c]];
                    float deficit = ce.Population - ce.Stock;
                    if (deficit > 0f)
                        totalDeficit += deficit;
                }

                if (totalDeficit <= 0f) continue;

                float available = pe.Stockpile;
                for (int c = 0; c < countyIds.Length; c++)
                {
                    var ce = counties[countyIds[c]];
                    float deficit = ce.Population - ce.Stock;
                    if (deficit <= 0f) continue;

                    float share = deficit / totalDeficit;
                    float relief = Math.Min(share * available, deficit);
                    ce.Stock += relief;
                    ce.Relief = relief;
                    pe.Stockpile -= relief;
                    pe.ReliefGiven += relief;
                }
            }
        }

        /// <summary>
        /// Sum of (dailyNeed - stock) across deficit counties in a province.
        /// Accounts for what the provincial stockpile could cover locally.
        /// </summary>
        static float ComputeProvinceDeficit(
            ProvinceEconomy pe, CountyEconomy[] counties, int[] countyIds)
        {
            float totalDeficit = 0f;
            for (int c = 0; c < countyIds.Length; c++)
            {
                var ce = counties[countyIds[c]];
                float deficit = ce.Population - ce.Stock;
                if (deficit > 0f)
                    totalDeficit += deficit;
            }

            // Province can cover some of the deficit locally
            return Math.Max(0f, totalDeficit - pe.Stockpile);
        }

        void ResetAccumulators(
            CountyEconomy[] counties, ProvinceEconomy[] provinces, RealmEconomy[] realms)
        {
            for (int p = 0; p < _provinceIds.Length; p++)
            {
                int provId = _provinceIds[p];
                var pe = provinces[provId];
                pe.TaxCollected = 0f;
                pe.ReliefGiven = 0f;

                var countyIds = _provinceCounties[provId];
                for (int c = 0; c < countyIds.Length; c++)
                {
                    var ce = counties[countyIds[c]];
                    ce.TaxPaid = 0f;
                    ce.Relief = 0f;
                }
            }

            for (int r = 0; r < _realmIds.Length; r++)
            {
                var re = realms[_realmIds[r]];
                re.TaxCollected = 0f;
                re.ReliefGiven = 0f;
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
        }
    }
}
