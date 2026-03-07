using System;
using System.Collections.Generic;
using EconSim.Core.Actors;
using EconSim.Core.Common;
using EconSim.Core.Data;
using PopGen.Core;

namespace EconSim.Core.Religious
{
    /// <summary>
    /// Generates the religious organizational hierarchy (parishes, dioceses, archdioceses)
    /// from adherence data, and spawns clergy actors to fill them.
    /// </summary>
    public static class ReligionBootstrap
    {
        const float ParishSeedThreshold = 0.40f;
        const float ParishExpandThreshold = 0.10f;
        const int ParishMaxCounties = 4;
        const int ParishMinCounties = 1;

        const int DioceseMinParishes = 3;
        const int DioceseMaxParishes = 8;

        const int ArchdioceseMinDioceses = 2;
        const int ArchdioceseMaxDioceses = 6;

        public static void Generate(
            ReligionState religion,
            MapData mapData,
            ActorState actors,
            int seed)
        {
            var parishes = new List<Parish>();
            var dioceses = new List<Diocese>();
            var archdioceses = new List<Archdiocese>();

            // For each faith, build parishes from adherence data
            for (int fi = 0; fi < religion.FaithCount; fi++)
            {
                var faithParishes = BuildParishes(fi, religion, mapData);

                // Assign parish IDs before building dioceses (dioceses reference parish IDs)
                for (int i = 0; i < faithParishes.Count; i++)
                    faithParishes[i].Id = parishes.Count + i + 1;
                parishes.AddRange(faithParishes);

                var faithDioceses = BuildDioceses(fi, faithParishes, religion, mapData);
                // Assign diocese IDs before building archdioceses
                for (int i = 0; i < faithDioceses.Count; i++)
                    faithDioceses[i].Id = dioceses.Count + i + 1;
                dioceses.AddRange(faithDioceses);

                var faithArchdioceses = BuildArchdioceses(fi, faithDioceses, religion, mapData);
                for (int i = 0; i < faithArchdioceses.Count; i++)
                    faithArchdioceses[i].Id = archdioceses.Count + i + 1;
                archdioceses.AddRange(faithArchdioceses);
            }

            // Store in state (slot 0 unused)
            religion.Parishes = new Parish[parishes.Count + 1];
            for (int i = 0; i < parishes.Count; i++)
                religion.Parishes[i + 1] = parishes[i];

            religion.Dioceses = new Diocese[dioceses.Count + 1];
            for (int i = 0; i < dioceses.Count; i++)
                religion.Dioceses[i + 1] = dioceses[i];

            religion.Archdioceses = new Archdiocese[archdioceses.Count + 1];
            for (int i = 0; i < archdioceses.Count; i++)
                religion.Archdioceses[i + 1] = archdioceses[i];

            // Build county → parish lookup
            foreach (var parish in parishes)
            {
                foreach (int countyId in parish.CountyIds)
                {
                    if (countyId >= 0 && countyId < religion.CountyParishes.Length)
                        religion.CountyParishes[countyId].Add(parish.Id);
                }
            }

            // Spawn clergy actors
            SpawnClergy(religion, mapData, actors, seed);

            SimLog.Log("Religion",
                $"Hierarchy: {parishes.Count} parishes, {dioceses.Count} dioceses, " +
                $"{archdioceses.Count} archdioceses across {religion.FaithCount} faiths");
        }

