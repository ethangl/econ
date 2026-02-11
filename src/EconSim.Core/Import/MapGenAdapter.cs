using System;
using System.Collections.Generic;
using EconSim.Core.Common;
using EconSim.Core.Data;
using MapGen.Core;
using MGVec2 = MapGen.Core.Vec2;
using ECVec2 = EconSim.Core.Common.Vec2;
using ECRiver = EconSim.Core.Data.River;

namespace EconSim.Core.Import
{
    /// <summary>
    /// Converts MapGen pipeline output into EconSim MapData.
    /// </summary>
    public static class MapGenAdapter
    {
        /// <summary>
        /// Convert a MapGenResult into a fully populated MapData ready for simulation.
        /// </summary>
        public static MapData Convert(MapGenResult result)
        {
            var mesh = result.Mesh;
            var heights = result.Heights;
            var biomes = result.Biomes;
            var rivers = result.Rivers;
            var political = result.Political;
            var world = result.World ?? result.WorldConfig?.BuildMetadata(mesh);

            // MapGen uses Y-up (Y=0 south), same as Unity's texture convention.
            // Pass coordinates through directly — no flip needed.
            int cellCount = mesh.CellCount;
            float seaLevel = world?.SeaLevelHeight ?? HeightGrid.SeaLevel;
            Elevation.AssertAbsoluteHeightInRange(seaLevel, "MapGenAdapter sea level");

            // Build cells
            var cells = new List<Cell>(cellCount);
            for (int i = 0; i < cellCount; i++)
            {
                var center = mesh.CellCenters[i];
                float sourceHeight = heights.Heights[i];
                if (float.IsNaN(sourceHeight) || float.IsInfinity(sourceHeight))
                {
                    throw new InvalidOperationException($"MapGen returned non-finite height for cell {i}: {sourceHeight}");
                }

                int absoluteHeight = (int)Math.Round(sourceHeight);
                Elevation.AssertAbsoluteHeightInRange(absoluteHeight, $"MapGenAdapter source cell {i}");
                float seaRelativeElevation = Elevation.SeaRelativeFromAbsolute(absoluteHeight, seaLevel);
                var cell = new Cell
                {
                    Id = i,
                    Center = ToECVec2(center),
                    VertexIndices = new List<int>(mesh.CellVertices[i]),
                    NeighborIds = new List<int>(mesh.CellNeighbors[i]),
                    Height = absoluteHeight,
                    SeaRelativeElevation = seaRelativeElevation,
                    HasSeaRelativeElevation = true,
                    BiomeId = (int)biomes.Biome[i],
                    SoilId = (int)biomes.Soil[i],
                    IsLand = !heights.IsWater(i) && !biomes.IsLakeCell[i],
                    RealmId = political.RealmId[i],
                    ProvinceId = political.ProvinceId[i],
                    CountyId = political.CountyId[i],
                    Population = biomes.Population[i],
                };

                if (!cell.HasSeaRelativeElevation)
                    throw new InvalidOperationException($"Cell {i} was generated without canonical SeaRelativeElevation.");

                cells.Add(cell);
            }

            // Vertices
            var vertices = new List<ECVec2>(mesh.VertexCount);
            for (int v = 0; v < mesh.VertexCount; v++)
                vertices.Add(ToECVec2(mesh.Vertices[v]));

            // CoastDistance + FeatureId: pre-computed by MapGen's GeographyOps
            for (int i = 0; i < cellCount; i++)
            {
                cells[i].CoastDistance = biomes.CoastDistance[i];
                cells[i].FeatureId = biomes.FeatureId[i];
            }

            // Features: convert from MapGen WaterFeature to EconSim Feature
            var features = new List<Feature>();
            foreach (var wf in biomes.Features)
            {
                features.Add(new Feature
                {
                    Id = wf.Id,
                    Type = wf.Type == MapGen.Core.WaterFeatureType.Lake ? "lake" : "ocean",
                    IsBorder = wf.TouchesBorder,
                    CellCount = wf.CellCount
                });
            }

            // Rivers: convert vertex paths to point lists for rendering
            var riverList = ConvertRivers(rivers, mesh);

            // Burgs: county seats become burgs
            var burgs = BuildBurgs(cells, political);

            // Realms
            var realms = BuildRealms(cells, political);

            // Provinces
            var provinces = BuildProvinces(cells, political);

            // Counties
            var counties = BuildCounties(cells, political, biomes);

            // Biome definitions (static table for the 18+1 MapGen biomes)
            var biomeDefs = BuildBiomeDefinitions();

            // Map info
            int landCells = 0;
            foreach (var c in cells)
                if (c.IsLand) landCells++;

            var info = new MapInfo
            {
                Name = "Generated Map",
                Width = (int)Math.Round(mesh.Width),
                Height = (int)Math.Round(mesh.Height),
                Seed = "",
                TotalCells = cellCount,
                LandCells = landCells,
                SeaLevel = seaLevel,
                World = world == null
                    ? null
                    : new WorldInfo
                    {
                        CellSizeKm = world.CellSizeKm,
                        MapWidthKm = world.MapWidthKm,
                        MapHeightKm = world.MapHeightKm,
                        MapAreaKm2 = world.MapAreaKm2,
                        LatitudeSouth = world.LatitudeSouth,
                        LatitudeNorth = world.LatitudeNorth,
                        MinHeight = world.MinHeight,
                        SeaLevelHeight = world.SeaLevelHeight,
                        MaxHeight = world.MaxHeight,
                        MaxElevationMeters = world.MaxElevationMeters,
                        MaxSeaDepthMeters = world.MaxSeaDepthMeters
                    }
            };

            var mapData = new MapData
            {
                Info = info,
                Cells = cells,
                Vertices = vertices,
                Realms = realms,
                Provinces = provinces,
                Rivers = riverList,
                Biomes = biomeDefs,
                Burgs = burgs,
                Features = features,
                Counties = counties
            };

            mapData.BuildLookups();
            mapData.AssertElevationInvariants(requireCanonical: true);
            mapData.AssertWorldInvariants(requireWorldMetadata: true);

            return mapData;
        }

