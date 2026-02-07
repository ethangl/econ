using System;
using System.Collections.Generic;

namespace MapGen.Core
{
    public static class PoliticalOps
    {
        // ── Tuning Constants ────────────────────────────────────────────────

        const int MinLandmassSize = 5;          // absolute floor (filter map noise)
        const float MinLandmassPopFraction = 0.02f; // need 2% of total pop to qualify
        const float PopPerRealm = 200000f;
        const float PopPerProvince = 40000f;
        const float HighDensityThreshold = 20000f;
        const float TargetCountyPop = 5000f;
        const int MaxCellsPerCounty = 64;
        const float CapitalSpacingFactor = 0.6f;
        const float ProvinceSpacingFactor = 0.5f;

        // ── Stage 1: Landmass Detection ─────────────────────────────────────

        public static void DetectLandmasses(PoliticalData pol, HeightGrid heights, BiomeData biomes)
        {
            var mesh = pol.Mesh;
            int n = mesh.CellCount;
            int[] comp = pol.LandmassId; // initialized to -1

            var sizes = new List<int>();
            var pops = new List<float>();
            var stack = new List<int>();

            for (int i = 0; i < n; i++)
            {
                if (heights.IsWater(i) || biomes.IsLakeCell[i] || comp[i] >= 0) continue;

                int clusterId = sizes.Count;
                int size = 0;
                float pop = 0f;
                comp[i] = clusterId;
                stack.Add(i);

                while (stack.Count > 0)
                {
                    int cell = stack[stack.Count - 1];
                    stack.RemoveAt(stack.Count - 1);
                    size++;
                    pop += biomes.Population[cell];

                    int[] neighbors = mesh.CellNeighbors[cell];
                    for (int j = 0; j < neighbors.Length; j++)
                    {
                        int nb = neighbors[j];
                        if (nb >= 0 && nb < n && comp[nb] < 0 &&
                            !heights.IsWater(nb) && !biomes.IsLakeCell[nb])
                        {
                            comp[nb] = clusterId;
                            stack.Add(nb);
                        }
                    }
                }

                sizes.Add(size);
                pops.Add(pop);
            }

            pol.LandmassCount = sizes.Count;
            pol.LandmassCellCount = sizes.ToArray();
            pol.LandmassPop = pops.ToArray();

            // Filter tiny noise islands (below absolute cell count floor)
            for (int lm = 0; lm < sizes.Count; lm++)
            {
                if (sizes[lm] < MinLandmassSize)
                {
                    for (int i = 0; i < n; i++)
                    {
                        if (comp[i] == lm) comp[i] = -1;
                    }
                }
            }

            // Qualifying = has enough population fraction to merit a capital
            float totalPop = 0f;
            for (int lm = 0; lm < pops.Count; lm++)
            {
                if (sizes[lm] >= MinLandmassSize)
                    totalPop += pops[lm];
            }

            float popThreshold = totalPop * MinLandmassPopFraction;
            int qualifying = 0;
            for (int lm = 0; lm < sizes.Count; lm++)
            {
                if (sizes[lm] >= MinLandmassSize && pops[lm] >= popThreshold)
                    qualifying++;
            }

            pol.QualifyingLandmasses = qualifying;
        }

        // ── Stage 2: Capital Placement ──────────────────────────────────────

