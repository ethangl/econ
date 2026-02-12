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
            if (result == null)
                throw new ArgumentNullException(nameof(result));

            var mesh = result.Mesh;
            var elevation = result.Elevation;
            var biomes = result.Biomes;
            var rivers = result.Rivers;
            var political = result.Political;
            var world = result.World;
            if (world == null)
                throw new InvalidOperationException("MapGenResult.World metadata is required.");

            int cellCount = mesh.CellCount;
            var cells = new List<Cell>(cellCount);
            for (int i = 0; i < cellCount; i++)
            {
                float signedMeters = elevation.ElevationMetersSigned[i];
                if (float.IsNaN(signedMeters) || float.IsInfinity(signedMeters))
                    throw new InvalidOperationException($"MapGen returned non-finite elevation for cell {i}: {signedMeters}");
                if (signedMeters < -world.MaxSeaDepthMeters || signedMeters > world.MaxElevationMeters)
                {
                    throw new InvalidOperationException(
                        $"MapGen returned signed elevation out of configured range for cell {i}: {signedMeters} not in [-{world.MaxSeaDepthMeters}, {world.MaxElevationMeters}]");
                }

                var cell = new Cell
                {
                    Id = i,
                    Center = ToECVec2(mesh.CellCenters[i]),
                    VertexIndices = new List<int>(mesh.CellVertices[i]),
                    NeighborIds = new List<int>(mesh.CellNeighbors[i]),
                    SeaRelativeElevation = signedMeters,
                    HasSeaRelativeElevation = true,
                    BiomeId = (int)biomes.Biome[i],
                    SoilId = 0,
                    IsLand = elevation.IsLand(i) && !biomes.IsLakeCell[i],
                    RealmId = political.RealmId[i],
                    ProvinceId = political.ProvinceId[i],
                    CountyId = political.CountyId[i],
                    Population = biomes.Population[i]
                };

                cells.Add(cell);
            }

            var vertices = new List<ECVec2>(mesh.VertexCount);
            for (int v = 0; v < mesh.VertexCount; v++)
                vertices.Add(ToECVec2(mesh.Vertices[v]));

            for (int i = 0; i < cellCount; i++)
            {
                cells[i].CoastDistance = biomes.CoastDistance[i];
                cells[i].FeatureId = biomes.FeatureId[i];
            }

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

            var riverExports = LegacyExportOps.BuildRiverExports(rivers);
            var riverList = new List<ECRiver>(riverExports.Length);
            for (int i = 0; i < riverExports.Length; i++)
            {
                RiverExport exported = riverExports[i];
                var points = new List<ECVec2>(exported.Points.Length);
                for (int pi = 0; pi < exported.Points.Length; pi++)
                    points.Add(ToECVec2(exported.Points[pi]));

                riverList.Add(new ECRiver
                {
                    Id = exported.Id,
                    Name = $"River {exported.Id}",
                    Type = "River",
                    Points = points,
                    CellPath = new List<int>(),
                    Width = exported.Width,
                    Discharge = exported.Discharge,
                    Length = points.Count,
                });
            }
            PoliticalData legacyPolitical = ToPoliticalData(mesh, political);
            var burgs = BuildBurgs(cells, legacyPolitical);
            var realms = BuildRealms(cells, legacyPolitical);
            var provinces = BuildProvinces(cells, legacyPolitical);
            var counties = BuildCounties(cells, legacyPolitical);
            var biomeDefs = BuildBiomeDefinitions();

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
                World = new WorldInfo
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
            mapData.AssertElevationInvariants();
            mapData.AssertWorldInvariants();

            return mapData;
        }

        static ECVec2 ToECVec2(MGVec2 v) => new ECVec2(v.X, v.Y);


        static PoliticalData ToPoliticalData(CellMesh mesh, PoliticalField source)
        {
            var data = new PoliticalData(mesh)
            {
                RealmCount = source.RealmCount,
                ProvinceCount = source.ProvinceCount,
                CountyCount = source.CountyCount,
                LandmassCount = source.LandmassCount,
                Capitals = source.Capitals ?? Array.Empty<int>(),
                CountySeats = source.CountySeats ?? Array.Empty<int>()
            };

            if (source.LandmassId != null && source.LandmassId.Length == data.LandmassId.Length)
                Array.Copy(source.LandmassId, data.LandmassId, source.LandmassId.Length);
            if (source.RealmId != null && source.RealmId.Length == data.RealmId.Length)
                Array.Copy(source.RealmId, data.RealmId, source.RealmId.Length);
            if (source.ProvinceId != null && source.ProvinceId.Length == data.ProvinceId.Length)
                Array.Copy(source.ProvinceId, data.ProvinceId, source.ProvinceId.Length);
            if (source.CountyId != null && source.CountyId.Length == data.CountyId.Length)
                Array.Copy(source.CountyId, data.CountyId, source.CountyId.Length);

            return data;
        }

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
                float s = 0.42f + (ColorMath.HashToUnitFloat(realmId + 3000) - 0.5f) * 0.16f;
                float v = 0.70f + (ColorMath.HashToUnitFloat(realmId + 4000) - 0.5f) * 0.16f;
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
                    Color = ColorMath.HsvToColor32(h, s, v),
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
                float h = ColorMath.HashToUnitFloat(pid * 7919);
                float s = 0.35f + ColorMath.HashToUnitFloat(pid + 5000) * 0.15f;
                float v = 0.65f + ColorMath.HashToUnitFloat(pid + 6000) * 0.15f;

                provinces.Add(new Province
                {
                    Id = pid,
                    Name = $"Province {pid}",
                    FullName = $"Province {pid}",
                    RealmId = realmId,
                    CenterCellId = centerCell,
                    CapitalBurgId = cells[centerCell].BurgId,
                    Color = ColorMath.HsvToColor32(h, s, v),
                    LabelPosition = cells[centerCell].Center,
                    CellIds = new List<int>(pCells)
                });
            }

            return provinces;
        }

        #endregion

        #region Counties

        static List<County> BuildCounties(List<Cell> cells, PoliticalData political)
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
            var catalog = LegacyExportOps.GetLegacyBiomeCatalog();
            var biomes = new List<Biome>(catalog.Count);
            foreach (var item in catalog)
            {
                biomes.Add(new Biome
                {
                    Id = item.Id,
                    Name = item.Name,
                    Color = new Color32(item.Color.R, item.Color.G, item.Color.B, item.Color.A),
                    Habitability = item.Habitability,
                    MovementCost = item.MovementCost
                });
            }

            return biomes;
        }

        #endregion
    }
}