        static List<Parish> BuildParishes(int faithIndex, ReligionState state, MapData mapData)
        {
            var parishes = new List<Parish>();

            // Sort counties by adherence for this faith (descending)
            var candidates = new List<(int countyId, float adherence)>();
            foreach (var county in mapData.Counties)
            {
                var adh = state.Adherence[county.Id];
                if (adh != null && adh[faithIndex] >= ParishExpandThreshold)
                    candidates.Add((county.Id, adh[faithIndex]));
            }
            candidates.Sort((a, b) => b.adherence.CompareTo(a.adherence));

            var claimed = new HashSet<int>(); // counties already in a parish for this faith

            foreach (var (seedCountyId, seedAdh) in candidates)
            {
                if (claimed.Contains(seedCountyId)) continue;
                if (seedAdh < ParishSeedThreshold) break; // remaining counties are below seed threshold

                var parish = new Parish
                {
                    FaithIndex = faithIndex,
                    CountyIds = new List<int> { seedCountyId },
                };
                claimed.Add(seedCountyId);

                // Find seat cell (county seat of the seed)
                if (mapData.CountyById.TryGetValue(seedCountyId, out var seedCounty))
                    parish.SeatCellId = seedCounty.SeatCellId;

                // Expand to adjacent counties with sufficient adherence
                if (mapData.CountyById.TryGetValue(seedCountyId, out _))
                {
                    ExpandParish(parish, faithIndex, state, mapData, claimed);
                }

                parishes.Add(parish);
            }

            // Second pass: unclaimed counties above expand threshold get absorbed
            // into the nearest existing parish (if adjacent)
            foreach (var (countyId, adh) in candidates)
            {
                if (claimed.Contains(countyId)) continue;

                // Find adjacent parish
                Parish nearest = null;
                float bestAdh = 0f;
                foreach (var parish in parishes)
                {
                    if (parish.CountyIds.Count >= ParishMaxCounties) continue;
                    foreach (int pid in parish.CountyIds)
                    {
                        if (AreCountiesAdjacent(pid, countyId, mapData))
                        {
                            if (adh > bestAdh)
                            {
                                bestAdh = adh;
                                nearest = parish;
                            }
                            break;
                        }
                    }
                }

                if (nearest != null)
                {
                    nearest.CountyIds.Add(countyId);
                    claimed.Add(countyId);
                }
            }

            return parishes;
        }

        static void ExpandParish(Parish parish, int faithIndex, ReligionState state,
            MapData mapData, HashSet<int> claimed)
        {
            // BFS expansion from seed county
            var frontier = new Queue<int>();
            frontier.Enqueue(parish.CountyIds[0]);

            while (frontier.Count > 0 && parish.CountyIds.Count < ParishMaxCounties)
            {
                int current = frontier.Dequeue();
                foreach (int neighbor in GetAdjacentCounties(current, mapData))
                {
                    if (claimed.Contains(neighbor)) continue;
                    if (parish.CountyIds.Count >= ParishMaxCounties) break;

                    var adh = state.Adherence[neighbor];
                    if (adh == null || adh[faithIndex] < ParishExpandThreshold) continue;

                    parish.CountyIds.Add(neighbor);
                    claimed.Add(neighbor);
                    frontier.Enqueue(neighbor);
                }
            }
        }

        static List<Diocese> BuildDioceses(int faithIndex, List<Parish> parishes,
            ReligionState state, MapData mapData)
        {
            if (parishes.Count == 0) return new List<Diocese>();

            // If too few parishes for multiple dioceses, make one
            if (parishes.Count <= DioceseMaxParishes)
            {
                var diocese = new Diocese
                {
                    FaithIndex = faithIndex,
                    ParishIds = new List<int>(),
                    CathedralCellId = parishes[0].SeatCellId,
                };
                foreach (var p in parishes)
                    diocese.ParishIds.Add(p.Id);
                return new List<Diocese> { diocese };
            }

            // Greedy geographic clustering of parishes into dioceses
            return ClusterIntoDioceses(faithIndex, parishes, mapData);
        }

        static List<Diocese> ClusterIntoDioceses(int faithIndex, List<Parish> parishes, MapData mapData)
        {
            var dioceses = new List<Diocese>();
            int targetDioceseCount = Math.Max(1, (parishes.Count + DioceseMaxParishes - 1) / DioceseMaxParishes);

            // Pick diocese seeds: parishes spread out geographically
            var seeds = PickSpreadSeeds(parishes, targetDioceseCount, mapData);
            var assignment = new int[parishes.Count]; // parish local index → diocese local index
            for (int i = 0; i < assignment.Length; i++)
                assignment[i] = -1;

            // Seed assignment
            for (int d = 0; d < seeds.Count; d++)
                assignment[seeds[d]] = d;

            // Assign remaining parishes to nearest seed by centroid distance
            for (int i = 0; i < parishes.Count; i++)
            {
                if (assignment[i] >= 0) continue;
                var center = GetParishCentroid(parishes[i], mapData);
                float bestDist = float.MaxValue;
                int bestD = 0;
                for (int d = 0; d < seeds.Count; d++)
                {
                    var seedCenter = GetParishCentroid(parishes[seeds[d]], mapData);
                    float dist = Vec2.Distance(center, seedCenter);
                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        bestD = d;
                    }
                }
                assignment[i] = bestD;
            }