        public static void PlaceCapitals(PoliticalData pol, BiomeData biomes, HeightGrid heights)
        {
            var mesh = pol.Mesh;
            int n = mesh.CellCount;

            // Compute total population across non-tiny landmasses
            float totalPop = 0f;
            for (int lm = 0; lm < pol.LandmassCount; lm++)
            {
                if (pol.LandmassCellCount[lm] >= MinLandmassSize)
                    totalPop += pol.LandmassPop[lm];
            }

            if (totalPop <= 0f) return;

            // Qualifying = enough population to merit a capital
            float popThreshold = totalPop * MinLandmassPopFraction;
            bool[] qualifying = new bool[pol.LandmassCount];
            int qualCount = 0;
            for (int lm = 0; lm < pol.LandmassCount; lm++)
            {
                if (pol.LandmassCellCount[lm] >= MinLandmassSize &&
                    pol.LandmassPop[lm] >= popThreshold)
                {
                    qualifying[lm] = true;
                    qualCount++;
                }
            }

            if (qualCount == 0) return;

            // Qualifying pop (only landmasses that get capitals)
            float qualPop = 0f;
            for (int lm = 0; lm < pol.LandmassCount; lm++)
            {
                if (qualifying[lm]) qualPop += pol.LandmassPop[lm];
            }

            int targetCapitals = Math.Max(qualCount,
                (int)Math.Ceiling(qualPop / PopPerRealm));

            // Distribute capitals to landmasses: min 1 each, rest proportional
            int[] capsPerLandmass = new int[pol.LandmassCount];
            int remaining = targetCapitals;

            // First pass: 1 each for qualifying
            for (int lm = 0; lm < pol.LandmassCount; lm++)
            {
                if (!qualifying[lm]) continue;
                capsPerLandmass[lm] = 1;
                remaining--;
            }

            // Second pass: distribute remaining proportionally
            if (remaining > 0 && qualPop > 0f)
            {
                var qualLandmasses = new List<int>();
                for (int lm = 0; lm < pol.LandmassCount; lm++)
                {
                    if (qualifying[lm])
                        qualLandmasses.Add(lm);
                }

                // Proportional allocation (largest remainder method)
                float[] shares = new float[pol.LandmassCount];
                for (int i = 0; i < qualLandmasses.Count; i++)
                {
                    int lm = qualLandmasses[i];
                    shares[lm] = (pol.LandmassPop[lm] / qualPop) * remaining;
                }

                // Assign integer parts
                int assigned = 0;
                for (int i = 0; i < qualLandmasses.Count; i++)
                {
                    int lm = qualLandmasses[i];
                    int floor = (int)shares[lm];
                    capsPerLandmass[lm] += floor;
                    assigned += floor;
                    shares[lm] -= floor; // keep fractional part
                }

                // Assign remaining by largest fractional part
                int leftover = remaining - assigned;
                while (leftover > 0)
                {
                    int bestLm = -1;
                    float bestFrac = -1f;
                    for (int i = 0; i < qualLandmasses.Count; i++)
                    {
                        int lm = qualLandmasses[i];
                        if (shares[lm] > bestFrac)
                        {
                            bestFrac = shares[lm];
                            bestLm = lm;
                        }
                    }
                    if (bestLm < 0) break;
                    capsPerLandmass[bestLm]++;
                    shares[bestLm] = -1f;
                    leftover--;
                }
            }

            // Place capitals within each landmass
            var allCapitals = new List<int>();

            for (int lm = 0; lm < pol.LandmassCount; lm++)
            {
                int count = capsPerLandmass[lm];
                if (count <= 0) continue;

                // Collect cells in this landmass, sorted by suitability descending
                var cells = new List<int>();
                for (int i = 0; i < n; i++)
                {
                    if (pol.LandmassId[i] == lm)
                        cells.Add(i);
                }

                cells.Sort((a, b) => biomes.Suitability[b].CompareTo(biomes.Suitability[a]));

                float landmassArea = 0f;
                for (int i = 0; i < cells.Count; i++)
                    landmassArea += mesh.CellAreas[cells[i]];

                float minSpacing = (float)Math.Sqrt(landmassArea / count) * CapitalSpacingFactor;

                var placed = new List<int>();

                for (int cap = 0; cap < count; cap++)
                {
                    float spacing = minSpacing;

                    while (true)
                    {
                        int best = -1;
                        for (int ci = 0; ci < cells.Count; ci++)
                        {
                            int cell = cells[ci];
                            bool tooClose = false;
                            for (int p = 0; p < placed.Count; p++)
                            {
                                float dist = Vec2.Distance(
                                    mesh.CellCenters[cell],
                                    mesh.CellCenters[placed[p]]);
                                if (dist < spacing)
                                {
                                    tooClose = true;
                                    break;
                                }
                            }
                            if (!tooClose)
                            {
                                best = cell;
                                break;
                            }
                        }

                        if (best >= 0)
                        {
                            placed.Add(best);
                            break;
                        }

                        // Reduce spacing and retry
                        spacing *= 0.8f;
                        if (spacing < 1f)
                        {
                            // Fallback: just pick the best unplaced cell
                            for (int ci = 0; ci < cells.Count; ci++)
                            {
                                if (!placed.Contains(cells[ci]))
                                {
                                    placed.Add(cells[ci]);
                                    break;
                                }
                            }
                            break;
                        }
                    }
                }

                for (int i = 0; i < placed.Count; i++)
                    allCapitals.Add(placed[i]);
            }

            pol.Capitals = allCapitals.ToArray();
            pol.RealmCount = allCapitals.Count;

            // Assign realm IDs to capital cells (1-based)
            for (int s = 0; s < allCapitals.Count; s++)
                pol.RealmId[allCapitals[s]] = s + 1;
        }

