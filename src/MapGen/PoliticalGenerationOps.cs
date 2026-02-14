using System;
using System.Collections.Generic;

namespace MapGen.Core
{
    /// <summary>
    /// Bottom-up political assignment: landmasses -> cultures -> counties -> realms -> provinces.
    /// Counties form first (terrain-respecting), then group upward into realms via culture majority vote.
    /// </summary>
    public static class PoliticalGenerationOps
    {
        public static void Compute(
            PoliticalField political,
            BiomeField biomes,
            RiverField rivers,
            ElevationField elevation,
            MapGenConfig config)
        {
            DetectLandmasses(political, biomes, elevation);
            SpreadCultures(political, biomes, rivers, elevation, config);
            AssignCountiesGlobal(political, biomes, rivers, elevation, config);
            DeriveRealmsFromCountyCulture(political);
            AssignProvincesAtCountyGranularity(political, biomes, rivers, elevation, config);
        }

        // ────────────────────────────────────────────────────────────────
        // Step 1: DetectLandmasses — UNCHANGED
        // ────────────────────────────────────────────────────────────────

        static void DetectLandmasses(PoliticalField pol, BiomeField biomes, ElevationField elevation)
        {
            var mesh = pol.Mesh;
            int n = mesh.CellCount;
            var visited = new bool[n];
            int nextLandmassId = 1;

            for (int i = 0; i < n; i++)
            {
                bool land = elevation.IsLand(i) && !biomes.IsLakeCell[i];
                if (!land)
                {
                    pol.LandmassId[i] = -1;
                    continue;
                }

                if (visited[i])
                    continue;

                var queue = new Queue<int>();
                queue.Enqueue(i);
                visited[i] = true;
                pol.LandmassId[i] = nextLandmassId;

                while (queue.Count > 0)
                {
                    int c = queue.Dequeue();
                    int[] neighbors = mesh.CellNeighbors[c];
                    for (int ni = 0; ni < neighbors.Length; ni++)
                    {
                        int nb = neighbors[ni];
                        if (nb < 0 || nb >= n || visited[nb])
                            continue;

                        if (!(elevation.IsLand(nb) && !biomes.IsLakeCell[nb]))
                            continue;

                        visited[nb] = true;
                        pol.LandmassId[nb] = nextLandmassId;
                        queue.Enqueue(nb);
                    }
                }

                nextLandmassId++;
            }

            pol.LandmassCount = nextLandmassId - 1;
        }

        // ────────────────────────────────────────────────────────────────
        // Step 2: SpreadCultures — replaces AssignRealms
        // Same seed selection + transport frontier, writes CultureId[]
        // ────────────────────────────────────────────────────────────────

        static void SpreadCultures(PoliticalField pol, BiomeField biomes, RiverField rivers, ElevationField elevation, MapGenConfig config)
        {
            var mesh = pol.Mesh;
            var landCells = CollectLandCells(elevation, biomes);
            if (landCells.Count == 0)
            {
                pol.CultureCount = 0;
                pol.Capitals = Array.Empty<int>();
                return;
            }

            HeightmapTemplateTuningProfile profile = HeightmapTemplateCompiler.ResolveTuningProfile(config.Template, config);
            float realmScale = profile != null ? profile.RealmTargetScale : 1f;
            if (realmScale <= 0f) realmScale = 1f;

            Func<int, float> capitalScore = c => biomes.Suitability[c] + biomes.Population[c] * 0.02f;
            var landmassStats = BuildLandmassStats(landCells, pol.LandmassId, biomes);
            var eligibleLandmasses = ResolveEligibleRealmLandmasses(landmassStats, config);

            var cultureSeedCells = new List<int>(landCells.Count);
            for (int i = 0; i < landCells.Count; i++)
            {
                int c = landCells[i];
                if ((uint)c >= (uint)pol.LandmassId.Length)
                    continue;

                if (eligibleLandmasses.Contains(pol.LandmassId[c]))
                    cultureSeedCells.Add(c);
            }

            if (cultureSeedCells.Count == 0)
            {
                int fallbackLandmass = SelectFallbackLandmass(landmassStats);
                if (fallbackLandmass > 0)
                {
                    eligibleLandmasses.Add(fallbackLandmass);
                    for (int i = 0; i < landCells.Count; i++)
                    {
                        int c = landCells[i];
                        if ((uint)c < (uint)pol.LandmassId.Length && pol.LandmassId[c] == fallbackLandmass)
                            cultureSeedCells.Add(c);
                    }
                }
            }

            if (cultureSeedCells.Count == 0)
                cultureSeedCells = landCells;

            int targetCultures = Clamp((int)Math.Round((cultureSeedCells.Count / 900f) * realmScale), 1, 24);
            var capitals = SelectSeeds(
                cultureSeedCells,
                mesh,
                targetCultures,
                scoreCell: capitalScore,
                minSpacingKm: EstimateSpacing(mesh, targetCultures) * 0.35f);

            EnsureLandmassSeedCoverage(capitals, landCells, pol.LandmassId, capitalScore, eligibleLandmasses);
            if (capitals.Count == 0)
                capitals.Add(cultureSeedCells[0]);

            pol.CultureCount = capitals.Count;
            pol.Capitals = capitals.ToArray();

            AssignByTransportFrontier(
                pol.CultureId,
                landCells,
                capitals,
                mesh,
                biomes,
                rivers,
                config);
        }

        // ────────────────────────────────────────────────────────────────
        // Step 3: AssignCountiesGlobal — single global pass (no per-province loop)
        // ────────────────────────────────────────────────────────────────