        static ECVec2 ToECVec2(MGVec2 v) => new ECVec2(v.X, v.Y);


        #region Rivers

        static List<ECRiver> ConvertRivers(RiverData riverData, CellMesh mesh)
        {
            var rivers = new List<ECRiver>();

            for (int r = 0; r < riverData.Rivers.Length; r++)
            {
                ref var mgRiver = ref riverData.Rivers[r];
                if (mgRiver.Vertices.Length < 2) continue;

                // MapGen vertices are mouth-first; reverse for source→mouth
                var points = new List<ECVec2>(mgRiver.Vertices.Length + 1);
                for (int vi = mgRiver.Vertices.Length - 1; vi >= 0; vi--)
                {
                    int vertIdx = mgRiver.Vertices[vi];
                    if (vertIdx < 0 || vertIdx >= mesh.VertexCount) continue;
                    points.Add(ToECVec2(mesh.Vertices[vertIdx]));
                }

                if (points.Count < 2) continue;

                // Extend source end into adjacent lake vertex
                int sourceVert = mgRiver.SourceVertex;
                int[] sourceNeighbors = mesh.VertexNeighbors[sourceVert];
                if (sourceNeighbors != null)
                {
                    for (int i = 0; i < sourceNeighbors.Length; i++)
                    {
                        int nb = sourceNeighbors[i];
                        if (nb >= 0 && nb < mesh.VertexCount && riverData.IsLake(nb))
                        {
                            points.Insert(0, ToECVec2(mesh.Vertices[nb]));
                            break;
                        }
                    }
                }

                // Extend mouth end: add junction vertex for tributaries
                int mouthVert = mgRiver.MouthVertex;
                if (mouthVert != mgRiver.Vertices[0])
                {
                    points.Add(ToECVec2(mesh.Vertices[mouthVert]));
                }

                // Extend to ocean vertex so river visually reaches the coast
                int flowTarget = riverData.FlowTarget[mouthVert];
                if (flowTarget >= 0 && flowTarget < mesh.VertexCount && riverData.IsOcean(flowTarget))
                {
                    points.Add(ToECVec2(mesh.Vertices[flowTarget]));
                }

                int riverId = r + 1;
                rivers.Add(new ECRiver
                {
                    Id = riverId,
                    Name = $"River {riverId}",
                    Type = "River",
                    Points = points,
                    CellPath = new List<int>(),
                    Width = Math.Min(5f, Math.Max(0.5f, (float)Math.Log(mgRiver.Discharge + 1) * 0.4f)),
                    Discharge = (int)mgRiver.Discharge,
                    Length = points.Count,
                });
            }

            return rivers;
        }