        // ── Stage 3: Realm Growth ───────────────────────────────────────────

        public static void GrowRealms(PoliticalData pol, BiomeData biomes, HeightGrid heights)
        {
            var mesh = pol.Mesh;
            int n = mesh.CellCount;

            // Multi-source Dijkstra from all capitals
            float[] cost = new float[n];
            for (int i = 0; i < n; i++) cost[i] = float.MaxValue;

            var heap = new MinHeap(n);

            for (int s = 0; s < pol.RealmCount; s++)
            {
                int cap = pol.Capitals[s];
                cost[cap] = 0f;
                heap.Push(cap, 0f);
            }

            while (heap.Count > 0)
            {
                var (cell, cellCost) = heap.Pop();

                if (cellCost > cost[cell]) continue;

                int[] neighbors = mesh.CellNeighbors[cell];
                for (int j = 0; j < neighbors.Length; j++)
                {
                    int nb = neighbors[j];
                    if (nb < 0 || nb >= n) continue;
                    if (heights.IsWater(nb) || biomes.IsLakeCell[nb]) continue;
                    if (pol.LandmassId[nb] < 0) continue; // tiny island

                    float edgeCost = biomes.MovementCost[nb];
                    if (edgeCost <= 0f) edgeCost = 1f; // safety

                    float newCost = cellCost + edgeCost;
                    if (newCost < cost[nb])
                    {
                        cost[nb] = newCost;
                        pol.RealmId[nb] = pol.RealmId[cell];
                        heap.Push(nb, newCost);
                    }
                }
            }
        }

        // ── Stage 4: Realm Normalization ────────────────────────────────────

        public static void NormalizeRealms(PoliticalData pol)
        {
            var mesh = pol.Mesh;
            int n = mesh.CellCount;

            // Build capital set for quick lookup
            var isCapital = new bool[n];
            for (int s = 0; s < pol.RealmCount; s++)
                isCapital[pol.Capitals[s]] = true;

            for (int i = 0; i < n; i++)
            {
                if (pol.RealmId[i] == 0) continue; // unassigned
                if (isCapital[i]) continue;

                int[] neighbors = mesh.CellNeighbors[i];
                // Count neighbors per realm
                int bestRealm = 0;
                int bestCount = 0;
                int myRealm = pol.RealmId[i];
                int myCount = 0;

                for (int j = 0; j < neighbors.Length; j++)
                {
                    int nb = neighbors[j];
                    if (nb < 0 || nb >= n) continue;
                    int nbRealm = pol.RealmId[nb];
                    if (nbRealm == 0) continue;

                    if (nbRealm == myRealm)
                    {
                        myCount++;
                    }
                    else
                    {
                        int cnt = 0;
                        for (int k = 0; k < neighbors.Length; k++)
                        {
                            int nb2 = neighbors[k];
                            if (nb2 >= 0 && nb2 < n && pol.RealmId[nb2] == nbRealm)
                                cnt++;
                        }
                        if (cnt > bestCount)
                        {
                            bestCount = cnt;
                            bestRealm = nbRealm;
                        }
                    }
                }

                if (bestCount >= 2 && bestCount > myCount)
                    pol.RealmId[i] = bestRealm;
            }
        }

        // ── Stage 5: Province Subdivision ───────────────────────────────────