            // Build diocese objects
            for (int d = 0; d < seeds.Count; d++)
            {
                var diocese = new Diocese
                {
                    FaithIndex = faithIndex,
                    ParishIds = new List<int>(),
                    CathedralCellId = parishes[seeds[d]].SeatCellId,
                };

                for (int i = 0; i < parishes.Count; i++)
                {
                    if (assignment[i] == d)
                        diocese.ParishIds.Add(parishes[i].Id);
                }

                if (diocese.ParishIds.Count > 0)
                    dioceses.Add(diocese);
            }

            return dioceses;
        }

        static List<Archdiocese> BuildArchdioceses(int faithIndex, List<Diocese> dioceses,
            ReligionState state, MapData mapData)
        {
            if (dioceses.Count == 0) return new List<Archdiocese>();

            // If too few dioceses for multiple archdioceses, make one
            if (dioceses.Count <= ArchdioceseMaxDioceses)
            {
                var arch = new Archdiocese
                {
                    FaithIndex = faithIndex,
                    DioceseIds = new List<int>(),
                    SeatCellId = dioceses[0].CathedralCellId,
                };
                foreach (var d in dioceses)
                    arch.DioceseIds.Add(d.Id);
                return new List<Archdiocese> { arch };
            }

            // Cluster dioceses into archdioceses
            return ClusterIntoArchdioceses(faithIndex, dioceses, mapData);
        }

        static List<Archdiocese> ClusterIntoArchdioceses(int faithIndex, List<Diocese> dioceses, MapData mapData)
        {
            var archdioceses = new List<Archdiocese>();
            int targetCount = Math.Max(1, (dioceses.Count + ArchdioceseMaxDioceses - 1) / ArchdioceseMaxDioceses);

            // Pick seeds spread geographically
            var seeds = PickDioceseSpreadSeeds(dioceses, targetCount, mapData);
            var assignment = new int[dioceses.Count];
            for (int i = 0; i < assignment.Length; i++)
                assignment[i] = -1;

            for (int a = 0; a < seeds.Count; a++)
                assignment[seeds[a]] = a;

            // Assign remaining to nearest seed
            for (int i = 0; i < dioceses.Count; i++)
            {
                if (assignment[i] >= 0) continue;
                var center = GetDioceseCentroid(dioceses[i], mapData);
                float bestDist = float.MaxValue;
                int bestA = 0;
                for (int a = 0; a < seeds.Count; a++)
                {
                    var seedCenter = GetDioceseCentroid(dioceses[seeds[a]], mapData);
                    float dist = Vec2.Distance(center, seedCenter);
                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        bestA = a;
                    }
                }
                assignment[i] = bestA;
            }

            for (int a = 0; a < seeds.Count; a++)
            {
                var arch = new Archdiocese
                {
                    FaithIndex = faithIndex,
                    DioceseIds = new List<int>(),
                    SeatCellId = dioceses[seeds[a]].CathedralCellId,
                };

                for (int i = 0; i < dioceses.Count; i++)
                {
                    if (assignment[i] == a)
                        arch.DioceseIds.Add(dioceses[i].Id);
                }

                if (arch.DioceseIds.Count > 0)
                    archdioceses.Add(arch);
            }