        #endregion

        #region Burgs

        static List<Burg> BuildBurgs(List<Cell> cells, PoliticalData political)
        {
            var burgs = new List<Burg>();
            if (political.CountySeats == null) return burgs;

            // County seats become burgs
            for (int ci = 0; ci < political.CountySeats.Length; ci++)
            {
                int countyId = ci + 1; // 1-based
                int cellId = political.CountySeats[ci];
                if (cellId < 0 || cellId >= cells.Count) continue;

                var cell = cells[cellId];
                int burgId = ci + 1; // 1-based

                bool isCapital = false;
                if (political.Capitals != null)
                {
                    for (int ri = 0; ri < political.Capitals.Length; ri++)
                    {
                        if (political.Capitals[ri] == cellId)
                        {
                            isCapital = true;
                            break;
                        }
                    }
                }

                var burg = new Burg
                {
                    Id = burgId,
                    Name = $"Town {burgId}",
                    Position = cell.Center,
                    CellId = cellId,
                    RealmId = cell.RealmId,
                    CultureId = 0,
                    Population = cell.Population,
                    IsCapital = isCapital,
                    IsPort = false,
                    Type = isCapital ? "Capital" : "Town",
                    Group = isCapital ? "capital" : "town",
                };
                burgs.Add(burg);

                cell.BurgId = burgId;
            }

            return burgs;
        }

        #endregion

        #region Realms

        static List<Realm> BuildRealms(List<Cell> cells, PoliticalData political)
        {
            var realms = new List<Realm>();
            if (political.RealmCount == 0) return realms;

            // Gather cells per realm
            var realmCells = new Dictionary<int, List<int>>();
            for (int i = 0; i < cells.Count; i++)
            {
                int rid = political.RealmId[i];
                if (rid <= 0) continue;
                if (!realmCells.TryGetValue(rid, out var list))
                {
                    list = new List<int>();
                    realmCells[rid] = list;
                }
                list.Add(i);
            }

            // Gather provinces per realm
            var realmProvinces = new Dictionary<int, HashSet<int>>();
            for (int i = 0; i < cells.Count; i++)
            {
                int rid = political.RealmId[i];
                int pid = political.ProvinceId[i];
                if (rid <= 0 || pid <= 0) continue;
                if (!realmProvinces.TryGetValue(rid, out var set))
                {
                    set = new HashSet<int>();
                    realmProvinces[rid] = set;
                }
                set.Add(pid);
            }

            // Generate realm colors using even hue distribution
            for (int si = 0; si < political.RealmCount; si++)
            {
                int realmId = si + 1;
                if (!realmCells.TryGetValue(realmId, out var rCells)) continue;

                float h = (float)si / political.RealmCount;
                float s = 0.42f + (HashToFloat(realmId + 3000) - 0.5f) * 0.16f;
                float v = 0.70f + (HashToFloat(realmId + 4000) - 0.5f) * 0.16f;
                s = Math.Max(0.28f, Math.Min(0.55f, s));
                v = Math.Max(0.58f, Math.Min(0.85f, v));

                int capitalCell = (political.Capitals != null && si < political.Capitals.Length)
                    ? political.Capitals[si]
                    : rCells[0];

                // Find capital burg ID
                int capitalBurgId = 0;
                if (capitalCell >= 0 && capitalCell < cells.Count)
                    capitalBurgId = cells[capitalCell].BurgId;

                // Province IDs
                var provIds = new List<int>();
                if (realmProvinces.TryGetValue(realmId, out var pset))
                    provIds.AddRange(pset);

                // Population
                float totalPop = 0;
                foreach (int ci in rCells)
                    totalPop += cells[ci].Population;

                // Center = capital cell
                var centerPos = capitalCell >= 0 && capitalCell < cells.Count
                    ? cells[capitalCell].Center
                    : ECVec2.Zero;

                realms.Add(new Realm
                {
                    Id = realmId,
                    Name = $"Kingdom {realmId}",
                    FullName = $"Kingdom of Region {realmId}",
                    GovernmentForm = "",
                    CapitalBurgId = capitalBurgId,
                    CenterCellId = capitalCell,
                    CultureId = 0,
                    Color = HsvToColor32(h, s, v),
                    LabelPosition = centerPos,
                    ProvinceIds = provIds,
                    NeighborRealmIds = new List<int>(),
                    UrbanPopulation = 0,
                    RuralPopulation = totalPop,
                    TotalArea = rCells.Count
                });
            }

            // Compute neighbor realms
            var realmById = new Dictionary<int, Realm>();
            foreach (var r in realms)
                realmById[r.Id] = r;

            for (int i = 0; i < cells.Count; i++)
            {
                int rid = political.RealmId[i];
                if (rid <= 0) continue;
                foreach (int nb in cells[i].NeighborIds)
                {
                    if (nb >= 0 && nb < cells.Count)
                    {
                        int nrid = political.RealmId[nb];
                        if (nrid > 0 && nrid != rid)
                        {
                            if (realmById.TryGetValue(rid, out var r) && !r.NeighborRealmIds.Contains(nrid))
                                r.NeighborRealmIds.Add(nrid);
                        }
                    }
                }
            }

            return realms;
        }

