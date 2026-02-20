using System;
using System.Collections.Generic;
using EconSim.Core.Data;
using EconSim.Core.Simulation;
using EconSim.Core.Simulation.Systems;

namespace EconSim.Core.Economy
{
    /// <summary>
    /// Demand planning for facility production. Runs daily AFTER FiscalSystem.
    /// Computes per-county FacilityQuota[] based on province and realm needs,
    /// then propagates upstream input demand through the facility chain.
    ///
    /// Phase 9a: Provincial facility quotas (duke allocates province need to local producers)
    /// Phase 9b: Realm facility quotas (king allocates realm admin + non-producing provinces)
    /// Phase 9c: Upstream quota propagation (downstream output quotas → upstream input quotas)
    /// </summary>
    public class FacilityQuotaSystem : ITickSystem
    {
        public string Name => "FacilityQuota";
        public int TickInterval => SimulationConfig.Intervals.Daily;

        int[] _provinceIds;
        int[] _realmIds;
        int[][] _realmProvinces;
        int[] _countyToRealm;
        int[] _countyToProvince;
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

        /// <summary>Simulation day when facility pop caches were last refreshed.</summary>
        int _lastFacilityPopRefreshDay;

        public void Initialize(SimulationState state, MapData mapData)
        {
            // Build hierarchy mappings
            int maxProvId = 0;
            foreach (var prov in mapData.Provinces)
                if (prov.Id > maxProvId) maxProvId = prov.Id;

            var provIdList = new List<int>(mapData.Provinces.Count);
            foreach (var prov in mapData.Provinces)
                provIdList.Add(prov.Id);
            _provinceIds = provIdList.ToArray();

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
                if (realmId >= 0 && realmId < realmProvLists.Length && realmProvLists[realmId] != null)
                    realmProvLists[realmId].Add(prov.Id);
            }
            _realmProvinces = new int[maxRealmId + 1][];
            foreach (var realm in mapData.Realms)
                _realmProvinces[realm.Id] = realmProvLists[realm.Id].ToArray();
            _realmIds = realmIdList.ToArray();

            _provinceToRealm = new int[maxProvId + 1];
            foreach (var prov in mapData.Provinces)
                _provinceToRealm[prov.Id] = prov.RealmId;

            int maxCountyId = 0;
            foreach (var county in mapData.Counties)
                if (county.Id > maxCountyId) maxCountyId = county.Id;

            _countyToRealm = new int[maxCountyId + 1];
            _countyToProvince = new int[maxCountyId + 1];
            foreach (var county in mapData.Counties)
            {
                int provId = county.ProvinceId;
                _countyToRealm[county.Id] = provId >= 0 && provId < _provinceToRealm.Length
                    ? _provinceToRealm[provId]
                    : 0;
                _countyToProvince[county.Id] = provId;
            }

            BuildFacilityMappings(state.Economy, maxRealmId, maxProvId);
            _lastFacilityPopRefreshDay = state.CurrentDay;
        }

