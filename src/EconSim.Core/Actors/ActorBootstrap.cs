using System;
using System.Collections.Generic;
using EconSim.Core.Data;
using PopGen.Core;

namespace EconSim.Core.Actors
{
    /// <summary>
    /// Creates the initial set of actors and titles from map data at world gen time.
    /// Every county, province, and realm gets a title. Every title gets a noble holder.
    /// Capital titles are collapsed (king also holds his capital province and county).
    /// </summary>
    public static class ActorBootstrap
    {
        public static ActorState Generate(MapData map, int seed)
        {
            int countyCount = map.Counties.Count;
            int provinceCount = map.Provinces.Count;
            int realmCount = map.Realms.Count;
            int titleCount = countyCount + provinceCount + realmCount;

            var state = new ActorState
            {
                CountyTitleCount = countyCount,
                ProvinceTitleCount = provinceCount,
                RealmTitleCount = realmCount,
                TitleCount = titleCount,
                Titles = new Title[titleCount + 1], // slot 0 unused
            };

            // Create all titles
            for (int i = 1; i <= titleCount; i++)
                state.Titles[i] = new Title { Id = i };

            // County titles: ID = countyId (1-based)
            foreach (var county in map.Counties)
            {
                int tid = state.GetCountyTitleId(county.Id);
                state.Titles[tid].Rank = TitleRank.County;
                state.Titles[tid].TerritoryId = county.Id;
            }

            // Province titles
            foreach (var province in map.Provinces)
            {
                int tid = state.GetProvinceTitleId(province.Id);
                state.Titles[tid].Rank = TitleRank.Province;
                state.Titles[tid].TerritoryId = province.Id;
            }

            // Realm titles
            foreach (var realm in map.Realms)
            {
                int tid = state.GetRealmTitleId(realm.Id);
                state.Titles[tid].Rank = TitleRank.Realm;
                state.Titles[tid].TerritoryId = realm.Id;
            }

            // Build CultureType lookup by name
            var cultureTypeByName = new Dictionary<string, CultureType>(StringComparer.OrdinalIgnoreCase);
            foreach (var ct in CultureTypes.All)
                cultureTypeByName[ct.Name] = ct;

            // Build province -> realm lookup, county -> province lookup
            var provinceToRealm = new Dictionary<int, int>();
            foreach (var province in map.Provinces)
                provinceToRealm[province.Id] = province.RealmId;

            var countyToProvince = new Dictionary<int, int>();
            foreach (var county in map.Counties)
                countyToProvince[county.Id] = county.ProvinceId;

            // Build cell -> county lookup for capital resolution
            var cellToCounty = new Dictionary<int, int>();
            foreach (var county in map.Counties)
            {
                if (county.CellIds != null)
                {
                    foreach (int cellId in county.CellIds)
                        cellToCounty[cellId] = county.Id;
                }
            }

            // Find capital counties for realms and provinces (for collapsing)
            var realmCapitalCounty = new Dictionary<int, int>();  // realmId -> countyId
            var realmCapitalProvince = new Dictionary<int, int>(); // realmId -> provinceId
            foreach (var realm in map.Realms)
            {
                // Find the county containing the realm's capital cell
                int capitalCell = realm.CenterCellId;
                if (cellToCounty.TryGetValue(capitalCell, out int ctyId)
                    && map.CountyById.TryGetValue(ctyId, out var cty)
                    && cty.RealmId == realm.Id)
                {
                    realmCapitalCounty[realm.Id] = cty.Id;
                    realmCapitalProvince[realm.Id] = cty.ProvinceId;
                }
                else
                {
                    // Fallback: first county in realm
                    foreach (var county in map.Counties)
                    {
                        if (county.RealmId == realm.Id)
                        {
                            realmCapitalCounty[realm.Id] = county.Id;
                            realmCapitalProvince[realm.Id] = county.ProvinceId;
                            break;
                        }
                    }
                }
            }

            var provinceCapitalCounty = new Dictionary<int, int>(); // provinceId -> countyId
            foreach (var province in map.Provinces)
            {
                int capitalCell = province.CenterCellId;
                if (cellToCounty.TryGetValue(capitalCell, out int pctyId)
                    && map.CountyById.TryGetValue(pctyId, out var pcty)
                    && pcty.ProvinceId == province.Id)
                {
                    provinceCapitalCounty[province.Id] = pcty.Id;
                }
                else
                {
                    // Fallback: first county in province
                    foreach (var county in map.Counties)
                    {
                        if (county.ProvinceId == province.Id)
                        {
                            provinceCapitalCounty[province.Id] = county.Id;
                            break;
                        }
                    }
                }
            }

            // Determine which counties/provinces are collapsed into higher titles
            var collapsedCounties = new HashSet<int>();  // county IDs held by duke or king
            var collapsedProvinces = new HashSet<int>(); // province IDs held by king

            foreach (var realm in map.Realms)
            {
                if (realmCapitalProvince.TryGetValue(realm.Id, out int provId))
                    collapsedProvinces.Add(provId);
                if (realmCapitalCounty.TryGetValue(realm.Id, out int ctyId))
                    collapsedCounties.Add(ctyId);
            }

            foreach (var province in map.Provinces)
            {
                if (collapsedProvinces.Contains(province.Id))
                    continue; // king holds this province's capital county
                if (provinceCapitalCounty.TryGetValue(province.Id, out int ctyId))
                    collapsedCounties.Add(ctyId);
            }

            // Count actors needed
            int actorCount = realmCount; // one per realm
            foreach (var province in map.Provinces)
            {
                if (!collapsedProvinces.Contains(province.Id))
                    actorCount++;
            }
            foreach (var county in map.Counties)
            {
                if (!collapsedCounties.Contains(county.Id))
                    actorCount++;
            }

            state.ActorCount = actorCount;
            state.Actors = new Actor[actorCount + 1]; // slot 0 unused

            var popSeed = new PopGenSeed(seed);
            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int nextActorId = 1;

            CultureType GetCultureType(int cultureId)
            {
                if (cultureId > 0 && map.CultureById != null && map.CultureById.TryGetValue(cultureId, out var culture))
                {
                    if (cultureTypeByName.TryGetValue(culture.TypeName, out var ct))
                        return ct;
                }
                return CultureTypes.All[0];
            }

            int GetReligionId(int cultureId)
            {
                if (cultureId > 0 && map.CultureById != null && map.CultureById.TryGetValue(cultureId, out var culture))
                    return culture.ReligionId;
                return 1;
            }

            Actor SpawnActor(int cultureId, int countyId, int titleId, int liegeActorId)
            {
                int actorId = nextActorId++;
                var ct = GetCultureType(cultureId);
                // Seeded gender: ~50/50 but deterministic
                uint gRng = (uint)(seed * 31 + actorId * 97);
                gRng ^= gRng >> 16;
                gRng *= 0x85EBCA6Bu;
                bool isFemale = (gRng & 1) == 0;

                string name = PopNameGenerator.GeneratePersonName(actorId, cultureId, isFemale, ct, popSeed, usedNames);
                string givenName = PopNameGenerator.GeneratePersonGivenName(actorId, cultureId, isFemale, ct, popSeed);

                // Age 25-55, seeded
                uint aRng = (uint)(seed * 53 + actorId * 113);
                aRng ^= aRng >> 16;
                aRng *= 0x85EBCA6Bu;
                int ageYears = 25 + (int)(aRng % 31);
                int birthDay = -(ageYears * 365); // negative = born before simulation start

                var actor = new Actor
                {
                    Id = actorId,
                    Name = name,
                    GivenName = givenName,
                    BirthDay = birthDay,
                    IsFemale = isFemale,
                    CultureId = cultureId,
                    ReligionId = GetReligionId(cultureId),
                    TitleId = titleId,
                    LiegeActorId = liegeActorId,
                    CountyId = countyId,
                };

                state.Actors[actorId] = actor;
                state.Titles[titleId].HolderActorId = actorId;
                state.Titles[titleId].DeJureActorId = actorId;
                return actor;
            }

            // Spawn realm actors (kings)
            var realmActors = new Dictionary<int, int>(); // realmId -> actorId
            foreach (var realm in map.Realms)
            {
                int capitalCounty = realmCapitalCounty.TryGetValue(realm.Id, out int cc) ? cc : 0;
                int realmTitleId = state.GetRealmTitleId(realm.Id);
                var actor = SpawnActor(realm.CultureId, capitalCounty, realmTitleId, 0);
                realmActors[realm.Id] = actor.Id;

                // King also holds capital province and capital county titles
                if (realmCapitalProvince.TryGetValue(realm.Id, out int provId))
                {
                    int provTitleId = state.GetProvinceTitleId(provId);
                    state.Titles[provTitleId].HolderActorId = actor.Id;
                    state.Titles[provTitleId].DeJureActorId = actor.Id;
                }
                if (capitalCounty > 0)
                {
                    int ctyTitleId = state.GetCountyTitleId(capitalCounty);
                    state.Titles[ctyTitleId].HolderActorId = actor.Id;
                    state.Titles[ctyTitleId].DeJureActorId = actor.Id;
                }
            }

            // Spawn province actors (dukes)
            var provinceActors = new Dictionary<int, int>(); // provinceId -> actorId
            foreach (var province in map.Provinces)
            {
                if (collapsedProvinces.Contains(province.Id))
                {
                    // King holds this — record king as province actor
                    int realmId = province.RealmId;
                    if (realmActors.TryGetValue(realmId, out int kingId))
                        provinceActors[province.Id] = kingId;
                    continue;
                }

                int realmId2 = province.RealmId;
                int liegeActorId = realmActors.TryGetValue(realmId2, out int rid) ? rid : 0;
                int cultureId = 0;
                if (realmId2 > 0 && map.RealmById.TryGetValue(realmId2, out var realm))
                    cultureId = realm.CultureId;

                int capitalCounty = provinceCapitalCounty.TryGetValue(province.Id, out int pcid) ? pcid : 0;
                int provTitleId = state.GetProvinceTitleId(province.Id);
                var actor = SpawnActor(cultureId, capitalCounty, provTitleId, liegeActorId);
                provinceActors[province.Id] = actor.Id;

                // Duke also holds capital county title
                if (capitalCounty > 0)
                {
                    int ctyTitleId = state.GetCountyTitleId(capitalCounty);
                    state.Titles[ctyTitleId].HolderActorId = actor.Id;
                    state.Titles[ctyTitleId].DeJureActorId = actor.Id;
                }
            }

            // Spawn county actors (barons)
            foreach (var county in map.Counties)
            {
                if (collapsedCounties.Contains(county.Id))
                    continue; // duke or king holds this

                int provinceId = county.ProvinceId;
                int liegeActorId = provinceActors.TryGetValue(provinceId, out int pid) ? pid : 0;
                int cultureId = 0;
                int realmId = county.RealmId;
                if (realmId > 0 && map.RealmById.TryGetValue(realmId, out var realm))
                    cultureId = realm.CultureId;

                int ctyTitleId = state.GetCountyTitleId(county.Id);
                SpawnActor(cultureId, county.Id, ctyTitleId, liegeActorId);
            }

            return state;
        }
    }
}