        public static void SubdivideProvinces(PoliticalData pol, BiomeData biomes, HeightGrid heights)
        {
            var mesh = pol.Mesh;
            int n = mesh.CellCount;

            int nextProvinceId = 1;

            for (int r = 1; r <= pol.RealmCount; r++)
            {
                // Collect cells in this realm
                var realmCells = new List<int>();
                float realmPop = 0f;
                float realmArea = 0f;

                for (int i = 0; i < n; i++)
                {
                    if (pol.RealmId[i] != r) continue;
                    realmCells.Add(i);
                    realmPop += biomes.Population[i];
                    realmArea += mesh.CellAreas[i];
                }

                if (realmCells.Count == 0) continue;

                int provCount = Math.Max(2, (int)Math.Ceiling(realmPop / PopPerProvince));
                if (provCount > realmCells.Count)
                    provCount = realmCells.Count;

                var seeds = new List<int>();
                int capitalCell = pol.Capitals[r - 1];
                seeds.Add(capitalCell);

                realmCells.Sort((a, b) => biomes.Suitability[b].CompareTo(biomes.Suitability[a]));

                float spacing = (float)Math.Sqrt(realmArea / provCount) * ProvinceSpacingFactor;

                for (int p = 1; p < provCount; p++)
                {
                    float sp = spacing;
                    while (true)
                    {
                        int best = -1;
                        for (int ci = 0; ci < realmCells.Count; ci++)
                        {
                            int cell = realmCells[ci];
                            if (seeds.Contains(cell)) continue;

                            bool tooClose = false;
                            for (int si = 0; si < seeds.Count; si++)
                            {
                                float dist = Vec2.Distance(
                                    mesh.CellCenters[cell],
                                    mesh.CellCenters[seeds[si]]);
                                if (dist < sp)
                                {
                                    tooClose = true;
                                    break;
                                }
                            }
                            if (!tooClose)
                            {
                                best = cell;
                                break;
                            }
                        }

                        if (best >= 0)
                        {
                            seeds.Add(best);
                            break;
                        }

                        sp *= 0.8f;
                        if (sp < 0.5f)
                        {
                            // Fallback: pick best unselected cell
                            for (int ci = 0; ci < realmCells.Count; ci++)
                            {
                                if (!seeds.Contains(realmCells[ci]))
                                {
                                    seeds.Add(realmCells[ci]);
                                    break;
                                }
                            }
                            break;
                        }
                    }
                }

                // Multi-source Dijkstra within this realm
                float[] cost = new float[n];
                for (int i = 0; i < n; i++) cost[i] = float.MaxValue;

                var heap = new MinHeap(realmCells.Count);

                for (int si = 0; si < seeds.Count; si++)
                {
                    int seed = seeds[si];
                    int provId = nextProvinceId + si;
                    pol.ProvinceId[seed] = provId;
                    cost[seed] = 0f;
                    heap.Push(seed, 0f);
                }

                while (heap.Count > 0)
                {
                    var (cell, cellCost) = heap.Pop();
                    if (cellCost > cost[cell]) continue;

                    int[] neighbors = mesh.CellNeighbors[cell];
                    for (int j = 0; j < neighbors.Length; j++)
                    {
                        int nb = neighbors[j];
                        if (nb < 0 || nb >= n) continue;
                        if (pol.RealmId[nb] != r) continue; // stay within realm

                        float edgeCost = biomes.MovementCost[nb];
                        if (edgeCost <= 0f) edgeCost = 1f;

                        float newCost = cellCost + edgeCost;
                        if (newCost < cost[nb])
                        {
                            cost[nb] = newCost;
                            pol.ProvinceId[nb] = pol.ProvinceId[cell];
                            heap.Push(nb, newCost);
                        }
                    }
                }

                nextProvinceId += seeds.Count;
            }

            pol.ProvinceCount = nextProvinceId - 1;
        }

        // ── Stage 6: County Grouping ────────────────────────────────────────

