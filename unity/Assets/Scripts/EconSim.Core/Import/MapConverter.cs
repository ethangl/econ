using System.Collections.Generic;
using System.Globalization;
using EconSim.Core.Common;
using EconSim.Core.Data;

namespace EconSim.Core.Import
{
    /// <summary>
    /// Converts parsed Azgaar data into simulation-ready MapData structures.
    /// </summary>
    public static class MapConverter
    {
        private const int SEA_LEVEL = 20;

        /// <summary>
        /// Convert an AzgaarMap to MapData for use in simulation and rendering.
        /// </summary>
        public static MapData Convert(AzgaarMap azgaar)
        {
            var mapData = new MapData
            {
                Info = ConvertInfo(azgaar),
                Vertices = ConvertVertices(azgaar.pack.vertices),
                Biomes = ConvertBiomes(azgaar.biomesData),
                Cells = ConvertCells(azgaar.pack.cells),
                States = ConvertStates(azgaar.pack.states),
                Provinces = ConvertProvinces(azgaar.pack.provinces),
                Rivers = ConvertRivers(azgaar.pack.rivers),
                Burgs = ConvertBurgs(azgaar.pack.burgs)
            };

            // Build lookup tables
            mapData.BuildLookups();

            // Populate province cell lists
            PopulateProvinceCells(mapData);

            return mapData;
        }

        private static MapInfo ConvertInfo(AzgaarMap azgaar)
        {
            int landCells = 0;
            foreach (var cell in azgaar.pack.cells)
            {
                if (cell.h >= SEA_LEVEL) landCells++;
            }

            return new MapInfo
            {
                Name = azgaar.info.mapName,
                Width = azgaar.info.width,
                Height = azgaar.info.height,
                Seed = azgaar.info.seed,
                TotalCells = azgaar.pack.cells.Count,
                LandCells = landCells,
                SeaLevel = SEA_LEVEL
            };
        }

        private static List<Vec2> ConvertVertices(List<AzgaarVertex> azgaarVertices)
        {
            var vertices = new List<Vec2>(azgaarVertices.Count);
            foreach (var v in azgaarVertices)
            {
                vertices.Add(v.Position);
            }
            return vertices;
        }

        private static List<Biome> ConvertBiomes(AzgaarBiomesData biomesData)
        {
            var biomes = new List<Biome>();
            if (biomesData?.i == null) return biomes;

            for (int idx = 0; idx < biomesData.i.Count; idx++)
            {
                var biome = new Biome
                {
                    Id = biomesData.i[idx],
                    Name = idx < biomesData.name.Count ? biomesData.name[idx] : "Unknown",
                    Color = idx < biomesData.color.Count
                        ? ParseColor(biomesData.color[idx])
                        : new Color32(128, 128, 128, 255),
                    Habitability = idx < biomesData.habitability.Count
                        ? biomesData.habitability[idx]
                        : 50,
                    MovementCost = idx < biomesData.cost.Count
                        ? biomesData.cost[idx]
                        : 100
                };
                biomes.Add(biome);
            }
            return biomes;
        }

        private static List<Cell> ConvertCells(List<AzgaarCell> azgaarCells)
        {
            var cells = new List<Cell>(azgaarCells.Count);
            foreach (var ac in azgaarCells)
            {
                var cell = new Cell
                {
                    Id = ac.i,
                    Center = ac.Position,
                    VertexIndices = ac.v ?? new List<int>(),
                    NeighborIds = ac.c ?? new List<int>(),
                    Height = ac.h,
                    BiomeId = ac.biome,
                    IsLand = ac.h >= SEA_LEVEL,
                    CoastDistance = ac.t,
                    StateId = ac.state,
                    ProvinceId = ac.province,
                    BurgId = ac.burg,
                    RiverId = ac.r,
                    RiverFlow = ac.fl,
                    Population = ac.pop,
                    CultureId = ac.culture,
                    ReligionId = ac.religion
                };
                cells.Add(cell);
            }
            return cells;
        }

        private static List<State> ConvertStates(List<AzgaarState> azgaarStates)
        {
            var states = new List<State>();
            foreach (var astate in azgaarStates)
            {
                // Skip null/neutral state (index 0 is often "Neutrals")
                if (string.IsNullOrEmpty(astate.name) && astate.i == 0) continue;

                var state = new State
                {
                    Id = astate.i,
                    Name = astate.name ?? "",
                    FullName = astate.fullName ?? astate.name ?? "",
                    GovernmentForm = astate.form ?? "",
                    CapitalBurgId = astate.capital,
                    CenterCellId = astate.center,
                    CultureId = astate.culture,
                    Color = ParseColor(astate.color),
                    LabelPosition = astate.PolePosition,
                    ProvinceIds = astate.provinces ?? new List<int>(),
                    NeighborStateIds = astate.neighbors ?? new List<int>(),
                    UrbanPopulation = astate.urban,
                    RuralPopulation = astate.rural,
                    TotalArea = astate.area
                };
                states.Add(state);
            }
            return states;
        }