        public void Tick(SimulationState state, MapData mapData)
        {
            var econ = state.Economy;
            var counties = econ.Counties;
            var provincePop = econ.ProvincePop;
            var realmPop = econ.RealmPop;
            var effectiveDemandPerPop = econ.EffectiveDemandPerPop;

            // Refresh facility pop caches monthly (day%30==1)
            if (state.CurrentDay % SimulationConfig.Intervals.Monthly == 1
                && _lastFacilityPopRefreshDay != state.CurrentDay)
            {
                RecomputeFacilityPopulationCaches(counties);
                _lastFacilityPopRefreshDay = state.CurrentDay;
            }

            // Clear FacilityQuota for all counties
            for (int i = 0; i < counties.Length; i++)
            {
                var ce = counties[i];
                if (ce == null) continue;
                Array.Clear(ce.FacilityQuota, 0, ce.FacilityQuota.Length);
            }

            // Phase 9a: Provincial facility quotas
            if (_provinceFacilityCounties != null)
            {
                for (int p = 0; p < _provinceIds.Length; p++)
                {
                    int provId = _provinceIds[p];
                    float provPopulation = provincePop[provId];

                    for (int g = 0; g < Goods.Count; g++)
                    {
                        var producingCounties = _provinceFacilityCounties[provId][g];
                        if (producingCounties == null || producingCounties.Length == 0) continue;

                        float totalProducingPop = _provinceFacilityPop[provId][g];
                        if (totalProducingPop <= 0f) continue;

                        float perCapNeed = effectiveDemandPerPop[g]
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
            if (_realmFacilityCounties != null)
            {
                for (int r = 0; r < _realmIds.Length; r++)
                {
                    int realmId = _realmIds[r];
                    float realmPopulation = realmPop[realmId];

                    for (int g = 0; g < Goods.Count; g++)
                    {
                        var producingCounties = _realmFacilityCounties[realmId][g];
                        if (producingCounties == null || producingCounties.Length == 0) continue;

                        float totalProducingPop = _realmFacilityPop[realmId][g];
                        if (totalProducingPop <= 0f) continue;

                        float realmQuota = realmPopulation * Goods.RealmAdminPerPop[g];

                        var provIds = _realmProvinces[realmId];
                        for (int pi = 0; pi < provIds.Length; pi++)
                        {
                            int provId = provIds[pi];
                            if (_provinceFacilityCounties != null
                                && provId < _provinceFacilityCounties.Length
                                && _provinceFacilityCounties[provId] != null
                                && _provinceFacilityCounties[provId][g] != null
                                && _provinceFacilityCounties[provId][g].Length > 0)
                                continue;

                            float perCapNeed = effectiveDemandPerPop[g]
                                             + Goods.CountyAdminPerPop[g]
                                             + Goods.ProvinceAdminPerPop[g];
                            realmQuota += provincePop[provId] * perCapNeed;
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
            if (_isFacilityOutput != null && econ.Facilities != null)
            {
                var facIndices = econ.CountyFacilityIndices;
                var facilities = econ.Facilities;
                for (int i = 0; i < counties.Length; i++)
                {
                    var ce = counties[i];
                    if (ce == null) continue;
                    if (i >= facIndices.Length || facIndices[i] == null || facIndices[i].Count == 0) continue;

                    var indices = facIndices[i];
                    for (int fi = indices.Count - 1; fi >= 0; fi--)
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

        void BuildFacilityMappings(EconomyState econ, int maxRealmId, int maxProvId)
        {
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

                    if (realmLists[realmId][outputGood] == null)
                        realmLists[realmId][outputGood] = new List<int>();
                    realmLists[realmId][outputGood].Add(i);
                    realmPops[realmId][outputGood] += ce.Population;

                    if (provId >= 0 && provId <= maxProvId)
                    {
                        if (provLists[provId][outputGood] == null)
                            provLists[provId][outputGood] = new List<int>();
                        provLists[provId][outputGood].Add(i);
                        provPops[provId][outputGood] += ce.Population;
                    }
                }
            }

            _realmFacilityCounties = new int[maxRealmId + 1][][];
            _realmFacilityPop = new float[maxRealmId + 1][];
            for (int r = 0; r <= maxRealmId; r++)
            {
                _realmFacilityCounties[r] = new int[Goods.Count][];
                _realmFacilityPop[r] = realmPops[r];
                for (int g = 0; g < Goods.Count; g++)
                    _realmFacilityCounties[r][g] = realmLists[r][g]?.ToArray();
            }

            _provinceFacilityCounties = new int[maxProvId + 1][][];
            _provinceFacilityPop = new float[maxProvId + 1][];
            for (int p = 0; p <= maxProvId; p++)
            {
                _provinceFacilityCounties[p] = new int[Goods.Count][];
                _provinceFacilityPop[p] = provPops[p];
                for (int g = 0; g < Goods.Count; g++)
                    _provinceFacilityCounties[p][g] = provLists[p][g]?.ToArray();
            }

            RecomputeFacilityPopulationCaches(econ.Counties);
        }

        void RecomputeFacilityPopulationCaches(CountyEconomy[] counties)
        {
            if (_realmFacilityCounties == null || _realmFacilityPop == null
                || _provinceFacilityCounties == null || _provinceFacilityPop == null)
                return;

            for (int r = 0; r < _realmIds.Length; r++)
            {
                int realmId = _realmIds[r];
                if (realmId < 0 || realmId >= _realmFacilityPop.Length || _realmFacilityPop[realmId] == null)
                    continue;
                Array.Clear(_realmFacilityPop[realmId], 0, _realmFacilityPop[realmId].Length);

                if (realmId >= _realmFacilityCounties.Length || _realmFacilityCounties[realmId] == null)
                    continue;

                for (int g = 0; g < Goods.Count; g++)
                {
                    var producingCounties = _realmFacilityCounties[realmId][g];
                    if (producingCounties == null || producingCounties.Length == 0) continue;

                    float total = 0f;
                    for (int i = 0; i < producingCounties.Length; i++)
                    {
                        int countyId = producingCounties[i];
                        if (countyId < 0 || countyId >= counties.Length) continue;
                        var ce = counties[countyId];
                        if (ce == null) continue;
                        total += ce.Population;
                    }
                    _realmFacilityPop[realmId][g] = total;
                }
            }

            for (int p = 0; p < _provinceIds.Length; p++)
            {
                int provId = _provinceIds[p];
                if (provId < 0 || provId >= _provinceFacilityPop.Length || _provinceFacilityPop[provId] == null)
                    continue;
                Array.Clear(_provinceFacilityPop[provId], 0, _provinceFacilityPop[provId].Length);

                if (provId >= _provinceFacilityCounties.Length || _provinceFacilityCounties[provId] == null)
                    continue;

                for (int g = 0; g < Goods.Count; g++)
                {
                    var producingCounties = _provinceFacilityCounties[provId][g];
                    if (producingCounties == null || producingCounties.Length == 0) continue;

                    float total = 0f;
                    for (int i = 0; i < producingCounties.Length; i++)
                    {
                        int countyId = producingCounties[i];
                        if (countyId < 0 || countyId >= counties.Length) continue;
                        var ce = counties[countyId];
                        if (ce == null) continue;
                        total += ce.Population;
                    }
                    _provinceFacilityPop[provId][g] = total;
                }
            }
        }
    }
}