        static void AssignCountiesGlobal(PoliticalField pol, BiomeField biomes, RiverField rivers, ElevationField elevation, MapGenConfig config)
        {
            var mesh = pol.Mesh;
            HeightmapTemplateTuningProfile profile = HeightmapTemplateCompiler.ResolveTuningProfile(config.Template, config);
            float countyScale = profile != null ? profile.CountyTargetScale : 1f;
            if (countyScale <= 0f) countyScale = 1f;

            // Collect all land cells with culture assignment
            var landCells = new List<int>();
            var landMask = new bool[mesh.CellCount];
            for (int i = 0; i < mesh.CellCount; i++)
            {
                if (pol.CultureId[i] > 0)
                {
                    landCells.Add(i);
                    landMask[i] = true;
                }
            }

            if (landCells.Count == 0)
            {
                pol.CountyId = new int[mesh.CellCount];
                pol.CountySeats = Array.Empty<int>();
                pol.CountyCount = 0;
                return;
            }

            int[][] sharedNeighborEdges = BuildNeighborEdgeLookup(mesh);
            float nominalMovementCost = EstimateNominalMovementCost(landCells, biomes);
            float[] riverPenaltyByEdge = BuildRiverCrossingPenaltyByEdge(mesh, rivers, config, nominalMovementCost);
            float nominalNeighborDistance = EstimateNominalNeighborDistance(mesh, landMask);

            int targetCounties = Clamp((int)Math.Round((landCells.Count / 120f) * countyScale), 1, 4096);

            var seeds = SelectSeeds(
                landCells,
                mesh,
                targetCounties,
                scoreCell: c => biomes.Population[c] * 1.25f + biomes.Suitability[c] * 0.4f,
                minSpacingKm: EstimateSpacing(mesh, targetCounties) * 0.18f);

            DeduplicateSeedCells(seeds);
            if (seeds.Count == 0)
                seeds.Add(landCells[0]);

            float totalPopulation = SumPopulation(landCells, biomes);
            float targetCountyPopulation = totalPopulation / Math.Max(1, seeds.Count);
            if (targetCountyPopulation < 1f)
                targetCountyPopulation = 1f;

            var localAssignment = new int[mesh.CellCount];
            AssignCountiesByPopulationFrontier(
                localAssignment,
                landCells,
                seeds,
                mesh,
                biomes,
                sharedNeighborEdges,
                riverPenaltyByEdge,
                nominalNeighborDistance,
                targetCountyPopulation);
            MergeCountyOrphans(
                localAssignment,
                landCells,
                mesh,
                biomes,
                sharedNeighborEdges,
                riverPenaltyByEdge,
                nominalNeighborDistance,
                targetCountyPopulation);

            // Remap local IDs to contiguous global IDs and extract county seats
            var countyIds = new int[mesh.CellCount];
            var localToGlobal = new Dictionary<int, int>();
            var seatPopulationByGlobal = new Dictionary<int, float>();
            var seatCellByGlobal = new Dictionary<int, int>();
            var firstCellByGlobal = new Dictionary<int, int>();
            int nextCounty = 1;

            for (int i = 0; i < landCells.Count; i++)
            {
                int c = landCells[i];
                int local = localAssignment[c];
                if (local <= 0)
                    local = 1;

                if (!localToGlobal.TryGetValue(local, out int globalId))
                {
                    globalId = nextCounty++;
                    localToGlobal[local] = globalId;
                }

                countyIds[c] = globalId;
                if (!firstCellByGlobal.ContainsKey(globalId))
                    firstCellByGlobal[globalId] = c;

                float pop = CellPopulation(biomes, c);
                if (!seatPopulationByGlobal.TryGetValue(globalId, out float bestPop) || pop > bestPop)
                {
                    seatPopulationByGlobal[globalId] = pop;
                    seatCellByGlobal[globalId] = c;
                }
            }

            int totalCounties = nextCounty - 1;
            var countySeats = new int[totalCounties];
            for (int countyId = 1; countyId <= totalCounties; countyId++)
            {
                if (seatCellByGlobal.TryGetValue(countyId, out int seat))
                    countySeats[countyId - 1] = seat;
                else if (firstCellByGlobal.TryGetValue(countyId, out int fallbackSeat))
                    countySeats[countyId - 1] = fallbackSeat;
                else
                    countySeats[countyId - 1] = landCells[0];
            }

            pol.CountyId = countyIds;
            pol.CountySeats = countySeats;
            pol.CountyCount = totalCounties;
        }

        // ────────────────────────────────────────────────────────────────
        // Step 4: DeriveRealmsFromCountyCulture — majority culture vote per county
        // ────────────────────────────────────────────────────────────────

        static void DeriveRealmsFromCountyCulture(PoliticalField pol)
        {
            var mesh = pol.Mesh;
            int n = mesh.CellCount;

            // Tally culture votes per county
            // cultureTally[county][culture] = vote count
            var cultureTally = new Dictionary<int, Dictionary<int, int>>();
            for (int cell = 0; cell < n; cell++)
            {
                int county = pol.CountyId[cell];
                if (county <= 0) continue;
                int culture = pol.CultureId[cell];
                if (culture <= 0) continue;

                if (!cultureTally.TryGetValue(county, out var tally))
                {
                    tally = new Dictionary<int, int>();
                    cultureTally[county] = tally;
                }

                tally.TryGetValue(culture, out int votes);
                tally[culture] = votes + 1;
            }

            // Assign each county to its majority culture (ties: lower culture ID)
            var countyRealm = new int[pol.CountyCount + 1];
            for (int county = 1; county <= pol.CountyCount; county++)
            {
                if (!cultureTally.TryGetValue(county, out var tally))
                {
                    // Fallback: use culture of seat cell
                    int seat = pol.CountySeats[county - 1];
                    countyRealm[county] = pol.CultureId[seat] > 0 ? pol.CultureId[seat] : 1;
                    continue;
                }

                int bestCulture = 0;
                int bestVotes = 0;
                foreach (var kvp in tally)
                {
                    if (kvp.Value > bestVotes || (kvp.Value == bestVotes && kvp.Key < bestCulture))
                    {
                        bestCulture = kvp.Key;
                        bestVotes = kvp.Value;
                    }
                }

                countyRealm[county] = bestCulture > 0 ? bestCulture : 1;
            }

            // Stamp RealmId on all cells based on their county's realm
            pol.RealmCount = pol.CultureCount; // 1:1 culture → realm
            for (int cell = 0; cell < n; cell++)
            {
                int county = pol.CountyId[cell];
                if (county > 0 && county < countyRealm.Length)
                    pol.RealmId[cell] = countyRealm[county];
            }
        }

        // ────────────────────────────────────────────────────────────────
        // Step 5: AssignProvincesAtCountyGranularity — county adjacency graph + Dijkstra
        // ────────────────────────────────────────────────────────────────

