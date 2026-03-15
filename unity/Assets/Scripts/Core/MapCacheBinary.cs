using System;
using System.Collections.Generic;
using System.IO;
using EconSim.Core.Common;
using EconSim.Core.Data;

namespace EconSim.Core
{
    /// <summary>
    /// Fast binary serialization for MapData cache.
    /// JSON deserialization of 67MB takes ~7s; binary takes ~0.2s.
    /// </summary>
    public static class MapCacheBinary
    {
        private const int FormatVersion = 1;
        private static readonly byte[] Magic = { 0x45, 0x43, 0x4D, 0x42 }; // "ECMB"

        public static void Write(BinaryWriter w, MapData map, int rootSeed, int mapGenSeed,
            int popGenSeed, int economySeed, int simulationSeed)
        {
            w.Write(Magic);
            w.Write(FormatVersion);

            // Generation seeds
            w.Write(rootSeed);
            w.Write(mapGenSeed);
            w.Write(popGenSeed);
            w.Write(economySeed);
            w.Write(simulationSeed);

            WriteMapInfo(w, map.Info);
            WriteCells(w, map.Cells);
            WriteVertices(w, map.Vertices);
            WriteProvinces(w, map.Provinces);
            WriteRealms(w, map.Realms);
            WriteRivers(w, map.Rivers);
            WriteBiomes(w, map.Biomes);
            WriteBurgs(w, map.Burgs);
            WriteFeatures(w, map.Features);
            WriteCounties(w, map.Counties);
            WriteCultures(w, map.Cultures);
            WriteReligions(w, map.Religions);
            WriteEdgeData(w, map);
        }

        public static MapData Read(BinaryReader r, out int rootSeed, out int mapGenSeed,
            out int popGenSeed, out int economySeed, out int simulationSeed)
        {
            byte[] magic = r.ReadBytes(4);
            if (magic.Length != 4 || magic[0] != Magic[0] || magic[1] != Magic[1] ||
                magic[2] != Magic[2] || magic[3] != Magic[3])
                throw new InvalidDataException("Invalid map cache binary magic");

            int version = r.ReadInt32();
            if (version != FormatVersion)
                throw new InvalidDataException($"Unsupported map cache binary version {version}");

            rootSeed = r.ReadInt32();
            mapGenSeed = r.ReadInt32();
            popGenSeed = r.ReadInt32();
            economySeed = r.ReadInt32();
            simulationSeed = r.ReadInt32();

            var map = new MapData();
            map.Info = ReadMapInfo(r);
            map.Cells = ReadCells(r);
            map.Vertices = ReadVertices(r);
            map.Provinces = ReadProvinces(r);
            map.Realms = ReadRealms(r);
            map.Rivers = ReadRivers(r);
            map.Biomes = ReadBiomes(r);
            map.Burgs = ReadBurgs(r);
            map.Features = ReadFeatures(r);
            map.Counties = ReadCounties(r);
            map.Cultures = ReadCultures(r);
            map.Religions = ReadReligions(r);
            ReadEdgeData(r, map);
            return map;
        }

        // --- MapInfo ---

        static void WriteMapInfo(BinaryWriter w, MapInfo info)
        {
            WriteString(w, info.Name);
            w.Write(info.Width);
            w.Write(info.Height);
            WriteString(w, info.Seed);
            w.Write(info.RootSeed);
            w.Write(info.MapGenSeed);
            w.Write(info.PopGenSeed);
            w.Write(info.EconomySeed);
            w.Write(info.SimulationSeed);
            w.Write(info.TotalCells);
            w.Write(info.LandCells);

            bool hasWorld = info.World != null;
            w.Write(hasWorld);
            if (hasWorld)
            {
                var world = info.World;
                w.Write(world.CellSizeKm);
                w.Write(world.MapWidthKm);
                w.Write(world.MapHeightKm);
                w.Write(world.MapAreaKm2);
                w.Write(world.LatitudeSouth);
                w.Write(world.LatitudeNorth);
                w.Write(world.MinHeight);
                w.Write(world.SeaLevelHeight);
                w.Write(world.MaxHeight);
                w.Write(world.MaxElevationMeters);
                w.Write(world.MaxSeaDepthMeters);
            }

            bool hasTrade = info.Trade != null;
            w.Write(hasTrade);
            if (hasTrade)
            {
                w.Write(info.Trade.OverseasSurcharge);
                w.Write(info.Trade.TradeVolumeScale);
                w.Write(info.Trade.NearestContinentHops);
                w.Write(info.Trade.ContinentNeighborCount);
            }
        }

