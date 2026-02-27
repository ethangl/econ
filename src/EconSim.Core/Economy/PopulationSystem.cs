using System;
using EconSim.Core.Data;
using EconSim.Core.Simulation;
using EconSim.Core.Simulation.Systems;

namespace EconSim.Core.Economy
{
    /// <summary>
    /// Monthly population dynamics: birth, death, and intra-realm migration.
    /// Runs every 30 days after all other systems.
    ///
    /// Birth rate scales with basic-needs satisfaction (well-supplied counties grow faster).
    /// Death rate spikes exponentially with deprivation.
    /// Migration flows from low-satisfaction to high-satisfaction within same realm.
    /// </summary>
    public class PopulationSystem : ITickSystem
    {
        public string Name => "Population";
        public int TickInterval => SimulationConfig.Intervals.Monthly;

        /// <summary>Base monthly birth rate per capita at neutral satisfaction.</summary>
        const float BaseBirthRate = 3f / 1000f;

        /// <summary>Base monthly death rate per capita at full satisfaction.</summary>
        const float BaseDeathRate = 2.5f / 1000f;

        /// <summary>Minimum satisfaction gap before migration occurs.</summary>
        const float MigrationGapThreshold = 0.15f;

        /// <summary>Maximum emigration rate per month (fraction of population).</summary>
        const float MaxEmigrationRate = 0.02f;

        /// <summary>Counties at or below this population don't lose people.</summary>
        const float PopulationFloor = 10f;

        int[] _countyIds;
        int[] _countyRealmIds;
        int[] _countyProvinceIds;
        int[] _provinceIds;
        int[] _provinceRealmIds;
        int[] _idToIndex;
        float[] _migrationOut;
        float[] _migrationIn;

        public void Initialize(SimulationState state, MapData mapData)
        {
            _countyIds = new int[mapData.Counties.Count];
            _countyRealmIds = new int[mapData.Counties.Count];
            _countyProvinceIds = new int[mapData.Counties.Count];
            int maxCountyId = 0;
            for (int i = 0; i < mapData.Counties.Count; i++)
            {
                int countyId = mapData.Counties[i].Id;
                _countyIds[i] = countyId;
                _countyRealmIds[i] = mapData.Counties[i].RealmId;
                _countyProvinceIds[i] = mapData.Counties[i].ProvinceId;
                if (countyId > maxCountyId) maxCountyId = countyId;
            }

            _idToIndex = new int[maxCountyId + 1];
            for (int i = 0; i < _idToIndex.Length; i++)
                _idToIndex[i] = -1;
            for (int i = 0; i < _countyIds.Length; i++)
                _idToIndex[_countyIds[i]] = i;

            _migrationOut = new float[_countyIds.Length];
            _migrationIn = new float[_countyIds.Length];

            _provinceIds = new int[mapData.Provinces.Count];
            _provinceRealmIds = new int[mapData.Provinces.Count];
            for (int i = 0; i < mapData.Provinces.Count; i++)
            {
                _provinceIds[i] = mapData.Provinces[i].Id;
                _provinceRealmIds[i] = mapData.Provinces[i].RealmId;
            }
        }