        static void AssignProvincesAtCountyGranularity(PoliticalField pol, BiomeField biomes, RiverField rivers, ElevationField elevation, MapGenConfig config)
        {
            var mesh = pol.Mesh;
            int n = mesh.CellCount;
            HeightmapTemplateTuningProfile profile = HeightmapTemplateCompiler.ResolveTuningProfile(config.Template, config);
            float provinceScale = profile != null ? profile.ProvinceTargetScale : 1f;
            if (provinceScale <= 0f) provinceScale = 1f;

            int countyCount = pol.CountyCount;
            if (countyCount == 0)
            {
                pol.ProvinceId = new int[n];
                pol.ProvinceCount = 0;
                return;
            }

            // Build county metadata arrays (1-indexed)
            var seatCell = new int[countyCount + 1];
            var countyRealm = new int[countyCount + 1];
            var countyPopulation = new float[countyCount + 1];
            var countyCellCount = new int[countyCount + 1];

            for (int county = 1; county <= countyCount; county++)
                seatCell[county] = pol.CountySeats[county - 1];

            for (int cell = 0; cell < n; cell++)
            {
                int county = pol.CountyId[cell];
                if (county <= 0 || county > countyCount) continue;
                countyCellCount[county]++;
                countyPopulation[county] += CellPopulation(biomes, cell);
                countyRealm[county] = pol.RealmId[cell];
            }

            // Build county adjacency graph
            var neighborSets = new HashSet<int>[countyCount + 1];
            for (int i = 1; i <= countyCount; i++)
                neighborSets[i] = new HashSet<int>();

            for (int cell = 0; cell < n; cell++)
            {
                int county = pol.CountyId[cell];
                if (county <= 0) continue;

                int[] neighbors = mesh.CellNeighbors[cell];
                if (neighbors == null) continue;

                for (int ni = 0; ni < neighbors.Length; ni++)
                {
                    int nb = neighbors[ni];
                    if ((uint)nb >= (uint)n) continue;
                    int nbCounty = pol.CountyId[nb];
                    if (nbCounty > 0 && nbCounty != county)
                        neighborSets[county].Add(nbCounty);
                }
            }

            var countyNeighbors = new int[countyCount + 1][];
            for (int i = 1; i <= countyCount; i++)
            {
                var set = neighborSets[i];
                var arr = new int[set.Count];
                set.CopyTo(arr);
                countyNeighbors[i] = arr;
            }

            // Group counties by realm
            var realmCounties = new List<int>[pol.RealmCount + 1];
            var realmCellCount = new int[pol.RealmCount + 1];
            for (int i = 1; i <= pol.RealmCount; i++)
                realmCounties[i] = new List<int>();

            for (int county = 1; county <= countyCount; county++)
            {
                int realm = countyRealm[county];
                if (realm > 0 && realm <= pol.RealmCount)
                {
                    realmCounties[realm].Add(county);
                    realmCellCount[realm] += countyCellCount[county];
                }
            }

            // Per-realm province assignment via competitive Dijkstra on county graph
            var provinceIds = new int[n];
            int nextProvince = 1;

            for (int realm = 1; realm <= pol.RealmCount; realm++)
            {
                var counties = realmCounties[realm];
                if (counties.Count == 0) continue;

                int provinceTarget = Clamp((int)Math.Round((realmCellCount[realm] / 450f) * provinceScale), 1, 18);

                // Select seed counties (highest population, with spacing)
                var seedCounties = SelectCountySeeds(
                    counties,
                    mesh,
                    provinceTarget,
                    county => countyPopulation[county],
                    EstimateSpacing(mesh, provinceTarget) * 0.25f,
                    seatCell);

                if (seedCounties.Count == 0)
                    seedCounties.Add(counties[0]);

                // Competitive Dijkstra on county graph
                // Reuse FrontierQueue: Cell=countyId, RealmId=provinceLocalId
                var bestCost = new float[countyCount + 1];
                var countyProvince = new int[countyCount + 1];
                for (int i = 0; i <= countyCount; i++)
                    bestCost[i] = float.PositiveInfinity;

                var frontier = new FrontierQueue(Math.Max(64, counties.Count));

                for (int i = 0; i < seedCounties.Count; i++)
                {
                    int seedCounty = seedCounties[i];
                    int localProvId = i + 1;
                    bestCost[seedCounty] = 0f;
                    countyProvince[seedCounty] = localProvId;
                    frontier.Push(seedCounty, localProvId, 0f);
                }

                const float tieEpsilon = 1e-4f;
                while (frontier.Count > 0)
                {
                    FrontierNode current = frontier.Pop();
                    int curCounty = current.Cell;

                    if ((uint)curCounty > (uint)countyCount)
                        continue;

                    float settledCost = bestCost[curCounty];
                    if (current.Cost > settledCost + tieEpsilon)
                        continue;

                    int[] nbCounties = countyNeighbors[curCounty];
                    if (nbCounties == null) continue;

                    for (int ni = 0; ni < nbCounties.Length; ni++)
                    {
                        int nbCounty = nbCounties[ni];
                        if ((uint)nbCounty > (uint)countyCount) continue;

                        // Stay within realm
                        if (countyRealm[nbCounty] != realm) continue;

                        float edgeCost = ComputeCountyEdgeCost(curCounty, nbCounty, mesh, biomes, seatCell);
                        if (!(edgeCost > 0f) || float.IsNaN(edgeCost) || float.IsInfinity(edgeCost))
                            continue;

                        float candidateCost = current.Cost + edgeCost;
                        float existingCost = bestCost[nbCounty];
                        int existingProv = countyProvince[nbCounty];

                        bool better = candidateCost + tieEpsilon < existingCost;
                        bool tieBreak = Math.Abs(candidateCost - existingCost) <= tieEpsilon &&
                            (existingProv == 0 || current.RealmId < existingProv);

                        if (!better && !tieBreak)
                            continue;

                        bestCost[nbCounty] = candidateCost;
                        countyProvince[nbCounty] = current.RealmId;
                        frontier.Push(nbCounty, current.RealmId, candidateCost);
                    }
                }

                // Safety net: nearest-seed fallback for disconnected counties
                for (int ci = 0; ci < counties.Count; ci++)
                {
                    int county = counties[ci];
                    if (countyProvince[county] != 0)
                        continue;

                    int bestLocal = 1;
                    float bestDist = float.MaxValue;
                    for (int si = 0; si < seedCounties.Count; si++)
                    {
                        int seedCounty = seedCounties[si];
                        float d = Vec2.Distance(mesh.CellCenters[seatCell[county]], mesh.CellCenters[seatCell[seedCounty]]);
                        if (d < bestDist)
                        {
                            bestDist = d;
                            bestLocal = si + 1;
                        }
                    }

                    countyProvince[county] = bestLocal;
                }

                // Remap local province IDs to global
                var remap = new Dictionary<int, int>();
                for (int s = 0; s < seedCounties.Count; s++)
                    remap[s + 1] = nextProvince++;

                // Stamp ProvinceId on all cells via county membership
                for (int ci = 0; ci < counties.Count; ci++)
                {
                    int county = counties[ci];
                    int local = countyProvince[county];
                    if (local <= 0) local = 1;
                    if (!remap.TryGetValue(local, out int globalProv))
                        globalProv = remap.Count > 0 ? nextProvince - 1 : nextProvince++;

                    // Stamp all cells in this county
                    // (iterate global array — county membership is sparse)
                    for (int cell = 0; cell < n; cell++)
                    {
                        if (pol.CountyId[cell] == county)
                            provinceIds[cell] = globalProv;
                    }
                }
            }

            pol.ProvinceId = provinceIds;
            pol.ProvinceCount = nextProvince - 1;
        }

        // ────────────────────────────────────────────────────────────────
        // Province helpers
        // ────────────────────────────────────────────────────────────────

        static List<int> SelectCountySeeds(
            List<int> countyIds,
            CellMesh mesh,
            int target,
            Func<int, float> scoreCounty,
            float minSpacingKm,
            int[] seatCell)
        {
            var ranked = new List<int>(countyIds);
            ranked.Sort((a, b) => scoreCounty(b).CompareTo(scoreCounty(a)));

            var seeds = new List<int>();
            for (int i = 0; i < ranked.Count && seeds.Count < target; i++)
            {
                int c = ranked[i];
                bool farEnough = true;
                for (int s = 0; s < seeds.Count; s++)
                {
                    float d = Vec2.Distance(mesh.CellCenters[seatCell[c]], mesh.CellCenters[seatCell[seeds[s]]]);
                    if (d < minSpacingKm)
                    {
                        farEnough = false;
                        break;
                    }
                }

                if (farEnough)
                    seeds.Add(c);
            }

            for (int i = 0; i < ranked.Count && seeds.Count < target; i++)
            {
                int c = ranked[i];
                if (!seeds.Contains(c))
                    seeds.Add(c);
            }

            return seeds;
        }