        static MapInfo ReadMapInfo(BinaryReader r)
        {
            var info = new MapInfo();
            info.Name = ReadString(r);
            info.Width = r.ReadInt32();
            info.Height = r.ReadInt32();
            info.Seed = ReadString(r);
            info.RootSeed = r.ReadInt32();
            info.MapGenSeed = r.ReadInt32();
            info.PopGenSeed = r.ReadInt32();
            info.EconomySeed = r.ReadInt32();
            info.SimulationSeed = r.ReadInt32();
            info.TotalCells = r.ReadInt32();
            info.LandCells = r.ReadInt32();

            if (r.ReadBoolean())
            {
                info.World = new WorldInfo
                {
                    CellSizeKm = r.ReadSingle(),
                    MapWidthKm = r.ReadSingle(),
                    MapHeightKm = r.ReadSingle(),
                    MapAreaKm2 = r.ReadSingle(),
                    LatitudeSouth = r.ReadSingle(),
                    LatitudeNorth = r.ReadSingle(),
                    MinHeight = r.ReadSingle(),
                    SeaLevelHeight = r.ReadSingle(),
                    MaxHeight = r.ReadSingle(),
                    MaxElevationMeters = r.ReadSingle(),
                    MaxSeaDepthMeters = r.ReadSingle()
                };
            }

            if (r.ReadBoolean())
            {
                info.Trade = new GlobalTradeContext
                {
                    OverseasSurcharge = r.ReadSingle(),
                    TradeVolumeScale = r.ReadSingle(),
                    NearestContinentHops = r.ReadInt32(),
                    ContinentNeighborCount = r.ReadInt32()
                };
            }

            return info;
        }

        // --- Cells ---

        static void WriteCells(BinaryWriter w, List<Cell> cells)
        {
            int count = cells?.Count ?? 0;
            w.Write(count);
            for (int i = 0; i < count; i++)
            {
                var c = cells[i];
                w.Write(c.Id);
                w.Write(c.Center.X);
                w.Write(c.Center.Y);
                WriteIntList(w, c.VertexIndices);
                WriteIntList(w, c.NeighborIds);
                w.Write(c.SeaRelativeElevation);
                w.Write(c.HasSeaRelativeElevation);
                w.Write(c.BiomeId);
                w.Write(c.SoilId);
                w.Write(c.VegetationTypeId);
                w.Write(c.VegetationDensity);
                w.Write(c.RockId);
                w.Write(c.Temperature);
                w.Write(c.Precipitation);
                w.Write(c.MovementCost);
                w.Write(c.IsLand);
                w.Write(c.CoastDistance);
                w.Write(c.FeatureId);
                w.Write(c.RealmId);
                w.Write(c.ProvinceId);
                w.Write(c.BurgId);
                w.Write(c.CountyId);
                w.Write(c.Population);
                w.Write(c.CultureId);
                w.Write(c.ReligionId);
                w.Write(c.IsBoundary);
            }
        }

        static List<Cell> ReadCells(BinaryReader r)
        {
            int count = r.ReadInt32();
            var cells = new List<Cell>(count);
            for (int i = 0; i < count; i++)
            {
                cells.Add(new Cell
                {
                    Id = r.ReadInt32(),
                    Center = new Vec2(r.ReadSingle(), r.ReadSingle()),
                    VertexIndices = ReadIntList(r),
                    NeighborIds = ReadIntList(r),
                    SeaRelativeElevation = r.ReadSingle(),
                    HasSeaRelativeElevation = r.ReadBoolean(),
                    BiomeId = r.ReadInt32(),
                    SoilId = r.ReadInt32(),
                    VegetationTypeId = r.ReadInt32(),
                    VegetationDensity = r.ReadSingle(),
                    RockId = r.ReadInt32(),
                    Temperature = r.ReadSingle(),
                    Precipitation = r.ReadSingle(),
                    MovementCost = r.ReadSingle(),
                    IsLand = r.ReadBoolean(),
                    CoastDistance = r.ReadInt32(),
                    FeatureId = r.ReadInt32(),
                    RealmId = r.ReadInt32(),
                    ProvinceId = r.ReadInt32(),
                    BurgId = r.ReadInt32(),
                    CountyId = r.ReadInt32(),
                    Population = r.ReadSingle(),
                    CultureId = r.ReadInt32(),
                    ReligionId = r.ReadInt32(),
                    IsBoundary = r.ReadBoolean()
                });
            }
            return cells;
        }

