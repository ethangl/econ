using System;
using System.Collections.Generic;
using MapGen.Core;

namespace PopGen.Core
{
    /// <summary>
    /// Generates human/political structures from MapGen fields.
    /// </summary>
    public static class PopGenPipeline
    {
        public static PopGenResult Generate(MapGenResult map, PopGenConfig config, PopGenSeed seed)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (map.Mesh == null) throw new InvalidOperationException("MapGenResult.Mesh is required.");
            if (map.Political == null) throw new InvalidOperationException("MapGenResult.Political is required.");
            if (map.Biomes == null) throw new InvalidOperationException("MapGenResult.Biomes is required.");

            config ??= new PopGenConfig();

            var mesh = map.Mesh;
            var political = map.Political;
            var biomes = map.Biomes;
            int cellCount = mesh.CellCount;

            var populations = new float[cellCount];
            if (biomes.Population != null)
            {
                int n = Math.Min(cellCount, biomes.Population.Length);
                Array.Copy(biomes.Population, populations, n);
            }

            int[] realmIds = political.RealmId ?? new int[cellCount];
            int[] provinceIds = political.ProvinceId ?? new int[cellCount];
            int[] countyIds = political.CountyId ?? new int[cellCount];
            int[] capitals = political.Capitals ?? Array.Empty<int>();
            int[] countySeats = political.CountySeats ?? Array.Empty<int>();

            var cellBurgId = new int[cellCount];
            PopBurg[] burgs = BuildBurgs(mesh, populations, realmIds, capitals, countySeats, cellBurgId);
            PopProvince[] provinces = BuildProvinces(mesh, populations, realmIds, provinceIds, cellBurgId, config, seed);
            PopRealm[] realms = BuildRealms(mesh, populations, realmIds, provinceIds, capitals, cellBurgId, political.RealmCount, config, seed);
            PopCounty[] counties = BuildCounties(mesh, populations, realmIds, provinceIds, countyIds, countySeats, cellBurgId, political.CountyCount);

            return new PopGenResult
            {
                Burgs = burgs,
                Counties = counties,
                Provinces = provinces,
                Realms = realms,
                CellBurgId = cellBurgId
            };
        }

        static PopBurg[] BuildBurgs(
            CellMesh mesh,
            float[] populations,
            int[] realmIds,
            int[] capitals,
            int[] countySeats,
            int[] cellBurgId)
        {
            if (countySeats == null || countySeats.Length == 0)
                return Array.Empty<PopBurg>();

            var capitalCells = new HashSet<int>(capitals ?? Array.Empty<int>());
            var burgs = new List<PopBurg>(countySeats.Length);
            for (int ci = 0; ci < countySeats.Length; ci++)
            {
                int cellId = countySeats[ci];
                if ((uint)cellId >= (uint)mesh.CellCount)
                    continue;

                int burgId = ci + 1;
                burgs.Add(new PopBurg
                {
                    Id = burgId,
                    Name = $"Town {burgId}",
                    Position = mesh.CellCenters[cellId],
                    CellId = cellId,
                    RealmId = (uint)cellId < (uint)realmIds.Length ? realmIds[cellId] : 0,
                    CultureId = 0,
                    Population = (uint)cellId < (uint)populations.Length ? populations[cellId] : 0f,
                    IsCapital = capitalCells.Contains(cellId),
                    IsPort = false,
                    Type = capitalCells.Contains(cellId) ? "Capital" : "Town",
                    Group = capitalCells.Contains(cellId) ? "capital" : "town"
                });

                cellBurgId[cellId] = burgId;
            }

            return burgs.ToArray();
        }

        static PopProvince[] BuildProvinces(
            CellMesh mesh,
            float[] populations,
            int[] realmIds,
            int[] provinceIds,
            int[] cellBurgId,
            PopGenConfig config,
            PopGenSeed seed)
        {
            var provCells = new Dictionary<int, List<int>>();
            var provRealm = new Dictionary<int, int>();
            for (int i = 0; i < mesh.CellCount; i++)
            {
                int pid = (uint)i < (uint)provinceIds.Length ? provinceIds[i] : 0;
                if (pid <= 0) continue;
                if (!provCells.TryGetValue(pid, out var list))
                {
                    list = new List<int>();
                    provCells[pid] = list;
                }

                list.Add(i);
                provRealm[pid] = (uint)i < (uint)realmIds.Length ? realmIds[i] : 0;
            }

            int colorSalt = config.UseSeedForPoliticalColorVariation ? seed.Value : 0;
            var provinces = new List<PopProvince>(provCells.Count);
            foreach (var kvp in provCells)
            {
                int pid = kvp.Key;
                List<int> pCells = kvp.Value;
                int realmId = provRealm.TryGetValue(pid, out var rid) ? rid : 0;

                int centerCell = pCells[0];
                float maxPop = 0f;
                foreach (int ci in pCells)
                {
                    float pop = (uint)ci < (uint)populations.Length ? populations[ci] : 0f;
                    if (pop > maxPop)
                    {
                        maxPop = pop;
                        centerCell = ci;
                    }
                }

                float h = HashToUnitFloat(pid * 7919 + colorSalt);
                float s = 0.35f + HashToUnitFloat(pid + 5000 + colorSalt) * 0.15f;
                float v = 0.65f + HashToUnitFloat(pid + 6000 + colorSalt) * 0.15f;
                PopColor32 color = HsvToColor32(h, s, v);

                provinces.Add(new PopProvince
                {
                    Id = pid,
                    Name = $"Province {pid}",
                    FullName = $"Province {pid}",
                    RealmId = realmId,
                    CenterCellId = centerCell,
                    CapitalBurgId = (uint)centerCell < (uint)cellBurgId.Length ? cellBurgId[centerCell] : 0,
                    Color = color,
                    LabelPosition = mesh.CellCenters[centerCell],
                    CellIds = pCells.ToArray()
                });
            }

            return provinces.ToArray();
        }