        #endregion

        #region Provinces

        static List<Province> BuildProvinces(List<Cell> cells, PoliticalData political)
        {
            var provinces = new List<Province>();
            if (political.ProvinceCount == 0) return provinces;

            // Gather cells per province
            var provCells = new Dictionary<int, List<int>>();
            var provRealm = new Dictionary<int, int>();
            for (int i = 0; i < cells.Count; i++)
            {
                int pid = political.ProvinceId[i];
                if (pid <= 0) continue;
                if (!provCells.TryGetValue(pid, out var list))
                {
                    list = new List<int>();
                    provCells[pid] = list;
                }
                list.Add(i);
                provRealm[pid] = political.RealmId[i];
            }

            foreach (var kvp in provCells)
            {
                int pid = kvp.Key;
                var pCells = kvp.Value;
                int realmId = provRealm.TryGetValue(pid, out var sid) ? sid : 0;

                // Find highest-pop cell as center
                int centerCell = pCells[0];
                float maxPop = 0;
                foreach (int ci in pCells)
                {
                    if (cells[ci].Population > maxPop)
                    {
                        maxPop = cells[ci].Population;
                        centerCell = ci;
                    }
                }

                // Province color derived from realm color with small variance
                float h = HashToFloat(pid * 7919);
                float s = 0.35f + HashToFloat(pid + 5000) * 0.15f;
                float v = 0.65f + HashToFloat(pid + 6000) * 0.15f;

                provinces.Add(new Province
                {
                    Id = pid,
                    Name = $"Province {pid}",
                    FullName = $"Province {pid}",
                    RealmId = realmId,
                    CenterCellId = centerCell,
                    CapitalBurgId = cells[centerCell].BurgId,
                    Color = HsvToColor32(h, s, v),
                    LabelPosition = cells[centerCell].Center,
                    CellIds = new List<int>(pCells)
                });
            }

            return provinces;
        }

        #endregion

        #region Counties

        static List<County> BuildCounties(List<Cell> cells, PoliticalData political, BiomeData biomes)
        {
            var counties = new List<County>();
            if (political.CountyCount == 0) return counties;

            // Gather cells per county
            var countyCells = new Dictionary<int, List<int>>();
            for (int i = 0; i < cells.Count; i++)
            {
                int cid = political.CountyId[i];
                if (cid <= 0) continue;
                if (!countyCells.TryGetValue(cid, out var list))
                {
                    list = new List<int>();
                    countyCells[cid] = list;
                }
                list.Add(i);
            }

            foreach (var kvp in countyCells)
            {
                int cid = kvp.Key;
                var cCells = kvp.Value;

                int seatCell = (political.CountySeats != null && cid - 1 >= 0 && cid - 1 < political.CountySeats.Length)
                    ? political.CountySeats[cid - 1]
                    : cCells[0];

                float totalPop = 0;
                float sumX = 0, sumY = 0, sumW = 0;
                int provinceId = 0;
                int realmId = 0;

                foreach (int ci in cCells)
                {
                    var cell = cells[ci];
                    totalPop += cell.Population;
                    float w = cell.Population > 0 ? cell.Population : 1;
                    sumX += cell.Center.X * w;
                    sumY += cell.Center.Y * w;
                    sumW += w;
                    if (provinceId == 0) provinceId = cell.ProvinceId;
                    if (realmId == 0) realmId = cell.RealmId;
                }

                var centroid = sumW > 0
                    ? new ECVec2(sumX / sumW, sumY / sumW)
                    : cells[seatCell].Center;

                // Name from burg if seat has one
                string name = cells[seatCell].BurgId > 0
                    ? $"Town {cells[seatCell].BurgId}"
                    : $"County {cid}";

                counties.Add(new County
                {
                    Id = cid,
                    Name = name,
                    SeatCellId = seatCell,
                    CellIds = new List<int>(cCells),
                    ProvinceId = provinceId,
                    RealmId = realmId,
                    TotalPopulation = totalPop,
                    Centroid = centroid
                });
            }

            return counties;
        }