        // --- Vertices ---

        static void WriteVertices(BinaryWriter w, List<Vec2> vertices)
        {
            int count = vertices?.Count ?? 0;
            w.Write(count);
            for (int i = 0; i < count; i++)
            {
                w.Write(vertices[i].X);
                w.Write(vertices[i].Y);
            }
        }

        static List<Vec2> ReadVertices(BinaryReader r)
        {
            int count = r.ReadInt32();
            var vertices = new List<Vec2>(count);
            for (int i = 0; i < count; i++)
                vertices.Add(new Vec2(r.ReadSingle(), r.ReadSingle()));
            return vertices;
        }

        // --- Provinces ---

        static void WriteProvinces(BinaryWriter w, List<Province> provinces)
        {
            int count = provinces?.Count ?? 0;
            w.Write(count);
            for (int i = 0; i < count; i++)
            {
                var p = provinces[i];
                w.Write(p.Id);
                WriteString(w, p.Name);
                WriteString(w, p.FullName);
                w.Write(p.RealmId);
                w.Write(p.CenterCellId);
                w.Write(p.CapitalBurgId);
                WriteColor32(w, p.Color);
                w.Write(p.LabelPosition.X);
                w.Write(p.LabelPosition.Y);
                WriteIntList(w, p.CellIds);
            }
        }

        static List<Province> ReadProvinces(BinaryReader r)
        {
            int count = r.ReadInt32();
            var provinces = new List<Province>(count);
            for (int i = 0; i < count; i++)
            {
                provinces.Add(new Province
                {
                    Id = r.ReadInt32(),
                    Name = ReadString(r),
                    FullName = ReadString(r),
                    RealmId = r.ReadInt32(),
                    CenterCellId = r.ReadInt32(),
                    CapitalBurgId = r.ReadInt32(),
                    Color = ReadColor32(r),
                    LabelPosition = new Vec2(r.ReadSingle(), r.ReadSingle()),
                    CellIds = ReadIntList(r)
                });
            }
            return provinces;
        }

        // --- Realms ---

        static void WriteRealms(BinaryWriter w, List<Realm> realms)
        {
            int count = realms?.Count ?? 0;
            w.Write(count);
            for (int i = 0; i < count; i++)
            {
                var realm = realms[i];
                w.Write(realm.Id);
                WriteString(w, realm.Name);
                WriteString(w, realm.FullName);
                WriteString(w, realm.GovernmentForm);
                w.Write(realm.CapitalBurgId);
                w.Write(realm.CenterCellId);
                w.Write(realm.CultureId);
                WriteColor32(w, realm.Color);
                w.Write(realm.LabelPosition.X);
                w.Write(realm.LabelPosition.Y);
                WriteIntList(w, realm.ProvinceIds);
                WriteIntList(w, realm.NeighborRealmIds);
                w.Write(realm.UrbanPopulation);
                w.Write(realm.RuralPopulation);
                w.Write(realm.TotalArea);
            }
        }

        static List<Realm> ReadRealms(BinaryReader r)
        {
            int count = r.ReadInt32();
            var realms = new List<Realm>(count);
            for (int i = 0; i < count; i++)
            {
                realms.Add(new Realm
                {
                    Id = r.ReadInt32(),
                    Name = ReadString(r),
                    FullName = ReadString(r),
                    GovernmentForm = ReadString(r),
                    CapitalBurgId = r.ReadInt32(),
                    CenterCellId = r.ReadInt32(),
                    CultureId = r.ReadInt32(),
                    Color = ReadColor32(r),
                    LabelPosition = new Vec2(r.ReadSingle(), r.ReadSingle()),
                    ProvinceIds = ReadIntList(r),
                    NeighborRealmIds = ReadIntList(r),
                    UrbanPopulation = r.ReadSingle(),
                    RuralPopulation = r.ReadSingle(),
                    TotalArea = r.ReadInt32()
                });
            }
            return realms;
        }