        static PopRealm[] BuildRealms(
            CellMesh mesh,
            float[] populations,
            int[] realmIds,
            int[] provinceIds,
            int[] capitals,
            int[] cellBurgId,
            int realmCount,
            PopGenConfig config,
            PopGenSeed seed)
        {
            if (realmCount <= 0)
                return Array.Empty<PopRealm>();

            var realmCells = new Dictionary<int, List<int>>();
            for (int i = 0; i < mesh.CellCount; i++)
            {
                int rid = (uint)i < (uint)realmIds.Length ? realmIds[i] : 0;
                if (rid <= 0) continue;
                if (!realmCells.TryGetValue(rid, out var list))
                {
                    list = new List<int>();
                    realmCells[rid] = list;
                }

                list.Add(i);
            }

            var realmProvinces = new Dictionary<int, HashSet<int>>();
            for (int i = 0; i < mesh.CellCount; i++)
            {
                int rid = (uint)i < (uint)realmIds.Length ? realmIds[i] : 0;
                int pid = (uint)i < (uint)provinceIds.Length ? provinceIds[i] : 0;
                if (rid <= 0 || pid <= 0) continue;
                if (!realmProvinces.TryGetValue(rid, out var set))
                {
                    set = new HashSet<int>();
                    realmProvinces[rid] = set;
                }

                set.Add(pid);
            }

            int colorSalt = config.UseSeedForPoliticalColorVariation ? seed.Value : 0;
            var realms = new List<PopRealm>(realmCount);
            for (int si = 0; si < realmCount; si++)
            {
                int realmId = si + 1;
                if (!realmCells.TryGetValue(realmId, out var rCells))
                    continue;

                float h = (float)si / realmCount;
                float s = 0.42f + (HashToUnitFloat(realmId + 3000 + colorSalt) - 0.5f) * 0.16f;
                float v = 0.70f + (HashToUnitFloat(realmId + 4000 + colorSalt) - 0.5f) * 0.16f;
                s = Math.Max(0.28f, Math.Min(0.55f, s));
                v = Math.Max(0.58f, Math.Min(0.85f, v));
                PopColor32 color = HsvToColor32(h, s, v);

                int capitalCell = (capitals != null && si < capitals.Length)
                    ? capitals[si]
                    : rCells[0];

                int capitalBurgId = ((uint)capitalCell < (uint)cellBurgId.Length)
                    ? cellBurgId[capitalCell]
                    : 0;

                int[] provinceSet = Array.Empty<int>();
                if (realmProvinces.TryGetValue(realmId, out var pset))
                {
                    provinceSet = new int[pset.Count];
                    pset.CopyTo(provinceSet);
                }

                float totalPop = 0f;
                foreach (int ci in rCells)
                    totalPop += (uint)ci < (uint)populations.Length ? populations[ci] : 0f;

                Vec2 centerPos = (uint)capitalCell < (uint)mesh.CellCount
                    ? mesh.CellCenters[capitalCell]
                    : new Vec2(0f, 0f);

                realms.Add(new PopRealm
                {
                    Id = realmId,
                    Name = $"Kingdom {realmId}",
                    FullName = $"Kingdom of Region {realmId}",
                    GovernmentForm = "",
                    CapitalBurgId = capitalBurgId,
                    CenterCellId = capitalCell,
                    CultureId = 0,
                    Color = color,
                    LabelPosition = centerPos,
                    ProvinceIds = provinceSet,
                    NeighborRealmIds = Array.Empty<int>(),
                    UrbanPopulation = 0f,
                    RuralPopulation = totalPop,
                    TotalArea = rCells.Count
                });
            }

            var neighborByRealm = new Dictionary<int, HashSet<int>>();
            for (int i = 0; i < mesh.CellCount; i++)
            {
                int rid = (uint)i < (uint)realmIds.Length ? realmIds[i] : 0;
                if (rid <= 0) continue;

                int[] neighbors = mesh.CellNeighbors[i];
                if (neighbors == null) continue;

                if (!neighborByRealm.TryGetValue(rid, out var set))
                {
                    set = new HashSet<int>();
                    neighborByRealm[rid] = set;
                }

                for (int ni = 0; ni < neighbors.Length; ni++)
                {
                    int nb = neighbors[ni];
                    if ((uint)nb >= (uint)mesh.CellCount) continue;
                    int nrid = (uint)nb < (uint)realmIds.Length ? realmIds[nb] : 0;
                    if (nrid > 0 && nrid != rid)
                        set.Add(nrid);
                }
            }

            foreach (PopRealm realm in realms)
            {
                if (neighborByRealm.TryGetValue(realm.Id, out var set))
                {
                    int[] neighbors = new int[set.Count];
                    set.CopyTo(neighbors);
                    realm.NeighborRealmIds = neighbors;
                }
            }

            return realms.ToArray();
        }

