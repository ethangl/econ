using System;
using System.Collections.Generic;
using EconSim.Core.Common;
using EconSim.Core.Data;
using MapGen.Core;
using PopGen.Core;
using MGVec2 = MapGen.Core.Vec2;
using ECVec2 = EconSim.Core.Common.Vec2;
using ECRiver = EconSim.Core.Data.River;

namespace EconSim.Core.Import
{
    /// <summary>
    /// Converts MapGen + PopGen output into EconSim MapData.
    /// </summary>
    public static class WorldGenImporter
    {
        /// <summary>
        /// Convert a MapGenResult into a fully populated MapData ready for simulation.
        /// </summary>
        public static MapData Convert(MapGenResult result)
        {
            return Convert(result, null);
        }

        /// <summary>
        /// Convert a MapGenResult into a fully populated MapData ready for simulation.
        /// Optionally stamps world-generation contract metadata onto MapInfo.
        /// </summary>
        public static MapData Convert(MapGenResult result, WorldGenerationContext? generationContext)
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
            bool hasSoilData = biomes.Soil != null && biomes.Soil.Length == cellCount;
            bool hasVegetationTypeData = biomes.Vegetation != null && biomes.Vegetation.Length == cellCount;
            bool hasVegetationDensityData = biomes.VegetationDensity != null && biomes.VegetationDensity.Length == cellCount;
            bool hasMovementCostData = biomes.MovementCost != null && biomes.MovementCost.Length == cellCount;
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
                    SoilId = hasSoilData ? (int)biomes.Soil[i] : 0,
                    VegetationTypeId = hasVegetationTypeData ? (int)biomes.Vegetation[i] : 0,
                    VegetationDensity = hasVegetationDensityData ? Clamp01(biomes.VegetationDensity[i]) : 0f,
                    MovementCost = hasMovementCostData ? biomes.MovementCost[i] : 0f,
                    IsLand = elevation.IsLand(i) && !biomes.IsLakeCell[i],
                    RealmId = political.RealmId[i],
                    ProvinceId = political.ProvinceId[i],
                    CountyId = political.CountyId[i],
                    Population = biomes.Population[i],
                    IsBoundary = mesh.CellIsBoundary[i]
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

            PopGenSeed popSeed = generationContext.HasValue
                ? new PopGenSeed(generationContext.Value.PopGenSeed)
                : PopGenSeed.Default;
            PopGenResult popResult = PopGenPipeline.Generate(result, new PopGenConfig(), popSeed);

            ApplyCellBurgIds(cells, popResult.CellBurgId);
            ApplyCellCultureIds(cells, popResult.Realms);
            ApplyCellReligionIds(cells, popResult.Realms, popResult.Cultures);
            var burgs = ConvertBurgs(popResult.Burgs);
            var realms = ConvertRealms(popResult.Realms);
            var provinces = ConvertProvinces(popResult.Provinces);
            var counties = ConvertCounties(popResult.Counties);
            var cultures = ConvertCultures(popResult.Cultures);
            var religions = ConvertReligions(popResult.Religions);
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
                Counties = counties,
                Cultures = cultures,
                Religions = religions
            };

            mapData.BuildLookups();
            mapData.AssertElevationInvariants();
            mapData.AssertWorldInvariants();

            if (generationContext.HasValue)
            {
                var context = generationContext.Value;
                mapData.Info.Seed = context.RootSeed.ToString();
                mapData.Info.RootSeed = context.RootSeed;
                mapData.Info.MapGenSeed = context.MapGenSeed;
                mapData.Info.PopGenSeed = context.PopGenSeed;
                mapData.Info.EconomySeed = context.EconomySeed;
                mapData.Info.SimulationSeed = context.SimulationSeed;
            }