        #endregion

        #region Biome Definitions

        /// <summary>
        /// Static table matching the 19 MapGen BiomeId enum values.
        /// </summary>
        static List<Biome> BuildBiomeDefinitions()
        {
            return new List<Biome>
            {
                new Biome { Id = 0,  Name = "Glacier",             Color = new Color32(220, 235, 250, 255), Habitability = 2,  MovementCost = 200 },
                new Biome { Id = 1,  Name = "Tundra",              Color = new Color32(180, 210, 200, 255), Habitability = 8,  MovementCost = 140 },
                new Biome { Id = 2,  Name = "Salt Flat",           Color = new Color32(230, 220, 200, 255), Habitability = 3,  MovementCost = 80  },
                new Biome { Id = 3,  Name = "Coastal Marsh",       Color = new Color32(140, 175, 140, 255), Habitability = 25, MovementCost = 160 },
                new Biome { Id = 4,  Name = "Alpine Barren",       Color = new Color32(170, 170, 170, 255), Habitability = 5,  MovementCost = 180 },
                new Biome { Id = 5,  Name = "Mountain Shrub",      Color = new Color32(140, 160, 120, 255), Habitability = 15, MovementCost = 150 },
                new Biome { Id = 6,  Name = "Floodplain",          Color = new Color32(90,  160, 70,  255), Habitability = 80, MovementCost = 100 },
                new Biome { Id = 7,  Name = "Wetland",             Color = new Color32(100, 150, 120, 255), Habitability = 20, MovementCost = 170 },
                new Biome { Id = 8,  Name = "Hot Desert",          Color = new Color32(220, 200, 140, 255), Habitability = 5,  MovementCost = 120 },
                new Biome { Id = 9,  Name = "Cold Desert",         Color = new Color32(200, 195, 170, 255), Habitability = 8,  MovementCost = 110 },
                new Biome { Id = 10, Name = "Scrubland",           Color = new Color32(180, 180, 100, 255), Habitability = 30, MovementCost = 100 },
                new Biome { Id = 11, Name = "Tropical Rainforest", Color = new Color32(40,  120, 40,  255), Habitability = 40, MovementCost = 160 },
                new Biome { Id = 12, Name = "Tropical Dry Forest", Color = new Color32(100, 140, 60,  255), Habitability = 50, MovementCost = 130 },
                new Biome { Id = 13, Name = "Savanna",             Color = new Color32(170, 180, 80,  255), Habitability = 45, MovementCost = 90  },
                new Biome { Id = 14, Name = "Boreal Forest",       Color = new Color32(70,  110, 80,  255), Habitability = 20, MovementCost = 140 },
                new Biome { Id = 15, Name = "Temperate Forest",    Color = new Color32(60,  140, 60,  255), Habitability = 60, MovementCost = 120 },
                new Biome { Id = 16, Name = "Grassland",           Color = new Color32(150, 190, 90,  255), Habitability = 70, MovementCost = 80  },
                new Biome { Id = 17, Name = "Woodland",            Color = new Color32(90,  150, 70,  255), Habitability = 55, MovementCost = 110 },
                new Biome { Id = 18, Name = "Lake",                Color = new Color32(80,  130, 190, 255), Habitability = 0,  MovementCost = 250 },
            };
        }

        #endregion

        #region Color Utilities

        static float HashToFloat(int value)
        {
            uint h = (uint)value;
            h ^= h >> 16;
            h *= 0x85ebca6b;
            h ^= h >> 13;
            h *= 0xc2b2ae35;
            h ^= h >> 16;
            return (h & 0x7FFFFFFF) / (float)0x7FFFFFFF;
        }

        static Color32 HsvToColor32(float h, float s, float v)
        {
            float r, g, b;
            if (s <= 0)
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
            return new Color32(
                (byte)(r * 255),
                (byte)(g * 255),
                (byte)(b * 255),
                255
            );
        }

        #endregion
    }
}