        static PopCounty[] BuildCounties(
            CellMesh mesh,
            float[] populations,
            int[] realmIds,
            int[] provinceIds,
            int[] countyIds,
            int[] countySeats,
            int[] cellBurgId,
            int countyCount)
        {
            if (countyCount <= 0)
                return Array.Empty<PopCounty>();

            var countyCells = new Dictionary<int, List<int>>();
            for (int i = 0; i < mesh.CellCount; i++)
            {
                int cid = (uint)i < (uint)countyIds.Length ? countyIds[i] : 0;
                if (cid <= 0) continue;
                if (!countyCells.TryGetValue(cid, out var list))
                {
                    list = new List<int>();
                    countyCells[cid] = list;
                }

                list.Add(i);
            }

            var counties = new List<PopCounty>(countyCells.Count);
            foreach (var kvp in countyCells)
            {
                int cid = kvp.Key;
                List<int> cCells = kvp.Value;
                int seatCell = (countySeats != null && cid - 1 >= 0 && cid - 1 < countySeats.Length)
                    ? countySeats[cid - 1]
                    : cCells[0];

                float totalPop = 0f;
                float sumX = 0f;
                float sumY = 0f;
                float sumW = 0f;
                int provinceId = 0;
                int realmId = 0;
                foreach (int ci in cCells)
                {
                    float pop = (uint)ci < (uint)populations.Length ? populations[ci] : 0f;
                    totalPop += pop;
                    float w = pop > 0f ? pop : 1f;
                    sumX += mesh.CellCenters[ci].X * w;
                    sumY += mesh.CellCenters[ci].Y * w;
                    sumW += w;

                    if (provinceId == 0 && (uint)ci < (uint)provinceIds.Length)
                        provinceId = provinceIds[ci];
                    if (realmId == 0 && (uint)ci < (uint)realmIds.Length)
                        realmId = realmIds[ci];
                }

                Vec2 centroid = sumW > 0f
                    ? new Vec2(sumX / sumW, sumY / sumW)
                    : ((uint)seatCell < (uint)mesh.CellCount ? mesh.CellCenters[seatCell] : new Vec2(0f, 0f));

                int seatBurgId = (uint)seatCell < (uint)cellBurgId.Length ? cellBurgId[seatCell] : 0;
                string name = seatBurgId > 0
                    ? $"Town {seatBurgId}"
                    : $"County {cid}";

                counties.Add(new PopCounty
                {
                    Id = cid,
                    Name = name,
                    SeatCellId = seatCell,
                    CellIds = cCells.ToArray(),
                    ProvinceId = provinceId,
                    RealmId = realmId,
                    TotalPopulation = totalPop,
                    Centroid = centroid
                });
            }

            return counties.ToArray();
        }

        static float HashToUnitFloat(int value)
        {
            uint h = (uint)value;
            h ^= h >> 16;
            h *= 0x85ebca6b;
            h ^= h >> 13;
            h *= 0xc2b2ae35;
            h ^= h >> 16;
            return (h & 0x7FFFFFFF) / (float)0x7FFFFFFF;
        }

        static PopColor32 HsvToColor32(float h, float s, float v)
        {
            float r;
            float g;
            float b;
            if (s <= 0f)
            {
                r = g = b = v;
            }
            else
            {
                float hh = h * 6f;
                int i = (int)hh;
                float ff = hh - i;
                float p = v * (1f - s);
                float q = v * (1f - (s * ff));
                float t = v * (1f - (s * (1f - ff)));
                switch (i)
                {
                    case 0: r = v; g = t; b = p; break;
                    case 1: r = q; g = v; b = p; break;
                    case 2: r = p; g = v; b = t; break;
                    case 3: r = p; g = q; b = v; break;
                    case 4: r = t; g = p; b = v; break;
                    default: r = v; g = p; b = q; break;
                }
            }

            return new PopColor32(
                (byte)(r * 255f),
                (byte)(g * 255f),
                (byte)(b * 255f),
                255);
        }
    }
}