        // --- Rivers ---

        static void WriteRivers(BinaryWriter w, List<River> rivers)
        {
            int count = rivers?.Count ?? 0;
            w.Write(count);
            for (int i = 0; i < count; i++)
            {
                var river = rivers[i];
                w.Write(river.Id);
                WriteString(w, river.Name);
                WriteString(w, river.Type);
                w.Write(river.SourceCellId);
                w.Write(river.MouthCellId);
                WriteIntList(w, river.CellPath);
                WriteVec2List(w, river.Points);
                w.Write(river.Length);
                w.Write(river.Width);
                w.Write(river.Discharge);
                w.Write(river.ParentRiverId);
                w.Write(river.BasinId);
            }
        }

        static List<River> ReadRivers(BinaryReader r)
        {
            int count = r.ReadInt32();
            var rivers = new List<River>(count);
            for (int i = 0; i < count; i++)
            {
                rivers.Add(new River
                {
                    Id = r.ReadInt32(),
                    Name = ReadString(r),
                    Type = ReadString(r),
                    SourceCellId = r.ReadInt32(),
                    MouthCellId = r.ReadInt32(),
                    CellPath = ReadIntList(r),
                    Points = ReadVec2List(r),
                    Length = r.ReadSingle(),
                    Width = r.ReadSingle(),
                    Discharge = r.ReadInt32(),
                    ParentRiverId = r.ReadInt32(),
                    BasinId = r.ReadInt32()
                });
            }
            return rivers;
        }

        // --- Biomes ---

        static void WriteBiomes(BinaryWriter w, List<Biome> biomes)
        {
            int count = biomes?.Count ?? 0;
            w.Write(count);
            for (int i = 0; i < count; i++)
            {
                var b = biomes[i];
                w.Write(b.Id);
                WriteString(w, b.Name);
                WriteColor32(w, b.Color);
            }
        }

        static List<Biome> ReadBiomes(BinaryReader r)
        {
            int count = r.ReadInt32();
            var biomes = new List<Biome>(count);
            for (int i = 0; i < count; i++)
            {
                biomes.Add(new Biome
                {
                    Id = r.ReadInt32(),
                    Name = ReadString(r),
                    Color = ReadColor32(r)
                });
            }
            return biomes;
        }

        // --- Burgs ---

        static void WriteBurgs(BinaryWriter w, List<Burg> burgs)
        {
            int count = burgs?.Count ?? 0;
            w.Write(count);
            for (int i = 0; i < count; i++)
            {
                var b = burgs[i];
                w.Write(b.Id);
                WriteString(w, b.Name);
                w.Write(b.Position.X);
                w.Write(b.Position.Y);
                w.Write(b.CellId);
                w.Write(b.RealmId);
                w.Write(b.CultureId);
                w.Write(b.Population);
                w.Write(b.IsCapital);
                w.Write(b.IsPort);
                WriteString(w, b.Type);
                WriteString(w, b.Group);
                w.Write(b.HasCitadel);
                w.Write(b.HasPlaza);
                w.Write(b.HasWalls);
                w.Write(b.HasTemple);
            }
        }

        static List<Burg> ReadBurgs(BinaryReader r)
        {
            int count = r.ReadInt32();
            var burgs = new List<Burg>(count);
            for (int i = 0; i < count; i++)
            {
                burgs.Add(new Burg
                {
                    Id = r.ReadInt32(),
                    Name = ReadString(r),
                    Position = new Vec2(r.ReadSingle(), r.ReadSingle()),
                    CellId = r.ReadInt32(),
                    RealmId = r.ReadInt32(),
                    CultureId = r.ReadInt32(),
                    Population = r.ReadSingle(),
                    IsCapital = r.ReadBoolean(),
                    IsPort = r.ReadBoolean(),
                    Type = ReadString(r),
                    Group = ReadString(r),
                    HasCitadel = r.ReadBoolean(),
                    HasPlaza = r.ReadBoolean(),
                    HasWalls = r.ReadBoolean(),
                    HasTemple = r.ReadBoolean()
                });
            }
            return burgs;
        }