        static float ComputeCountyEdgeCost(
            int countyA, int countyB,
            CellMesh mesh, BiomeField biomes,
            int[] seatCell)
        {
            int seatA = seatCell[countyA];
            int seatB = seatCell[countyB];
            float distance = Vec2.Distance(mesh.CellCenters[seatA], mesh.CellCenters[seatB]);

            float movA = biomes.MovementCost[seatA];
            float movB = biomes.MovementCost[seatB];
            if (movA < 1f) movA = 1f;
            if (movB < 1f) movB = 1f;
            float avgMovement = 0.5f * (movA + movB);

            return distance * avgMovement;
        }

        // ────────────────────────────────────────────────────────────────
        // County formation helpers (unchanged)
        // ────────────────────────────────────────────────────────────────

        static void DeduplicateSeedCells(List<int> seeds)
        {
            if (seeds == null || seeds.Count <= 1)
                return;

            var seen = new HashSet<int>();
            int write = 0;
            for (int i = 0; i < seeds.Count; i++)
            {
                int cell = seeds[i];
                if (!seen.Add(cell))
                    continue;

                seeds[write++] = cell;
            }

            if (write < seeds.Count)
                seeds.RemoveRange(write, seeds.Count - write);
        }

        static float EstimateAveragePopulation(List<int> cells, BiomeField biomes)
        {
            if (cells == null || cells.Count == 0)
                return 1f;

            float total = SumPopulation(cells, biomes);
            return total / Math.Max(1, cells.Count);
        }

        static float SumPopulation(List<int> cells, BiomeField biomes)
        {
            if (cells == null || cells.Count == 0)
                return 0f;

            float total = 0f;
            for (int i = 0; i < cells.Count; i++)
                total += CellPopulation(biomes, cells[i]);
            return total;
        }

        static float CellPopulation(BiomeField biomes, int cell)
        {
            if ((uint)cell >= (uint)biomes.Population.Length)
                return 0f;

            float pop = biomes.Population[cell];
            if (!(pop > 0f) || float.IsNaN(pop) || float.IsInfinity(pop))
                return 0f;

            return pop;
        }

        static void AssignCountiesByPopulationFrontier(
            int[] outAssignment,
            List<int> cells,
            List<int> seeds,
            CellMesh mesh,
            BiomeField biomes,
            int[][] neighborEdges,
            float[] riverPenaltyByEdge,
            float nominalNeighborDistance,
            float targetCountyPopulation)
        {
            if (cells == null || cells.Count == 0 || seeds == null || seeds.Count == 0)
                return;

            int n = mesh.CellCount;
            var isCandidateCell = new bool[n];
            for (int i = 0; i < cells.Count; i++)
            {
                int c = cells[i];
                if ((uint)c < (uint)n)
                    isCandidateCell[c] = true;
            }

            int countyCount = Math.Max(1, seeds.Count);
            float targetCountyCells = Math.Max(1f, (float)cells.Count / countyCount);
            float normalizedTargetPopulation = Math.Max(1f, targetCountyPopulation);

            var pathCostByCell = new float[n];
            for (int i = 0; i < n; i++)
                pathCostByCell[i] = float.PositiveInfinity;

            var countyPopulation = new float[countyCount + 1];
            var countyCellCount = new int[countyCount + 1];
            var frontier = new CountyFrontierQueue(Math.Max(64, cells.Count / 4));

            for (int i = 0; i < seeds.Count; i++)
            {
                int seed = seeds[i];
                int countyId = i + 1;
                if ((uint)seed >= (uint)n || !isCandidateCell[seed] || countyId >= countyPopulation.Length)
                    continue;

                if (outAssignment[seed] != 0)
                    continue;

                outAssignment[seed] = countyId;
                pathCostByCell[seed] = 0f;
                countyPopulation[countyId] += CellPopulation(biomes, seed);
                countyCellCount[countyId]++;
            }

            for (int i = 0; i < cells.Count; i++)
            {
                int cell = cells[i];
                int countyId = outAssignment[cell];
                if (countyId <= 0 || countyId >= countyPopulation.Length)
                    continue;

                int[] neighbors = mesh.CellNeighbors[cell];
                int[] edgeLookup = neighborEdges[cell];
                if (neighbors == null)
                    continue;

                for (int ni = 0; ni < neighbors.Length; ni++)
                {
                    int nb = neighbors[ni];
                    if ((uint)nb >= (uint)n || !isCandidateCell[nb] || outAssignment[nb] != 0)
                        continue;

                    float edgeCost = ComputeRealmEdgeCost(
                        cell,
                        nb,
                        ni < edgeLookup.Length ? edgeLookup[ni] : -1,
                        mesh,
                        biomes,
                        riverPenaltyByEdge,
                        nominalNeighborDistance);
                    if (!(edgeCost > 0f) || float.IsNaN(edgeCost) || float.IsInfinity(edgeCost))
                        continue;

                    float pathCost = pathCostByCell[cell] + edgeCost;
                    float priority = ComputeCountyExpansionPriority(
                        pathCost,
                        countyPopulation[countyId],
                        countyCellCount[countyId],
                        normalizedTargetPopulation,
                        targetCountyCells);
                    frontier.Push(new CountyFrontierNode
                    {
                        Cell = nb,
                        CountyId = countyId,
                        PathCost = pathCost,
                        Priority = priority
                    });
                }
            }

            const float priorityEpsilon = 1e-3f;
            while (frontier.Count > 0)
            {
                CountyFrontierNode node = frontier.Pop();
                int cell = node.Cell;
                int countyId = node.CountyId;

                if ((uint)cell >= (uint)n || !isCandidateCell[cell] || outAssignment[cell] != 0)
                    continue;
                if (countyId <= 0 || countyId >= countyPopulation.Length)
                    continue;

                float refreshedPriority = ComputeCountyExpansionPriority(
                    node.PathCost,
                    countyPopulation[countyId],
                    countyCellCount[countyId],
                    normalizedTargetPopulation,
                    targetCountyCells);
                if (refreshedPriority > node.Priority + priorityEpsilon)
                {
                    node.Priority = refreshedPriority;
                    frontier.Push(node);
                    continue;
                }

                outAssignment[cell] = countyId;
                pathCostByCell[cell] = node.PathCost;
                countyPopulation[countyId] += CellPopulation(biomes, cell);
                countyCellCount[countyId]++;

                int[] neighbors = mesh.CellNeighbors[cell];
                int[] edgeLookup = neighborEdges[cell];
                if (neighbors == null)
                    continue;

                for (int ni = 0; ni < neighbors.Length; ni++)
                {
                    int nb = neighbors[ni];
                    if ((uint)nb >= (uint)n || !isCandidateCell[nb] || outAssignment[nb] != 0)
                        continue;

                    float edgeCost = ComputeRealmEdgeCost(
                        cell,
                        nb,
                        ni < edgeLookup.Length ? edgeLookup[ni] : -1,
                        mesh,
                        biomes,
                        riverPenaltyByEdge,
                        nominalNeighborDistance);
                    if (!(edgeCost > 0f) || float.IsNaN(edgeCost) || float.IsInfinity(edgeCost))
                        continue;

                    float pathCost = node.PathCost + edgeCost;
                    float priority = ComputeCountyExpansionPriority(
                        pathCost,
                        countyPopulation[countyId],
                        countyCellCount[countyId],
                        normalizedTargetPopulation,
                        targetCountyCells);
                    frontier.Push(new CountyFrontierNode
                    {
                        Cell = nb,
                        CountyId = countyId,
                        PathCost = pathCost,
                        Priority = priority
                    });
                }
            }

            // Resolve any stragglers by attaching to neighboring assigned counties first.
            bool changed;
            do
            {
                changed = false;
                for (int i = 0; i < cells.Count; i++)
                {
                    int cell = cells[i];
                    if (outAssignment[cell] != 0)
                        continue;

                    int[] neighbors = mesh.CellNeighbors[cell];
                    if (neighbors == null)
                        continue;

                    int bestCounty = 0;
                    float bestEdge = float.MaxValue;
                    int[] edgeLookup = neighborEdges[cell];
                    for (int ni = 0; ni < neighbors.Length; ni++)
                    {
                        int nb = neighbors[ni];
                        if ((uint)nb >= (uint)n || !isCandidateCell[nb])
                            continue;

                        int neighborCounty = outAssignment[nb];
                        if (neighborCounty <= 0)
                            continue;

                        float edgeCost = ComputeRealmEdgeCost(
                            cell,
                            nb,
                            ni < edgeLookup.Length ? edgeLookup[ni] : -1,
                            mesh,
                            biomes,
                            riverPenaltyByEdge,
                            nominalNeighborDistance);
                        if (edgeCost < bestEdge)
                        {
                            bestEdge = edgeCost;
                            bestCounty = neighborCounty;
                        }
                    }

                    if (bestCounty > 0)
                    {
                        outAssignment[cell] = bestCounty;
                        changed = true;
                    }
                }
            } while (changed);

            // Final safety net: nearest-seed fallback for disconnected remnants.
            for (int i = 0; i < cells.Count; i++)
            {
                int cell = cells[i];
                if (outAssignment[cell] != 0)
                    continue;

                int bestCounty = 1;
                float bestDistance = float.MaxValue;
                for (int s = 0; s < seeds.Count; s++)
                {
                    int seed = seeds[s];
                    float distance = Vec2.Distance(mesh.CellCenters[cell], mesh.CellCenters[seed]);
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        bestCounty = s + 1;
                    }
                }

                outAssignment[cell] = bestCounty;
            }
        }