        private static List<Province> ConvertProvinces(List<AzgaarProvince> azgaarProvinces)
        {
            var provinces = new List<Province>();
            foreach (var ap in azgaarProvinces)
            {
                // Skip empty provinces
                if (string.IsNullOrEmpty(ap.name) && ap.i == 0) continue;

                var province = new Province
                {
                    Id = ap.i,
                    Name = ap.name ?? "",
                    FullName = ap.fullName ?? ap.name ?? "",
                    StateId = ap.state,
                    CenterCellId = ap.center,
                    CapitalBurgId = ap.burg,
                    Color = ParseColor(ap.color),
                    LabelPosition = ap.PolePosition,
                    CellIds = new List<int>() // Will be populated later
                };
                provinces.Add(province);
            }
            return provinces;
        }

        private static List<River> ConvertRivers(List<AzgaarRiver> azgaarRivers)
        {
            var rivers = new List<River>();
            foreach (var ar in azgaarRivers)
            {
                var river = new River
                {
                    Id = ar.i,
                    Name = ar.name ?? "",
                    Type = ar.type ?? "River",
                    SourceCellId = ar.source,
                    MouthCellId = ar.mouth,
                    CellPath = ar.cells ?? new List<int>(),
                    Length = ar.length,
                    Width = ar.width,
                    Discharge = ar.discharge,
                    ParentRiverId = ar.parent,
                    BasinId = ar.basin
                };
                rivers.Add(river);
            }
            return rivers;
        }

        private static List<Burg> ConvertBurgs(List<AzgaarBurg> azgaarBurgs)
        {
            var burgs = new List<Burg>();
            foreach (var ab in azgaarBurgs)
            {
                // Skip null/empty burgs (index 0 is often empty)
                if (string.IsNullOrEmpty(ab.name) && ab.i == 0) continue;

                var burg = new Burg
                {
                    Id = ab.i,
                    Name = ab.name ?? "",
                    Position = ab.Position,
                    CellId = ab.cell,
                    StateId = ab.state,
                    CultureId = ab.culture,
                    Population = ab.population,
                    IsCapital = ab.IsCapital,
                    IsPort = ab.IsPort,
                    Type = ab.type ?? "",
                    Group = ab.group ?? "",
                    HasCitadel = ab.citadel == 1,
                    HasPlaza = ab.plaza == 1,
                    HasWalls = ab.walls == 1,
                    HasTemple = ab.temple == 1
                };
                burgs.Add(burg);
            }
            return burgs;
        }

        /// <summary>
        /// Build the list of cells belonging to each province.
        /// </summary>
        private static void PopulateProvinceCells(MapData mapData)
        {
            // Create lookup from province ID to province
            var provinceById = new Dictionary<int, Province>();
            foreach (var p in mapData.Provinces)
            {
                provinceById[p.Id] = p;
            }

            // Scan all cells and add them to their province
            foreach (var cell in mapData.Cells)
            {
                if (cell.ProvinceId > 0 && provinceById.TryGetValue(cell.ProvinceId, out var province))
                {
                    province.CellIds.Add(cell.Id);
                }
            }
        }

        /// <summary>
        /// Parse a hex color string (#RRGGBB or #RGB) to Color32.
        /// </summary>
        private static Color32 ParseColor(string hex)
        {
            if (string.IsNullOrEmpty(hex))
                return new Color32(128, 128, 128, 255);

            hex = hex.TrimStart('#');

            if (hex.Length == 3)
            {
                // #RGB -> #RRGGBB
                hex = $"{hex[0]}{hex[0]}{hex[1]}{hex[1]}{hex[2]}{hex[2]}";
            }

            if (hex.Length != 6)
                return new Color32(128, 128, 128, 255);

            byte r = byte.Parse(hex.Substring(0, 2), NumberStyles.HexNumber);
            byte g = byte.Parse(hex.Substring(2, 2), NumberStyles.HexNumber);
            byte b = byte.Parse(hex.Substring(4, 2), NumberStyles.HexNumber);

            return new Color32(r, g, b, 255);
        }
    }
}