        // --- Features ---

        static void WriteFeatures(BinaryWriter w, List<Feature> features)
        {
            int count = features?.Count ?? 0;
            w.Write(count);
            for (int i = 0; i < count; i++)
            {
                var f = features[i];
                w.Write(f.Id);
                WriteString(w, f.Type);
                w.Write(f.IsBorder);
                w.Write(f.CellCount);
            }
        }

        static List<Feature> ReadFeatures(BinaryReader r)
        {
            int count = r.ReadInt32();
            var features = new List<Feature>(count);
            for (int i = 0; i < count; i++)
            {
                features.Add(new Feature
                {
                    Id = r.ReadInt32(),
                    Type = ReadString(r),
                    IsBorder = r.ReadBoolean(),
                    CellCount = r.ReadInt32()
                });
            }
            return features;
        }

        // --- Counties ---

        static void WriteCounties(BinaryWriter w, List<County> counties)
        {
            int count = counties?.Count ?? 0;
            w.Write(count);
            for (int i = 0; i < count; i++)
            {
                var c = counties[i];
                w.Write(c.Id);
                WriteString(w, c.Name);
                w.Write(c.SeatCellId);
                WriteIntList(w, c.CellIds);
                w.Write(c.ProvinceId);
                w.Write(c.RealmId);
                w.Write(c.TotalPopulation);
                w.Write(c.Centroid.X);
                w.Write(c.Centroid.Y);
            }
        }

        static List<County> ReadCounties(BinaryReader r)
        {
            int count = r.ReadInt32();
            var counties = new List<County>(count);
            for (int i = 0; i < count; i++)
            {
                counties.Add(new County
                {
                    Id = r.ReadInt32(),
                    Name = ReadString(r),
                    SeatCellId = r.ReadInt32(),
                    CellIds = ReadIntList(r),
                    ProvinceId = r.ReadInt32(),
                    RealmId = r.ReadInt32(),
                    TotalPopulation = r.ReadSingle(),
                    Centroid = new Vec2(r.ReadSingle(), r.ReadSingle())
                });
            }
            return counties;
        }

        // --- Cultures ---

        static void WriteCultures(BinaryWriter w, List<Culture> cultures)
        {
            int count = cultures?.Count ?? 0;
            w.Write(count);
            for (int i = 0; i < count; i++)
            {
                var c = cultures[i];
                w.Write(c.Id);
                WriteString(w, c.Name);
                WriteString(w, c.TypeName);
                WriteString(w, c.NodeId);
                w.Write(c.ReligionId);
                w.Write(c.SuccessionLaw);
                w.Write(c.GenderLaw);
            }
        }

        static List<Culture> ReadCultures(BinaryReader r)
        {
            int count = r.ReadInt32();
            var cultures = new List<Culture>(count);
            for (int i = 0; i < count; i++)
            {
                cultures.Add(new Culture
                {
                    Id = r.ReadInt32(),
                    Name = ReadString(r),
                    TypeName = ReadString(r),
                    NodeId = ReadString(r),
                    ReligionId = r.ReadInt32(),
                    SuccessionLaw = r.ReadInt32(),
                    GenderLaw = r.ReadInt32()
                });
            }
            return cultures;
        }

        // --- Religions ---

        static void WriteReligions(BinaryWriter w, List<Religion> religions)
        {
            int count = religions?.Count ?? 0;
            w.Write(count);
            for (int i = 0; i < count; i++)
            {
                var rel = religions[i];
                w.Write(rel.Id);
                WriteString(w, rel.Name);
                WriteString(w, rel.TypeName);
                w.Write(rel.SabbathDay);
                w.Write(rel.ParentId);
                w.Write(rel.Worldview);
                w.Write(rel.Celibacy);
                w.Write(rel.HolyWar);
            }
        }

