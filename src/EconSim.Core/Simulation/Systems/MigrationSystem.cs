using System;
using System.Collections.Generic;
using EconSim.Core.Common;
using EconSim.Core.Data;
using EconSim.Core.Economy;

namespace EconSim.Core.Simulation.Systems
{
    /// <summary>
    /// Monthly migration between counties based on employment, culture, and distance.
    /// Laborers are most mobile; artisans less so; merchants least.
    /// Landed, clergy, and nobility don't migrate.
    /// </summary>
    public class MigrationSystem : ITickSystem
    {
        public string Name => "Migration";
        public int TickInterval => SimulationConfig.Intervals.Monthly;

        private const float BaseMigrationRate = 0.01f;
        private const float MaxTransportCost = 150f;
        private const float DistanceDecayScale = 30f;
        private const float CulturalAffinityForeign = 0.2f;
        private const int MinMigrationPop = 1;
        private const float MerchantFixedPush = 0.1f;
        private const string FoodGoodId = "bread";
        private const float FoodDaysSupplyBase = 30f; // Days of food = no push/full pull

        // Estate mobility weights
        private static readonly Dictionary<Estate, float> EstateMobility = new Dictionary<Estate, float>
        {
            { Estate.Laborers, 0.40f },
            { Estate.Artisans, 0.20f },
            { Estate.Merchants, 0.10f },
        };

        // Estate → skill mapping for cohort lookup
        private static readonly Dictionary<Estate, SkillLevel> EstateSkill = new Dictionary<Estate, SkillLevel>
        {
            { Estate.Laborers, SkillLevel.Unskilled },
            { Estate.Artisans, SkillLevel.Skilled },
            { Estate.Merchants, SkillLevel.None },
        };

        private Dictionary<int, int> _countyToCulture;
        // Precomputed reachable counties per source county: countyId → [(destCountyId, cost)]
        private Dictionary<int, List<(int countyId, float cost)>> _reachableCache;
        private float[] _scoreBuffer = Array.Empty<float>();

        public void Initialize(SimulationState state, MapData mapData)
        {
            // Build county → culture lookup: county.RealmId → realm.CultureId
            _countyToCulture = new Dictionary<int, int>();
            if (mapData.Counties != null)
            {
                foreach (var county in mapData.Counties)
                {
                    if (county == null) continue;
                    if (mapData.RealmById.TryGetValue(county.RealmId, out var realm))
                        _countyToCulture[county.Id] = realm.CultureId;
                }
            }

            // Precompute reachable counties for each county seat
            _reachableCache = new Dictionary<int, List<(int, float)>>();
            var economy = state.Economy;
            var transport = state.Transport;
            foreach (var countyEcon in economy.Counties.Values)
            {
                int countyId = countyEcon.CountyId;
                if (!mapData.CountyById.TryGetValue(countyId, out var county))
                    continue;

                var reachableCells = transport.FindReachable(county.SeatCellId, MaxTransportCost);
                var bestCost = new Dictionary<int, float>();
                foreach (var kvp in reachableCells)
                {
                    if (!economy.CellToCounty.TryGetValue(kvp.Key, out int cId))
                        continue;
                    if (cId == countyId) continue;
                    if (!economy.Counties.ContainsKey(cId)) continue;
                    if (!bestCost.TryGetValue(cId, out float existing) || kvp.Value < existing)
                        bestCost[cId] = kvp.Value;
                }

                var candidates = new List<(int, float)>(bestCost.Count);
                foreach (var kvp in bestCost)
                    candidates.Add((kvp.Key, kvp.Value));

                _reachableCache[countyId] = candidates;
            }

            SimLog.Log("Migration", $"Migration system initialized, {_countyToCulture.Count} counties, {_reachableCache.Count} reachability maps cached");
        }

        public void Tick(SimulationState state, MapData mapData)
        {
            var economy = state.Economy;
            int totalMigrants = 0;
            int totalEvents = 0;

            foreach (var countyEcon in economy.Counties.Values)
            {
                var pop = countyEcon.Population;
                int countyId = countyEcon.CountyId;

                if (!_reachableCache.TryGetValue(countyId, out var candidates) || candidates == null || candidates.Count == 0)
                    continue;

                foreach (var kvp in EstateMobility)
                {
                    var estate = kvp.Key;
                    float mobility = kvp.Value;

                    int estatePop = pop.GetEstatePopulation(estate);
                    if (estatePop < 2) continue;

                    float pushScore = GetPushScore(countyEcon, estate);
                    if (pushScore <= 0f) continue;

                    int migrants = (int)(estatePop * BaseMigrationRate * mobility * pushScore);
                    if (migrants < MinMigrationPop) continue;

                    migrants = Math.Min(migrants, estatePop - 1);

                    int moved = DistributeMigrants(
                        migrants, estate, countyId, candidates, economy);

                    if (moved > 0)
                    {
                        totalMigrants += moved;
                        totalEvents++;
                    }
                }
            }

            if (totalMigrants > 0)
            {
                SimLog.Log("Migration",
                    $"Day {state.CurrentDay}: {totalMigrants} migrants in {totalEvents} flows");
            }
        }