        public static void GroupCounties(PoliticalData pol, BiomeData biomes, HeightGrid heights)
        {
            var mesh = pol.Mesh;
            int n = mesh.CellCount;

            int nextCountyId = 1;
            var seats = new List<int>(); // index = countyId-1

            // Process per province
            for (int prov = 1; prov <= pol.ProvinceCount; prov++)
            {
                // Collect cells in this province
                var provCells = new List<int>();
                for (int i = 0; i < n; i++)
                {
                    if (pol.ProvinceId[i] == prov)
                        provCells.Add(i);
                }

                if (provCells.Count == 0) continue;

                // Phase 1: High-density single-cell counties
                var assigned = new bool[n];
                for (int ci = 0; ci < provCells.Count; ci++)
                {
                    int cell = provCells[ci];
                    if (biomes.Population[cell] >= HighDensityThreshold)
                    {
                        pol.CountyId[cell] = nextCountyId++;
                        seats.Add(cell);
                        assigned[cell] = true;
                    }
                }

                // Phase 2: Seed + flood fill from highest population
                // Sort unassigned cells by population descending
                var unassigned = new List<int>();
                for (int ci = 0; ci < provCells.Count; ci++)
                {
                    int cell = provCells[ci];
                    if (!assigned[cell])
                        unassigned.Add(cell);
                }

                unassigned.Sort((a, b) => biomes.Population[b].CompareTo(biomes.Population[a]));

                for (int ui = 0; ui < unassigned.Count; ui++)
                {
                    int seed = unassigned[ui];
                    if (assigned[seed]) continue;

                    int countyId = nextCountyId++;
                    pol.CountyId[seed] = countyId;
                    seats.Add(seed);
                    assigned[seed] = true;

                    float totalPop = biomes.Population[seed];
                    int cellCount = 1;

                    // Greedy flood fill using frontier
                    var frontier = new List<int>();
                    Vec2 seedPos = mesh.CellCenters[seed];
                    AddFrontier(frontier, seed, mesh, n, prov, pol, assigned);

                    while (frontier.Count > 0 &&
                           totalPop < TargetCountyPop &&
                           cellCount < MaxCellsPerCounty)
                    {
                        // Pick best frontier cell: closest to seed (compact growth)
                        int bestIdx = 0;
                        float bestDist = float.MaxValue;

                        for (int fi = 0; fi < frontier.Count; fi++)
                        {
                            int fc = frontier[fi];
                            float dist = Vec2.SqrDistance(mesh.CellCenters[fc], seedPos);
                            if (dist < bestDist)
                            {
                                bestDist = dist;
                                bestIdx = fi;
                            }
                        }

                        int pick = frontier[bestIdx];
                        frontier.RemoveAt(bestIdx);

                        if (assigned[pick]) continue;

                        pol.CountyId[pick] = countyId;
                        assigned[pick] = true;
                        totalPop += biomes.Population[pick];
                        cellCount++;

                        AddFrontier(frontier, pick, mesh, n, prov, pol, assigned);
                    }
                }
            }

            pol.CountyCount = nextCountyId - 1;
            pol.CountySeats = seats.ToArray();
            for (int i = 0; i < seats.Count; i++)
                pol.IsCountySeat[seats[i]] = true;
        }

        private static void AddFrontier(List<int> frontier, int cell, CellMesh mesh,
            int n, int prov, PoliticalData pol, bool[] assigned)
        {
            int[] neighbors = mesh.CellNeighbors[cell];
            for (int j = 0; j < neighbors.Length; j++)
            {
                int nb = neighbors[j];
                if (nb < 0 || nb >= n) continue;
                if (assigned[nb]) continue;
                if (pol.ProvinceId[nb] != prov) continue;
                if (!frontier.Contains(nb))
                    frontier.Add(nb);
            }
        }

        // ── MinHeap ─────────────────────────────────────────────────────────

        private class MinHeap
        {
            private (int cell, float cost)[] _data;
            private int _count;

            public int Count => _count;

            public MinHeap(int capacity)
            {
                _data = new (int, float)[Math.Max(16, capacity)];
                _count = 0;
            }

            public void Push(int cell, float cost)
            {
                if (_count == _data.Length)
                    Array.Resize(ref _data, _data.Length * 2);

                _data[_count] = (cell, cost);
                BubbleUp(_count);
                _count++;
            }

            public (int cell, float cost) Pop()
            {
                var top = _data[0];
                _count--;
                _data[0] = _data[_count];
                BubbleDown(0);
                return top;
            }

            private void BubbleUp(int i)
            {
                while (i > 0)
                {
                    int parent = (i - 1) / 2;
                    if (_data[i].cost >= _data[parent].cost) break;
                    var tmp = _data[i];
                    _data[i] = _data[parent];
                    _data[parent] = tmp;
                    i = parent;
                }
            }

            private void BubbleDown(int i)
            {
                while (true)
                {
                    int left = 2 * i + 1;
                    int right = 2 * i + 2;
                    int smallest = i;

                    if (left < _count && _data[left].cost < _data[smallest].cost)
                        smallest = left;
                    if (right < _count && _data[right].cost < _data[smallest].cost)
                        smallest = right;

                    if (smallest == i) break;

                    var tmp = _data[i];
                    _data[i] = _data[smallest];
                    _data[smallest] = tmp;
                    i = smallest;
                }
            }
        }
    }
}