        static float ComputeCountyExpansionPriority(
            float pathCost,
            float countyPopulation,
            int countyCellCount,
            float targetCountyPopulation,
            float targetCountyCells)
        {
            float popTarget = Math.Max(1f, targetCountyPopulation);
            float cellTarget = Math.Max(1f, targetCountyCells);

            float popRatio = countyPopulation / popTarget;
            float cellRatio = countyCellCount / cellTarget;

            float balance = 1f;
            if (popRatio < 1f)
                balance *= 0.55f + 0.45f * popRatio;
            else
                balance *= 1f + (popRatio - 1f) * 4f;

            if (cellRatio > 1f)
                balance *= 1f + (cellRatio - 1f) * 1.5f;

            // Preserve very high-pop tiny counties as deliberate single-cell hubs.
            if (countyCellCount <= 2 && popRatio >= 0.9f)
                balance *= 6f;

            return pathCost * balance;
        }

        static void MergeCountyOrphans(
            int[] assignment,
            List<int> cells,
            CellMesh mesh,
            BiomeField biomes,
            int[][] neighborEdges,
            float[] riverPenaltyByEdge,
            float nominalNeighborDistance,
            float targetCountyPopulation)
        {
            if (assignment == null || cells == null || cells.Count == 0)
                return;

            const int orphanCellLimit = 2;
            float preservePopulationThreshold = Math.Max(1f, targetCountyPopulation * 0.85f);

            int maxCountyId = 0;
            for (int i = 0; i < cells.Count; i++)
            {
                int cell = cells[i];
                int countyId = assignment[cell];
                if (countyId > maxCountyId)
                    maxCountyId = countyId;
            }

            if (maxCountyId <= 0)
                return;

            for (int pass = 0; pass < 4; pass++)
            {
                var countyCellCount = new int[maxCountyId + 1];
                var countyPopulation = new float[maxCountyId + 1];
                for (int i = 0; i < cells.Count; i++)
                {
                    int cell = cells[i];
                    int countyId = assignment[cell];
                    if (countyId <= 0 || countyId >= countyCellCount.Length)
                        continue;

                    countyCellCount[countyId]++;
                    countyPopulation[countyId] += CellPopulation(biomes, cell);
                }

                bool anyChanged = false;
                for (int countyId = 1; countyId <= maxCountyId; countyId++)
                {
                    int cellCount = countyCellCount[countyId];
                    float population = countyPopulation[countyId];
                    if (cellCount == 0)
                        continue;
                    if (cellCount > orphanCellLimit || population >= preservePopulationThreshold)
                        continue;

                    int mergeTarget = FindCountyMergeTarget(
                        countyId,
                        assignment,
                        cells,
                        mesh,
                        biomes,
                        neighborEdges,
                        riverPenaltyByEdge,
                        nominalNeighborDistance,
                        countyCellCount,
                        countyPopulation,
                        preservePopulationThreshold,
                        preferStableTargets: true);
                    if (mergeTarget <= 0)
                    {
                        mergeTarget = FindCountyMergeTarget(
                            countyId,
                            assignment,
                            cells,
                            mesh,
                            biomes,
                            neighborEdges,
                            riverPenaltyByEdge,
                            nominalNeighborDistance,
                            countyCellCount,
                            countyPopulation,
                            preservePopulationThreshold,
                            preferStableTargets: false);
                    }

                    if (mergeTarget <= 0)
                        continue;

                    for (int i = 0; i < cells.Count; i++)
                    {
                        int cell = cells[i];
                        if (assignment[cell] == countyId)
                            assignment[cell] = mergeTarget;
                    }

                    anyChanged = true;
                }

                if (!anyChanged)
                    break;
            }
        }

        static int FindCountyMergeTarget(
            int sourceCountyId,
            int[] assignment,
            List<int> cells,
            CellMesh mesh,
            BiomeField biomes,
            int[][] neighborEdges,
            float[] riverPenaltyByEdge,
            float nominalNeighborDistance,
            int[] countyCellCount,
            float[] countyPopulation,
            float preservePopulationThreshold,
            bool preferStableTargets)
        {
            int bestTarget = 0;
            float bestScore = float.MaxValue;

            for (int i = 0; i < cells.Count; i++)
            {
                int cell = cells[i];
                if (assignment[cell] != sourceCountyId)
                    continue;

                int[] neighbors = mesh.CellNeighbors[cell];
                int[] edgeLookup = neighborEdges[cell];
                if (neighbors == null)
                    continue;

                for (int ni = 0; ni < neighbors.Length; ni++)
                {
                    int nb = neighbors[ni];
                    if ((uint)nb >= (uint)assignment.Length)
                        continue;

                    int targetCounty = assignment[nb];
                    if (targetCounty <= 0 || targetCounty == sourceCountyId)
                        continue;

                    if (preferStableTargets &&
                        targetCounty < countyCellCount.Length &&
                        countyCellCount[targetCounty] <= 2 &&
                        targetCounty < countyPopulation.Length &&
                        countyPopulation[targetCounty] < preservePopulationThreshold)
                        continue;

                    float score = ComputeRealmEdgeCost(
                        cell,
                        nb,
                        ni < edgeLookup.Length ? edgeLookup[ni] : -1,
                        mesh,
                        biomes,
                        riverPenaltyByEdge,
                        nominalNeighborDistance);

                    if (score < bestScore || (Math.Abs(score - bestScore) <= 1e-4f && targetCounty < bestTarget))
                    {
                        bestScore = score;
                        bestTarget = targetCounty;
                    }
                }
            }

            return bestTarget;
        }

