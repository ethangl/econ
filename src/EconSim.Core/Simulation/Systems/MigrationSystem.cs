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
        private MapData _mapData;

        public void Initialize(SimulationState state, MapData mapData)
        {
            _mapData = mapData;

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

            SimLog.Log("Migration", $"Migration system initialized, {_countyToCulture.Count} counties mapped to cultures");
        }

        public void Tick(SimulationState state, MapData mapData)
        {
            var economy = state.Economy;
            var transport = state.Transport;
            int totalMigrants = 0;
            int totalEvents = 0;

            foreach (var countyEcon in economy.Counties.Values)
            {
                var pop = countyEcon.Population;
                int countyId = countyEcon.CountyId;

                if (!mapData.CountyById.TryGetValue(countyId, out var county))
                    continue;

                foreach (var kvp in EstateMobility)
                {
                    var estate = kvp.Key;
                    float mobility = kvp.Value;

                    int estatePop = pop.GetEstatePopulation(estate);
                    if (estatePop < 2) continue; // need at least 2 to leave 1 behind

                    float pushScore = GetPushScore(pop, estate);
                    if (pushScore <= 0f) continue;

                    int migrants = (int)(estatePop * BaseMigrationRate * mobility * pushScore);
                    if (migrants < MinMigrationPop) continue;

                    // Don't empty a county — leave at least 1
                    migrants = Math.Min(migrants, estatePop - 1);

                    // Find reachable counties
                    var reachableCells = transport.FindReachable(county.SeatCellId, MaxTransportCost);
                    var candidates = BuildCandidates(reachableCells, economy, countyId);
                    if (candidates.Count == 0) continue;

                    // Score and distribute
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

        private float GetPushScore(CountyPopulation pop, Estate estate)
        {
            if (estate == Estate.Merchants)
                return MerchantFixedPush;

            if (estate == Estate.Laborers)
            {
                int total = pop.TotalUnskilled;
                if (total <= 0) return 0f;
                return (float)pop.IdleUnskilled / total;
            }

            if (estate == Estate.Artisans)
            {
                int total = pop.TotalSkilled;
                if (total <= 0) return 0f;
                return (float)pop.IdleSkilled / total;
            }

            return 0f;
        }

        private float GetPullScore(CountyPopulation pop, Estate estate)
        {
            if (estate == Estate.Merchants)
                return 1f; // merchants always pulled toward trade

            if (estate == Estate.Laborers)
            {
                int total = pop.TotalUnskilled;
                if (total <= 0) return 1f; // no workers = high demand
                return 1f - (float)pop.IdleUnskilled / total;
            }

            if (estate == Estate.Artisans)
            {
                int total = pop.TotalSkilled;
                if (total <= 0) return 1f;
                return 1f - (float)pop.IdleSkilled / total;
            }

            return 0f;
        }

        /// <summary>
        /// Convert reachable cells to unique candidate counties with min transport cost.
        /// </summary>
        private List<(int countyId, float cost)> BuildCandidates(
            Dictionary<int, float> reachableCells,
            EconomyState economy,
            int sourceCountyId)
        {
            var bestCost = new Dictionary<int, float>();

            foreach (var kvp in reachableCells)
            {
                if (!economy.CellToCounty.TryGetValue(kvp.Key, out int cId))
                    continue;
                if (cId == sourceCountyId) continue;
                if (!economy.Counties.ContainsKey(cId)) continue;

                if (!bestCost.TryGetValue(cId, out float existing) || kvp.Value < existing)
                    bestCost[cId] = kvp.Value;
            }

            var result = new List<(int, float)>(bestCost.Count);
            foreach (var kvp in bestCost)
            {
                // Exclude counties with no working population
                if (economy.Counties[kvp.Key].Population.WorkingAge > 0)
                    result.Add((kvp.Key, kvp.Value));
            }

            return result;
        }

        private int DistributeMigrants(
            int migrants,
            Estate estate,
            int sourceCountyId,
            List<(int countyId, float cost)> candidates,
            EconomyState economy)
        {
            int sourceCulture = _countyToCulture.TryGetValue(sourceCountyId, out int sc) ? sc : -1;

            // Score each candidate
            float totalScore = 0f;
            var scores = new float[candidates.Count];
            for (int i = 0; i < candidates.Count; i++)
            {
                var (destId, cost) = candidates[i];

                float pull = GetPullScore(economy.Counties[destId].Population, estate);
                float distanceDecay = 1f / (1f + cost / DistanceDecayScale);

                int destCulture = _countyToCulture.TryGetValue(destId, out int dc) ? dc : -2;
                float cultureAffinity = (sourceCulture >= 0 && sourceCulture == destCulture)
                    ? 1f : CulturalAffinityForeign;

                float score = pull * cultureAffinity * distanceDecay;
                scores[i] = score;
                totalScore += score;
            }

            if (totalScore <= 0f) return 0;

            // Distribute proportionally
            int totalMoved = 0;
            var skill = EstateSkill[estate];

            for (int i = 0; i < candidates.Count; i++)
            {
                if (scores[i] <= 0f) continue;

                int share = (int)(migrants * scores[i] / totalScore);
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
