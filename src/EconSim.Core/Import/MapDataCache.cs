using System;
using System.Collections.Generic;
using System.IO;
using EconSim.Core.Common;
using EconSim.Core.Data;

namespace EconSim.Core.Import
{
    /// <summary>
    /// Caches MapData to disk in binary format for fast loading.
    /// Skips JSON parsing and conversion on subsequent loads.
    /// </summary>
    public static class MapDataCache
    {
        private const int CacheVersion = 1;

        /// <summary>
        /// Try to load MapData from cache.
        /// </summary>
        /// <param name="sourceFilePath">Original JSON file path (used for cache invalidation)</param>
        /// <param name="cacheDir">Directory to store cache files</param>
        /// <param name="mapData">Loaded MapData if successful</param>
        /// <returns>True if cache was loaded successfully</returns>
        public static bool TryLoad(string sourceFilePath, string cacheDir, out MapData mapData)
        {
            mapData = null;

            string cachePath = GetCachePath(sourceFilePath, cacheDir);
            if (!File.Exists(cachePath))
                return false;

            // Check if source file is newer than cache
            if (File.Exists(sourceFilePath))
            {
                var sourceTime = File.GetLastWriteTimeUtc(sourceFilePath);
                var cacheTime = File.GetLastWriteTimeUtc(cachePath);
                if (sourceTime > cacheTime)
                {
                    SimLog.Log("MapCache", "Source file newer than cache, rebuilding");
                    return false;
                }
            }

            try
            {
                using (var stream = File.OpenRead(cachePath))
                using (var reader = new BinaryReader(stream))
                {
                    // Verify version
                    int version = reader.ReadInt32();
                    if (version != CacheVersion)
                    {
                        SimLog.Log("MapCache", $"Cache version mismatch ({version} vs {CacheVersion}), rebuilding");
                        return false;
                    }

                    mapData = ReadMapData(reader);
                    mapData.BuildLookups();

                    SimLog.Log("MapCache", $"Loaded map from cache: {mapData.Info.Name}");
                    return true;
                }
            }
            catch (Exception ex)
            {
                SimLog.Log("MapCache", $"Failed to load cache: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Save MapData to cache.
        /// </summary>
        public static void Save(string sourceFilePath, string cacheDir, MapData mapData)
        {
            try
            {
                if (!Directory.Exists(cacheDir))
                    Directory.CreateDirectory(cacheDir);

                string cachePath = GetCachePath(sourceFilePath, cacheDir);

                using (var stream = File.Create(cachePath))
                using (var writer = new BinaryWriter(stream))
                {
                    writer.Write(CacheVersion);
                    WriteMapData(writer, mapData);
                }

                SimLog.Log("MapCache", $"Saved map to cache: {cachePath}");
            }
            catch (Exception ex)
            {
                SimLog.Log("MapCache", $"Failed to save cache: {ex.Message}");
            }
        }

        private static string GetCachePath(string sourceFilePath, string cacheDir)
        {
            string fileName = Path.GetFileNameWithoutExtension(sourceFilePath);
            return Path.Combine(cacheDir, $"{fileName}.mapdata");
        }

        #region Serialization

        private static void WriteMapData(BinaryWriter w, MapData m)
        {
            // Info
            WriteMapInfo(w, m.Info);

            // Vertices
            w.Write(m.Vertices.Count);
            foreach (var v in m.Vertices)
                WriteVec2(w, v);

            // Biomes
            w.Write(m.Biomes.Count);
            foreach (var b in m.Biomes)
                WriteBiome(w, b);

            // Cells
            w.Write(m.Cells.Count);
            foreach (var c in m.Cells)
                WriteCell(w, c);

            // States
            w.Write(m.States.Count);
            foreach (var s in m.States)
                WriteState(w, s);

            // Provinces
            w.Write(m.Provinces.Count);
            foreach (var p in m.Provinces)
                WriteProvince(w, p);

            // Rivers
            w.Write(m.Rivers.Count);
            foreach (var r in m.Rivers)
                WriteRiver(w, r);

            // Burgs
            w.Write(m.Burgs.Count);
            foreach (var b in m.Burgs)
                WriteBurg(w, b);

            // Features
            w.Write(m.Features.Count);
            foreach (var f in m.Features)
                WriteFeature(w, f);

            // Counties
            w.Write(m.Counties.Count);
            foreach (var c in m.Counties)
                WriteCounty(w, c);
        }

        private static MapData ReadMapData(BinaryReader r)
        {
            var m = new MapData();

            // Info
            m.Info = ReadMapInfo(r);

            // Vertices
            int vertexCount = r.ReadInt32();
            m.Vertices = new List<Vec2>(vertexCount);
            for (int i = 0; i < vertexCount; i++)
                m.Vertices.Add(ReadVec2(r));

            // Biomes
            int biomeCount = r.ReadInt32();
            m.Biomes = new List<Biome>(biomeCount);
            for (int i = 0; i < biomeCount; i++)
                m.Biomes.Add(ReadBiome(r));

            // Cells
            int cellCount = r.ReadInt32();
            m.Cells = new List<Cell>(cellCount);
            for (int i = 0; i < cellCount; i++)
                m.Cells.Add(ReadCell(r));

            // States
            int stateCount = r.ReadInt32();
            m.States = new List<State>(stateCount);
            for (int i = 0; i < stateCount; i++)
                m.States.Add(ReadState(r));

            // Provinces
            int provinceCount = r.ReadInt32();
            m.Provinces = new List<Province>(provinceCount);
            for (int i = 0; i < provinceCount; i++)
                m.Provinces.Add(ReadProvince(r));

            // Rivers
            int riverCount = r.ReadInt32();
            m.Rivers = new List<River>(riverCount);
            for (int i = 0; i < riverCount; i++)
                m.Rivers.Add(ReadRiver(r));

            // Burgs
            int burgCount = r.ReadInt32();
            m.Burgs = new List<Burg>(burgCount);
            for (int i = 0; i < burgCount; i++)
                m.Burgs.Add(ReadBurg(r));

            // Features
            int featureCount = r.ReadInt32();
            m.Features = new List<Feature>(featureCount);
            for (int i = 0; i < featureCount; i++)
                m.Features.Add(ReadFeature(r));

            // Counties
            int countyCount = r.ReadInt32();
            m.Counties = new List<County>(countyCount);
            for (int i = 0; i < countyCount; i++)
                m.Counties.Add(ReadCounty(r));

            return m;
        }

        // Primitives
        private static void WriteVec2(BinaryWriter w, Vec2 v)
        {
            w.Write(v.X);
            w.Write(v.Y);
        }

        private static Vec2 ReadVec2(BinaryReader r)
        {
            return new Vec2(r.ReadSingle(), r.ReadSingle());
        }

        private static void WriteColor32(BinaryWriter w, Color32 c)
        {
            w.Write(c.R);
            w.Write(c.G);
            w.Write(c.B);
            w.Write(c.A);
        }

        private static Color32 ReadColor32(BinaryReader r)
        {
            return new Color32(r.ReadByte(), r.ReadByte(), r.ReadByte(), r.ReadByte());
        }

        private static void WriteString(BinaryWriter w, string s)
        {
            w.Write(s ?? "");
        }

        private static string ReadString(BinaryReader r)
        {
            return r.ReadString();
        }

        private static void WriteIntList(BinaryWriter w, List<int> list)
        {
            if (list == null)
            {
                w.Write(0);
                return;
            }
            w.Write(list.Count);
            foreach (var i in list)
                w.Write(i);
        }

        private static List<int> ReadIntList(BinaryReader r)
        {
            int count = r.ReadInt32();
            var list = new List<int>(count);
            for (int i = 0; i < count; i++)
                list.Add(r.ReadInt32());
            return list;
        }

        // MapInfo
        private static void WriteMapInfo(BinaryWriter w, MapInfo info)
        {
            WriteString(w, info.Name);
            w.Write(info.Width);
            w.Write(info.Height);
            WriteString(w, info.Seed);
            w.Write(info.TotalCells);
            w.Write(info.LandCells);
            w.Write(info.SeaLevel);
        }

        private static MapInfo ReadMapInfo(BinaryReader r)
        {
            return new MapInfo
            {
                Name = ReadString(r),
                Width = r.ReadInt32(),
                Height = r.ReadInt32(),
                Seed = ReadString(r),
                TotalCells = r.ReadInt32(),
                LandCells = r.ReadInt32(),
                SeaLevel = r.ReadInt32()
            };
        }

        // Biome
        private static void WriteBiome(BinaryWriter w, Biome b)
        {
            w.Write(b.Id);
            WriteString(w, b.Name);
            WriteColor32(w, b.Color);
            w.Write(b.Habitability);
            w.Write(b.MovementCost);
        }

        private static Biome ReadBiome(BinaryReader r)
        {
            return new Biome
            {
                Id = r.ReadInt32(),
                Name = ReadString(r),
                Color = ReadColor32(r),
                Habitability = r.ReadInt32(),
                MovementCost = r.ReadInt32()
            };
        }

        // Cell
        private static void WriteCell(BinaryWriter w, Cell c)
        {
            w.Write(c.Id);
            WriteVec2(w, c.Center);
            WriteIntList(w, c.VertexIndices);
            WriteIntList(w, c.NeighborIds);
            w.Write(c.Height);
            w.Write(c.BiomeId);
            w.Write(c.IsLand);
            w.Write(c.CoastDistance);
            w.Write(c.FeatureId);
            w.Write(c.StateId);
            w.Write(c.ProvinceId);
            w.Write(c.CountyId);
            w.Write(c.BurgId);
            w.Write(c.RiverId);
            w.Write(c.RiverFlow);
            w.Write(c.Population);
            w.Write(c.CultureId);
            w.Write(c.ReligionId);
        }

        private static Cell ReadCell(BinaryReader r)
        {
            return new Cell
            {
                Id = r.ReadInt32(),
                Center = ReadVec2(r),
                VertexIndices = ReadIntList(r),
                NeighborIds = ReadIntList(r),
                Height = r.ReadInt32(),
                BiomeId = r.ReadInt32(),
                IsLand = r.ReadBoolean(),
                CoastDistance = r.ReadInt32(),
                FeatureId = r.ReadInt32(),
                StateId = r.ReadInt32(),
                ProvinceId = r.ReadInt32(),
                CountyId = r.ReadInt32(),
                BurgId = r.ReadInt32(),
                RiverId = r.ReadInt32(),
                RiverFlow = r.ReadInt32(),
                Population = r.ReadSingle(),
                CultureId = r.ReadInt32(),
                ReligionId = r.ReadInt32()
            };
        }

        // State
        private static void WriteState(BinaryWriter w, State s)
        {
            w.Write(s.Id);
            WriteString(w, s.Name);
            WriteString(w, s.FullName);
            WriteString(w, s.GovernmentForm);
            w.Write(s.CapitalBurgId);
            w.Write(s.CenterCellId);
            w.Write(s.CultureId);
            WriteColor32(w, s.Color);
            WriteVec2(w, s.LabelPosition);
            WriteIntList(w, s.ProvinceIds);
            WriteIntList(w, s.NeighborStateIds);
            w.Write(s.UrbanPopulation);
            w.Write(s.RuralPopulation);
            w.Write(s.TotalArea);
        }

        private static State ReadState(BinaryReader r)
        {
            return new State
            {
                Id = r.ReadInt32(),
                Name = ReadString(r),
                FullName = ReadString(r),
                GovernmentForm = ReadString(r),
                CapitalBurgId = r.ReadInt32(),
                CenterCellId = r.ReadInt32(),
                CultureId = r.ReadInt32(),
                Color = ReadColor32(r),
                LabelPosition = ReadVec2(r),
                ProvinceIds = ReadIntList(r),
                NeighborStateIds = ReadIntList(r),
                UrbanPopulation = r.ReadSingle(),
                RuralPopulation = r.ReadSingle(),
                TotalArea = r.ReadInt32()
            };
        }

        // Province
        private static void WriteProvince(BinaryWriter w, Province p)
        {
            w.Write(p.Id);
            WriteString(w, p.Name);
            WriteString(w, p.FullName);
            w.Write(p.StateId);
            w.Write(p.CenterCellId);
            w.Write(p.CapitalBurgId);
            WriteColor32(w, p.Color);
            WriteVec2(w, p.LabelPosition);
            WriteIntList(w, p.CellIds);
        }

        private static Province ReadProvince(BinaryReader r)
        {
            return new Province
            {
                Id = r.ReadInt32(),
                Name = ReadString(r),
                FullName = ReadString(r),
                StateId = r.ReadInt32(),
                CenterCellId = r.ReadInt32(),
                CapitalBurgId = r.ReadInt32(),
                Color = ReadColor32(r),
                LabelPosition = ReadVec2(r),
                CellIds = ReadIntList(r)
            };
        }

        // River
        private static void WriteRiver(BinaryWriter w, River river)
        {
            w.Write(river.Id);
            WriteString(w, river.Name);
            WriteString(w, river.Type);
            w.Write(river.SourceCellId);
            w.Write(river.MouthCellId);
            WriteIntList(w, river.CellPath);
            w.Write(river.Length);
            w.Write(river.Width);
            w.Write(river.Discharge);
            w.Write(river.ParentRiverId);
            w.Write(river.BasinId);
        }

        private static River ReadRiver(BinaryReader r)
        {
            return new River
            {
                Id = r.ReadInt32(),
                Name = ReadString(r),
                Type = ReadString(r),
                SourceCellId = r.ReadInt32(),
                MouthCellId = r.ReadInt32(),
                CellPath = ReadIntList(r),
                Length = r.ReadSingle(),
                Width = r.ReadSingle(),
                Discharge = r.ReadInt32(),
                ParentRiverId = r.ReadInt32(),
                BasinId = r.ReadInt32()
            };
        }

        // Burg
        private static void WriteBurg(BinaryWriter w, Burg b)
        {
            w.Write(b.Id);
            WriteString(w, b.Name);
            WriteVec2(w, b.Position);
            w.Write(b.CellId);
            w.Write(b.StateId);
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

        private static Burg ReadBurg(BinaryReader r)
        {
            return new Burg
            {
                Id = r.ReadInt32(),
                Name = ReadString(r),
                Position = ReadVec2(r),
                CellId = r.ReadInt32(),
                StateId = r.ReadInt32(),
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
            };
        }

        // Feature
        private static void WriteFeature(BinaryWriter w, Feature f)
        {
            w.Write(f.Id);
            WriteString(w, f.Type);
            w.Write(f.IsBorder);
            w.Write(f.CellCount);
        }

        private static Feature ReadFeature(BinaryReader r)
        {
            return new Feature
            {
                Id = r.ReadInt32(),
                Type = ReadString(r),
                IsBorder = r.ReadBoolean(),
                CellCount = r.ReadInt32()
            };
        }

        // County
        private static void WriteCounty(BinaryWriter w, County c)
        {
            w.Write(c.Id);
            WriteString(w, c.Name);
            w.Write(c.SeatCellId);
            WriteIntList(w, c.CellIds);
            w.Write(c.ProvinceId);
            w.Write(c.StateId);
            w.Write(c.TotalPopulation);
            WriteVec2(w, c.Centroid);
        }

        private static County ReadCounty(BinaryReader r)
        {
            var c = new County(r.ReadInt32());
            c.Name = ReadString(r);
            c.SeatCellId = r.ReadInt32();
            c.CellIds = ReadIntList(r);
            c.ProvinceId = r.ReadInt32();
            c.StateId = r.ReadInt32();
            c.TotalPopulation = r.ReadSingle();
            c.Centroid = ReadVec2(r);
            return c;
        }

        #endregion
    }
}