        // ────────────────────────────────────────────────────────────────
        // Shared helpers (unchanged)
        // ────────────────────────────────────────────────────────────────

        static List<int> CollectLandCells(ElevationField elevation, BiomeField biomes)
        {
            var cells = new List<int>();
            for (int i = 0; i < elevation.CellCount; i++)
            {
                if (elevation.IsLand(i) && !biomes.IsLakeCell[i])
                    cells.Add(i);
            }

            return cells;
        }

        static List<int> SelectSeeds(
            List<int> cells,
            CellMesh mesh,
            int target,
            Func<int, float> scoreCell,
            float minSpacingKm)
        {
            var ranked = new List<int>(cells);
            ranked.Sort((a, b) => scoreCell(b).CompareTo(scoreCell(a)));

            var seeds = new List<int>();
            for (int i = 0; i < ranked.Count && seeds.Count < target; i++)
            {
                int c = ranked[i];
                bool farEnough = true;
                for (int s = 0; s < seeds.Count; s++)
                {
                    float d = Vec2.Distance(mesh.CellCenters[c], mesh.CellCenters[seeds[s]]);
                    if (d < minSpacingKm)
                    {
                        farEnough = false;
                        break;
                    }
                }

                if (farEnough)
                    seeds.Add(c);
            }

            for (int i = 0; i < ranked.Count && seeds.Count < target; i++)
            {
                int c = ranked[i];
                if (!seeds.Contains(c))
                    seeds.Add(c);
            }

            return seeds;
        }

        static void EnsureLandmassSeedCoverage(
            List<int> seeds,
            List<int> landCells,
            int[] landmassIdByCell,
            Func<int, float> scoreCell,
            HashSet<int> requiredLandmasses)
        {
            var coveredLandmasses = new HashSet<int>();
            for (int i = 0; i < seeds.Count; i++)
            {
                int seed = seeds[i];
                if ((uint)seed < (uint)landmassIdByCell.Length)
                    coveredLandmasses.Add(landmassIdByCell[seed]);
            }

            var bestCellByLandmass = new Dictionary<int, int>();
            var bestScoreByLandmass = new Dictionary<int, float>();

            for (int i = 0; i < landCells.Count; i++)
            {
                int cell = landCells[i];
                if ((uint)cell >= (uint)landmassIdByCell.Length)
                    continue;

                int landmassId = landmassIdByCell[cell];
                if (landmassId <= 0 ||
                    !requiredLandmasses.Contains(landmassId) ||
                    coveredLandmasses.Contains(landmassId))
                    continue;

                float score = scoreCell(cell);
                if (!bestScoreByLandmass.TryGetValue(landmassId, out float currentBest) || score > currentBest)
                {
                    bestScoreByLandmass[landmassId] = score;
                    bestCellByLandmass[landmassId] = cell;
                }
            }

            var missingLandmasses = new List<int>(bestCellByLandmass.Keys);
            missingLandmasses.Sort();
            for (int i = 0; i < missingLandmasses.Count; i++)
            {
                int landmassId = missingLandmasses[i];
                seeds.Add(bestCellByLandmass[landmassId]);
            }
        }

        static Dictionary<int, LandmassStats> BuildLandmassStats(
            List<int> landCells,
            int[] landmassIdByCell,
            BiomeField biomes)
        {
            var stats = new Dictionary<int, LandmassStats>();

            for (int i = 0; i < landCells.Count; i++)
            {
                int cell = landCells[i];
                if ((uint)cell >= (uint)landmassIdByCell.Length)
                    continue;

                int landmassId = landmassIdByCell[cell];
                if (landmassId <= 0)
                    continue;

                if (!stats.TryGetValue(landmassId, out LandmassStats s))
                    s = new LandmassStats();

                s.CellCount++;
                float pop = (uint)cell < (uint)biomes.Population.Length ? biomes.Population[cell] : 0f;
                if (pop > 0f && !float.IsNaN(pop) && !float.IsInfinity(pop))
                    s.Population += pop;

                stats[landmassId] = s;
            }

            return stats;
        }

        static HashSet<int> ResolveEligibleRealmLandmasses(Dictionary<int, LandmassStats> landmassStats, MapGenConfig config)
        {
            var eligible = new HashSet<int>();
            if (landmassStats == null || landmassStats.Count == 0)
                return eligible;

            int minCells = config != null ? Math.Max(1, config.MinRealmCells) : 1;
            float minPopFraction = config != null ? config.MinRealmPopulationFraction : 0f;
            if (minPopFraction < 0f) minPopFraction = 0f;
            if (minPopFraction > 1f) minPopFraction = 1f;

            double totalPopulation = 0d;
            foreach (var kvp in landmassStats)
            {
                double pop = kvp.Value.Population;
                if (pop > 0d)
                    totalPopulation += pop;
            }

            double minPopulation = totalPopulation * minPopFraction;
            foreach (var kvp in landmassStats)
            {
                LandmassStats stats = kvp.Value;
                if (stats.CellCount < minCells)
                    continue;
                if (stats.Population < minPopulation)
                    continue;

                eligible.Add(kvp.Key);
            }

            return eligible;
        }

        static int SelectFallbackLandmass(Dictionary<int, LandmassStats> landmassStats)
        {
            int bestId = 0;
            float bestPopulation = float.NegativeInfinity;
            int bestCellCount = -1;

            foreach (var kvp in landmassStats)
            {
                int id = kvp.Key;
                LandmassStats s = kvp.Value;
                if (s.Population > bestPopulation)
                {
                    bestPopulation = s.Population;
                    bestCellCount = s.CellCount;
                    bestId = id;
                    continue;
                }

                if (Math.Abs(s.Population - bestPopulation) <= 1e-4f && s.CellCount > bestCellCount)
                {
                    bestCellCount = s.CellCount;
                    bestId = id;
                }
            }

            return bestId;
        }

        // ────────────────────────────────────────────────────────────────
        // Transport frontier (unchanged)
        // ────────────────────────────────────────────────────────────────

        static void AssignByTransportFrontier(
            int[] outAssignment,
            List<int> cells,
            List<int> seeds,
            CellMesh mesh,
            BiomeField biomes,
            RiverField rivers,
            MapGenConfig config)
        {
            int[][] neighborEdges = BuildNeighborEdgeLookup(mesh);
            float nominalMovementCost = EstimateNominalMovementCost(cells, biomes);
            float[] riverPenaltyByEdge = BuildRiverCrossingPenaltyByEdge(mesh, rivers, config, nominalMovementCost);
            var includeCell = new bool[mesh.CellCount];
            for (int i = 0; i < cells.Count; i++)
            {
                int c = cells[i];
                if ((uint)c < (uint)includeCell.Length)
                    includeCell[c] = true;
            }

            float nominalNeighborDistance = EstimateNominalNeighborDistance(mesh, includeCell);
            AssignByTransportFrontier(
                outAssignment,
                cells,
                seeds,
                mesh,
                biomes,
                neighborEdges,
                riverPenaltyByEdge,
                nominalNeighborDistance);
        }