        public void Tick(SimulationState state, MapData mapData)
        {
            var econ = state.Economy;
            var counties = econ.Counties;
            var adjacency = econ.CountyAdjacency;

            // Reset monthly counters
            for (int i = 0; i < _countyIds.Length; i++)
            {
                var ce = counties[_countyIds[i]];
                ce.BirthsThisMonth = 0f;
                ce.DeathsThisMonth = 0f;
                ce.NetMigrationThisMonth = 0f;
            }

            // Phase 1: Birth & Death
            for (int i = 0; i < _countyIds.Length; i++)
            {
                int cid = _countyIds[i];
                var ce = counties[cid];
                float pop = ce.Population;
                float sat = ce.BasicSatisfaction;

                // Birth rate: BaseBirth * lerp(0.5, 1.5, satisfaction)
                float birthMul = 0.5f + sat; // lerp(0.5, 1.5, sat) when sat in [0,1]
                float births = pop * BaseBirthRate * birthMul;

                // Death rate: BaseDeath * lerp(1, 10, starvation²)
                float starvation = Math.Max(0f, 1f - sat);
                float deathMul = 1f + 9f * starvation * starvation;
                float deaths = pop * BaseDeathRate * deathMul;

                float newPop = pop + births - deaths;
                if (newPop < PopulationFloor)
                    newPop = PopulationFloor;

                ce.Population = newPop;
                Estates.ComputeEstatePop(ce.Population, ce.EstatePop);
                ce.BirthsThisMonth = births;
                ce.DeathsThisMonth = deaths;
            }

            // Phase 2: Migration (buffered, atomic)
            // Buffers are initialized once; clear and reuse each monthly tick.
            Array.Clear(_migrationOut, 0, _migrationOut.Length);
            Array.Clear(_migrationIn, 0, _migrationIn.Length);

            for (int i = 0; i < _countyIds.Length; i++)
            {
                int cid = _countyIds[i];
                var ce = counties[cid];

                // Don't migrate from counties near population floor
                if (ce.Population <= PopulationFloor * 1.5f)
                    continue;

                int realmId = _countyRealmIds[i];
                float sat = ce.Satisfaction;

                // Find best adjacent same-realm neighbor with satisfaction gap > threshold
                int bestNeighborIdx = -1;
                float bestGap = MigrationGapThreshold;

                var neighbors = adjacency[cid];
                if (neighbors == null) continue;

                for (int n = 0; n < neighbors.Length; n++)
                {
                    int nid = neighbors[n];
                    if (nid >= _idToIndex.Length || _idToIndex[nid] < 0) continue;
                    int nIdx = _idToIndex[nid];

                    // Same realm only
                    if (_countyRealmIds[nIdx] != realmId) continue;

                    float nSat = counties[nid].Satisfaction;
                    float gap = nSat - sat;
                    if (gap > bestGap)
                    {
                        bestGap = gap;
                        bestNeighborIdx = nIdx;
                    }
                }

                if (bestNeighborIdx < 0) continue;

                // Emigration rate scales with gap: min(2%, gap * 2% / 0.5)
                float emigrationRate = Math.Min(MaxEmigrationRate, bestGap * MaxEmigrationRate / 0.5f);
                float migrants = ce.Population * emigrationRate;

                // Don't push source below floor
                if (ce.Population - migrants < PopulationFloor)
                    migrants = Math.Max(0f, ce.Population - PopulationFloor);

                if (migrants <= 0f) continue;

                _migrationOut[i] += migrants;
                _migrationIn[bestNeighborIdx] += migrants;
            }

            // Apply migration atomically
            for (int i = 0; i < _countyIds.Length; i++)
            {
                float net = _migrationIn[i] - _migrationOut[i];
                if (net == 0f) continue;

                var ce = counties[_countyIds[i]];
                ce.Population += net;
                ce.NetMigrationThisMonth = net;

                // Enforce floor after migration
                if (ce.Population < PopulationFloor)
                    ce.Population = PopulationFloor;

                Estates.ComputeEstatePop(ce.Population, ce.EstatePop);
            }

            // Refresh shared population caches after all demographic changes
            RefreshPopulationCaches(econ, counties);
        }

        void RefreshPopulationCaches(EconomyState econ, CountyEconomy[] counties)
        {
            var provincePop = econ.ProvincePop;
            var realmPop = econ.RealmPop;
            if (provincePop == null || realmPop == null) return;

            Array.Clear(provincePop, 0, provincePop.Length);
            Array.Clear(realmPop, 0, realmPop.Length);

            for (int i = 0; i < _countyIds.Length; i++)
            {
                int cid = _countyIds[i];
                var ce = counties[cid];
                int provId = _countyProvinceIds[i];
                if (provId >= 0 && provId < provincePop.Length)
                    provincePop[provId] += ce.Population;
            }

            for (int i = 0; i < _provinceIds.Length; i++)
            {
                int provId = _provinceIds[i];
                int realmId = _provinceRealmIds[i];
                if (realmId >= 0 && realmId < realmPop.Length)
                    realmPop[realmId] += provincePop[provId];
            }
        }
    }
}