        static List<Religion> ReadReligions(BinaryReader r)
        {
            int count = r.ReadInt32();
            var religions = new List<Religion>(count);
            for (int i = 0; i < count; i++)
            {
                religions.Add(new Religion
                {
                    Id = r.ReadInt32(),
                    Name = ReadString(r),
                    TypeName = ReadString(r),
                    SabbathDay = r.ReadInt32(),
                    ParentId = r.ReadInt32(),
                    Worldview = r.ReadInt32(),
                    Celibacy = r.ReadInt32(),
                    HolyWar = r.ReadInt32()
                });
            }
            return religions;
        }

        // --- Edge data ---

        static void WriteEdgeData(BinaryWriter w, MapData map)
        {
            w.Write(map.RiverFluxThreshold);
            w.Write(map.RiverTraceFluxThreshold);

            WriteIntList(w, map.EdgeRiverCell0);
            WriteIntList(w, map.EdgeRiverCell1);
            WriteFloatList(w, map.EdgeRiverFluxValues);
            WriteIntList(w, map.EdgeRiverV0);
            WriteIntList(w, map.EdgeRiverV1);
            WriteIntList(w, map.EdgeCoastCell0);
            WriteIntList(w, map.EdgeCoastCell1);
            WriteIntList(w, map.EdgeCoastV0);
            WriteIntList(w, map.EdgeCoastV1);
        }

        static void ReadEdgeData(BinaryReader r, MapData map)
        {
            map.RiverFluxThreshold = r.ReadSingle();
            map.RiverTraceFluxThreshold = r.ReadSingle();

            map.EdgeRiverCell0 = ReadIntList(r);
            map.EdgeRiverCell1 = ReadIntList(r);
            map.EdgeRiverFluxValues = ReadFloatList(r);
            map.EdgeRiverV0 = ReadIntList(r);
            map.EdgeRiverV1 = ReadIntList(r);
            map.EdgeCoastCell0 = ReadIntList(r);
            map.EdgeCoastCell1 = ReadIntList(r);
            map.EdgeCoastV0 = ReadIntList(r);
            map.EdgeCoastV1 = ReadIntList(r);
        }

        // --- Primitives ---

        static void WriteString(BinaryWriter w, string s)
        {
            if (s == null)
            {
                w.Write(-1);
                return;
            }
            w.Write(s.Length);
            w.Write(s.ToCharArray());
        }

        static string ReadString(BinaryReader r)
        {
            int len = r.ReadInt32();
            if (len < 0) return null;
            if (len == 0) return string.Empty;
            return new string(r.ReadChars(len));
        }

        static void WriteColor32(BinaryWriter w, Color32 c)
        {
            w.Write(c.R);
            w.Write(c.G);
            w.Write(c.B);
            w.Write(c.A);
        }

        static Color32 ReadColor32(BinaryReader r)
        {
            return new Color32(r.ReadByte(), r.ReadByte(), r.ReadByte(), r.ReadByte());
        }

        static void WriteIntList(BinaryWriter w, List<int> list)
        {
            int count = list?.Count ?? 0;
            w.Write(count);
            for (int i = 0; i < count; i++)
                w.Write(list[i]);
        }

        static List<int> ReadIntList(BinaryReader r)
        {
            int count = r.ReadInt32();
            var list = new List<int>(count);
            for (int i = 0; i < count; i++)
                list.Add(r.ReadInt32());
            return list;
        }

        static void WriteFloatList(BinaryWriter w, List<float> list)
        {
            int count = list?.Count ?? 0;
            w.Write(count);
            for (int i = 0; i < count; i++)
                w.Write(list[i]);
        }

        static List<float> ReadFloatList(BinaryReader r)
        {
            int count = r.ReadInt32();
            var list = new List<float>(count);
            for (int i = 0; i < count; i++)
                list.Add(r.ReadSingle());
            return list;
        }

        static void WriteVec2List(BinaryWriter w, List<Vec2> list)
        {
            int count = list?.Count ?? 0;
            w.Write(count);
            for (int i = 0; i < count; i++)
            {
                w.Write(list[i].X);
                w.Write(list[i].Y);
            }
        }

        static List<Vec2> ReadVec2List(BinaryReader r)
        {
            int count = r.ReadInt32();
            var list = new List<Vec2>(count);
            for (int i = 0; i < count; i++)
                list.Add(new Vec2(r.ReadSingle(), r.ReadSingle()));
            return list;
        }
    }
}