        static void AssignByTransportFrontier(
            int[] outAssignment,
            List<int> cells,
            List<int> seeds,
            CellMesh mesh,
            BiomeField biomes,
            int[][] neighborEdges,
            float[] riverPenaltyByEdge,
            float nominalNeighborDistance)
        {
            if (cells.Count == 0 || seeds.Count == 0)
                return;

            int n = mesh.CellCount;
            var isCandidateCell = new bool[n];
            for (int i = 0; i < cells.Count; i++)
            {
                int c = cells[i];
                if ((uint)c < (uint)n)
                    isCandidateCell[c] = true;
            }

            var bestCost = new float[n];
            for (int i = 0; i < n; i++)
                bestCost[i] = float.PositiveInfinity;
            var frontier = new FrontierQueue(Math.Max(64, cells.Count / 8));

            for (int i = 0; i < seeds.Count; i++)
            {
                int seed = seeds[i];
                if ((uint)seed >= (uint)n || !isCandidateCell[seed])
                    continue;

                int realmId = i + 1;
                bestCost[seed] = 0f;
                outAssignment[seed] = realmId;
                frontier.Push(seed, realmId, 0f);
            }

            const float tieEpsilon = 1e-4f;
            while (frontier.Count > 0)
            {
                FrontierNode current = frontier.Pop();
                int cell = current.Cell;

                if ((uint)cell >= (uint)n)
                    continue;

                float settledCost = bestCost[cell];
                if (current.Cost > settledCost + tieEpsilon)
                    continue;

                int[] neighbors = mesh.CellNeighbors[cell];
                int[] edgeLookup = neighborEdges[cell];
                if (neighbors == null || neighbors.Length == 0)
                    continue;

                for (int ni = 0; ni < neighbors.Length; ni++)
                {
                    int nb = neighbors[ni];
                    if ((uint)nb >= (uint)n || !isCandidateCell[nb])
                        continue;

                    float edgeCost = ComputeRealmEdgeCost(
                        cell,
                        nb,
                        ni < edgeLookup.Length ? edgeLookup[ni] : -1,
                        mesh,
                        biomes,
                        riverPenaltyByEdge,
                        nominalNeighborDistance);

                    if (!(edgeCost > 0f) || float.IsNaN(edgeCost) || float.IsInfinity(edgeCost))
                        continue;

                    float candidateCost = current.Cost + edgeCost;
                    float existingCost = bestCost[nb];
                    int existingRealm = outAssignment[nb];

                    bool better = candidateCost + tieEpsilon < existingCost;
                    bool tieBreak = Math.Abs(candidateCost - existingCost) <= tieEpsilon &&
                        (existingRealm == 0 || current.RealmId < existingRealm);

                    if (!better && !tieBreak)
                        continue;

                    bestCost[nb] = candidateCost;
                    outAssignment[nb] = current.RealmId;
                    frontier.Push(nb, current.RealmId, candidateCost);
                }
            }

            // Safety net: if a disconnected island was missed, fall back to nearest-capital assignment.
            for (int i = 0; i < cells.Count; i++)
            {
                int cell = cells[i];
                if ((uint)cell >= (uint)outAssignment.Length || outAssignment[cell] != 0)
                    continue;

                int bestRealm = 1;
                float bestDistance = float.MaxValue;
                for (int s = 0; s < seeds.Count; s++)
                {
                    int seed = seeds[s];
                    float distance = Vec2.Distance(mesh.CellCenters[cell], mesh.CellCenters[seed]);
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        bestRealm = s + 1;
                    }
                }

                outAssignment[cell] = bestRealm;
            }
        }

        // ────────────────────────────────────────────────────────────────
        // Edge cost + infrastructure helpers (unchanged)
        // ────────────────────────────────────────────────────────────────

        static float ComputeRealmEdgeCost(
            int fromCell,
            int toCell,
            int sharedEdge,
            CellMesh mesh,
            BiomeField biomes,
            float[] riverPenaltyByEdge,
            float nominalNeighborDistance)
        {
            float fromMovement = biomes.MovementCost[fromCell];
            float toMovement = biomes.MovementCost[toCell];
            if (fromMovement < 1f) fromMovement = 1f;
            if (toMovement < 1f) toMovement = 1f;

            float baseMovementCost = 0.5f * (fromMovement + toMovement);

            float distance = Vec2.Distance(mesh.CellCenters[fromCell], mesh.CellCenters[toCell]);
            float distanceFactor = nominalNeighborDistance > 1e-4f ? distance / nominalNeighborDistance : 1f;
            if (distanceFactor < 0.5f) distanceFactor = 0.5f;
            if (distanceFactor > 2.5f) distanceFactor = 2.5f;

            float total = baseMovementCost * distanceFactor;
            if ((uint)sharedEdge < (uint)riverPenaltyByEdge.Length)
                total += riverPenaltyByEdge[sharedEdge];

            return total;
        }

        static int[][] BuildNeighborEdgeLookup(CellMesh mesh)
        {
            int[][] lookup = new int[mesh.CellCount][];
            for (int cell = 0; cell < mesh.CellCount; cell++)
            {
                int[] neighbors = mesh.CellNeighbors[cell];
                if (neighbors == null || neighbors.Length == 0)
                {
                    lookup[cell] = Array.Empty<int>();
                    continue;
                }

                var edgeIndices = new int[neighbors.Length];
                for (int i = 0; i < neighbors.Length; i++)
                    edgeIndices[i] = FindSharedEdge(mesh, cell, neighbors[i]);

                lookup[cell] = edgeIndices;
            }

            return lookup;
        }

        static int FindSharedEdge(CellMesh mesh, int cellA, int cellB)
        {
            if ((uint)cellA >= (uint)mesh.CellCount || (uint)cellB >= (uint)mesh.CellCount)
                return -1;

            int[] edges = mesh.CellEdges[cellA];
            if (edges == null || edges.Length == 0)
                return -1;

            for (int i = 0; i < edges.Length; i++)
            {
                int edge = edges[i];
                if ((uint)edge >= (uint)mesh.EdgeCount)
                    continue;

                var cells = mesh.EdgeCells[edge];
                if ((cells.C0 == cellA && cells.C1 == cellB) || (cells.C0 == cellB && cells.C1 == cellA))
                    return edge;
            }

            return -1;
        }

        static float[] BuildRiverCrossingPenaltyByEdge(CellMesh mesh, RiverField rivers, MapGenConfig config, float nominalMovementCost)
        {
            var penalty = new float[mesh.EdgeCount];
            if (rivers == null || rivers.EdgeFlux == null)
                return penalty;

            float traceThreshold = config != null ? Math.Max(1f, config.EffectiveRiverTraceThreshold) : 1f;
            float majorThreshold = config != null ? Math.Max(traceThreshold * 8f, config.EffectiveRiverThreshold) : traceThreshold * 8f;

            float clampedNominalMovement = nominalMovementCost;
            if (clampedNominalMovement < 5f) clampedNominalMovement = 5f;
            if (clampedNominalMovement > 120f) clampedNominalMovement = 120f;

            float basePenalty = clampedNominalMovement * 0.15f;
            float maxAdditionalPenalty = clampedNominalMovement * 0.65f;

            int count = Math.Min(mesh.EdgeCount, rivers.EdgeFlux.Length);
            for (int edge = 0; edge < count; edge++)
            {
                float flux = rivers.EdgeFlux[edge];
                if (flux <= traceThreshold)
                    continue;

                float t = (flux - traceThreshold) / Math.Max(1f, majorThreshold - traceThreshold);
                if (t < 0f) t = 0f;
                if (t > 1f) t = 1f;
                penalty[edge] = basePenalty + maxAdditionalPenalty * t;
            }

            return penalty;
        }

        static float EstimateNominalMovementCost(List<int> cells, BiomeField biomes)
        {
            if (cells == null || cells.Count == 0)
                return 10f;

            double total = 0d;
            int count = 0;
            for (int i = 0; i < cells.Count; i++)
            {
                int cell = cells[i];
                if ((uint)cell >= (uint)biomes.MovementCost.Length)
                    continue;

                float movement = biomes.MovementCost[cell];
                if (!(movement > 0f) || float.IsNaN(movement) || float.IsInfinity(movement))
                    continue;

                total += movement;
                count++;
            }

            if (count <= 0 || total <= 0d)
                return 10f;

            return (float)(total / count);
        }

        static float EstimateNominalNeighborDistance(CellMesh mesh, bool[] includeCell)
        {
            double total = 0d;
            int count = 0;

            for (int cell = 0; cell < mesh.CellCount; cell++)
            {
                if ((uint)cell >= (uint)includeCell.Length || !includeCell[cell])
                    continue;

                int[] neighbors = mesh.CellNeighbors[cell];
                if (neighbors == null)
                    continue;

                for (int i = 0; i < neighbors.Length; i++)
                {
                    int nb = neighbors[i];
                    if (nb <= cell || (uint)nb >= (uint)includeCell.Length || !includeCell[nb])
                        continue;

                    total += Vec2.Distance(mesh.CellCenters[cell], mesh.CellCenters[nb]);
                    count++;
                }
            }

            if (count <= 0 || total <= 0d)
                return 1f;

            return (float)(total / count);
        }

        // ────────────────────────────────────────────────────────────────
        // Data structures (unchanged)
        // ────────────────────────────────────────────────────────────────

        struct CountyFrontierNode
        {
            public int Cell;
            public int CountyId;
            public float PathCost;
            public float Priority;
        }

        struct FrontierNode
        {
            public int Cell;
            public int RealmId;
            public float Cost;
        }

        struct LandmassStats
        {
            public int CellCount;
            public float Population;
        }

        sealed class FrontierQueue
        {
            FrontierNode[] _data;
            int _count;

            public int Count => _count;

            public FrontierQueue(int capacity)
            {
                if (capacity < 4) capacity = 4;
                _data = new FrontierNode[capacity];
                _count = 0;
            }

            public void Push(int cell, int realmId, float cost)
            {
                if (_count == _data.Length)
                    Array.Resize(ref _data, _data.Length * 2);

                int i = _count++;
                _data[i] = new FrontierNode { Cell = cell, RealmId = realmId, Cost = cost };
                SiftUp(i);
            }

            public FrontierNode Pop()
            {
                FrontierNode root = _data[0];
                _count--;
                if (_count > 0)
                {
                    _data[0] = _data[_count];
                    SiftDown(0);
                }

                return root;
            }

            void SiftUp(int index)
            {
                while (index > 0)
                {
                    int parent = (index - 1) / 2;
                    if (!IsHigherPriority(index, parent))
                        break;

                    Swap(index, parent);
                    index = parent;
                }
            }

            void SiftDown(int index)
            {
                while (true)
                {
                    int left = index * 2 + 1;
                    int right = left + 1;
                    int smallest = index;

                    if (left < _count && IsHigherPriority(left, smallest))
                        smallest = left;
                    if (right < _count && IsHigherPriority(right, smallest))
                        smallest = right;

                    if (smallest == index)
                        break;

                    Swap(index, smallest);
                    index = smallest;
                }
            }

            bool IsHigherPriority(int a, int b)
            {
                float costA = _data[a].Cost;
                float costB = _data[b].Cost;
                if (costA < costB) return true;
                if (costA > costB) return false;
                return _data[a].RealmId < _data[b].RealmId;
            }

            void Swap(int a, int b)
            {
                FrontierNode tmp = _data[a];
                _data[a] = _data[b];
                _data[b] = tmp;
            }
        }

        sealed class CountyFrontierQueue
        {
            CountyFrontierNode[] _data;
            int _count;

            public int Count => _count;

            public CountyFrontierQueue(int capacity)
            {
                if (capacity < 4) capacity = 4;
                _data = new CountyFrontierNode[capacity];
                _count = 0;
            }

            public void Push(CountyFrontierNode node)
            {
                if (_count == _data.Length)
                    Array.Resize(ref _data, _data.Length * 2);

                int i = _count++;
                _data[i] = node;
                SiftUp(i);
            }

            public CountyFrontierNode Pop()
            {
                CountyFrontierNode root = _data[0];
                _count--;
                if (_count > 0)
                {
                    _data[0] = _data[_count];
                    SiftDown(0);
                }

                return root;
            }

            void SiftUp(int index)
            {
                while (index > 0)
                {
                    int parent = (index - 1) / 2;
                    if (!IsHigherPriority(index, parent))
                        break;

                    Swap(index, parent);
                    index = parent;
                }
            }

            void SiftDown(int index)
            {
                while (true)
                {
                    int left = index * 2 + 1;
                    int right = left + 1;
                    int smallest = index;

                    if (left < _count && IsHigherPriority(left, smallest))
                        smallest = left;
                    if (right < _count && IsHigherPriority(right, smallest))
                        smallest = right;

                    if (smallest == index)
                        break;

                    Swap(index, smallest);
                    index = smallest;
                }
            }

            bool IsHigherPriority(int a, int b)
            {
                float pa = _data[a].Priority;
                float pb = _data[b].Priority;
                if (pa < pb) return true;
                if (pa > pb) return false;
                return _data[a].CountyId < _data[b].CountyId;
            }

            void Swap(int a, int b)
            {
                CountyFrontierNode tmp = _data[a];
                _data[a] = _data[b];
                _data[b] = tmp;
            }
        }

        static float EstimateSpacing(CellMesh mesh, int seedCount)
        {
            if (seedCount <= 0)
                return 1f;
            float area = mesh.Width * mesh.Height;
            if (area <= 1e-6f)
                return 1f;
            return (float)Math.Sqrt(area / seedCount);
        }

        static int Clamp(int x, int lo, int hi) => x < lo ? lo : (x > hi ? hi : x);
    }
}
