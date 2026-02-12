using System;
using System.Collections.Generic;
using EconSim.Core.Common;

namespace EconSim.Core.Data
{
    /// <summary>
    /// Processed map data ready for simulation and rendering.
    /// This is the authoritative representation after MapGen conversion.
    /// </summary>
    [Serializable]
    public class MapData
    {
        public MapInfo Info;
        public List<Cell> Cells;
        public List<Vec2> Vertices;  // Voronoi vertex positions
        public List<Province> Provinces;
        public List<Realm> Realms;
        public List<River> Rivers;
        public List<Biome> Biomes;
        public List<Burg> Burgs;
        public List<Feature> Features;
        public List<County> Counties;    // Economic unit groupings assigned during map generation

        // Lookup tables (built after loading)
        [NonSerialized] public Dictionary<int, Cell> CellById;
        [NonSerialized] public Dictionary<int, Province> ProvinceById;
        [NonSerialized] public Dictionary<int, Realm> RealmById;
        [NonSerialized] public Dictionary<int, Feature> FeatureById;
        [NonSerialized] public Dictionary<int, County> CountyById;

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

            RealmById = new Dictionary<int, Realm>();
            foreach (var realm in Realms)
            {
                RealmById[realm.Id] = realm;
            }

            FeatureById = new Dictionary<int, Feature>();
            if (Features != null)
            {
                foreach (var feature in Features)
                {
                    FeatureById[feature.Id] = feature;
                }
            }

            CountyById = new Dictionary<int, County>();
            if (Counties != null)
            {
                foreach (var county in Counties)
                {
                    CountyById[county.Id] = county;
                }
            }
        }

        /// <summary>
        /// Validate elevation invariants across all cells.
        /// Throws if any cell violates canonical or absolute-height constraints.
        /// </summary>
        public void AssertElevationInvariants()
        {
            if (Cells == null)
                throw new InvalidOperationException("MapData.Cells is null.");

            for (int i = 0; i < Cells.Count; i++)
            {
                Cell cell = Cells[i];
                if (cell == null)
                    throw new InvalidOperationException($"MapData.Cells[{i}] is null.");

                if (!cell.HasSeaRelativeElevation)
                {
                    throw new InvalidOperationException(
                        $"Cell {cell.Id} is missing canonical SeaRelativeElevation.");
                }

                float absoluteHeight = Elevation.GetAbsoluteHeight(cell, Info);
                Elevation.AssertAbsoluteHeightInRange(absoluteHeight, Info, $"cell {cell.Id}");
            }
        }

