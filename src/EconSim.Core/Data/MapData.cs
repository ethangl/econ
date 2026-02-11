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
        public List<County> Counties;    // Economic unit groupings (set by CountyGrouper)

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
        public void AssertElevationInvariants(bool requireCanonical)
        {
            if (Cells == null)
                throw new InvalidOperationException("MapData.Cells is null.");

            for (int i = 0; i < Cells.Count; i++)
            {
                Cell cell = Cells[i];
                if (cell == null)
                    throw new InvalidOperationException($"MapData.Cells[{i}] is null.");

                if (requireCanonical && !cell.HasSeaRelativeElevation)
                {
                    throw new InvalidOperationException(
                        $"Cell {cell.Id} is missing canonical SeaRelativeElevation.");
                }

                float absoluteHeight = Elevation.GetAbsoluteHeight(cell, Info);
                Elevation.AssertAbsoluteHeightInRange(absoluteHeight, $"cell {cell.Id}");
            }
        }

        /// <summary>
        /// Validate world-scale metadata invariants.
        /// </summary>
        public void AssertWorldInvariants(bool requireWorldMetadata)
        {
            if (!requireWorldMetadata && (Info == null || Info.World == null))
                return;

            if (Info == null)
                throw new InvalidOperationException("MapData.Info is null.");
            if (Info.World == null)
                throw new InvalidOperationException("MapData.Info.World is null.");

            WorldInfo world = Info.World;
            if (world.CellSizeKm <= 0f)
                throw new InvalidOperationException("World.CellSizeKm must be > 0.");
            if (world.MapWidthKm <= 0f || world.MapHeightKm <= 0f)
                throw new InvalidOperationException("World map dimensions must be > 0.");
            if (world.MaxElevationMeters <= 0f)
                throw new InvalidOperationException("World.MaxElevationMeters must be > 0.");
            if (world.MaxSeaDepthMeters <= 0f)
                throw new InvalidOperationException("World.MaxSeaDepthMeters must be > 0.");
            if (world.LatitudeNorth <= world.LatitudeSouth)
                throw new InvalidOperationException("World latitude span must be increasing (north > south).");
            if (world.MinHeight >= world.SeaLevelHeight || world.SeaLevelHeight >= world.MaxHeight)
                throw new InvalidOperationException("World height anchors must satisfy MinHeight < SeaLevelHeight < MaxHeight.");
            if (world.MinHeight < Elevation.LegacyMinHeight || world.MaxHeight > Elevation.LegacyMaxHeight)
                throw new InvalidOperationException("World height anchors must remain within legacy absolute range [0, 100].");
            float infoSeaLevel = Info.SeaLevel;
            if (infoSeaLevel > Elevation.LegacyMinHeight && infoSeaLevel < Elevation.LegacyMaxHeight &&
                Math.Abs(world.SeaLevelHeight - infoSeaLevel) > 0.0001f)
            {
                throw new InvalidOperationException("World.SeaLevelHeight must match MapInfo.SeaLevel when MapInfo.SeaLevel is set.");
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
        public float SeaLevel;  // Legacy absolute-domain sea level anchor (default 20)
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
        public int Height;              // Legacy absolute-domain storage (0-100, sea-level anchor = 20)
        public float SeaRelativeElevation; // Canonical elevation (sea level = 0)
        public bool HasSeaRelativeElevation; // False for legacy maps without canonical value
        public int BiomeId;
        public int SoilId;             // MapGen.Core.SoilType ordinal (0-7)
        public bool IsLand;
        public int CoastDistance;       // + for land (dist to coast), - for water
        public int FeatureId;           // Index into Features (ocean, lake, island, etc.)

        // Political
        public int RealmId;             // 0 = none/neutral
        public int ProvinceId;          // 0 = none
        public int BurgId;              // 0 = no settlement
        public int CountyId;            // County this cell belongs to (set by CountyGrouper)

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
    /// Elevation conversion helpers for canonical sea-relative and legacy absolute values.
    /// </summary>
    public static class Elevation
    {
        public const float LegacyMinHeight = 0f;
        public const float LegacyMaxHeight = 100f;
        public const float DefaultSeaLevel = 20f;
        public const float DefaultMaxElevationMeters = 5000f;

        public static float ResolveSeaLevel(MapInfo info)
        {
            if (info == null)
                return DefaultSeaLevel;

            // Prefer explicit world metadata when available.
            if (info.World != null)
            {
                float worldSeaLevel = info.World.SeaLevelHeight;
                if (worldSeaLevel > LegacyMinHeight && worldSeaLevel < LegacyMaxHeight)
                    return worldSeaLevel;
            }

            // Fallback to legacy field. Treat unset/invalid metadata as default.
            float seaLevel = info.SeaLevel;
            if (seaLevel <= LegacyMinHeight || seaLevel >= LegacyMaxHeight)
                return DefaultSeaLevel;

            return seaLevel;
        }

        public static float SeaRelativeFromAbsolute(float absoluteHeight, float seaLevel)
        {
            AssertAbsoluteHeightInRange(absoluteHeight, "SeaRelativeFromAbsolute input");
            return absoluteHeight - seaLevel;
        }

        public static float AbsoluteFromSeaRelative(float seaRelativeHeight, float seaLevel)
        {
            return seaRelativeHeight + seaLevel;
        }

        public static float GetSeaRelativeHeight(Cell cell, MapInfo info)
        {
            if (cell == null) return 0f;

            if (cell.HasSeaRelativeElevation)
            {
                float absolute = AbsoluteFromSeaRelative(cell.SeaRelativeElevation, ResolveSeaLevel(info));
                AssertAbsoluteHeightInRange(absolute, $"cell {cell.Id} (canonical)");
                return cell.SeaRelativeElevation;
            }

            return SeaRelativeFromAbsolute(cell.Height, ResolveSeaLevel(info));
        }

        public static float GetAbsoluteHeight(Cell cell, MapInfo info)
        {
            if (cell == null) return ResolveSeaLevel(info);

            if (cell.HasSeaRelativeElevation)
            {
                float absolute = AbsoluteFromSeaRelative(cell.SeaRelativeElevation, ResolveSeaLevel(info));
                AssertAbsoluteHeightInRange(absolute, $"cell {cell.Id} (canonical)");
                return absolute;
            }

            float legacyAbsolute = cell.Height;
            AssertAbsoluteHeightInRange(legacyAbsolute, $"cell {cell.Id} (legacy)");
            return legacyAbsolute;
        }

        public static float NormalizeAbsolute01(float absoluteHeight)
        {
            AssertAbsoluteHeightInRange(absoluteHeight, "NormalizeAbsolute01 input");
            float clamped = Math.Max(LegacyMinHeight, Math.Min(LegacyMaxHeight, absoluteHeight));
            return clamped / LegacyMaxHeight;
        }

        /// <summary>
        /// Convert a cell's canonical elevation to meters above sea level (ASL).
        /// </summary>
        public static float GetMetersASL(Cell cell, MapInfo info)
        {
            return AbsoluteToMetersASL(GetAbsoluteHeight(cell, info), info);
        }

        /// <summary>
        /// Convert a cell's canonical elevation to signed meters relative to sea level.
        /// </summary>
        public static float GetSignedMeters(Cell cell, MapInfo info)
        {
            return SeaRelativeToSignedMeters(GetSeaRelativeHeight(cell, info), info);
        }

        /// <summary>
        /// Convert absolute map height (0..100) to meters above sea level (ASL, clamped to [0, maxElevation]).
        /// </summary>
        public static float AbsoluteToMetersASL(float absoluteHeight, MapInfo info)
        {
            AssertAbsoluteHeightInRange(absoluteHeight, "AbsoluteToMetersASL input");
            float seaLevel = ResolveSeaLevel(info);
            float maxElevationMeters = ResolveMaxElevationMeters(info);

            if (absoluteHeight <= seaLevel)
                return 0f;

            float landRange = Math.Max(1f, LegacyMaxHeight - seaLevel);
            float normalizedLand = (absoluteHeight - seaLevel) / landRange;
            return normalizedLand * maxElevationMeters;
        }

        /// <summary>
        /// Convert meters above sea level to absolute map height (0..100).
        /// </summary>
        public static float MetersASLToAbsolute(float metersAsl, MapInfo info)
        {
            if (float.IsNaN(metersAsl) || float.IsInfinity(metersAsl))
                throw new InvalidOperationException($"MetersASLToAbsolute input is not finite: {metersAsl}");

            float seaLevel = ResolveSeaLevel(info);
            if (metersAsl <= 0f)
                return seaLevel;

            float maxElevationMeters = ResolveMaxElevationMeters(info);
            float normalized = Math.Min(1f, metersAsl / maxElevationMeters);
            float landRange = Math.Max(1f, LegacyMaxHeight - seaLevel);
            float absolute = seaLevel + normalized * landRange;
            AssertAbsoluteHeightInRange(absolute, "MetersASLToAbsolute output");
            return absolute;
        }

        /// <summary>
        /// Convert sea-relative height (legacy units with sea=0) to signed meters (sea=0m).
        /// Positive values are above sea level; negative values are below sea level.
        /// </summary>
        public static float SeaRelativeToSignedMeters(float seaRelativeHeight, MapInfo info)
        {
            if (float.IsNaN(seaRelativeHeight) || float.IsInfinity(seaRelativeHeight))
                throw new InvalidOperationException($"SeaRelativeToSignedMeters input is not finite: {seaRelativeHeight}");

            float seaLevel = ResolveSeaLevel(info);
            float landRange = Math.Max(1f, LegacyMaxHeight - seaLevel);
            float waterRange = Math.Max(1f, seaLevel - LegacyMinHeight);

            if (seaRelativeHeight >= 0f)
            {
                float normalized = seaRelativeHeight / landRange;
                return normalized * ResolveMaxElevationMeters(info);
            }

            float normalizedDepth = seaRelativeHeight / waterRange; // negative
            return normalizedDepth * ResolveMaxSeaDepthMeters(info);
        }

        /// <summary>
        /// Convert signed meters relative to sea level back to sea-relative legacy units.
        /// </summary>
        public static float SignedMetersToSeaRelative(float signedMeters, MapInfo info)
        {
            if (float.IsNaN(signedMeters) || float.IsInfinity(signedMeters))
                throw new InvalidOperationException($"SignedMetersToSeaRelative input is not finite: {signedMeters}");

            float seaLevel = ResolveSeaLevel(info);
            float landRange = Math.Max(1f, LegacyMaxHeight - seaLevel);
            float waterRange = Math.Max(1f, seaLevel - LegacyMinHeight);

            if (signedMeters >= 0f)
            {
                float normalized = signedMeters / ResolveMaxElevationMeters(info);
                return normalized * landRange;
            }

            float normalizedDepth = signedMeters / ResolveMaxSeaDepthMeters(info); // negative
            return normalizedDepth * waterRange;
        }

        public static void AssertAbsoluteHeightInRange(float absoluteHeight, string context)
        {
            if (float.IsNaN(absoluteHeight) || float.IsInfinity(absoluteHeight))
            {
                throw new InvalidOperationException(
                    $"Absolute elevation is not finite ({absoluteHeight}) for {context}.");
            }

            if (absoluteHeight < LegacyMinHeight || absoluteHeight > LegacyMaxHeight)
            {
                throw new InvalidOperationException(
                    $"Absolute elevation {absoluteHeight} is out of range [{LegacyMinHeight}, {LegacyMaxHeight}] for {context}.");
            }
        }

        public static float ResolveMaxElevationMeters(MapInfo info)
        {
            if (info?.World != null && info.World.MaxElevationMeters > 0f)
                return info.World.MaxElevationMeters;

            return DefaultMaxElevationMeters;
        }

        public static float ResolveMaxSeaDepthMeters(MapInfo info)
        {
            if (info?.World != null && info.World.MaxSeaDepthMeters > 0f)
                return info.World.MaxSeaDepthMeters;

            float seaLevel = ResolveSeaLevel(info);
            float landRange = Math.Max(1f, LegacyMaxHeight - seaLevel);
            float waterRange = Math.Max(1f, seaLevel - LegacyMinHeight);
            return ResolveMaxElevationMeters(info) * (waterRange / landRange);
        }
    }
}
