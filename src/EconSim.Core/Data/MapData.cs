using System;
using System.Collections.Generic;
using EconSim.Core.Common;

namespace EconSim.Core.Data
{
    /// <summary>
    /// Processed map data ready for simulation and rendering.
    /// This is the authoritative representation after Azgaar import.
    /// </summary>
    [Serializable]
    public class MapData
    {
        public MapInfo Info;
        public List<Cell> Cells;
        public List<Vec2> Vertices;  // Voronoi vertex positions
        public List<Province> Provinces;
        public List<State> States;
        public List<River> Rivers;
        public List<Biome> Biomes;
        public List<Burg> Burgs;
        public List<Feature> Features;

        // Lookup tables (built after loading)
        [NonSerialized] public Dictionary<int, Cell> CellById;
        [NonSerialized] public Dictionary<int, Province> ProvinceById;
        [NonSerialized] public Dictionary<int, State> StateById;
        [NonSerialized] public Dictionary<int, Feature> FeatureById;

        public void BuildLookups()
        {
            CellById = new Dictionary<int, Cell>();
            foreach (var cell in Cells)
            {
                CellById[cell.Id] = cell;
            }

            ProvinceById = new Dictionary<int, Province>();
            foreach (var province in Provinces)
            {
                ProvinceById[province.Id] = province;
            }

            StateById = new Dictionary<int, State>();
            foreach (var state in States)
            {
                StateById[state.Id] = state;
            }

            FeatureById = new Dictionary<int, Feature>();
            if (Features != null)
            {
                foreach (var feature in Features)
                {
                    FeatureById[feature.Id] = feature;
                }
            }
        }
    }

    [Serializable]
    public class MapInfo
    {
        public string Name;
        public int Width;
        public int Height;
        public string Seed;
        public int TotalCells;
        public int LandCells;
        public float SeaLevel;  // Height value for sea level (typically 20)
    }

    /// <summary>
    /// A map cell (Voronoi polygon). This is the smallest unit of geography.
    /// In the simulation, this corresponds to a "county".
    /// </summary>
    [Serializable]
    public class Cell
    {
        public int Id;
        public Vec2 Center;             // Cell center position
        public List<int> VertexIndices; // Indices into MapData.Vertices
        public List<int> NeighborIds;   // Adjacent cell IDs

        // Terrain
        public int Height;              // 0-100, sea level = 20
        public int BiomeId;
        public bool IsLand;
        public int CoastDistance;       // + for land (dist to coast), - for water
        public int FeatureId;           // Index into Features (ocean, lake, island, etc.)

        // Political
        public int StateId;             // 0 = none/neutral
        public int ProvinceId;          // 0 = none
        public int BurgId;              // 0 = no settlement

        // Rivers
        public int RiverId;             // 0 = no river
        public int RiverFlow;           // Water flux

        // Economic (will be expanded)
        public float Population;
        public int CultureId;
        public int ReligionId;

        public bool HasRiver => RiverId > 0;
        public bool HasBurg => BurgId > 0;
    }

    [Serializable]
    public class Province
    {
        public int Id;
        public string Name;
        public string FullName;
        public int StateId;
        public int CenterCellId;
        public int CapitalBurgId;
        public Color32 Color;
        public Vec2 LabelPosition;
        public List<int> CellIds;       // Populated during conversion
    }

    [Serializable]
    public class State
    {
        public int Id;
        public string Name;
        public string FullName;
        public string GovernmentForm;
        public int CapitalBurgId;
        public int CenterCellId;
        public int CultureId;
        public Color32 Color;
        public Vec2 LabelPosition;
        public List<int> ProvinceIds;
        public List<int> NeighborStateIds;
        public float UrbanPopulation;
        public float RuralPopulation;
        public int TotalArea;
    }

    [Serializable]
    public class River
    {
        public int Id;
        public string Name;
        public string Type;             // "River", "Creek", etc.
        public int SourceCellId;
        public int MouthCellId;
        public List<int> CellPath;      // Ordered list of cell IDs from source to mouth
        public float Length;
        public float Width;
        public int Discharge;           // Water volume
        public int ParentRiverId;       // 0 = main river, >0 = tributary of
        public int BasinId;
    }

    [Serializable]
    public class Biome
    {
        public int Id;
        public string Name;
        public Color32 Color;
        public int Habitability;        // 0-100
        public int MovementCost;        // Travel difficulty
    }

    [Serializable]
    public class Burg
    {
        public int Id;
        public string Name;
        public Vec2 Position;
        public int CellId;
        public int StateId;
        public int CultureId;
        public float Population;
        public bool IsCapital;
        public bool IsPort;
        public string Type;             // "Naval", "Highland", etc.
        public string Group;            // "capital", "city", "town", etc.

        // Features
        public bool HasCitadel;
        public bool HasPlaza;
        public bool HasWalls;
        public bool HasTemple;
    }

    [Serializable]
    public class Feature
    {
        public int Id;
        public string Type;             // "ocean", "lake", "island", "sea", etc.
        public bool IsBorder;           // Touches map edge
        public int CellCount;

        public bool IsOcean => Type == "ocean" || Type == "sea";
        public bool IsLake => Type == "lake";
        public bool IsWater => IsOcean || IsLake;
    }
}