        /// <summary>
        /// Validate world-scale metadata invariants.
        /// </summary>
        public void AssertWorldInvariants()
        {
            if (Info == null)
                throw new InvalidOperationException("MapData.Info is null.");
            if (Info.World == null)
                throw new InvalidOperationException("MapData.Info.World is null.");

            WorldInfo world = Info.World;
            if (float.IsNaN(world.MapAreaKm2) || float.IsInfinity(world.MapAreaKm2) || world.MapAreaKm2 <= 0f)
                throw new InvalidOperationException("World.MapAreaKm2 must be > 0.");
            if (float.IsNaN(world.CellSizeKm) || float.IsInfinity(world.CellSizeKm) || world.CellSizeKm <= 0f)
                throw new InvalidOperationException("World.CellSizeKm must be > 0.");
            if (float.IsNaN(world.MapWidthKm) || float.IsInfinity(world.MapWidthKm) || world.MapWidthKm <= 0f ||
                float.IsNaN(world.MapHeightKm) || float.IsInfinity(world.MapHeightKm) || world.MapHeightKm <= 0f)
                throw new InvalidOperationException("World map dimensions must be > 0.");
            if (float.IsNaN(world.MaxElevationMeters) || float.IsInfinity(world.MaxElevationMeters) || world.MaxElevationMeters <= 0f)
                throw new InvalidOperationException("World.MaxElevationMeters must be > 0.");
            if (float.IsNaN(world.MaxSeaDepthMeters) || float.IsInfinity(world.MaxSeaDepthMeters) || world.MaxSeaDepthMeters <= 0f)
                throw new InvalidOperationException("World.MaxSeaDepthMeters must be > 0.");
            if (float.IsNaN(world.LatitudeNorth) || float.IsInfinity(world.LatitudeNorth) ||
                float.IsNaN(world.LatitudeSouth) || float.IsInfinity(world.LatitudeSouth) ||
                world.LatitudeNorth <= world.LatitudeSouth)
                throw new InvalidOperationException("World latitude span must be increasing (north > south).");
            if (float.IsNaN(world.MinHeight) || float.IsInfinity(world.MinHeight) ||
                float.IsNaN(world.SeaLevelHeight) || float.IsInfinity(world.SeaLevelHeight) ||
                float.IsNaN(world.MaxHeight) || float.IsInfinity(world.MaxHeight))
            {
                throw new InvalidOperationException("World height anchors must be finite.");
            }
            if (world.MinHeight >= world.SeaLevelHeight || world.SeaLevelHeight >= world.MaxHeight)
                throw new InvalidOperationException("World height anchors must satisfy MinHeight < SeaLevelHeight < MaxHeight.");

            float landRange = world.MaxHeight - world.SeaLevelHeight;
            float waterRange = world.SeaLevelHeight - world.MinHeight;
            if (Math.Abs(landRange - world.MaxElevationMeters) > 0.001f)
            {
                throw new InvalidOperationException(
                    $"World.MaxHeight-SeaLevelHeight must equal MaxElevationMeters. Got landRange={landRange}, maxElevation={world.MaxElevationMeters}.");
            }
            if (Math.Abs(waterRange - world.MaxSeaDepthMeters) > 0.001f)
            {
                throw new InvalidOperationException(
                    $"World.SeaLevelHeight-MinHeight must equal MaxSeaDepthMeters. Got waterRange={waterRange}, maxSeaDepth={world.MaxSeaDepthMeters}.");
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
        public WorldInfo World;
    }

    [Serializable]
    public class WorldInfo
    {
        public float CellSizeKm;
        public float MapWidthKm;
        public float MapHeightKm;
        public float MapAreaKm2;
        public float LatitudeSouth;
        public float LatitudeNorth;
        public float MinHeight;
        public float SeaLevelHeight;
        public float MaxHeight;
        public float MaxElevationMeters;
        public float MaxSeaDepthMeters;
    }

    /// <summary>
    /// A map cell (Voronoi polygon). This is the smallest unit of geography.
    /// Multiple cells form a County, which is the unit of economic simulation.
    /// </summary>
    [Serializable]
    public class Cell
    {
        public int Id;
        public Vec2 Center;             // Cell center position
        public List<int> VertexIndices; // Indices into MapData.Vertices
        public List<int> NeighborIds;   // Adjacent cell IDs

        // Terrain
        public float SeaRelativeElevation; // Canonical signed elevation meters (sea level = 0)
        public bool HasSeaRelativeElevation; // Must be true for runtime-generated maps
        public int BiomeId;
        public int SoilId;             // MapGen.Core.SoilType ordinal (0-7)
        public bool IsLand;
        public int CoastDistance;       // + for land (dist to coast), - for water
        public int FeatureId;           // Index into Features (ocean, lake, island, etc.)

        // Political
        public int RealmId;             // 0 = none/neutral
        public int ProvinceId;          // 0 = none
        public int BurgId;              // 0 = no settlement
        public int CountyId;            // County this cell belongs to (assigned during map generation)

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

    /// <summary>
    /// A county groups multiple cells into a single economic unit.
    /// High-density areas (cities) are single-cell counties; sparse rural areas consolidate.
    /// </summary>
    [Serializable]
    public class County
    {
        public int Id;
        public string Name;             // From seat burg name or "County {Id}"
        public int SeatCellId;          // County seat (burg cell or highest-pop cell)
        public List<int> CellIds;       // All cells in this county
        public int ProvinceId;          // Province this county belongs to
        public int RealmId;             // Realm this county belongs to
        public float TotalPopulation;   // Sum of population from all cells
        public Vec2 Centroid;           // Center of mass (population-weighted or geometric)

        public County()
        {
            CellIds = new List<int>();
        }

        public County(int id) : this()
        {
            Id = id;
        }

        /// <summary>Number of cells in this county.</summary>
        public int CellCount => CellIds?.Count ?? 0;
    }

    [Serializable]
    public class Province
    {
        public int Id;
        public string Name;
        public string FullName;
        public int RealmId;
        public int CenterCellId;
        public int CapitalBurgId;
        public Color32 Color;
        public Vec2 LabelPosition;
        public List<int> CellIds;       // Populated during conversion
    }

    [Serializable]
    public class Realm
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
        public List<int> NeighborRealmIds;
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
        public List<Vec2> Points;       // Vertex positions source→mouth (for edge-based rendering)
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
        public int RealmId;
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

    /// <summary>
    /// Elevation helpers for canonical signed meters (sea level = 0) and world-height anchors.
    /// </summary>
    public static class Elevation
    {
        public static float ResolveSeaLevel(MapInfo info)
        {
            WorldInfo world = RequireWorldInfo(info, "ResolveSeaLevel");
            float seaLevel = world.SeaLevelHeight;
            if (float.IsNaN(seaLevel) || float.IsInfinity(seaLevel))
                throw new InvalidOperationException($"World.SeaLevelHeight must be finite, got {seaLevel}.");

            return seaLevel;
        }

        public static float ResolveMinHeight(MapInfo info)
        {
            WorldInfo world = RequireWorldInfo(info, "ResolveMinHeight");
            if (float.IsNaN(world.MinHeight) || float.IsInfinity(world.MinHeight))
                throw new InvalidOperationException($"World.MinHeight must be finite, got {world.MinHeight}.");
            return world.MinHeight;
        }

        public static float ResolveMaxHeight(MapInfo info)
        {
            WorldInfo world = RequireWorldInfo(info, "ResolveMaxHeight");
            if (float.IsNaN(world.MaxHeight) || float.IsInfinity(world.MaxHeight))
                throw new InvalidOperationException($"World.MaxHeight must be finite, got {world.MaxHeight}.");
            return world.MaxHeight;
        }

        static void ValidateFinite(float value, string context)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                throw new InvalidOperationException($"{context} must be finite, got {value}.");
        }

        static float ResolveWorldSpan(MapInfo info)
        {
            float min = ResolveMinHeight(info);
            float max = ResolveMaxHeight(info);
            if (max <= min)
                throw new InvalidOperationException($"World height anchors must satisfy MinHeight < MaxHeight, got [{min}, {max}].");
            return max - min;
        }

        public static float AbsoluteToSignedMeters(float absoluteHeight, MapInfo info)
        {
            AssertAbsoluteHeightInRange(absoluteHeight, info, "AbsoluteToSignedMeters input");
            return absoluteHeight - ResolveSeaLevel(info);
        }

        public static float SignedMetersToAbsolute(float signedMeters, MapInfo info)
        {
            ValidateFinite(signedMeters, "SignedMetersToAbsolute input");
            float absolute = ResolveSeaLevel(info) + signedMeters;
            AssertAbsoluteHeightInRange(absolute, info, "SignedMetersToAbsolute output");
            return absolute;
        }

        public static void AssertSignedMetersInRange(float signedMeters, MapInfo info, string context)
        {
            ValidateFinite(signedMeters, $"{context} signed elevation");
            float maxElevationMeters = ResolveMaxElevationMeters(info);
            float maxSeaDepthMeters = ResolveMaxSeaDepthMeters(info);
            const float tolerance = 0.001f;
            if (signedMeters < -maxSeaDepthMeters - tolerance || signedMeters > maxElevationMeters + tolerance)
            {
                throw new InvalidOperationException(
                    $"Signed elevation {signedMeters} is out of configured range [-{maxSeaDepthMeters}, {maxElevationMeters}] for {context}.");
            }
        }

        public static void AssertAbsoluteHeightInRange(float absoluteHeight, MapInfo info, string context)
        {
            ValidateFinite(absoluteHeight, $"{context} absolute elevation");
            float min = ResolveMinHeight(info);
            float max = ResolveMaxHeight(info);
            if (absoluteHeight < min || absoluteHeight > max)
            {
                throw new InvalidOperationException(
                    $"Absolute elevation {absoluteHeight} is out of world range [{min}, {max}] for {context}.");
            }
        }

        public static float NormalizeAbsolute01(float absoluteHeight, MapInfo info)
        {
            AssertAbsoluteHeightInRange(absoluteHeight, info, "NormalizeAbsolute01 input");
            float min = ResolveMinHeight(info);
            float span = ResolveWorldSpan(info);
            return (absoluteHeight - min) / span;
        }

        public static float SeaRelativeFromAbsolute(float absoluteHeight, float seaLevel)
        {
            ValidateFinite(absoluteHeight, "SeaRelativeFromAbsolute absoluteHeight");
            ValidateFinite(seaLevel, "SeaRelativeFromAbsolute seaLevel");
            return absoluteHeight - seaLevel;
        }

        public static float AbsoluteFromSeaRelative(float seaRelativeHeight, float seaLevel)
        {
            ValidateFinite(seaRelativeHeight, "AbsoluteFromSeaRelative seaRelativeHeight");
            ValidateFinite(seaLevel, "AbsoluteFromSeaRelative seaLevel");
            return seaRelativeHeight + seaLevel;
        }

        public static float GetSeaRelativeHeight(Cell cell, MapInfo info)
        {
            if (cell == null)
                throw new InvalidOperationException("GetSeaRelativeHeight requires a non-null cell.");
            if (!cell.HasSeaRelativeElevation)
                throw new InvalidOperationException($"Cell {cell.Id} is missing canonical SeaRelativeElevation.");

            AssertSignedMetersInRange(cell.SeaRelativeElevation, info, $"cell {cell.Id} (canonical)");
            return cell.SeaRelativeElevation;
        }

        public static float GetAbsoluteHeight(Cell cell, MapInfo info)
        {
            if (cell == null)
                throw new InvalidOperationException("GetAbsoluteHeight requires a non-null cell.");
            if (!cell.HasSeaRelativeElevation)
                throw new InvalidOperationException($"Cell {cell.Id} is missing canonical SeaRelativeElevation.");

            float absolute = SignedMetersToAbsolute(GetSeaRelativeHeight(cell, info), info);
            return absolute;
        }

        /// <summary>
        /// Convert a cell's canonical elevation to meters above sea level (non-negative).
        /// </summary>
        public static float GetMetersAboveSeaLevel(Cell cell, MapInfo info)
        {
            return Math.Max(0f, GetSignedMeters(cell, info));
        }

        /// <summary>
        /// Convert a cell's canonical elevation to signed meters relative to sea level.
        /// </summary>
        public static float GetSignedMeters(Cell cell, MapInfo info)
        {
            return GetSeaRelativeHeight(cell, info);
        }

        /// <summary>
        /// Normalize signed cell elevation by max elevation meters for terrain displacement.
        /// Land is [0..1], water is negative down to -maxDepth/maxElevation.
        /// </summary>
        public static float GetNormalizedSignedHeight(Cell cell, MapInfo info)
        {
            float maxElevationMeters = ResolveMaxElevationMeters(info);
            if (maxElevationMeters <= 0f)
                return 0f;

            float signedMeters = GetSignedMeters(cell, info);
            float normalized = signedMeters / maxElevationMeters;
            float minNormalized = -ResolveMaxSeaDepthMeters(info) / maxElevationMeters;
            return Math.Max(minNormalized, Math.Min(1f, normalized));
        }

        /// <summary>
        /// Normalize water depth to [0..1], where 0 is sea level and 1 is max configured sea depth.
        /// Land cells return 0.
        /// </summary>
        public static float GetNormalizedDepth01(Cell cell, MapInfo info)
        {
            float signedMeters = GetSignedMeters(cell, info);
            if (signedMeters >= 0f)
                return 0f;

            float maxSeaDepthMeters = ResolveMaxSeaDepthMeters(info);
            if (maxSeaDepthMeters <= 0f)
                return 0f;

            return Math.Min(1f, -signedMeters / maxSeaDepthMeters);
        }

        /// <summary>
        /// Convert absolute world height to meters above sea level (clamped to [0, maxElevation]).
        /// </summary>
        public static float AbsoluteToMetersAboveSeaLevel(float absoluteHeight, MapInfo info)
        {
            return Math.Max(0f, AbsoluteToSignedMeters(absoluteHeight, info));
        }

        /// <summary>
        /// Convert meters above sea level to absolute world height.
        /// </summary>
        public static float MetersAboveSeaLevelToAbsolute(float metersAboveSeaLevel, MapInfo info)
        {
            ValidateFinite(metersAboveSeaLevel, "MetersAboveSeaLevelToAbsolute input");
            return SignedMetersToAbsolute(Math.Max(0f, metersAboveSeaLevel), info);
        }

        /// <summary>
        /// Compatibility alias: canonical sea-relative height is already signed meters (sea=0m).
        /// </summary>
        public static float SeaRelativeToSignedMeters(float seaRelativeHeight, MapInfo info)
        {
            AssertSignedMetersInRange(seaRelativeHeight, info, "SeaRelativeToSignedMeters input");
            return seaRelativeHeight;
        }

        /// <summary>
        /// Canonical signed meters already use sea-relative semantics.
        /// </summary>
        public static float SignedMetersToSeaRelative(float signedMeters, MapInfo info)
        {
            AssertSignedMetersInRange(signedMeters, info, "SignedMetersToSeaRelative input");
            return signedMeters;
        }

        public static float ResolveMaxElevationMeters(MapInfo info)
        {
            WorldInfo world = RequireWorldInfo(info, "ResolveMaxElevationMeters");
            if (float.IsNaN(world.MaxElevationMeters) || float.IsInfinity(world.MaxElevationMeters) || world.MaxElevationMeters <= 0f)
                throw new InvalidOperationException($"World.MaxElevationMeters must be > 0, got {world.MaxElevationMeters}.");
            return world.MaxElevationMeters;
        }

        public static float ResolveMaxSeaDepthMeters(MapInfo info)
        {
            WorldInfo world = RequireWorldInfo(info, "ResolveMaxSeaDepthMeters");
            if (float.IsNaN(world.MaxSeaDepthMeters) || float.IsInfinity(world.MaxSeaDepthMeters) || world.MaxSeaDepthMeters <= 0f)
                throw new InvalidOperationException($"World.MaxSeaDepthMeters must be > 0, got {world.MaxSeaDepthMeters}.");
            return world.MaxSeaDepthMeters;
        }

        private static WorldInfo RequireWorldInfo(MapInfo info, string context)
        {
            if (info == null)
                throw new InvalidOperationException($"{context} requires non-null MapInfo.");
            if (info.World == null)
                throw new InvalidOperationException($"{context} requires MapInfo.World metadata.");
            return info.World;
        }
    }
}