            return mapData;
        }

        static ECVec2 ToECVec2(MGVec2 v) => new ECVec2(v.X, v.Y);
        static float Clamp01(float value) => value < 0f ? 0f : (value > 1f ? 1f : value);


        static void ApplyCellBurgIds(List<Cell> cells, int[] cellBurgId)
        {
            if (cellBurgId == null) return;
            int n = Math.Min(cells.Count, cellBurgId.Length);
            for (int i = 0; i < n; i++)
                cells[i].BurgId = cellBurgId[i];
        }

        static void ApplyCellCultureIds(List<Cell> cells, PopRealm[] realms)
        {
            if (realms == null || realms.Length == 0) return;
            var realmCulture = new Dictionary<int, int>(realms.Length);
            foreach (var r in realms)
                realmCulture[r.Id] = r.CultureId;

            foreach (var cell in cells)
            {
                if (cell.RealmId > 0 && realmCulture.TryGetValue(cell.RealmId, out int cid))
                    cell.CultureId = cid;
            }
        }

        static void ApplyCellReligionIds(List<Cell> cells, PopRealm[] realms, PopCulture[] cultures)
        {
            if (realms == null || realms.Length == 0 || cultures == null || cultures.Length == 0) return;

            var realmCulture = new Dictionary<int, int>(realms.Length);
            foreach (var r in realms)
                realmCulture[r.Id] = r.CultureId;

            var cultureReligion = new Dictionary<int, int>(cultures.Length);
            foreach (var c in cultures)
                cultureReligion[c.Id] = c.ReligionId;

            foreach (var cell in cells)
            {
                if (cell.RealmId > 0
                    && realmCulture.TryGetValue(cell.RealmId, out int cid)
                    && cultureReligion.TryGetValue(cid, out int rid))
                {
                    cell.ReligionId = rid;
                }
            }
        }

        static List<Culture> ConvertCultures(PopCulture[] source)
        {
            if (source == null || source.Length == 0)
                return new List<Culture>();

            var cultures = new List<Culture>(source.Length);
            for (int i = 0; i < source.Length; i++)
            {
                PopCulture c = source[i];
                cultures.Add(new Culture
                {
                    Id = c.Id,
                    Name = c.Name,
                    TypeName = c.TypeName ?? "Generic",
                    ReligionId = c.ReligionId
                });
            }

            return cultures;
        }

        static List<Religion> ConvertReligions(PopReligion[] source)
        {
            if (source == null || source.Length == 0)
                return new List<Religion>();

            var religions = new List<Religion>(source.Length);
            for (int i = 0; i < source.Length; i++)
            {
                PopReligion r = source[i];
                religions.Add(new Religion
                {
                    Id = r.Id,
                    Name = r.Name,
                    TypeName = r.TypeName ?? "Unknown"
                });
            }

            return religions;
        }

        static List<Burg> ConvertBurgs(PopBurg[] source)
        {
            if (source == null || source.Length == 0)
                return new List<Burg>();

            var burgs = new List<Burg>(source.Length);
            for (int i = 0; i < source.Length; i++)
            {
                PopBurg b = source[i];
                burgs.Add(new Burg
                {
                    Id = b.Id,
                    Name = b.Name,
                    Position = ToECVec2(b.Position),
                    CellId = b.CellId,
                    RealmId = b.RealmId,
                    CultureId = b.CultureId,
                    Population = b.Population,
                    IsCapital = b.IsCapital,
                    IsPort = b.IsPort,
                    Type = b.Type,
                    Group = b.Group
                });
            }

            return burgs;
        }

        static List<Province> ConvertProvinces(PopProvince[] source)
        {
            if (source == null || source.Length == 0)
                return new List<Province>();

            var provinces = new List<Province>(source.Length);
            for (int i = 0; i < source.Length; i++)
            {
                PopProvince p = source[i];
                provinces.Add(new Province
                {
                    Id = p.Id,
                    Name = p.Name,
                    FullName = p.FullName,
                    RealmId = p.RealmId,
                    CenterCellId = p.CenterCellId,
                    CapitalBurgId = p.CapitalBurgId,
                    Color = ToECColor32(p.Color),
                    LabelPosition = ToECVec2(p.LabelPosition),
                    CellIds = p.CellIds != null ? new List<int>(p.CellIds) : new List<int>()
                });
            }

            return provinces;
        }

        static List<Realm> ConvertRealms(PopRealm[] source)
        {
            if (source == null || source.Length == 0)
                return new List<Realm>();

            var realms = new List<Realm>(source.Length);
            for (int i = 0; i < source.Length; i++)
            {
                PopRealm r = source[i];
                realms.Add(new Realm
                {
                    Id = r.Id,
                    Name = r.Name,
                    FullName = r.FullName,
                    GovernmentForm = r.GovernmentForm,
                    CapitalBurgId = r.CapitalBurgId,
                    CenterCellId = r.CenterCellId,
                    CultureId = r.CultureId,
                    Color = ToECColor32(r.Color),
                    LabelPosition = ToECVec2(r.LabelPosition),
                    ProvinceIds = r.ProvinceIds != null ? new List<int>(r.ProvinceIds) : new List<int>(),
                    NeighborRealmIds = r.NeighborRealmIds != null ? new List<int>(r.NeighborRealmIds) : new List<int>(),
                    UrbanPopulation = r.UrbanPopulation,
                    RuralPopulation = r.RuralPopulation,
                    TotalArea = r.TotalArea
                });
            }

            return realms;
        }

        static List<County> ConvertCounties(PopCounty[] source)
        {
            if (source == null || source.Length == 0)
                return new List<County>();

            var counties = new List<County>(source.Length);
            for (int i = 0; i < source.Length; i++)
            {
                PopCounty c = source[i];
                counties.Add(new County
                {
                    Id = c.Id,
                    Name = c.Name,
                    SeatCellId = c.SeatCellId,
                    CellIds = c.CellIds != null ? new List<int>(c.CellIds) : new List<int>(),
                    ProvinceId = c.ProvinceId,
                    RealmId = c.RealmId,
                    TotalPopulation = c.TotalPopulation,
                    Centroid = ToECVec2(c.Centroid)
                });
            }

            return counties;
        }

        static Color32 ToECColor32(PopColor32 color) => new Color32(color.R, color.G, color.B, color.A);

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