        private float GetFoodDaysSupply(CountyEconomy county)
        {
            int pop = county.Population.Total;
            if (pop <= 0) return FoodDaysSupplyBase;
            float dailyDemand = pop * 0.01f; // bread BaseConsumption
            if (dailyDemand <= 0f) return FoodDaysSupplyBase;
            return county.Stockpile.Get(FoodGoodId) / dailyDemand;
        }

        private float GetPushScore(CountyEconomy county, Estate estate)
        {
            if (estate == Estate.Merchants)
                return MerchantFixedPush;

            var pop = county.Population;
            float foodPush = Math.Max(0f, 1f - GetFoodDaysSupply(county) / FoodDaysSupplyBase);

            if (estate == Estate.Laborers)
            {
                int total = pop.TotalUnskilled;
                float idlePush = total > 0 ? (float)pop.IdleUnskilled / total : 0f;
                return Math.Max(idlePush, foodPush);
            }

            if (estate == Estate.Artisans)
            {
                int total = pop.TotalSkilled;
                float idlePush = total > 0 ? (float)pop.IdleSkilled / total : 0f;
                return Math.Max(idlePush, foodPush);
            }

            return 0f;
        }

        private float GetPullScore(CountyEconomy county, Estate estate)
        {
            if (estate == Estate.Merchants)
                return 1f;

            var pop = county.Population;
            // Floor at 0.2 so food scarcity dampens but never vetoes migration
            float foodPull = 0.2f + 0.8f * Math.Min(1f, GetFoodDaysSupply(county) / FoodDaysSupplyBase);

            if (estate == Estate.Laborers)
            {
                int total = pop.TotalUnskilled;
                float empPull = total > 0 ? 1f - (float)pop.IdleUnskilled / total : 1f;
                return empPull * foodPull;
            }

            if (estate == Estate.Artisans)
            {
                int total = pop.TotalSkilled;
                float empPull = total > 0 ? 1f - (float)pop.IdleSkilled / total : 1f;
                return empPull * foodPull;
            }

            return 0f;
        }

        private int DistributeMigrants(
            int migrants,
            Estate estate,
            int sourceCountyId,
            List<(int countyId, float cost)> candidates,
            EconomyState economy)
        {
            int sourceCulture = _countyToCulture.TryGetValue(sourceCountyId, out int sc) ? sc : -1;
            EnsureScoreBufferCapacity(candidates.Count);

            // Score each candidate
            float totalScore = 0f;
            for (int i = 0; i < candidates.Count; i++)
            {
                var (destId, cost) = candidates[i];
                if (!economy.Counties.TryGetValue(destId, out var destination) || destination.Population.WorkingAge <= 0)
                {
                    _scoreBuffer[i] = 0f;
                    continue;
                }

                float pull = GetPullScore(destination, estate);
                float distanceDecay = 1f / (1f + cost / DistanceDecayScale);

                int destCulture = _countyToCulture.TryGetValue(destId, out int dc) ? dc : -2;
                float cultureAffinity = (sourceCulture >= 0 && sourceCulture == destCulture)
                    ? 1f : CulturalAffinityForeign;

                float score = pull * cultureAffinity * distanceDecay;
                _scoreBuffer[i] = score;
                totalScore += score;
            }

            if (totalScore <= 0f) return 0;

            // Distribute proportionally
            int totalMoved = 0;
            var skill = EstateSkill[estate];

            for (int i = 0; i < candidates.Count; i++)
            {
                float score = _scoreBuffer[i];
                if (score <= 0f) continue;

                int share = (int)(migrants * score / totalScore);
                if (share < MinMigrationPop) continue;

                // Don't exceed remaining migrants
                if (totalMoved + share > migrants)
                    share = migrants - totalMoved;
                if (share <= 0) break;

                TransferPopulation(economy, sourceCountyId, candidates[i].countyId, estate, skill, share);
                totalMoved += share;
            }

            return totalMoved;
        }

        private void EnsureScoreBufferCapacity(int count)
        {
            if (_scoreBuffer.Length >= count)
                return;

            int next = Math.Max(count, Math.Max(16, _scoreBuffer.Length * 2));
            _scoreBuffer = new float[next];
        }

        private void TransferPopulation(
            EconomyState economy,
            int fromCountyId,
            int toCountyId,
            Estate estate,
            SkillLevel skill,
            int count)
        {
            var fromPop = economy.Counties[fromCountyId].Population;
            var toPop = economy.Counties[toCountyId].Population;

            // Subtract from source cohort
            for (int i = 0; i < fromPop.Cohorts.Count; i++)
            {
                var c = fromPop.Cohorts[i];
                if (c.Age == AgeBracket.Working && c.Estate == estate)
                {
                    int actual = Math.Min(count, c.Count);
                    c.Count -= actual;
                    fromPop.Cohorts[i] = c;
                    count = actual; // clamp to what was actually available
                    break;
                }
            }

            if (count <= 0) return;

            // Add to destination cohort (find existing or append)
            bool found = false;
            for (int i = 0; i < toPop.Cohorts.Count; i++)
            {
                var c = toPop.Cohorts[i];
                if (c.Age == AgeBracket.Working && c.Estate == estate && c.Skill == skill)
                {
                    c.Count += count;
                    toPop.Cohorts[i] = c;
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                toPop.Cohorts.Add(new PopulationCohort(AgeBracket.Working, skill, count, estate));
            }
        }
    }
}