            return archdioceses;
        }

        static void SpawnClergy(ReligionState religion, MapData mapData, ActorState actors, int seed)
        {
            // Build CultureType lookup
            var cultureTypeByName = new Dictionary<string, CultureType>(StringComparer.OrdinalIgnoreCase);
            foreach (var ct in CultureTypes.All)
                cultureTypeByName[ct.Name] = ct;

            var popSeed = new PopGenSeed(seed + 7919); // offset from political seed
            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Collect existing names to avoid duplicates
            if (actors.Actors != null)
            {
                for (int i = 1; i < actors.Actors.Length; i++)
                {
                    if (actors.Actors[i] != null)
                        usedNames.Add(actors.Actors[i].Name);
                }
            }

            // Count new actors needed
            int clergyCount = 0;
            for (int i = 1; i < religion.Parishes.Length; i++)
                if (religion.Parishes[i] != null) clergyCount++;
            for (int i = 1; i < religion.Dioceses.Length; i++)
                if (religion.Dioceses[i] != null) clergyCount++;
            for (int i = 1; i < religion.Archdioceses.Length; i++)
                if (religion.Archdioceses[i] != null) clergyCount++;

            if (clergyCount == 0) return;

            // Expand actor array
            int oldCount = actors.ActorCount;
            int newCount = oldCount + clergyCount;
            var newActors = new Actor[newCount + 1];
            Array.Copy(actors.Actors, newActors, actors.Actors.Length);
            actors.Actors = newActors;
            actors.ActorCount = newCount;

            int nextId = oldCount + 1;

            // Spawn priests for parishes
            for (int i = 1; i < religion.Parishes.Length; i++)
            {
                var parish = religion.Parishes[i];
                if (parish == null) continue;

                int religionId = religion.FaithIndexToReligion[parish.FaithIndex];
                int cultureId = GetDominantCulture(parish.SeatCellId, mapData);
                var actor = SpawnClergyActor(
                    nextId++, cultureId, religionId, parish.SeatCellId,
                    GetCountyForCell(parish.SeatCellId, mapData),
                    seed, cultureTypeByName, popSeed, usedNames, mapData);
                actors.Actors[actor.Id] = actor;
                parish.PriestActorId = actor.Id;
            }

            // Spawn bishops for dioceses
            for (int i = 1; i < religion.Dioceses.Length; i++)
            {
                var diocese = religion.Dioceses[i];
                if (diocese == null) continue;

                int religionId = religion.FaithIndexToReligion[diocese.FaithIndex];
                int cultureId = GetDominantCulture(diocese.CathedralCellId, mapData);
                var actor = SpawnClergyActor(
                    nextId++, cultureId, religionId, diocese.CathedralCellId,
                    GetCountyForCell(diocese.CathedralCellId, mapData),
                    seed, cultureTypeByName, popSeed, usedNames, mapData);
                actors.Actors[actor.Id] = actor;
                diocese.BishopActorId = actor.Id;
            }

            // Spawn archbishops for archdioceses
            for (int i = 1; i < religion.Archdioceses.Length; i++)
            {
                var arch = religion.Archdioceses[i];
                if (arch == null) continue;

                int religionId = religion.FaithIndexToReligion[arch.FaithIndex];
                int cultureId = GetDominantCulture(arch.SeatCellId, mapData);
                var actor = SpawnClergyActor(
                    nextId++, cultureId, religionId, arch.SeatCellId,
                    GetCountyForCell(arch.SeatCellId, mapData),
                    seed, cultureTypeByName, popSeed, usedNames, mapData);
                actors.Actors[actor.Id] = actor;
                arch.ArchbishopActorId = actor.Id;
            }
        }

        static Actor SpawnClergyActor(
            int actorId, int cultureId, int religionId, int seatCellId, int countyId,
            int seed, Dictionary<string, CultureType> cultureTypeByName,
            PopGenSeed popSeed, HashSet<string> usedNames, MapData mapData)
        {
            CultureType ct = CultureTypes.All[0];
            if (cultureId > 0 && mapData.CultureById != null
                && mapData.CultureById.TryGetValue(cultureId, out var culture)
                && cultureTypeByName.TryGetValue(culture.TypeName, out var found))
            {
                ct = found;
            }

            // Seeded gender
            uint gRng = (uint)(seed * 31 + actorId * 97 + 12345);
            gRng ^= gRng >> 16;
            gRng *= 0x85EBCA6Bu;
            bool isFemale = (gRng & 1) == 0;

            string name = PopNameGenerator.GeneratePersonName(actorId, cultureId, isFemale, ct, popSeed, usedNames);
            string givenName = PopNameGenerator.GeneratePersonGivenName(actorId, cultureId, isFemale, ct, popSeed);

            // Age 30-60 for clergy
            uint aRng = (uint)(seed * 53 + actorId * 113 + 54321);
            aRng ^= aRng >> 16;
            aRng *= 0x85EBCA6Bu;
            int ageYears = 30 + (int)(aRng % 31);
            int birthDay = -(ageYears * 365);

            return new Actor
            {
                Id = actorId,
                Name = name,
                GivenName = givenName,
                BirthDay = birthDay,
                IsFemale = isFemale,
                CultureId = cultureId,
                ReligionId = religionId,
                TitleId = 0, // clergy don't hold political titles
                LiegeActorId = 0,
                CountyId = countyId,
            };
        }

        // --- Helpers ---

        static int GetDominantCulture(int cellId, MapData mapData)
        {
            if (mapData.CellById.TryGetValue(cellId, out var cell))
                return cell.CultureId;
            return 0;
        }

        static int GetCountyForCell(int cellId, MapData mapData)
        {
            if (mapData.CellById.TryGetValue(cellId, out var cell))
                return cell.CountyId;
            return 0;
        }

        static bool AreCountiesAdjacent(int countyA, int countyB, MapData mapData)
        {
            // Brute force via cells — acceptable at init time
            if (!mapData.CountyById.TryGetValue(countyA, out var ca)) return false;
            foreach (int cellId in ca.CellIds)
            {
                if (!mapData.CellById.TryGetValue(cellId, out var cell)) continue;
                foreach (int nid in cell.NeighborIds)
                {
                    if (mapData.CellById.TryGetValue(nid, out var neighbor) && neighbor.CountyId == countyB)
                        return true;
                }
            }
            return false;
        }

        static int[] GetAdjacentCounties(int countyId, MapData mapData)
        {
            if (!mapData.CountyById.TryGetValue(countyId, out var county))
                return Array.Empty<int>();

            var adj = new HashSet<int>();
            foreach (int cellId in county.CellIds)
            {
                if (!mapData.CellById.TryGetValue(cellId, out var cell)) continue;
                foreach (int nid in cell.NeighborIds)
                {
                    if (mapData.CellById.TryGetValue(nid, out var neighbor)
                        && neighbor.CountyId > 0 && neighbor.CountyId != countyId)
                        adj.Add(neighbor.CountyId);
                }
            }

            var result = new int[adj.Count];
            adj.CopyTo(result);
            return result;
        }

        static Vec2 GetParishCentroid(Parish parish, MapData mapData)
        {
            float x = 0f, y = 0f;
            int count = 0;
            foreach (int countyId in parish.CountyIds)
            {
                if (mapData.CountyById.TryGetValue(countyId, out var county))
                {
                    x += county.Centroid.X;
                    y += county.Centroid.Y;
                    count++;
                }
            }
            return count > 0 ? new Vec2(x / count, y / count) : Vec2.Zero;
        }

        static Vec2 GetDioceseCentroid(Diocese diocese, MapData mapData)
        {
            // Average of cathedral cell position (simple — could average parish centroids)
            if (mapData.CellById.TryGetValue(diocese.CathedralCellId, out var cell))
                return cell.Center;
            return Vec2.Zero;
        }

        static List<int> PickSpreadSeeds(List<Parish> parishes, int count, MapData mapData)
        {
            return PickSpreadSeedsGeneric(parishes.Count, count,
                i => GetParishCentroid(parishes[i], mapData));
        }

        static List<int> PickDioceseSpreadSeeds(List<Diocese> dioceses, int count, MapData mapData)
        {
            return PickSpreadSeedsGeneric(dioceses.Count, count,
                i => GetDioceseCentroid(dioceses[i], mapData));
        }

        /// <summary>
        /// Pick N spread-out seeds from a list using farthest-point sampling.
        /// Returns indices into the source list.
        /// </summary>
        static List<int> PickSpreadSeedsGeneric(int total, int count, Func<int, Vec2> getCentroid)
        {
            if (total <= count)
            {
                var all = new List<int>(total);
                for (int i = 0; i < total; i++) all.Add(i);
                return all;
            }

            var seeds = new List<int> { 0 }; // start with first item
            var minDist = new float[total];
            var firstCenter = getCentroid(0);
            for (int i = 0; i < total; i++)
                minDist[i] = Vec2.Distance(getCentroid(i), firstCenter);

            while (seeds.Count < count)
            {
                // Pick the item farthest from all existing seeds
                int best = -1;
                float bestDist = -1f;
                for (int i = 0; i < total; i++)
                {
                    if (minDist[i] > bestDist)
                    {
                        bestDist = minDist[i];
                        best = i;
                    }
                }

                if (best < 0) break;
                seeds.Add(best);
                minDist[best] = 0f;

                // Update min distances
                var newCenter = getCentroid(best);
                for (int i = 0; i < total; i++)
                {
                    float d = Vec2.Distance(getCentroid(i), newCenter);
                    if (d < minDist[i])
                        minDist[i] = d;
                }
            }

            return seeds;
        }
    }
}
