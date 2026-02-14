using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using EconSim.Core.Data;
using EconSim.Core.Economy;
using EconSim.Core.Rendering;
using EconSim.Bridge;
using Profiler = EconSim.Core.Common.StartupProfiler;

namespace EconSim.Renderer
{
    /// <summary>
    /// Manages shader-based map overlays by generating data textures and controlling shader parameters.
    /// Provides infrastructure for borders, heat maps, and other visual overlays without mesh regeneration.
    /// </summary>
public class MapOverlayManager
{
        public enum OverlayLayer
        {
            None = 0,
            PopulationDensity = 1
        }

        public enum ChannelDebugView
        {
            PoliticalIdsR = 0,
            PoliticalIdsG = 1,
            PoliticalIdsB = 2,
            PoliticalIdsA = 3,
            GeographyBaseR = 4,
            GeographyBaseG = 5,
            GeographyBaseB = 6,
            GeographyBaseA = 7,
            RealmBorderDist = 8,
            ProvinceBorderDist = 9,
            CountyBorderDist = 10,
            MarketBorderDist = 11,
            RiverMask = 12,
            Heightmap = 13,
            RoadMask = 14,
            ModeColorResolve = 15,
            ReliefNormal = 16,
            VegetationType = 17,
            VegetationDensity = 18
        }

        // Shader property IDs (cached for performance)
        private static readonly int OverlayTexId = Shader.PropertyToID("_OverlayTex");
        private static readonly int PoliticalIdsTexId = Shader.PropertyToID("_PoliticalIdsTex");
        private static readonly int GeographyBaseTexId = Shader.PropertyToID("_GeographyBaseTex");
        private static readonly int VegetationTexId = Shader.PropertyToID("_VegetationTex");
        private static readonly int ModeColorResolveTexId = Shader.PropertyToID("_ModeColorResolve");
        private static readonly int UseModeColorResolveId = Shader.PropertyToID("_UseModeColorResolve");
        private static readonly int OverlayOpacityId = Shader.PropertyToID("_OverlayOpacity");
        private static readonly int OverlayEnabledId = Shader.PropertyToID("_OverlayEnabled");
        private static readonly int HeightmapTexId = Shader.PropertyToID("_HeightmapTex");
        private static readonly int ReliefNormalTexId = Shader.PropertyToID("_ReliefNormalTex");
        private static readonly int RiverMaskTexId = Shader.PropertyToID("_RiverMaskTex");
        private static readonly int RealmPaletteTexId = Shader.PropertyToID("_RealmPaletteTex");
        private static readonly int MarketPaletteTexId = Shader.PropertyToID("_MarketPaletteTex");
        private static readonly int BiomePaletteTexId = Shader.PropertyToID("_BiomePaletteTex");
        private static readonly int CellToMarketTexId = Shader.PropertyToID("_CellToMarketTex");
        private static readonly int RealmBorderDistTexId = Shader.PropertyToID("_RealmBorderDistTex");
        private static readonly int ProvinceBorderDistTexId = Shader.PropertyToID("_ProvinceBorderDistTex");
        private static readonly int CountyBorderDistTexId = Shader.PropertyToID("_CountyBorderDistTex");
        private static readonly int MarketBorderDistTexId = Shader.PropertyToID("_MarketBorderDistTex");
        private static readonly int RoadMaskTexId = Shader.PropertyToID("_RoadMaskTex");
        private static readonly int PathDashLengthId = Shader.PropertyToID("_PathDashLength");
        private static readonly int PathGapLengthId = Shader.PropertyToID("_PathGapLength");
        private static readonly int PathWidthId = Shader.PropertyToID("_PathWidth");
        private static readonly int PathOpacityId = Shader.PropertyToID("_PathOpacity");
        private static readonly int GradientRadiusId = Shader.PropertyToID("_GradientRadius");
        private static readonly int GradientEdgeDarkeningId = Shader.PropertyToID("_GradientEdgeDarkening");
        private static readonly int RealmBorderWidthId = Shader.PropertyToID("_RealmBorderWidth");
        private static readonly int RealmBorderDarkeningId = Shader.PropertyToID("_RealmBorderDarkening");
        private static readonly int ProvinceBorderWidthId = Shader.PropertyToID("_ProvinceBorderWidth");
        private static readonly int ProvinceBorderDarkeningId = Shader.PropertyToID("_ProvinceBorderDarkening");
        private static readonly int CountyBorderWidthId = Shader.PropertyToID("_CountyBorderWidth");
        private static readonly int CountyBorderDarkeningId = Shader.PropertyToID("_CountyBorderDarkening");
        private static readonly int MarketBorderWidthId = Shader.PropertyToID("_MarketBorderWidth");
        private static readonly int MarketBorderDarkeningId = Shader.PropertyToID("_MarketBorderDarkening");
        private static readonly int MapModeId = Shader.PropertyToID("_MapMode");
        private static readonly int DebugViewId = Shader.PropertyToID("_DebugView");
        private static readonly int UseHeightDisplacementId = Shader.PropertyToID("_UseHeightDisplacement");
        private static readonly int HeightScaleId = Shader.PropertyToID("_HeightScale");
        private static readonly int SeaLevelId = Shader.PropertyToID("_SeaLevel");
        private static readonly int SelectedRealmIdId = Shader.PropertyToID("_SelectedRealmId");
        private static readonly int SelectedProvinceIdId = Shader.PropertyToID("_SelectedProvinceId");
        private static readonly int SelectedCountyIdId = Shader.PropertyToID("_SelectedCountyId");
        private static readonly int SelectedMarketIdId = Shader.PropertyToID("_SelectedMarketId");
        private static readonly int HoveredRealmIdId = Shader.PropertyToID("_HoveredRealmId");
        private static readonly int HoveredProvinceIdId = Shader.PropertyToID("_HoveredProvinceId");
        private static readonly int HoveredCountyIdId = Shader.PropertyToID("_HoveredCountyId");
        private static readonly int HoveredMarketIdId = Shader.PropertyToID("_HoveredMarketId");
        private static readonly int HoverIntensityId = Shader.PropertyToID("_HoverIntensity");
        private static readonly int SelectionDimmingId = Shader.PropertyToID("_SelectionDimming");
        private static readonly int SelectionDesaturationId = Shader.PropertyToID("_SelectionDesaturation");

        // Water layer property IDs
        private static readonly int WaterShallowColorId = Shader.PropertyToID("_WaterShallowColor");
        private static readonly int WaterDeepColorId = Shader.PropertyToID("_WaterDeepColor");
        private static readonly int WaterShallowAlphaId = Shader.PropertyToID("_WaterShallowAlpha");
        private static readonly int ShimmerScaleId = Shader.PropertyToID("_ShimmerScale");
        private static readonly int ShimmerSpeedId = Shader.PropertyToID("_ShimmerSpeed");
        private static readonly int ShimmerIntensityId = Shader.PropertyToID("_ShimmerIntensity");

        private MapData mapData;
        private EconomyState economyState;
        private Material styleMaterial;

        // Resolution multiplier for higher quality borders
        private int resolutionMultiplier;
        private int baseWidth;
        private int baseHeight;

        // Data textures
        private Texture2D politicalIdsTexture;  // RGBAFloat: RealmId, ProvinceId, CountyId, reserved
        private Texture2D geographyBaseTexture; // RGBAFloat: BiomeId, SoilId, reserved, WaterFlag
        private Texture2D vegetationTexture;    // RGFloat: VegetationTypeId, VegetationDensity

        /// <summary>
        /// Accessor for the political IDs texture (realm/province/county channels).
        /// </summary>
        public Texture2D PoliticalIdsTexture => politicalIdsTexture;
        private Texture2D cellToMarketTexture;  // R16: CellId -> MarketId mapping (dynamic)
        private Texture2D heightmapTexture;     // RFloat: smoothed height values
        private Texture2D reliefNormalTexture;  // RGBA32: normal map derived from visual height
        private Texture2D riverMaskTexture;     // R8: river mask (1 = river, 0 = not river)
        private Texture2D realmPaletteTexture;  // 256x1: realm colors
        private Texture2D marketPaletteTexture; // 256x1: market colors
        private Texture2D biomePaletteTexture;  // 256x1: biome colors
        private Texture2D realmBorderDistTexture; // R8: distance to nearest realm boundary (texels)
        private Texture2D provinceBorderDistTexture; // R8: distance to nearest province boundary (texels)
        private Texture2D countyBorderDistTexture;   // R8: distance to nearest county boundary (texels)
        private Texture2D marketBorderDistTexture;   // R8: distance to nearest market zone boundary (texels, dynamic)
        private Texture2D roadDistTexture;             // R8: distance to nearest road centerline (texels, dynamic)
        private Texture2D modeColorResolveTexture;     // RGBA32: resolved per-mode color overlay
        private byte[] riverMaskPixels;                // Cached to drive relief synthesis near rivers.
        private string overlayTextureCacheDirectory;
        private int cachedCountyToMarketHash;
        private int cachedRoadStateHash;
        private bool overlayCacheDirty;
        private bool pendingMarketModePrewarm;
        private bool[] cellIsLandById;
        private float[] cellHeight01ById;
        private int[] cellRealmIdById;
        private int[] cellProvinceIdById;
        private int[] cellCountyIdById;
        private int[] cellBiomeIdById;
        private int[] cellSoilIdById;
        private int[] cellVegetationTypeById;
        private float[] cellVegetationDensityById;

        // Visual relief synthesis parameters (visual-only; gameplay elevation remains authoritative).
        private const int ReliefBlurRadius = 4;
        private const float ReliefBlurCrossClassWeight = 0.25f;
        private const float ReliefErosionRadiusTexels = 7f;
        private const float ReliefErosionStrength = 0.012f;
        private const float ReliefLandMinAboveSea = 0.001f;
        private const int ReliefNormalPreBlurPasses = 1;
        private const float ReliefNormalDerivativeScale = 0.12f;
        private static readonly float[] ReliefGaussianKernel = { 1f, 8f, 28f, 56f, 70f, 56f, 28f, 8f, 1f };

        // Road state (cached for regeneration)
        private RoadState roadState;
        private float cachedPathDashLength = -1f;
        private float cachedPathGapLength = -1f;
        private float cachedPathWidth = -1f;
        private readonly Dictionary<MapView.MapMode, Texture2D> modeColorResolveCacheByMode = new Dictionary<MapView.MapMode, Texture2D>();
        private readonly Dictionary<MapView.MapMode, int> modeColorResolveCacheRevisionByMode = new Dictionary<MapView.MapMode, int>();
        private readonly Dictionary<MapView.MapMode, int> modeColorResolveRevisionByKey = new Dictionary<MapView.MapMode, int>();
        private readonly Dictionary<OverlayLayer, Texture2D> overlayTextureCacheByLayer = new Dictionary<OverlayLayer, Texture2D>();
        private OverlayLayer currentOverlayLayer = OverlayLayer.None;

        // Spatial lookup grid: maps data pixel coordinates to cell IDs
        private int[] spatialGrid;
        private int gridWidth;
        private int gridHeight;

        // Raw texture data for incremental updates
        private Color[] politicalIdsPixels;
        private Color[] geographyBasePixels;
        private MapView.MapMode currentMapMode = MapView.MapMode.Political;

        // Political color generator
        private PoliticalPalette politicalPalette;

        // Market zone/hub colors (matching MapView's colors for consistency)
        private static readonly Color[] MarketZoneColors = new Color[]
        {
            new Color(100/255f, 149/255f, 237/255f),  // Cornflower blue
            new Color(144/255f, 238/255f, 144/255f),  // Light green
            new Color(255/255f, 182/255f, 108/255f),  // Light orange
            new Color(221/255f, 160/255f, 221/255f),  // Plum
            new Color(240/255f, 230/255f, 140/255f),  // Khaki
            new Color(127/255f, 255/255f, 212/255f),  // Aquamarine
            new Color(255/255f, 160/255f, 122/255f),  // Light salmon
            new Color(176/255f, 196/255f, 222/255f),  // Light steel blue
        };

        // Predefined market hub colors - vivid, high-contrast versions
        private static readonly Color[] MarketHubColors = new Color[]
        {
            new Color(0/255f, 71/255f, 171/255f),     // Cobalt blue
            new Color(0/255f, 128/255f, 0/255f),      // Green
            new Color(255/255f, 140/255f, 0/255f),    // Dark orange
            new Color(148/255f, 0/255f, 211/255f),    // Dark violet
            new Color(184/255f, 134/255f, 11/255f),   // Dark goldenrod
            new Color(0/255f, 139/255f, 139/255f),    // Dark cyan
            new Color(220/255f, 20/255f, 60/255f),    // Crimson
            new Color(70/255f, 130/255f, 180/255f),   // Steel blue
        };

        // Transport heatmap colors (low -> high).
        private static readonly Color HeatLowColor = new Color(0.12f, 0.45f, 0.85f);
        private static readonly Color HeatMidColor = new Color(0.15f, 0.78f, 0.62f);
        private static readonly Color HeatHighColor = new Color(0.98f, 0.82f, 0.22f);
        private static readonly Color HeatExtremeColor = new Color(0.82f, 0.20f, 0.18f);
        private static readonly Color HeatMissingColor = new Color(0.25f, 0.25f, 0.25f);
        private const float DefaultOverlayOpacity = 0.65f;
        private float overlayOpacity = DefaultOverlayOpacity;
        private const float ProvinceHueShiftDegrees = 6f;
        private const float ProvinceSaturationShift = 0.06f;
        private const float ProvinceValueShift = 0.06f;
        private const float CountyHueShiftDegrees = 6f;
        private const float CountySaturationShift = 0.06f;
        private const float CountyValueShift = 0.06f;

        private const float OverlayDefaultMovementCost = 10.0f;

        private const int OverlayTextureCacheVersion = 3;
        private const string OverlayTextureCacheMetadataFileName = "overlay_cache.json";
        private const string CacheSpatialGridFile = "spatial_grid.bin";
        private const string CachePoliticalIdsFile = "political_ids.bin";
        private const string CacheGeographyBaseFile = "geography_base.bin";
        private const string CacheRiverMaskFile = "river_mask.bin";
        private const string CacheHeightmapFile = "heightmap.bin";
        private const string CacheReliefNormalFile = "relief_normal.bin";
        private const string CacheRealmBorderFile = "realm_border_dist.bin";
        private const string CacheProvinceBorderFile = "province_border_dist.bin";
        private const string CacheCountyBorderFile = "county_border_dist.bin";
        private const string CacheMarketBorderFile = "market_border_dist.bin";
        private const string CacheRoadDistFile = "road_dist.bin";

        [Serializable]
        private sealed class OverlayTextureCacheMetadata
        {
            public int Version;
            public int GridWidth;
            public int GridHeight;
            public int BaseWidth;
            public int BaseHeight;
            public int ResolutionMultiplier;
            public int RootSeed;
            public int MapGenSeed;
            public float LatitudeSouth;
            public float LatitudeNorth;
            public int CountyToMarketHash;
            public int RoadStateHash;
        }

        private readonly struct SpatialCellInfo
        {
            public readonly int CellId;
            public readonly int X0;
            public readonly int Y0;
            public readonly int X1;
            public readonly int Y1;
            public readonly float Cx;
            public readonly float Cy;

            public SpatialCellInfo(int cellId, int x0, int y0, int x1, int y1, float cx, float cy)
            {
                CellId = cellId;
                X0 = x0;
                Y0 = y0;
                X1 = x1;
                Y1 = y1;
                Cx = cx;
                Cy = cy;
            }
        }

        /// <summary>
        /// Create overlay manager with specified resolution multiplier.
        /// </summary>
        /// <param name="mapData">Map data source</param>
        /// <param name="styleMaterial">Material to apply textures to</param>
        /// <param name="resolutionMultiplier">Multiplier for data texture resolution (1=base, 2=2x, 3=3x). Higher = smoother borders but more memory.</param>
        public MapOverlayManager(
            MapData mapData,
            Material styleMaterial,
            int resolutionMultiplier = 2,
            string overlayTextureCacheDirectory = null,
            bool preferCachedOverlayTextures = false)
        {
            this.mapData = mapData;
            this.styleMaterial = styleMaterial;
            this.resolutionMultiplier = Mathf.Clamp(resolutionMultiplier, 1, 8);
            this.overlayTextureCacheDirectory = overlayTextureCacheDirectory;

            baseWidth = mapData.Info.Width;
            baseHeight = mapData.Info.Height;
            gridWidth = baseWidth * this.resolutionMultiplier;
            gridHeight = baseHeight * this.resolutionMultiplier;
            BuildCellLookupCache();

            bool loadedSpatialGrid = false;
            if (preferCachedOverlayTextures && !string.IsNullOrWhiteSpace(overlayTextureCacheDirectory))
            {
                Profiler.Begin("LoadSpatialGridCache");
                loadedSpatialGrid = TryLoadSpatialGridCache(overlayTextureCacheDirectory);
                Profiler.End();
            }

            if (!loadedSpatialGrid)
            {
                Profiler.Begin("BuildSpatialGrid");
                BuildSpatialGrid();
                Profiler.End();
            }

            bool loadedFromTextureCache = false;
            if (preferCachedOverlayTextures && !string.IsNullOrWhiteSpace(overlayTextureCacheDirectory))
            {
                Profiler.Begin("LoadOverlayTextureCache");
                loadedFromTextureCache = TryLoadOverlayTextureCache(overlayTextureCacheDirectory);
                Profiler.End();
            }

            if (!loadedFromTextureCache)
            {
                Profiler.Begin("GenerateDataTextures");
                GenerateDataTextures();
                Profiler.End();

                Profiler.Begin("GenerateRiverMaskTexture");
                GenerateRiverMaskTexture();
                Profiler.End();

                Profiler.Begin("GenerateHeightmapTexture");
                GenerateHeightmapTexture();
                Profiler.End();

                Profiler.Begin("GenerateAdministrativeBorderDistTextures");
                GenerateAdministrativeBorderDistTextures();
                Profiler.End();
            }

            Profiler.Begin("GeneratePaletteTextures");
            GeneratePaletteTextures();
            Profiler.End();

            Profiler.Begin("GenerateVegetationTexture");
            GenerateVegetationTexture();
            Profiler.End();


            Profiler.Begin("ApplyTexturesToMaterial");
            ApplyTexturesToMaterial();
            Profiler.End();

            if (!loadedFromTextureCache && !string.IsNullOrWhiteSpace(overlayTextureCacheDirectory))
            {
                TrySaveOverlayTextureCache(overlayTextureCacheDirectory);
            }
            else if (!loadedSpatialGrid && !string.IsNullOrWhiteSpace(overlayTextureCacheDirectory))
            {
                // Backfill spatial grid cache when loading an older texture cache that predates it.
                TrySaveSpatialGridCache(overlayTextureCacheDirectory);
            }
        }

        private void BuildCellLookupCache()
        {
            int maxCellId = -1;
            for (int i = 0; i < mapData.Cells.Count; i++)
            {
                if (mapData.Cells[i].Id > maxCellId)
                    maxCellId = mapData.Cells[i].Id;
            }

            int lookupSize = maxCellId + 1;
            if (lookupSize <= 0)
                lookupSize = 1;

            cellIsLandById = new bool[lookupSize];
            cellHeight01ById = new float[lookupSize];
            cellRealmIdById = new int[lookupSize];
            cellProvinceIdById = new int[lookupSize];
            cellCountyIdById = new int[lookupSize];
            cellBiomeIdById = new int[lookupSize];
            cellSoilIdById = new int[lookupSize];
            cellVegetationTypeById = new int[lookupSize];
            cellVegetationDensityById = new float[lookupSize];

            for (int i = 0; i < mapData.Cells.Count; i++)
            {
                var cell = mapData.Cells[i];
                int cellId = cell.Id;
                if (cellId < 0 || cellId >= lookupSize)
                    continue;

                cellIsLandById[cellId] = cell.IsLand;
                float absoluteHeight = Elevation.GetAbsoluteHeight(cell, mapData.Info);
                cellHeight01ById[cellId] = Elevation.NormalizeAbsolute01(absoluteHeight, mapData.Info);
                cellRealmIdById[cellId] = cell.RealmId;
                cellProvinceIdById[cellId] = cell.ProvinceId;
                cellCountyIdById[cellId] = cell.CountyId;
                cellBiomeIdById[cellId] = cell.BiomeId;
                cellSoilIdById[cellId] = cell.SoilId;
                cellVegetationTypeById[cellId] = Mathf.Clamp(cell.VegetationTypeId, 0, 255);
                float vegetationDensity = float.IsNaN(cell.VegetationDensity) || float.IsInfinity(cell.VegetationDensity)
                    ? 0f
                    : cell.VegetationDensity;
                cellVegetationDensityById[cellId] = Mathf.Clamp01(vegetationDensity);
            }
        }

        private bool TryLoadSpatialGridCache(string cacheDirectory)
        {
            try
            {
                if (!Directory.Exists(cacheDirectory))
                    return false;

                string metadataPath = Path.Combine(cacheDirectory, OverlayTextureCacheMetadataFileName);
                if (!File.Exists(metadataPath))
                    return false;

                OverlayTextureCacheMetadata metadata = JsonUtility.FromJson<OverlayTextureCacheMetadata>(File.ReadAllText(metadataPath));
                if (!IsOverlayTextureCacheMetadataCompatible(metadata))
                    return false;

                string spatialGridPath = Path.Combine(cacheDirectory, CacheSpatialGridFile);
                if (!File.Exists(spatialGridPath))
                    return false;

                byte[] bytes = File.ReadAllBytes(spatialGridPath);
                int expectedBytes = gridWidth * gridHeight * sizeof(int);
                if (bytes.Length != expectedBytes)
                    return false;

                int[] loadedSpatialGrid = new int[gridWidth * gridHeight];
                Buffer.BlockCopy(bytes, 0, loadedSpatialGrid, 0, bytes.Length);
                spatialGrid = loadedSpatialGrid;

                Debug.Log($"MapOverlayManager: Loaded cached spatial grid from {spatialGridPath}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"MapOverlayManager: Failed to load spatial grid cache ({cacheDirectory}): {ex.Message}");
                return false;
            }
        }

        private void TrySaveSpatialGridCache(string cacheDirectory)
        {
            if (spatialGrid == null || spatialGrid.Length == 0)
                return;

            try
            {
                Directory.CreateDirectory(cacheDirectory);
                string spatialGridPath = Path.Combine(cacheDirectory, CacheSpatialGridFile);
                byte[] bytes = new byte[spatialGrid.Length * sizeof(int)];
                Buffer.BlockCopy(spatialGrid, 0, bytes, 0, bytes.Length);
                File.WriteAllBytes(spatialGridPath, bytes);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"MapOverlayManager: Failed to save spatial grid cache ({cacheDirectory}): {ex.Message}");
            }
        }

        private bool TryLoadOverlayTextureCache(string cacheDirectory)
        {
            try
            {
                if (!Directory.Exists(cacheDirectory))
                    return false;

                string metadataPath = Path.Combine(cacheDirectory, OverlayTextureCacheMetadataFileName);
                if (!File.Exists(metadataPath))
                    return false;

                OverlayTextureCacheMetadata metadata = JsonUtility.FromJson<OverlayTextureCacheMetadata>(File.ReadAllText(metadataPath));
                if (!IsOverlayTextureCacheMetadataCompatible(metadata))
                    return false;

                Texture2D loadedPoliticalIds = LoadTextureFromRaw(
                    Path.Combine(cacheDirectory, CachePoliticalIdsFile),
                    gridWidth,
                    gridHeight,
                    TextureFormat.RGBAFloat,
                    FilterMode.Point,
                    TextureWrapMode.Clamp);

                Texture2D loadedGeographyBase = LoadTextureFromRaw(
                    Path.Combine(cacheDirectory, CacheGeographyBaseFile),
                    gridWidth,
                    gridHeight,
                    TextureFormat.RGBAFloat,
                    FilterMode.Point,
                    TextureWrapMode.Clamp);

                Texture2D loadedRiverMask = LoadTextureFromRaw(
                    Path.Combine(cacheDirectory, CacheRiverMaskFile),
                    gridWidth,
                    gridHeight,
                    TextureFormat.R8,
                    FilterMode.Bilinear,
                    TextureWrapMode.Clamp,
                    out byte[] loadedRiverMaskPixels);

                Texture2D loadedHeightmap = LoadTextureFromRaw(
                    Path.Combine(cacheDirectory, CacheHeightmapFile),
                    gridWidth,
                    gridHeight,
                    TextureFormat.RFloat,
                    FilterMode.Bilinear,
                    TextureWrapMode.Clamp);

                Texture2D loadedReliefNormal = LoadTextureFromRaw(
                    Path.Combine(cacheDirectory, CacheReliefNormalFile),
                    gridWidth,
                    gridHeight,
                    TextureFormat.RGBA32,
                    FilterMode.Bilinear,
                    TextureWrapMode.Clamp,
                    linear: true);

                Texture2D loadedRealmBorder = LoadTextureFromRaw(
                    Path.Combine(cacheDirectory, CacheRealmBorderFile),
                    gridWidth,
                    gridHeight,
                    TextureFormat.R8,
                    FilterMode.Bilinear,
                    TextureWrapMode.Clamp,
                    anisoLevel: 8);

                Texture2D loadedProvinceBorder = LoadTextureFromRaw(
                    Path.Combine(cacheDirectory, CacheProvinceBorderFile),
                    gridWidth,
                    gridHeight,
                    TextureFormat.R8,
                    FilterMode.Bilinear,
                    TextureWrapMode.Clamp,
                    anisoLevel: 8);

                Texture2D loadedCountyBorder = LoadTextureFromRaw(
                    Path.Combine(cacheDirectory, CacheCountyBorderFile),
                    gridWidth,
                    gridHeight,
                    TextureFormat.R8,
                    FilterMode.Bilinear,
                    TextureWrapMode.Clamp,
                    anisoLevel: 8);

                Texture2D loadedMarketBorder = LoadTextureFromRaw(
                    Path.Combine(cacheDirectory, CacheMarketBorderFile),
                    gridWidth,
                    gridHeight,
                    TextureFormat.R8,
                    FilterMode.Bilinear,
                    TextureWrapMode.Clamp,
                    anisoLevel: 8);

                Texture2D loadedRoadDist = LoadTextureFromRaw(
                    Path.Combine(cacheDirectory, CacheRoadDistFile),
                    gridWidth,
                    gridHeight,
                    TextureFormat.R8,
                    FilterMode.Bilinear,
                    TextureWrapMode.Clamp,
                    anisoLevel: 8);

                if (loadedPoliticalIds == null ||
                    loadedGeographyBase == null ||
                    loadedRiverMask == null ||
                    loadedHeightmap == null ||
                    loadedReliefNormal == null ||
                    loadedRealmBorder == null ||
                    loadedProvinceBorder == null ||
                    loadedCountyBorder == null)
                {
                    DestroyTexture(loadedPoliticalIds);
                    DestroyTexture(loadedGeographyBase);
                    DestroyTexture(loadedRiverMask);
                    DestroyTexture(loadedHeightmap);
                    DestroyTexture(loadedReliefNormal);
                    DestroyTexture(loadedRealmBorder);
                    DestroyTexture(loadedProvinceBorder);
                    DestroyTexture(loadedCountyBorder);
                    return false;
                }

                politicalIdsTexture = loadedPoliticalIds;
                geographyBaseTexture = loadedGeographyBase;
                riverMaskTexture = loadedRiverMask;
                heightmapTexture = loadedHeightmap;
                reliefNormalTexture = loadedReliefNormal;
                realmBorderDistTexture = loadedRealmBorder;
                provinceBorderDistTexture = loadedProvinceBorder;
                countyBorderDistTexture = loadedCountyBorder;
                marketBorderDistTexture = loadedMarketBorder;
                roadDistTexture = loadedRoadDist;
                riverMaskPixels = loadedRiverMaskPixels;
                politicalIdsPixels = null;
                geographyBasePixels = null;
                cachedCountyToMarketHash = metadata.CountyToMarketHash;
                cachedRoadStateHash = metadata.RoadStateHash;

                Debug.Log($"MapOverlayManager: Loaded cached overlay textures from {cacheDirectory}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"MapOverlayManager: Failed to load overlay texture cache ({cacheDirectory}): {ex.Message}");
                return false;
            }
        }

        private bool TrySaveOverlayTextureCache(string cacheDirectory)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(cacheDirectory))
                    return false;

                Directory.CreateDirectory(cacheDirectory);
                TrySaveSpatialGridCache(cacheDirectory);

                SaveTextureToRaw(Path.Combine(cacheDirectory, CachePoliticalIdsFile), politicalIdsTexture);
                SaveTextureToRaw(Path.Combine(cacheDirectory, CacheGeographyBaseFile), geographyBaseTexture);
                SaveTextureToRaw(Path.Combine(cacheDirectory, CacheRiverMaskFile), riverMaskTexture);
                SaveTextureToRaw(Path.Combine(cacheDirectory, CacheHeightmapFile), heightmapTexture);
                SaveTextureToRaw(Path.Combine(cacheDirectory, CacheReliefNormalFile), reliefNormalTexture);
                SaveTextureToRaw(Path.Combine(cacheDirectory, CacheRealmBorderFile), realmBorderDistTexture);
                SaveTextureToRaw(Path.Combine(cacheDirectory, CacheProvinceBorderFile), provinceBorderDistTexture);
                SaveTextureToRaw(Path.Combine(cacheDirectory, CacheCountyBorderFile), countyBorderDistTexture);
                if (marketBorderDistTexture != null)
                    SaveTextureToRaw(Path.Combine(cacheDirectory, CacheMarketBorderFile), marketBorderDistTexture);
                if (roadDistTexture != null)
                    SaveTextureToRaw(Path.Combine(cacheDirectory, CacheRoadDistFile), roadDistTexture);

                var metadata = new OverlayTextureCacheMetadata
                {
                    Version = OverlayTextureCacheVersion,
                    GridWidth = gridWidth,
                    GridHeight = gridHeight,
                    BaseWidth = baseWidth,
                    BaseHeight = baseHeight,
                    ResolutionMultiplier = resolutionMultiplier,
                    RootSeed = mapData?.Info != null ? mapData.Info.RootSeed : 0,
                    MapGenSeed = mapData?.Info != null ? mapData.Info.MapGenSeed : 0,
                    LatitudeSouth = mapData?.Info?.World != null ? mapData.Info.World.LatitudeSouth : float.NaN,
                    LatitudeNorth = mapData?.Info?.World != null ? mapData.Info.World.LatitudeNorth : float.NaN,
                    CountyToMarketHash = cachedCountyToMarketHash,
                    RoadStateHash = cachedRoadStateHash
                };

                string metadataPath = Path.Combine(cacheDirectory, OverlayTextureCacheMetadataFileName);
                File.WriteAllText(metadataPath, JsonUtility.ToJson(metadata, false));
                Debug.Log($"MapOverlayManager: Saved overlay texture cache to {cacheDirectory}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"MapOverlayManager: Failed to save overlay texture cache ({cacheDirectory}): {ex.Message}");
                return false;
            }
        }

        private bool IsOverlayTextureCacheMetadataCompatible(OverlayTextureCacheMetadata metadata)
        {
            if (metadata == null)
                return false;

            if (metadata.Version != OverlayTextureCacheVersion)
                return false;

            if (metadata.GridWidth != gridWidth ||
                metadata.GridHeight != gridHeight ||
                metadata.BaseWidth != baseWidth ||
                metadata.BaseHeight != baseHeight ||
                metadata.ResolutionMultiplier != resolutionMultiplier)
            {
                return false;
            }

            if (mapData?.Info != null)
            {
                if (metadata.RootSeed > 0 && mapData.Info.RootSeed > 0 && metadata.RootSeed != mapData.Info.RootSeed)
                    return false;
                if (metadata.MapGenSeed > 0 && mapData.Info.MapGenSeed > 0 && metadata.MapGenSeed != mapData.Info.MapGenSeed)
                    return false;

                float expectedLatitudeSouth = mapData.Info.World != null ? mapData.Info.World.LatitudeSouth : float.NaN;
                float expectedLatitudeNorth = mapData.Info.World != null ? mapData.Info.World.LatitudeNorth : float.NaN;
                if (!CacheFloatMatches(metadata.LatitudeSouth, expectedLatitudeSouth))
                    return false;
                if (!CacheFloatMatches(metadata.LatitudeNorth, expectedLatitudeNorth))
                    return false;
            }

            return true;
        }

        private static bool CacheFloatMatches(float cachedValue, float expectedValue)
        {
            bool cachedIsFinite = IsFinite(cachedValue);
            bool expectedIsFinite = IsFinite(expectedValue);

            if (cachedIsFinite != expectedIsFinite)
                return false;

            if (!cachedIsFinite)
                return true;

            return Mathf.Abs(cachedValue - expectedValue) <= 0.0001f;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static Texture2D LoadTextureFromRaw(
            string path,
            int width,
            int height,
            TextureFormat format,
            FilterMode filterMode,
            TextureWrapMode wrapMode,
            out byte[] rawBytes,
            int anisoLevel = 1,
            bool linear = false)
        {
            rawBytes = null;
            if (!File.Exists(path))
                return null;

            Texture2D texture = null;
            try
            {
                rawBytes = File.ReadAllBytes(path);
                texture = new Texture2D(width, height, format, false, linear);
                texture.LoadRawTextureData(rawBytes);
                texture.filterMode = filterMode;
                texture.wrapMode = wrapMode;
                texture.anisoLevel = anisoLevel;
                texture.Apply(false, false);
                return texture;
            }
            catch
            {
                if (texture != null)
                    DestroyTexture(texture);
                rawBytes = null;
                return null;
            }
        }

        private static Texture2D LoadTextureFromRaw(
            string path,
            int width,
            int height,
            TextureFormat format,
            FilterMode filterMode,
            TextureWrapMode wrapMode,
            int anisoLevel = 1,
            bool linear = false)
        {
            return LoadTextureFromRaw(path, width, height, format, filterMode, wrapMode, out _, anisoLevel, linear);
        }

        private static void SaveTextureToRaw(string path, Texture2D texture)
        {
            if (texture == null)
                throw new InvalidOperationException($"Cannot cache null texture: {path}");

            byte[] raw = texture.GetRawTextureData<byte>().ToArray();
            File.WriteAllBytes(path, raw);
        }

        /// <summary>
        /// Build spatial lookup grid mapping data coordinates to cell IDs.
        /// Uses cell centers to determine ownership of each grid position.
        /// Applies domain warping for organic, meandering borders.
        /// </summary>
        private void BuildSpatialGrid()
        {
            BuildSpatialGridFromScratch();
            Debug.Log($"MapOverlayManager: Built spatial grid {gridWidth}x{gridHeight} ({resolutionMultiplier}x resolution)");
        }

        /// <summary>
        /// Build the spatial grid using nearest-center Voronoi assignment.
        /// </summary>
        private void BuildSpatialGridFromScratch()
        {
            int size = gridWidth * gridHeight;
            spatialGrid = new int[size];
            var distanceSqGrid = new float[size];
            var warpX = new float[size];
            var warpY = new float[size];

            // Initialize grids
            Parallel.For(0, size, i =>
            {
                spatialGrid[i] = -1;
                distanceSqGrid[i] = float.MaxValue;
            });

            // Domain warp is deterministic per pixel; compute once and reuse.
            Parallel.For(0, gridHeight, y =>
            {
                int row = y * gridWidth;
                for (int x = 0; x < gridWidth; x++)
                {
                    int idx = row + x;
                    var (wx, wy) = DomainWarp.Warp(x, y);
                    warpX[idx] = wx;
                    warpY[idx] = wy;
                }
            });

            float scale = resolutionMultiplier;

            // PHASE 1: Fast Voronoi fill using cell centers (complete coverage, no gaps)
            var cellInfos = new List<SpatialCellInfo>(mapData.Cells.Count);

            foreach (var cell in mapData.Cells)
            {
                if (cell.VertexIndices == null || cell.VertexIndices.Count < 3)
                    continue;

                float minX = float.MaxValue, maxX = float.MinValue;
                float minY = float.MaxValue, maxY = float.MinValue;

                foreach (int vIdx in cell.VertexIndices)
                {
                    if (vIdx >= 0 && vIdx < mapData.Vertices.Count)
                    {
                        var v = mapData.Vertices[vIdx];
                        minX = Mathf.Min(minX, v.X * scale);
                        maxX = Mathf.Max(maxX, v.X * scale);
                        minY = Mathf.Min(minY, v.Y * scale);
                        maxY = Mathf.Max(maxY, v.Y * scale);
                    }
                }

                // Expand bounding box by warp amplitude to ensure coverage
                int x0 = Mathf.Max(0, Mathf.FloorToInt(minX - DomainWarp.Amplitude));
                int x1 = Mathf.Min(gridWidth - 1, Mathf.CeilToInt(maxX + DomainWarp.Amplitude));
                int y0 = Mathf.Max(0, Mathf.FloorToInt(minY - DomainWarp.Amplitude));
                int y1 = Mathf.Min(gridHeight - 1, Mathf.CeilToInt(maxY + DomainWarp.Amplitude));

                float cx = cell.Center.X * scale;
                float cy = cell.Center.Y * scale;

                cellInfos.Add(new SpatialCellInfo(cell.Id, x0, y0, x1, y1, cx, cy));
            }

            // Partition by row stripes so each worker owns exclusive output rows (no locks).
            const int stripeHeight = 32;
            int stripeCount = (gridHeight + stripeHeight - 1) / stripeHeight;
            var stripeCellIndices = new List<int>[stripeCount];

            for (int i = 0; i < cellInfos.Count; i++)
            {
                SpatialCellInfo info = cellInfos[i];
                int stripeStart = info.Y0 / stripeHeight;
                int stripeEnd = info.Y1 / stripeHeight;
                for (int stripe = stripeStart; stripe <= stripeEnd; stripe++)
                {
                    stripeCellIndices[stripe] ??= new List<int>(64);
                    stripeCellIndices[stripe].Add(i);
                }
            }

            Parallel.For(0, stripeCount, stripe =>
            {
                List<int> cellsInStripe = stripeCellIndices[stripe];
                if (cellsInStripe == null || cellsInStripe.Count == 0)
                    return;

                int stripeY0 = stripe * stripeHeight;
                int stripeY1 = Mathf.Min(gridHeight - 1, stripeY0 + stripeHeight - 1);

                for (int i = 0; i < cellsInStripe.Count; i++)
                {
                    SpatialCellInfo info = cellInfos[cellsInStripe[i]];
                    int y0 = info.Y0 > stripeY0 ? info.Y0 : stripeY0;
                    int y1 = info.Y1 < stripeY1 ? info.Y1 : stripeY1;

                    for (int y = y0; y <= y1; y++)
                    {
                        int row = y * gridWidth;
                        for (int x = info.X0; x <= info.X1; x++)
                        {
                            int gridIdx = row + x;
                            float dx = warpX[gridIdx] - info.Cx;
                            float dy = warpY[gridIdx] - info.Cy;
                            float distSq = dx * dx + dy * dy;

                            if (distSq < distanceSqGrid[gridIdx])
                            {
                                distanceSqGrid[gridIdx] = distSq;
                                spatialGrid[gridIdx] = info.CellId;
                            }
                        }
                    }
                }
            });
        }

        /// <summary>
        /// Generate core data textures from spatial grid + cell data.
        /// Primary flow uses split textures:
        /// - Political IDs: realm/province/county/reserved
        /// - Geography Base: biome/soil/reserved/water-flag
        /// </summary>
        private void GenerateDataTextures()
        {
            politicalIdsTexture = new Texture2D(gridWidth, gridHeight, TextureFormat.RGBAFloat, false);
            politicalIdsTexture.name = "PoliticalIdsTexture";
            politicalIdsTexture.filterMode = FilterMode.Point;
            politicalIdsTexture.wrapMode = TextureWrapMode.Clamp;

            geographyBaseTexture = new Texture2D(gridWidth, gridHeight, TextureFormat.RGBAFloat, false);
            geographyBaseTexture.name = "GeographyBaseTexture";
            geographyBaseTexture.filterMode = FilterMode.Point;
            geographyBaseTexture.wrapMode = TextureWrapMode.Clamp;

            politicalIdsPixels = new Color[gridWidth * gridHeight];
            geographyBasePixels = new Color[gridWidth * gridHeight];

            // Fill textures from spatial grid (parallelized by row).
            Parallel.For(0, gridHeight, y =>
            {
                for (int x = 0; x < gridWidth; x++)
                {
                    int gridIdx = y * gridWidth + x;
                    int cellId = spatialGrid[gridIdx];

                    Color political;
                    Color geography;

                    if (cellId >= 0 && cellId < cellIsLandById.Length)
                    {
                        political.r = cellRealmIdById[cellId] / 65535f;
                        political.g = cellProvinceIdById[cellId] / 65535f;
                        political.b = cellCountyIdById[cellId] / 65535f;
                        political.a = 0f;

                        geography.r = cellBiomeIdById[cellId] / 65535f;
                        geography.g = cellSoilIdById[cellId] / 65535f;
                        geography.b = 0f;
                        geography.a = cellIsLandById[cellId] ? 0f : 1f;
                    }
                    else
                    {
                        political = new Color(0f, 0f, 0f, 0f);
                        geography = new Color(0f, 0f, 0f, 1f);
                    }

                    politicalIdsPixels[gridIdx] = political;
                    geographyBasePixels[gridIdx] = geography;
                }
            });

            politicalIdsTexture.SetPixels(politicalIdsPixels);
            politicalIdsTexture.Apply();

            geographyBaseTexture.SetPixels(geographyBasePixels);
            geographyBaseTexture.Apply();

            Debug.Log($"MapOverlayManager: Generated core textures {gridWidth}x{gridHeight}");
        }

        private void GenerateVegetationTexture()
        {
            vegetationTexture = new Texture2D(gridWidth, gridHeight, TextureFormat.RGFloat, false);
            vegetationTexture.name = "VegetationTexture";
            vegetationTexture.filterMode = FilterMode.Point;
            vegetationTexture.wrapMode = TextureWrapMode.Clamp;

            var vegetationPixels = new Color[gridWidth * gridHeight];

            Parallel.For(0, gridHeight, y =>
            {
                int row = y * gridWidth;
                for (int x = 0; x < gridWidth; x++)
                {
                    int gridIdx = row + x;
                    int cellId = spatialGrid[gridIdx];

                    if (cellId >= 0 && cellId < cellIsLandById.Length && cellIsLandById[cellId])
                    {
                        vegetationPixels[gridIdx] = new Color(
                            cellVegetationTypeById[cellId] / 65535f,
                            cellVegetationDensityById[cellId],
                            0f,
                            0f);
                    }
                    else
                    {
                        vegetationPixels[gridIdx] = new Color(0f, 0f, 0f, 0f);
                    }
                }
            });

            vegetationTexture.SetPixels(vegetationPixels);
            vegetationTexture.Apply();
        }

        /// <summary>
        /// Generate a visual heightmap from cell height data with deterministic relief synthesis.
        /// This is render-only (water shading, displacement, and normal derivation), not gameplay elevation.
        /// Parallelized for performance.
        /// </summary>
        private void GenerateHeightmapTexture()
        {
            // Sample absolute heights from spatial grid (Y-up matches texture row order, no flip needed).
            float[] baseHeightData = new float[gridWidth * gridHeight];
            bool[] isLand = new bool[gridWidth * gridHeight];

            Parallel.For(0, gridHeight, y =>
            {
                int row = y * gridWidth;

                for (int x = 0; x < gridWidth; x++)
                {
                    int idx = row + x;
                    int cellId = spatialGrid[idx];

                    float height = 0f;
                    if (cellId >= 0 && cellId < cellIsLandById.Length)
                    {
                        height = cellHeight01ById[cellId];
                        isLand[idx] = cellIsLandById[cellId];
                    }

                    baseHeightData[idx] = height;
                }
            });

            float seaLevel01 = Elevation.NormalizeAbsolute01(Elevation.ResolveSeaLevel(mapData.Info), mapData.Info);
            float[] riverDistance = BuildRiverDistanceField(isLand);
            float[] heightData = ApplyLandAwareGaussianBlur(baseHeightData, isLand);
            ApplyRiverBankErosion(heightData, isLand, riverDistance, seaLevel01);

            Parallel.For(0, heightData.Length, i =>
            {
                if (!isLand[i])
                {
                    // Keep water heights untouched so depth shading stays grounded in gameplay height.
                    heightData[i] = baseHeightData[i];
                    return;
                }

                heightData[i] = Mathf.Clamp(heightData[i], seaLevel01 + ReliefLandMinAboveSea, 1f);
            });

            GenerateReliefNormalTexture(heightData, isLand);

            // Create texture
            heightmapTexture = new Texture2D(gridWidth, gridHeight, TextureFormat.RFloat, false);
            heightmapTexture.name = "HeightmapTexture";
            heightmapTexture.filterMode = FilterMode.Bilinear;
            heightmapTexture.wrapMode = TextureWrapMode.Clamp;
            heightmapTexture.SetPixelData(heightData, 0);
            heightmapTexture.Apply();

            Debug.Log($"MapOverlayManager: Generated heightmap {gridWidth}x{gridHeight} with relief synthesis");
        }

        private void GenerateReliefNormalTexture(float[] heightData, bool[] isLand)
        {
            int size = gridWidth * gridHeight;
            var normalPixels = new Color[size];

            float[] normalSource = (float[])heightData.Clone();
            for (int i = 0; i < ReliefNormalPreBlurPasses; i++)
                normalSource = ApplyLandAwareGaussianBlur(normalSource, isLand);

            float scaleX = gridWidth > 1 ? (gridWidth - 1) * ReliefNormalDerivativeScale : 1f;
            float scaleY = gridHeight > 1 ? (gridHeight - 1) * ReliefNormalDerivativeScale : 1f;

            Parallel.For(0, gridHeight, y =>
            {
                int row = y * gridWidth;

                for (int x = 0; x < gridWidth; x++)
                {
                    int idx = row + x;
                    if (!isLand[idx])
                    {
                        normalPixels[idx] = new Color(0.5f, 1f, 0.5f, 1f);
                        continue;
                    }

                    float center = normalSource[idx];

                    float h00 = SampleNormalHeight(normalSource, isLand, x - 1, y - 1, center);
                    float h10 = SampleNormalHeight(normalSource, isLand, x, y - 1, center);
                    float h20 = SampleNormalHeight(normalSource, isLand, x + 1, y - 1, center);
                    float h01 = SampleNormalHeight(normalSource, isLand, x - 1, y, center);
                    float h21 = SampleNormalHeight(normalSource, isLand, x + 1, y, center);
                    float h02 = SampleNormalHeight(normalSource, isLand, x - 1, y + 1, center);
                    float h12 = SampleNormalHeight(normalSource, isLand, x, y + 1, center);
                    float h22 = SampleNormalHeight(normalSource, isLand, x + 1, y + 1, center);

                    // Sobel gradient gives smoother normals than raw central differences.
                    float gx = (h20 + 2f * h21 + h22) - (h00 + 2f * h01 + h02);
                    float gy = (h02 + 2f * h12 + h22) - (h00 + 2f * h10 + h20);
                    float ddx = gx * 0.125f * scaleX;
                    float ddy = gy * 0.125f * scaleY;

                    Vector3 normal = new Vector3(-ddx, 1f, -ddy).normalized;
                    normalPixels[idx] = new Color(
                        normal.x * 0.5f + 0.5f,
                        normal.y * 0.5f + 0.5f,
                        normal.z * 0.5f + 0.5f,
                        1f);
                }
            });

            reliefNormalTexture = new Texture2D(gridWidth, gridHeight, TextureFormat.RGBA32, false, true);
            reliefNormalTexture.name = "ReliefNormalTexture";
            reliefNormalTexture.filterMode = FilterMode.Bilinear;
            reliefNormalTexture.wrapMode = TextureWrapMode.Clamp;
            reliefNormalTexture.SetPixels(normalPixels);
            reliefNormalTexture.Apply();

            TextureDebugger.SaveTexture(reliefNormalTexture, "relief_normal");
            Debug.Log($"MapOverlayManager: Generated relief normal map {gridWidth}x{gridHeight}");
        }

        private float SampleNormalHeight(float[] source, bool[] isLand, int x, int y, float fallback)
        {
            int cx = Mathf.Clamp(x, 0, gridWidth - 1);
            int cy = Mathf.Clamp(y, 0, gridHeight - 1);
            int idx = cy * gridWidth + cx;
            return isLand[idx] ? source[idx] : fallback;
        }

        private float[] ApplyLandAwareGaussianBlur(float[] source, bool[] isLand)
        {
            int size = source.Length;
            var horizontal = new float[size];
            var output = new float[size];

            Parallel.For(0, gridHeight, y =>
            {
                int row = y * gridWidth;
                for (int x = 0; x < gridWidth; x++)
                {
                    int idx = row + x;
                    if (!isLand[idx])
                    {
                        horizontal[idx] = source[idx];
                        continue;
                    }

                    float sum = 0f;
                    float weightSum = 0f;
                    for (int k = -ReliefBlurRadius; k <= ReliefBlurRadius; k++)
                    {
                        int sx = Mathf.Clamp(x + k, 0, gridWidth - 1);
                        int sampleIdx = row + sx;

                        float w = ReliefGaussianKernel[k + ReliefBlurRadius];
                        if (isLand[sampleIdx] != isLand[idx])
                            w *= ReliefBlurCrossClassWeight;

                        sum += source[sampleIdx] * w;
                        weightSum += w;
                    }

                    horizontal[idx] = weightSum > 0f ? sum / weightSum : source[idx];
                }
            });

            Parallel.For(0, gridHeight, y =>
            {
                int row = y * gridWidth;
                for (int x = 0; x < gridWidth; x++)
                {
                    int idx = row + x;
                    if (!isLand[idx])
                    {
                        output[idx] = source[idx];
                        continue;
                    }

                    float sum = 0f;
                    float weightSum = 0f;
                    for (int k = -ReliefBlurRadius; k <= ReliefBlurRadius; k++)
                    {
                        int sy = Mathf.Clamp(y + k, 0, gridHeight - 1);
                        int sampleIdx = sy * gridWidth + x;

                        float w = ReliefGaussianKernel[k + ReliefBlurRadius];
                        if (isLand[sampleIdx] != isLand[idx])
                            w *= ReliefBlurCrossClassWeight;

                        sum += horizontal[sampleIdx] * w;
                        weightSum += w;
                    }

                    output[idx] = weightSum > 0f ? sum / weightSum : source[idx];
                }
            });

            return output;
        }

        private void ApplyRiverBankErosion(float[] heightData, bool[] isLand, float[] riverDistance, float seaLevel01)
        {
            if (riverDistance == null || riverDistance.Length != heightData.Length)
                return;

            float minLandHeight = seaLevel01 + ReliefLandMinAboveSea;
            Parallel.For(0, gridHeight, y =>
            {
                int row = y * gridWidth;
                for (int x = 0; x < gridWidth; x++)
                {
                    int idx = row + x;
                    if (!isLand[idx])
                        continue;

                    float distance = riverDistance[idx];
                    if (distance >= ReliefErosionRadiusTexels)
                        continue;

                    float t = 1f - Mathf.Clamp01(distance / ReliefErosionRadiusTexels);
                    float carve = t * t * ReliefErosionStrength;
                    heightData[idx] = Mathf.Max(minLandHeight, heightData[idx] - carve);
                }
            });
        }

        private float[] BuildRiverDistanceField(bool[] isLand)
        {
            if (riverMaskPixels == null || riverMaskPixels.Length != isLand.Length)
                return null;

            int size = isLand.Length;
            var dist = new float[size];
            bool hasRiver = false;

            for (int i = 0; i < size; i++)
            {
                if (!isLand[i])
                {
                    dist[i] = 255f;
                    continue;
                }

                bool isRiver = riverMaskPixels[i] >= 8;
                dist[i] = isRiver ? 0f : 255f;
                hasRiver |= isRiver;
            }

            if (!hasRiver)
                return null;

            RunChamferTransform(dist, isLand);
            return dist;
        }

        /// <summary>
        /// Generate river mask texture by rasterizing river paths.
        /// Rivers are "knocked out" of the land in the shader, showing water underneath.
        /// </summary>
        private void GenerateRiverMaskTexture()
        {
            riverMaskTexture = new Texture2D(gridWidth, gridHeight, TextureFormat.R8, false);
            riverMaskTexture.name = "RiverMaskTexture";
            riverMaskTexture.filterMode = FilterMode.Bilinear;
            riverMaskTexture.wrapMode = TextureWrapMode.Clamp;

            riverMaskPixels = GenerateRiverMaskPixels();
            riverMaskTexture.LoadRawTextureData(riverMaskPixels);
            riverMaskTexture.Apply();

            TextureDebugger.SaveTexture(riverMaskTexture, "river_mask");
            Debug.Log($"MapOverlayManager: Generated river mask {gridWidth}x{gridHeight} ({mapData.Rivers.Count} rivers)");
        }

        private byte[] GenerateRiverMaskPixels()
        {
            var pixels = new byte[gridWidth * gridHeight];

            float scale = resolutionMultiplier;

            // River width settings
            float baseWidth = 0.6f * resolutionMultiplier;  // Minimum river width in pixels
            float widthScale = 0.3f * resolutionMultiplier; // Scale factor for discharge-based width

            foreach (var river in mapData.Rivers)
            {
                // Get river path points from vertex positions or cell centers
                var pathPoints = new List<Vector2>();
                if (river.Points != null && river.Points.Count >= 2)
                {
                    foreach (var pt in river.Points)
                    {
                        // Y-up data coords match texture row order directly
                        float x = pt.X * scale;
                        float y = pt.Y * scale;
                        var (wx, wy) = DomainWarp.Warp(x, y);
                        pathPoints.Add(new Vector2(wx, wy));
                    }
                }
                else if (river.CellPath != null && river.CellPath.Count >= 2)
                {
                    foreach (int cellId in river.CellPath)
                    {
                        if (mapData.CellById.TryGetValue(cellId, out var cell))
                        {
                            float x = cell.Center.X * scale;
                            float y = cell.Center.Y * scale;
                            var (wx, wy) = DomainWarp.Warp(x, y);
                            pathPoints.Add(new Vector2(wx, wy));
                        }
                    }
                }

                if (pathPoints.Count < 2)
                    continue;

                // Smooth the path using Catmull-Rom interpolation
                var smoothedPoints = SmoothPath(pathPoints, 4);

                // Calculate max width for this river
                float maxWidth = baseWidth + river.Width * widthScale;

                // Draw the river as a series of thick line segments
                for (int i = 0; i < smoothedPoints.Count - 1; i++)
                {
                    // Width tapers from 30% at source to 100% at mouth
                    float t = (float)i / (smoothedPoints.Count - 1);
                    float width = maxWidth * (0.3f + 0.7f * t);

                    DrawThickLine(pixels, smoothedPoints[i], smoothedPoints[i + 1], width);
                }


            }

            return pixels;
        }

        /// <summary>
        /// Smooth a path using Catmull-Rom spline interpolation.
        /// </summary>
        private List<Vector2> SmoothPath(List<Vector2> points, int samplesPerSegment)
        {
            if (points.Count < 2)
                return points;

            var result = new List<Vector2>();

            for (int i = 0; i < points.Count - 1; i++)
            {
                // Get 4 control points for Catmull-Rom (clamped at ends)
                Vector2 p0 = points[Mathf.Max(0, i - 1)];
                Vector2 p1 = points[i];
                Vector2 p2 = points[i + 1];
                Vector2 p3 = points[Mathf.Min(points.Count - 1, i + 2)];

                for (int j = 0; j < samplesPerSegment; j++)
                {
                    float t = (float)j / samplesPerSegment;
                    result.Add(CatmullRom(p0, p1, p2, p3, t));
                }
            }

            // Add final point
            result.Add(points[points.Count - 1]);

            return result;
        }

        /// <summary>
        /// Catmull-Rom spline interpolation.
        /// </summary>
        private Vector2 CatmullRom(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
        {
            float t2 = t * t;
            float t3 = t2 * t;

            return 0.5f * (
                (2f * p1) +
                (-p0 + p2) * t +
                (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
                (-p0 + 3f * p1 - 3f * p2 + p3) * t3
            );
        }

        /// <summary>
        /// Draw a thick anti-aliased line into the pixel buffer.
        /// </summary>
        private void DrawThickLine(byte[] pixels, Vector2 start, Vector2 end, float width)
        {
            // Calculate line direction and perpendicular
            Vector2 dir = (end - start).normalized;
            Vector2 perp = new Vector2(-dir.y, dir.x);
            float halfWidth = width * 0.5f;

            // Calculate bounding box
            float minX = Mathf.Min(start.x, end.x) - halfWidth - 1;
            float maxX = Mathf.Max(start.x, end.x) + halfWidth + 1;
            float minY = Mathf.Min(start.y, end.y) - halfWidth - 1;
            float maxY = Mathf.Max(start.y, end.y) + halfWidth + 1;

            int x0 = Mathf.Max(0, Mathf.FloorToInt(minX));
            int x1 = Mathf.Min(gridWidth - 1, Mathf.CeilToInt(maxX));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(minY));
            int y1 = Mathf.Min(gridHeight - 1, Mathf.CeilToInt(maxY));

            float lineLength = (end - start).magnitude;
            if (lineLength < 0.001f)
                return;

            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                    Vector2 toP = p - start;

                    // Project onto line
                    float along = Vector2.Dot(toP, dir);
                    along = Mathf.Clamp(along, 0, lineLength);

                    // Find closest point on line segment
                    Vector2 closest = start + dir * along;
                    float dist = (p - closest).magnitude;

                    // Calculate coverage (1 inside, 0 outside, smooth transition at edge)
                    float coverage = 1f - Mathf.Clamp01((dist - halfWidth + 0.5f) / 1f);

                    if (coverage > 0)
                    {
                        int idx = y * gridWidth + x;
                        // Max blend (river overwrites)
                        int newVal = Mathf.RoundToInt(coverage * 255);
                        if (newVal > pixels[idx])
                            pixels[idx] = (byte)newVal;
                    }
                }
            }
        }

        /// <summary>
        /// Two-pass chamfer distance transform (forward + backward).
        /// Approximates Euclidean distance using 3-4 weights (orthCost=1, diagCost=1.414).
        /// Operates in-place on dist[], which should be pre-seeded with 0 at boundary pixels
        /// and 255 elsewhere. isLand[] masks which pixels participate.
        /// </summary>
        private void RunChamferTransform(float[] dist, bool[] isLand)
        {
            const float orthCost = 1f;
            const float diagCost = 1.414f;

            // Forward pass: top-to-bottom, left-to-right
            for (int y = 1; y < gridHeight; y++)
            {
                for (int x = 0; x < gridWidth; x++)
                {
                    int idx = y * gridWidth + x;
                    if (!isLand[idx]) continue;

                    if (x > 0) dist[idx] = Mathf.Min(dist[idx], dist[idx - 1] + orthCost);
                    if (x > 0) dist[idx] = Mathf.Min(dist[idx], dist[(y - 1) * gridWidth + x - 1] + diagCost);
                    dist[idx] = Mathf.Min(dist[idx], dist[(y - 1) * gridWidth + x] + orthCost);
                    if (x < gridWidth - 1) dist[idx] = Mathf.Min(dist[idx], dist[(y - 1) * gridWidth + x + 1] + diagCost);
                }
            }

            // Backward pass: bottom-to-top, right-to-left
            for (int y = gridHeight - 2; y >= 0; y--)
            {
                for (int x = gridWidth - 1; x >= 0; x--)
                {
                    int idx = y * gridWidth + x;
                    if (!isLand[idx]) continue;

                    if (x < gridWidth - 1) dist[idx] = Mathf.Min(dist[idx], dist[idx + 1] + orthCost);
                    if (x < gridWidth - 1) dist[idx] = Mathf.Min(dist[idx], dist[(y + 1) * gridWidth + x + 1] + diagCost);
                    dist[idx] = Mathf.Min(dist[idx], dist[(y + 1) * gridWidth + x] + orthCost);
                    if (x > 0) dist[idx] = Mathf.Min(dist[idx], dist[(y + 1) * gridWidth + x - 1] + diagCost);
                }
            }
        }

        /// <summary>
        /// Convert float distance field to byte array, capping at 255.
        /// </summary>
        private static byte[] DistToBytes(float[] dist)
        {
            byte[] pixels = new byte[dist.Length];
            for (int i = 0; i < dist.Length; i++)
                pixels[i] = (byte)Mathf.Min(255, Mathf.RoundToInt(dist[i]));
            return pixels;
        }

        private readonly struct AdministrativeBorderDistPixels
        {
            public readonly byte[] Realm;
            public readonly byte[] Province;
            public readonly byte[] County;

            public AdministrativeBorderDistPixels(byte[] realm, byte[] province, byte[] county)
            {
                Realm = realm;
                Province = province;
                County = county;
            }
        }

        /// <summary>
        /// Generate realm/province/county border distance textures together so we can
        /// reuse a single land + boundary classification pass.
        /// </summary>
        private void GenerateAdministrativeBorderDistTextures()
        {
            Profiler.Begin("GenerateAdministrativeBorderDistPixels");
            AdministrativeBorderDistPixels pixels = GenerateAdministrativeBorderDistPixels();
            Profiler.End();

            realmBorderDistTexture = CreateBorderDistTexture("RealmBorderDistTexture", pixels.Realm, "realm_border_dist");
            provinceBorderDistTexture = CreateBorderDistTexture("ProvinceBorderDistTexture", pixels.Province, "province_border_dist");
            countyBorderDistTexture = CreateBorderDistTexture("CountyBorderDistTexture", pixels.County, "county_border_dist");

            Debug.Log($"MapOverlayManager: Generated administrative border distance textures {gridWidth}x{gridHeight}");
        }

        private AdministrativeBorderDistPixels GenerateAdministrativeBorderDistPixels()
        {
            int size = gridWidth * gridHeight;

            int[] realmGrid = new int[size];
            int[] provinceGrid = new int[size];
            int[] countyGrid = new int[size];
            bool[] isLand = new bool[size];

            for (int i = 0; i < size; i++)
            {
                int cellId = spatialGrid[i];
                if (cellId >= 0 && cellId < cellIsLandById.Length && cellIsLandById[cellId])
                {
                    realmGrid[i] = cellRealmIdById[cellId];
                    provinceGrid[i] = cellProvinceIdById[cellId];
                    countyGrid[i] = cellCountyIdById[cellId];
                    isLand[i] = true;
                }
                else
                {
                    realmGrid[i] = -1;
                    provinceGrid[i] = -1;
                    countyGrid[i] = -1;
                    isLand[i] = false;
                }
            }

            float[] realmDist = new float[size];
            float[] provinceDist = new float[size];
            float[] countyDist = new float[size];
            Array.Fill(realmDist, 255f);
            Array.Fill(provinceDist, 255f);
            Array.Fill(countyDist, 255f);

            // Single seed scan for all administrative boundary classes.
            Parallel.For(0, gridHeight, y =>
            {
                int row = y * gridWidth;
                for (int x = 0; x < gridWidth; x++)
                {
                    int idx = row + x;
                    if (!isLand[idx])
                        continue;

                    int realm = realmGrid[idx];
                    int province = provinceGrid[idx];
                    int county = countyGrid[idx];

                    bool realmBoundary = false;
                    bool provinceBoundary = false;
                    bool countyBoundary = false;

                    for (int dy = -1; dy <= 1 && !(realmBoundary && provinceBoundary && countyBoundary); dy++)
                    {
                        for (int dx = -1; dx <= 1 && !(realmBoundary && provinceBoundary && countyBoundary); dx++)
                        {
                            if (dx == 0 && dy == 0)
                                continue;

                            int nx = x + dx;
                            int ny = y + dy;
                            if (nx < 0 || nx >= gridWidth || ny < 0 || ny >= gridHeight)
                                continue;

                            int nIdx = ny * gridWidth + nx;
                            if (!isLand[nIdx])
                                continue;

                            int nRealm = realmGrid[nIdx];
                            if (!realmBoundary && nRealm != realm)
                            {
                                realmBoundary = true;
                                continue;
                            }

                            int nProvince = provinceGrid[nIdx];
                            if (!provinceBoundary && nRealm == realm && nProvince != province)
                            {
                                provinceBoundary = true;
                                continue;
                            }

                            if (!countyBoundary && nRealm == realm && nProvince == province && countyGrid[nIdx] != county)
                            {
                                countyBoundary = true;
                            }
                        }
                    }

                    if (realmBoundary)
                        realmDist[idx] = 0f;
                    if (provinceBoundary)
                        provinceDist[idx] = 0f;
                    if (countyBoundary)
                        countyDist[idx] = 0f;
                }
            });

            Profiler.Begin("RunAdministrativeBorderChamferTransforms");
            Task realmTask = Task.Run(() => RunChamferTransform(realmDist, isLand));
            Task provinceTask = Task.Run(() => RunChamferTransform(provinceDist, isLand));
            Task countyTask = Task.Run(() => RunChamferTransform(countyDist, isLand));
            Task.WaitAll(realmTask, provinceTask, countyTask);
            Profiler.End();

            return new AdministrativeBorderDistPixels(
                DistToBytes(realmDist),
                DistToBytes(provinceDist),
                DistToBytes(countyDist));
        }

        private Texture2D CreateBorderDistTexture(string textureName, byte[] pixels, string debugName)
        {
            var texture = new Texture2D(gridWidth, gridHeight, TextureFormat.R8, false);
            texture.name = textureName;
            texture.filterMode = FilterMode.Bilinear;
            texture.anisoLevel = 8;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.LoadRawTextureData(pixels);
            texture.Apply();
            TextureDebugger.SaveTexture(texture, debugName);
            return texture;
        }

        /// <summary>
        /// Generate color palette textures for realms, markets, and biomes.
        /// Province/county colors are derived from realm colors during mode-color resolve.
        /// </summary>
        private void GeneratePaletteTextures()
        {
            // Generate realm colors using HSV distribution
            politicalPalette = new PoliticalPalette(mapData);

            // Realm palette
            realmPaletteTexture = new Texture2D(256, 1, TextureFormat.RGBA32, false);
            realmPaletteTexture.name = "RealmPalette";
            realmPaletteTexture.filterMode = FilterMode.Point;
            realmPaletteTexture.wrapMode = TextureWrapMode.Clamp;

            var realmColors = new Color[256];
            realmColors[0] = new Color(0.5f, 0.5f, 0.5f);  // Neutral/no realm

            foreach (var realm in mapData.Realms)
            {
                if (realm.Id > 0 && realm.Id < 256)
                {
                    var c = politicalPalette.GetRealmColor(realm.Id);
                    realmColors[realm.Id] = new Color(c.R / 255f, c.G / 255f, c.B / 255f);
                }
            }
            realmPaletteTexture.SetPixels(realmColors);
            realmPaletteTexture.Apply();

            // Market palette (pre-populated with zone colors, updated when economy is set)
            marketPaletteTexture = new Texture2D(256, 1, TextureFormat.RGBA32, false);
            marketPaletteTexture.name = "MarketPalette";
            marketPaletteTexture.filterMode = FilterMode.Point;
            marketPaletteTexture.wrapMode = TextureWrapMode.Clamp;

            var marketColors = new Color[256];
            marketColors[0] = new Color(0.6f, 0.6f, 0.6f);  // No market - light gray

            for (int i = 1; i < 256; i++)
            {
                int colorIdx = (i - 1) % MarketZoneColors.Length;
                marketColors[i] = MarketZoneColors[colorIdx];
            }
            marketPaletteTexture.SetPixels(marketColors);
            marketPaletteTexture.Apply();

            // Biome palette
            biomePaletteTexture = new Texture2D(256, 1, TextureFormat.RGBA32, false);
            biomePaletteTexture.name = "BiomePalette";
            biomePaletteTexture.filterMode = FilterMode.Point;
            biomePaletteTexture.wrapMode = TextureWrapMode.Clamp;

            var biomeColors = new Color[256];
            biomeColors[0] = new Color(0.4f, 0.6f, 0.4f);  // Default green

            foreach (var biome in mapData.Biomes)
            {
                if (biome.Id >= 0 && biome.Id < 256)
                {
                    biomeColors[biome.Id] = new Color(
                        biome.Color.R / 255f,
                        biome.Color.G / 255f,
                        biome.Color.B / 255f
                    );
                }
            }
            biomePaletteTexture.SetPixels(biomeColors);
            biomePaletteTexture.Apply();

            Debug.Log($"MapOverlayManager: Generated palette textures ({mapData.Realms.Count} realms, {mapData.Biomes.Count} biomes)");
        }

        /// <summary>
        /// Apply all textures and initial settings to the terrain material.
        /// </summary>
        private void ApplyTexturesToMaterial()
        {
            if (styleMaterial == null) return;

            styleMaterial.SetTexture(PoliticalIdsTexId, politicalIdsTexture);
            styleMaterial.SetTexture(GeographyBaseTexId, geographyBaseTexture);
            styleMaterial.SetTexture(VegetationTexId, vegetationTexture);
            styleMaterial.SetTexture(OverlayTexId, politicalIdsTexture);
            styleMaterial.SetTexture(HeightmapTexId, heightmapTexture);
            styleMaterial.SetTexture(ReliefNormalTexId, reliefNormalTexture);
            styleMaterial.SetTexture(RiverMaskTexId, riverMaskTexture);
            styleMaterial.SetTexture(RealmPaletteTexId, realmPaletteTexture);
            styleMaterial.SetTexture(MarketPaletteTexId, marketPaletteTexture);
            styleMaterial.SetTexture(BiomePaletteTexId, biomePaletteTexture);
            styleMaterial.SetTexture(RealmBorderDistTexId, realmBorderDistTexture);
            styleMaterial.SetTexture(ProvinceBorderDistTexId, provinceBorderDistTexture);
            styleMaterial.SetTexture(CountyBorderDistTexId, countyBorderDistTexture);

            // Create market border dist texture (initially all-white = no borders, regenerated when economy is set).
            if (marketBorderDistTexture == null)
            {
                marketBorderDistTexture = new Texture2D(gridWidth, gridHeight, TextureFormat.R8, false);
                marketBorderDistTexture.name = "MarketBorderDistTexture";
                marketBorderDistTexture.filterMode = FilterMode.Bilinear;
                marketBorderDistTexture.anisoLevel = 8;
                marketBorderDistTexture.wrapMode = TextureWrapMode.Clamp;
                var whitePixels = new byte[gridWidth * gridHeight];
                for (int i = 0; i < whitePixels.Length; i++)
                    whitePixels[i] = 255;
                marketBorderDistTexture.LoadRawTextureData(whitePixels);
                marketBorderDistTexture.Apply();
            }
            styleMaterial.SetTexture(MarketBorderDistTexId, marketBorderDistTexture);

            // Create road mask texture (initially all-black = no roads, regenerated when road state is set).
            if (roadDistTexture == null)
            {
                roadDistTexture = new Texture2D(gridWidth, gridHeight, TextureFormat.R8, false);
                roadDistTexture.name = "RoadMaskTexture";
                roadDistTexture.filterMode = FilterMode.Bilinear;
                roadDistTexture.anisoLevel = 8;
                roadDistTexture.wrapMode = TextureWrapMode.Clamp;
                // R8 textures initialize to 0 (black = no roads), just apply.
                roadDistTexture.Apply();
            }
            styleMaterial.SetTexture(RoadMaskTexId, roadDistTexture);

            // Create cell-to-market texture (16384 cells max, updated when economy is set)
            if (cellToMarketTexture == null)
            {
                cellToMarketTexture = new Texture2D(16384, 1, TextureFormat.RHalf, false);
                cellToMarketTexture.name = "CellToMarketTexture";
                cellToMarketTexture.filterMode = FilterMode.Point;
                cellToMarketTexture.wrapMode = TextureWrapMode.Clamp;
                // Initialize to 0 (no market)
                var emptyMarkets = new Color[16384];
                cellToMarketTexture.SetPixels(emptyMarkets);
                cellToMarketTexture.Apply();
            }
            styleMaterial.SetTexture(CellToMarketTexId, cellToMarketTexture);
            styleMaterial.SetFloat(SeaLevelId, Elevation.NormalizeAbsolute01(Elevation.ResolveSeaLevel(mapData.Info), mapData.Info));
            overlayOpacity = Mathf.Clamp01(GetMaterialFloatOr(OverlayOpacityId, DefaultOverlayOpacity));
            styleMaterial.SetFloat(OverlayOpacityId, overlayOpacity);
            styleMaterial.SetInt(OverlayEnabledId, 0);

            // Water layer properties are set via shader defaults + material Inspector
            // (not overwritten here so Inspector tweaks persist)

            // Default to political mode
            styleMaterial.SetInt(MapModeId, 1);
            styleMaterial.SetInt(DebugViewId, (int)ChannelDebugView.PoliticalIdsR);

            // Clear any persisted selection/hover from previous play session
            ClearSelection();
            ClearHover();

            RegenerateModeColorResolveTexture();
            SetOverlay(OverlayLayer.None);
        }

        /// <summary>
        /// Swap the backing material while keeping generated textures/state.
        /// Useful for mode-driven render-style material switches (e.g., Flat/Biome).
        /// </summary>
        public void RebindMaterial(Material material)
        {
            if (material == null || ReferenceEquals(styleMaterial, material))
                return;

            styleMaterial = material;
            ApplyTexturesToMaterial();
        }

        public OverlayLayer CurrentOverlay => currentOverlayLayer;

        public void SetOverlay(OverlayLayer layer)
        {
            if (currentOverlayLayer == layer)
                return;

            currentOverlayLayer = layer;
            ApplyOverlayToMaterial();
        }

        public float OverlayOpacity => overlayOpacity;

        public void SetOverlayOpacity(float opacity)
        {
            float clamped = Mathf.Clamp01(opacity);
            if (Mathf.Abs(overlayOpacity - clamped) < 0.0001f)
                return;

            overlayOpacity = clamped;
            if (styleMaterial != null)
                styleMaterial.SetFloat(OverlayOpacityId, overlayOpacity);
        }

        private void ApplyOverlayToMaterial()
        {
            if (styleMaterial == null)
                return;

            overlayOpacity = Mathf.Clamp01(GetMaterialFloatOr(OverlayOpacityId, overlayOpacity));
            styleMaterial.SetFloat(OverlayOpacityId, overlayOpacity);

            if (currentOverlayLayer == OverlayLayer.None)
            {
                styleMaterial.SetTexture(OverlayTexId, politicalIdsTexture);
                styleMaterial.SetInt(OverlayEnabledId, 0);
                return;
            }

            Texture2D overlayTexture = GetOrCreateOverlayTexture(currentOverlayLayer);
            if (overlayTexture == null)
            {
                styleMaterial.SetTexture(OverlayTexId, politicalIdsTexture);
                styleMaterial.SetInt(OverlayEnabledId, 0);
                return;
            }

            styleMaterial.SetTexture(OverlayTexId, overlayTexture);
            styleMaterial.SetInt(OverlayEnabledId, 1);
        }

        /// <summary>
        /// Set the economy state to enable market-related overlays.
        /// Updates the county-to-market texture (indexed by countyId) and regenerates market palette.
        /// </summary>
        public void SetEconomyState(EconomyState economy)
        {
            economyState = economy;
            InvalidateModeColorResolveCache(MapView.MapMode.Market);
            InvalidateModeColorResolveCache(MapView.MapMode.MarketAccess);

            if (economy == null || economy.CountyToMarket == null)
            {
                if (currentMapMode == MapView.MapMode.Market ||
                    currentMapMode == MapView.MapMode.MarketAccess)
                    RegenerateModeColorResolveTexture();
                return;
            }

            // Regenerate market palette based on hub realm colors
            RegenerateMarketPalette(economy);

            // Update county-to-market lookup texture (indexed by countyId)
            var marketPixels = new Color[16384];
            foreach (var kvp in economy.CountyToMarket)
            {
                int countyId = kvp.Key;
                int marketId = kvp.Value;

                if (countyId >= 0 && countyId < 16384)
                {
                    marketPixels[countyId] = new Color(marketId / 65535f, 0, 0, 1);
                }
            }

            cellToMarketTexture.SetPixels(marketPixels);
            cellToMarketTexture.Apply();

            int countyToMarketHash = ComputeCountyToMarketHash(economy.CountyToMarket);
            bool canReuseCachedMarketBorder =
                marketBorderDistTexture != null &&
                cachedCountyToMarketHash != 0 &&
                cachedCountyToMarketHash == countyToMarketHash;

            if (canReuseCachedMarketBorder)
            {
                Debug.Log("MapOverlayManager: Reused cached market border texture");
            }
            else
            {
                // Regenerate market border distance texture now that zone assignments are known.
                RegenerateMarketBorderDistTexture(economy);
                cachedCountyToMarketHash = countyToMarketHash;
                overlayCacheDirty = true;
            }

            if (currentMapMode == MapView.MapMode.Market ||
                currentMapMode == MapView.MapMode.MarketAccess)
                RegenerateModeColorResolveTexture();
            if (currentMapMode != MapView.MapMode.Market)
                pendingMarketModePrewarm = true;

            Debug.Log($"MapOverlayManager: Updated county-to-market texture ({economy.CountyToMarket.Count} counties mapped)");
        }


        /// <summary>
        /// Regenerate market palette based on hub realm colors.
        /// Each market's color is derived from its hub cell's state.
        /// </summary>
        private void RegenerateMarketPalette(EconomyState economy)
        {
            if (politicalPalette == null || marketPaletteTexture == null)
                return;

            var marketColors = new Color[256];
            marketColors[0] = new Color(0.5f, 0.5f, 0.5f);  // No market - neutral grey

            foreach (var market in economy.Markets.Values)
            {
                if (market.Id <= 0 || market.Id >= 256)
                    continue;

                // Get the hub cell's realm color
                if (mapData.CellById.TryGetValue(market.LocationCellId, out var hubCell))
                {
                    var c = politicalPalette.GetRealmColor(hubCell.RealmId);
                    marketColors[market.Id] = new Color(c.R / 255f, c.G / 255f, c.B / 255f);
                }
                else
                {
                    // Fallback to zone colors if hub not found
                    int colorIdx = (market.Id - 1) % MarketZoneColors.Length;
                    marketColors[market.Id] = MarketZoneColors[colorIdx];
                }
            }

            marketPaletteTexture.SetPixels(marketColors);
            marketPaletteTexture.Apply();

            Debug.Log($"MapOverlayManager: Regenerated market palette ({economy.Markets.Count} markets)");
        }

        /// <summary>
        /// Regenerate market border distance texture from county-to-market mapping.
        /// Boundary condition: land pixel adjacent to land pixel whose county maps to a different market.
        /// </summary>
        private void RegenerateMarketBorderDistTexture(EconomyState economy)
        {
            if (marketBorderDistTexture == null) return;

            int size = gridWidth * gridHeight;

            // Build market ID grid from spatial grid + county-to-market mapping
            int[] marketGrid = new int[size];
            bool[] isLand = new bool[size];

            for (int i = 0; i < size; i++)
            {
                int cellId = spatialGrid[i];
                if (cellId >= 0 && mapData.CellById.TryGetValue(cellId, out var cell) && cell.IsLand)
                {
                    isLand[i] = true;
                    if (economy.CountyToMarket.TryGetValue(cell.CountyId, out int mktId))
                        marketGrid[i] = mktId;
                    else
                        marketGrid[i] = 0;
                }
                else
                {
                    marketGrid[i] = -1;
                    isLand[i] = false;
                }
            }

            // Distance field — initialize to max
            float[] dist = new float[size];
            for (int i = 0; i < size; i++)
                dist[i] = 255f;

            // Seed: land pixels adjacent to land pixel with different market
            for (int y = 0; y < gridHeight; y++)
            {
                for (int x = 0; x < gridWidth; x++)
                {
                    int idx = y * gridWidth + x;
                    if (!isLand[idx]) continue;

                    int market = marketGrid[idx];
                    bool isBoundary = false;

                    for (int dy = -1; dy <= 1 && !isBoundary; dy++)
                    {
                        for (int dx = -1; dx <= 1 && !isBoundary; dx++)
                        {
                            if (dx == 0 && dy == 0) continue;
                            int nx = x + dx, ny = y + dy;
                            if (nx < 0 || nx >= gridWidth || ny < 0 || ny >= gridHeight) continue;
                            int nIdx = ny * gridWidth + nx;
                            if (isLand[nIdx] && marketGrid[nIdx] != market)
                                isBoundary = true;
                        }
                    }

                    if (isBoundary)
                        dist[idx] = 0f;
                }
            }

            RunChamferTransform(dist, isLand);

            // Write to texture
            byte[] pixels = DistToBytes(dist);
            marketBorderDistTexture.LoadRawTextureData(pixels);
            marketBorderDistTexture.Apply();

            TextureDebugger.SaveTexture(marketBorderDistTexture, "market_border_dist");
            Debug.Log($"MapOverlayManager: Generated market border distance texture {gridWidth}x{gridHeight}");
        }

        private static bool IsModeResolveOverlay(MapView.MapMode mode)
        {
            return mode == MapView.MapMode.Political ||
                   mode == MapView.MapMode.Province ||
                   mode == MapView.MapMode.County ||
                   mode == MapView.MapMode.Market ||
                   mode == MapView.MapMode.TransportCost ||
                   mode == MapView.MapMode.MarketAccess;
        }

        private static MapView.MapMode ResolveCacheKeyForMode(MapView.MapMode mode)
        {
            return mode;
        }

        private int GetModeColorResolveRevision(MapView.MapMode cacheKey)
        {
            if (modeColorResolveRevisionByKey.TryGetValue(cacheKey, out int revision) && revision > 0)
                return revision;

            modeColorResolveRevisionByKey[cacheKey] = 1;
            return 1;
        }

        private void InvalidateModeColorResolveCache(MapView.MapMode mode)
        {
            MapView.MapMode cacheKey = ResolveCacheKeyForMode(mode);
            int currentRevision = GetModeColorResolveRevision(cacheKey);

            unchecked
            {
                int nextRevision = currentRevision + 1;
                if (nextRevision <= 0)
                    nextRevision = 1;
                modeColorResolveRevisionByKey[cacheKey] = nextRevision;
            }
        }

        private Texture2D GetOrCreateModeColorResolveTexture(MapView.MapMode mode)
        {
            MapView.MapMode cacheKey = ResolveCacheKeyForMode(mode);
            if (modeColorResolveCacheByMode.TryGetValue(cacheKey, out var cached) && cached != null)
            {
                if (cached.width == gridWidth && cached.height == gridHeight)
                    return cached;

                DestroyTexture(cached);
                modeColorResolveCacheByMode.Remove(cacheKey);
                modeColorResolveCacheRevisionByMode.Remove(cacheKey);
            }

            var texture = new Texture2D(gridWidth, gridHeight, TextureFormat.RGBA32, false);
            texture.name = $"ModeColorResolveTexture_{cacheKey}";
            texture.filterMode = FilterMode.Bilinear;
            texture.wrapMode = TextureWrapMode.Clamp;
            modeColorResolveCacheByMode[cacheKey] = texture;
            return texture;
        }

        private bool TryBindCachedModeColorResolveTexture(MapView.MapMode mode)
        {
            if (styleMaterial == null)
                return false;

            MapView.MapMode cacheKey = ResolveCacheKeyForMode(mode);
            if (!modeColorResolveCacheByMode.TryGetValue(cacheKey, out var cached) || cached == null)
                return false;

            if (cached.width != gridWidth || cached.height != gridHeight)
                return false;

            int currentRevision = GetModeColorResolveRevision(cacheKey);
            if (!modeColorResolveCacheRevisionByMode.TryGetValue(cacheKey, out int cachedRevision) ||
                cachedRevision != currentRevision)
            {
                return false;
            }

            modeColorResolveTexture = cached;
            styleMaterial.SetTexture(ModeColorResolveTexId, cached);
            styleMaterial.SetInt(UseModeColorResolveId, 1);
            return true;
        }

        private void RegenerateModeColorResolveTexture()
        {
            if (styleMaterial == null || mapData == null || spatialGrid == null)
                return;

            if (!IsModeResolveOverlay(currentMapMode))
            {
                styleMaterial.SetInt(UseModeColorResolveId, 0);
                return;
            }

            if (TryBindCachedModeColorResolveTexture(currentMapMode))
                return;

            modeColorResolveTexture = GetOrCreateModeColorResolveTexture(currentMapMode);
            int size = gridWidth * gridHeight;
            var resolved = new Color[size];

            Color[] rivers = riverMaskTexture.GetPixels();
            Color[] realmPalette = realmPaletteTexture.GetPixels();
            Color[] marketPalette = marketPaletteTexture.GetPixels();

            bool isLocalTransportMode = currentMapMode == MapView.MapMode.TransportCost;
            bool isMarketTransportMode = currentMapMode == MapView.MapMode.MarketAccess;
            if (isLocalTransportMode || isMarketTransportMode)
            {
                var values = new float[size];
                for (int i = 0; i < size; i++)
                    values[i] = float.NaN;

                float minValue = float.MaxValue;
                float maxValue = float.MinValue;

                for (int i = 0; i < size; i++)
                {
                    int cellId = spatialGrid[i];
                    if (cellId < 0 || !mapData.CellById.TryGetValue(cellId, out var cell))
                        continue;

                    if (!cell.IsLand || rivers[i].r > 0.5f)
                        continue;

                    float value;
                    bool hasValue;
                    if (isLocalTransportMode)
                    {
                        value = ComputeTransportCost(cell);
                        hasValue = true;
                    }
                    else
                    {
                        hasValue = TryGetAssignedMarketAccess(cellId, cell.CountyId, out value);
                    }

                    if (!hasValue || float.IsNaN(value) || float.IsInfinity(value))
                        continue;

                    values[i] = value;
                    minValue = Mathf.Min(minValue, value);
                    maxValue = Mathf.Max(maxValue, value);
                }

                bool minFinite = !float.IsNaN(minValue) && !float.IsInfinity(minValue);
                bool maxFinite = !float.IsNaN(maxValue) && !float.IsInfinity(maxValue);
                if (!minFinite || !maxFinite || maxValue <= minValue)
                {
                    minValue = 0f;
                    maxValue = 1f;
                }

                float range = Mathf.Max(0.0001f, maxValue - minValue);
                for (int i = 0; i < size; i++)
                {
                    int cellId = spatialGrid[i];
                    if (cellId < 0 || !mapData.CellById.TryGetValue(cellId, out var cell))
                        continue;

                    if (!cell.IsLand || rivers[i].r > 0.5f)
                        continue;

                    float value = values[i];
                    if (float.IsNaN(value) || float.IsInfinity(value))
                    {
                        Color missing = HeatMissingColor;
                        missing.a = ResolveNormalizedMarketId(cell, cellId);
                        resolved[i] = missing;
                        continue;
                    }

                    float normalized = Mathf.Clamp01((value - minValue) / range);
                    Color heat = EvaluateHeatColor(normalized);
                    heat.a = ResolveNormalizedMarketId(cell, cellId);
                    resolved[i] = heat;
                }
            }
            else
            {
                Dictionary<int, Color> provinceColorById = null;
                Dictionary<int, Color> countyColorById = null;
                if (currentMapMode == MapView.MapMode.Province ||
                    currentMapMode == MapView.MapMode.County)
                {
                    provinceColorById = BuildProvinceColorOverrides(realmPalette);
                    if (currentMapMode == MapView.MapMode.County)
                        countyColorById = BuildCountyColorOverrides(provinceColorById, realmPalette);
                }

                for (int i = 0; i < size; i++)
                {
                    int cellId = spatialGrid[i];
                    if (cellId < 0 || !mapData.CellById.TryGetValue(cellId, out var cell))
                        continue;

                    bool isCellWater = !cell.IsLand;
                    bool isRiver = rivers[i].r > 0.5f;
                    if (isCellWater || isRiver)
                        continue;

                    if (currentMapMode == MapView.MapMode.Market)
                    {
                        int marketId = 0;
                        if (economyState != null && economyState.CountyToMarket != null)
                            economyState.CountyToMarket.TryGetValue(cell.CountyId, out marketId);

                        Color marketColor = LookupPaletteColor(marketPalette, marketId);
                        marketColor.a = marketId <= 0 ? 0f : marketId / 65535f;
                        resolved[i] = marketColor;
                    }
                    else
                    {
                        Color politicalColor = LookupPaletteColor(realmPalette, cell.RealmId);
                        if (currentMapMode == MapView.MapMode.Province)
                        {
                            if (provinceColorById != null && provinceColorById.TryGetValue(cell.ProvinceId, out Color provinceColor))
                                politicalColor = provinceColor;
                            else
                                politicalColor = DeriveProvinceColorFromRealm(politicalColor, cell.ProvinceId);
                        }
                        else if (currentMapMode == MapView.MapMode.County)
                        {
                            if (countyColorById != null && countyColorById.TryGetValue(cell.CountyId, out Color countyColor))
                            {
                                politicalColor = countyColor;
                            }
                            else
                            {
                                if (provinceColorById != null && provinceColorById.TryGetValue(cell.ProvinceId, out Color provinceColor))
                                    politicalColor = DeriveCountyColorFromProvince(provinceColor, cell.CountyId);
                                else
                                {
                                    Color fallbackProvinceColor = DeriveProvinceColorFromRealm(politicalColor, cell.ProvinceId);
                                    politicalColor = DeriveCountyColorFromProvince(fallbackProvinceColor, cell.CountyId);
                                }
                            }
                        }
                        politicalColor.a = ResolveNormalizedMarketId(cell, cellId);
                        resolved[i] = politicalColor;
                    }
                }
            }

            modeColorResolveTexture.SetPixels(resolved);
            modeColorResolveTexture.Apply();
            styleMaterial.SetTexture(ModeColorResolveTexId, modeColorResolveTexture);
            styleMaterial.SetInt(UseModeColorResolveId, 1);
            MapView.MapMode cacheKey = ResolveCacheKeyForMode(currentMapMode);
            modeColorResolveCacheRevisionByMode[cacheKey] = GetModeColorResolveRevision(cacheKey);
            TextureDebugger.SaveTexture(modeColorResolveTexture, "mode_color_resolve");
        }

        private float ComputeTransportCost(Cell cell)
        {
            float cost = cell.MovementCost;
            return cost > 0 ? cost : OverlayDefaultMovementCost;
        }

        private static Color DeriveProvinceColorFromRealm(Color realmColor, int provinceId)
        {
            return DeriveProvinceColorFromRealmVariant(realmColor, provinceId, 0, out _);
        }

        private static Color DeriveCountyColorFromProvince(Color provinceColor, int countyId)
        {
            return DeriveCountyColorFromProvinceVariant(provinceColor, countyId, 0, out _);
        }

        private static float Hash01(uint value)
        {
            uint h = value;
            h ^= h >> 16;
            h *= 0x85ebca6bu;
            h ^= h >> 13;
            h *= 0xc2b2ae35u;
            h ^= h >> 16;
            return (h & 0x7fffffffu) / (float)0x7fffffffu;
        }

        private static Color DeriveProvinceColorFromRealmVariant(Color realmColor, int provinceId, int variantIndex, out Vector3 hsv)
        {
            Color.RGBToHSV(realmColor, out float h, out float s, out float v);

            if (provinceId <= 0)
            {
                hsv = new Vector3(h, s, v);
                return realmColor;
            }

            uint variant = (uint)Mathf.Max(0, variantIndex + 1);
            uint seed = ((uint)provinceId * 747796405u) ^ (variant * 2891336453u);

            float hueRand = Hash01(seed ^ 0x68bc21ebu) * 2f - 1f;
            float satRand = Hash01(seed ^ 0x02e5be93u) * 2f - 1f;
            float valRand = Hash01(seed ^ 0x967a889bu) * 2f - 1f;

            h = Mathf.Repeat(h + hueRand * (ProvinceHueShiftDegrees / 360f), 1f);
            s = Mathf.Clamp01(s + satRand * ProvinceSaturationShift);
            v = Mathf.Clamp01(v + valRand * ProvinceValueShift);
            hsv = new Vector3(h, s, v);

            Color derived = Color.HSVToRGB(h, s, v);
            derived.a = realmColor.a;
            return derived;
        }

        private static Color DeriveCountyColorFromProvinceVariant(Color provinceColor, int countyId, int variantIndex, out Vector3 hsv)
        {
            Color.RGBToHSV(provinceColor, out float h, out float s, out float v);

            if (countyId <= 0)
            {
                hsv = new Vector3(h, s, v);
                return provinceColor;
            }

            uint variant = (uint)Mathf.Max(0, variantIndex + 1);
            uint seed = ((uint)countyId * 1640531513u) ^ (variant * 1013904223u);

            float hueRand = Hash01(seed ^ 0x4f1bbcdcu) * 2f - 1f;
            float satRand = Hash01(seed ^ 0x8f2e7ab7u) * 2f - 1f;
            float valRand = Hash01(seed ^ 0xbd4f2699u) * 2f - 1f;

            h = Mathf.Repeat(h + hueRand * (CountyHueShiftDegrees / 360f), 1f);
            s = Mathf.Clamp01(s + satRand * CountySaturationShift);
            v = Mathf.Clamp01(v + valRand * CountyValueShift);
            hsv = new Vector3(h, s, v);

            Color derived = Color.HSVToRGB(h, s, v);
            derived.a = provinceColor.a;
            return derived;
        }

        private Dictionary<int, Color> BuildProvinceColorOverrides(Color[] realmPalette)
        {
            var provinceColorById = new Dictionary<int, Color>();
            if (mapData?.Provinces == null || mapData.Provinces.Count == 0 || mapData.CellById == null)
                return provinceColorById;

            var adjacencyByProvince = BuildProvinceAdjacency();
            var orderedProvinceIds = new List<int>(mapData.Provinces.Count);
            for (int i = 0; i < mapData.Provinces.Count; i++)
            {
                var province = mapData.Provinces[i];
                if (province != null && province.Id > 0)
                    orderedProvinceIds.Add(province.Id);
            }

            orderedProvinceIds.Sort((a, b) =>
            {
                int degreeA = adjacencyByProvince.TryGetValue(a, out var neighborsA) ? neighborsA.Count : 0;
                int degreeB = adjacencyByProvince.TryGetValue(b, out var neighborsB) ? neighborsB.Count : 0;
                int degreeCompare = degreeB.CompareTo(degreeA);
                return degreeCompare != 0 ? degreeCompare : a.CompareTo(b);
            });

            var provinceHsvById = new Dictionary<int, Vector3>(orderedProvinceIds.Count);
            const int candidateCount = 12;

            for (int i = 0; i < orderedProvinceIds.Count; i++)
            {
                int provinceId = orderedProvinceIds[i];
                if (!mapData.ProvinceById.TryGetValue(provinceId, out var province) || province == null)
                    continue;

                Color realmColor = LookupPaletteColor(realmPalette, province.RealmId);
                Color bestColor = DeriveProvinceColorFromRealmVariant(realmColor, provinceId, 0, out Vector3 bestHsv);
                float bestScore = float.NegativeInfinity;
                bool comparedAnyNeighbors = false;

                if (adjacencyByProvince.TryGetValue(provinceId, out var neighbors) && neighbors.Count > 0)
                {
                    for (int candidate = 0; candidate < candidateCount; candidate++)
                    {
                        Color candidateColor = DeriveProvinceColorFromRealmVariant(realmColor, provinceId, candidate, out Vector3 candidateHsv);
                        float minNeighborDistance = float.PositiveInfinity;
                        bool hasAssignedNeighbor = false;

                        foreach (int neighborProvinceId in neighbors)
                        {
                            if (!provinceHsvById.TryGetValue(neighborProvinceId, out Vector3 neighborHsv))
                                continue;

                            hasAssignedNeighbor = true;
                            float distance = ComputeHsvDistance(candidateHsv, neighborHsv);
                            if (distance < minNeighborDistance)
                                minNeighborDistance = distance;
                        }

                        if (!hasAssignedNeighbor)
                            continue;

                        comparedAnyNeighbors = true;
                        if (minNeighborDistance > bestScore)
                        {
                            bestScore = minNeighborDistance;
                            bestColor = candidateColor;
                            bestHsv = candidateHsv;
                        }
                    }
                }

                if (!comparedAnyNeighbors)
                    bestColor = DeriveProvinceColorFromRealmVariant(realmColor, provinceId, 0, out bestHsv);

                provinceColorById[provinceId] = bestColor;
                provinceHsvById[provinceId] = bestHsv;
            }

            return provinceColorById;
        }

        private Dictionary<int, Color> BuildCountyColorOverrides(Dictionary<int, Color> provinceColorById, Color[] realmPalette)
        {
            var countyColorById = new Dictionary<int, Color>();
            if (mapData?.Counties == null || mapData.Counties.Count == 0 || mapData.CellById == null || mapData.CountyById == null)
                return countyColorById;

            var adjacencyByCounty = BuildCountyAdjacency();
            var orderedCountyIds = new List<int>(mapData.Counties.Count);
            for (int i = 0; i < mapData.Counties.Count; i++)
            {
                var county = mapData.Counties[i];
                if (county != null && county.Id > 0)
                    orderedCountyIds.Add(county.Id);
            }

            orderedCountyIds.Sort((a, b) =>
            {
                int degreeA = adjacencyByCounty.TryGetValue(a, out var neighborsA) ? neighborsA.Count : 0;
                int degreeB = adjacencyByCounty.TryGetValue(b, out var neighborsB) ? neighborsB.Count : 0;
                int degreeCompare = degreeB.CompareTo(degreeA);
                return degreeCompare != 0 ? degreeCompare : a.CompareTo(b);
            });

            var countyHsvById = new Dictionary<int, Vector3>(orderedCountyIds.Count);
            const int candidateCount = 12;

            for (int i = 0; i < orderedCountyIds.Count; i++)
            {
                int countyId = orderedCountyIds[i];
                if (!mapData.CountyById.TryGetValue(countyId, out var county) || county == null)
                    continue;

                Color provinceColor = Color.white;
                if (provinceColorById != null &&
                    county.ProvinceId > 0 &&
                    provinceColorById.TryGetValue(county.ProvinceId, out Color resolvedProvinceColor))
                {
                    provinceColor = resolvedProvinceColor;
                }
                else
                {
                    Color realmColor = LookupPaletteColor(realmPalette, county.RealmId);
                    provinceColor = county.ProvinceId > 0
                        ? DeriveProvinceColorFromRealm(realmColor, county.ProvinceId)
                        : realmColor;
                }

                Color bestColor = DeriveCountyColorFromProvinceVariant(provinceColor, countyId, 0, out Vector3 bestHsv);
                float bestScore = float.NegativeInfinity;
                bool comparedAnyNeighbors = false;

                if (adjacencyByCounty.TryGetValue(countyId, out var neighbors) && neighbors.Count > 0)
                {
                    for (int candidate = 0; candidate < candidateCount; candidate++)
                    {
                        Color candidateColor = DeriveCountyColorFromProvinceVariant(provinceColor, countyId, candidate, out Vector3 candidateHsv);
                        float minNeighborDistance = float.PositiveInfinity;
                        bool hasAssignedNeighbor = false;

                        foreach (int neighborCountyId in neighbors)
                        {
                            if (!countyHsvById.TryGetValue(neighborCountyId, out Vector3 neighborHsv))
                                continue;

                            hasAssignedNeighbor = true;
                            float distance = ComputeHsvDistance(candidateHsv, neighborHsv);
                            if (distance < minNeighborDistance)
                                minNeighborDistance = distance;
                        }

                        if (!hasAssignedNeighbor)
                            continue;

                        comparedAnyNeighbors = true;
                        if (minNeighborDistance > bestScore)
                        {
                            bestScore = minNeighborDistance;
                            bestColor = candidateColor;
                            bestHsv = candidateHsv;
                        }
                    }
                }

                if (!comparedAnyNeighbors)
                    bestColor = DeriveCountyColorFromProvinceVariant(provinceColor, countyId, 0, out bestHsv);

                countyColorById[countyId] = bestColor;
                countyHsvById[countyId] = bestHsv;
            }

            return countyColorById;
        }

        private Dictionary<int, HashSet<int>> BuildProvinceAdjacency()
        {
            var adjacencyByProvince = new Dictionary<int, HashSet<int>>();
            if (mapData?.Cells == null || mapData.CellById == null)
                return adjacencyByProvince;

            for (int i = 0; i < mapData.Cells.Count; i++)
            {
                var cell = mapData.Cells[i];
                if (cell == null || cell.ProvinceId <= 0 || cell.NeighborIds == null)
                    continue;

                int provinceId = cell.ProvinceId;
                if (!adjacencyByProvince.TryGetValue(provinceId, out var neighbors))
                {
                    neighbors = new HashSet<int>();
                    adjacencyByProvince[provinceId] = neighbors;
                }

                for (int ni = 0; ni < cell.NeighborIds.Count; ni++)
                {
                    int neighborCellId = cell.NeighborIds[ni];
                    if (!mapData.CellById.TryGetValue(neighborCellId, out var neighborCell) || neighborCell == null)
                        continue;

                    int neighborProvinceId = neighborCell.ProvinceId;
                    if (neighborProvinceId <= 0 || neighborProvinceId == provinceId)
                        continue;

                    neighbors.Add(neighborProvinceId);

                    if (!adjacencyByProvince.TryGetValue(neighborProvinceId, out var reverseNeighbors))
                    {
                        reverseNeighbors = new HashSet<int>();
                        adjacencyByProvince[neighborProvinceId] = reverseNeighbors;
                    }

                    reverseNeighbors.Add(provinceId);
                }
            }

            return adjacencyByProvince;
        }

        private Dictionary<int, HashSet<int>> BuildCountyAdjacency()
        {
            var adjacencyByCounty = new Dictionary<int, HashSet<int>>();
            if (mapData?.Cells == null || mapData.CellById == null)
                return adjacencyByCounty;

            for (int i = 0; i < mapData.Cells.Count; i++)
            {
                var cell = mapData.Cells[i];
                if (cell == null || cell.CountyId <= 0 || cell.NeighborIds == null)
                    continue;

                int countyId = cell.CountyId;
                if (!adjacencyByCounty.TryGetValue(countyId, out var neighbors))
                {
                    neighbors = new HashSet<int>();
                    adjacencyByCounty[countyId] = neighbors;
                }

                for (int ni = 0; ni < cell.NeighborIds.Count; ni++)
                {
                    int neighborCellId = cell.NeighborIds[ni];
                    if (!mapData.CellById.TryGetValue(neighborCellId, out var neighborCell) || neighborCell == null)
                        continue;

                    int neighborCountyId = neighborCell.CountyId;
                    if (neighborCountyId <= 0 || neighborCountyId == countyId)
                        continue;

                    neighbors.Add(neighborCountyId);

                    if (!adjacencyByCounty.TryGetValue(neighborCountyId, out var reverseNeighbors))
                    {
                        reverseNeighbors = new HashSet<int>();
                        adjacencyByCounty[neighborCountyId] = reverseNeighbors;
                    }

                    reverseNeighbors.Add(countyId);
                }
            }

            return adjacencyByCounty;
        }

        private static float ComputeHsvDistance(Vector3 a, Vector3 b)
        {
            float hueDelta = Mathf.Abs(a.x - b.x);
            hueDelta = Mathf.Min(hueDelta, 1f - hueDelta) * 360f;
            float satDelta = Mathf.Abs(a.y - b.y) * 100f;
            float valDelta = Mathf.Abs(a.z - b.z) * 100f;
            return hueDelta * 0.5f + satDelta * 0.25f + valDelta * 0.25f;
        }


        private bool TryGetAssignedMarketAccess(int cellId, int countyId, out float cost)
        {
            cost = 0f;
            if (economyState?.Markets == null)
                return false;

            int marketId = 0;
            if (economyState.CountyToMarket != null)
                economyState.CountyToMarket.TryGetValue(countyId, out marketId);
            if (marketId <= 0 && economyState.CellToMarket != null)
                economyState.CellToMarket.TryGetValue(cellId, out marketId);
            if (marketId <= 0)
                return false;

            if (!economyState.Markets.TryGetValue(marketId, out var market))
                return false;
            if (market.Type == MarketType.Black || market.ZoneCellCosts == null)
                return false;

            return market.ZoneCellCosts.TryGetValue(cellId, out cost);
        }

        private float ResolveNormalizedMarketId(Cell cell, int cellId)
        {
            if (cell == null || economyState == null)
                return 0f;

            int marketId = 0;
            if (economyState.CountyToMarket != null)
                economyState.CountyToMarket.TryGetValue(cell.CountyId, out marketId);
            if (marketId <= 0 && economyState.CellToMarket != null)
                economyState.CellToMarket.TryGetValue(cellId, out marketId);

            if (marketId <= 0)
                return 0f;

            return marketId / 65535f;
        }

        private static Color EvaluateHeatColor(float t)
        {
            t = Mathf.Clamp01(t);
            if (t <= 0.33f)
                return Color.Lerp(HeatLowColor, HeatMidColor, t / 0.33f);
            if (t <= 0.66f)
                return Color.Lerp(HeatMidColor, HeatHighColor, (t - 0.33f) / 0.33f);
            return Color.Lerp(HeatHighColor, HeatExtremeColor, (t - 0.66f) / 0.34f);
        }

        private Texture2D GetOrCreateOverlayTexture(OverlayLayer layer)
        {
            if (layer == OverlayLayer.None)
                return null;

            if (overlayTextureCacheByLayer.TryGetValue(layer, out Texture2D cached) && cached != null)
            {
                if (cached.width == gridWidth && cached.height == gridHeight)
                    return cached;

                DestroyTexture(cached);
                overlayTextureCacheByLayer.Remove(layer);
            }

            Texture2D texture = layer switch
            {
                OverlayLayer.PopulationDensity => GeneratePopulationDensityOverlayTexture(),
                _ => null
            };

            if (texture != null)
                overlayTextureCacheByLayer[layer] = texture;

            return texture;
        }

        private Texture2D GeneratePopulationDensityOverlayTexture()
        {
            var texture = new Texture2D(gridWidth, gridHeight, TextureFormat.RGBA32, false);
            texture.name = "OverlayPopulationDensity";
            texture.filterMode = FilterMode.Bilinear;
            texture.wrapMode = TextureWrapMode.Clamp;

            int size = gridWidth * gridHeight;
            var pixels = new Color32[size];
            if (spatialGrid == null || spatialGrid.Length != size || mapData?.Counties == null || mapData.Counties.Count == 0)
            {
                texture.SetPixels32(pixels);
                texture.Apply();
                return texture;
            }

            float areaPerLandCellKm2 = 1f;
            if (mapData.Info?.World != null && mapData.Info.World.MapAreaKm2 > 0f && mapData.Info.LandCells > 0)
                areaPerLandCellKm2 = mapData.Info.World.MapAreaKm2 / mapData.Info.LandCells;

            var countyLogDensityById = new Dictionary<int, float>(mapData.Counties.Count);
            float minLogDensity = float.MaxValue;
            float maxLogDensity = float.MinValue;

            for (int i = 0; i < mapData.Counties.Count; i++)
            {
                County county = mapData.Counties[i];
                if (county == null || county.Id <= 0 || county.CellCount <= 0)
                    continue;

                float countyAreaKm2 = Mathf.Max(0.001f, county.CellCount * areaPerLandCellKm2);
                float density = Mathf.Max(0f, county.TotalPopulation) / countyAreaKm2;
                float logDensity = Mathf.Log(1f + density);
                countyLogDensityById[county.Id] = logDensity;
                minLogDensity = Mathf.Min(minLogDensity, logDensity);
                maxLogDensity = Mathf.Max(maxLogDensity, logDensity);
            }

            bool hasRange = countyLogDensityById.Count > 0 && maxLogDensity > minLogDensity + 1e-5f;
            Color[] riverPixels = riverMaskTexture != null ? riverMaskTexture.GetPixels() : null;

            for (int i = 0; i < size; i++)
            {
                int cellId = spatialGrid[i];
                if (cellId < 0 || cellId >= cellIsLandById.Length || !cellIsLandById[cellId])
                    continue;

                if (riverPixels != null && i < riverPixels.Length && riverPixels[i].r > 0.5f)
                    continue;

                int countyId = (cellId >= 0 && cellId < cellCountyIdById.Length) ? cellCountyIdById[cellId] : 0;
                if (countyId <= 0 || !countyLogDensityById.TryGetValue(countyId, out float logDensity))
                    continue;

                float normalized = hasRange
                    ? Mathf.Clamp01((logDensity - minLogDensity) / Mathf.Max(1e-5f, maxLogDensity - minLogDensity))
                    : 0f;
                Color overlayColor = EvaluateHeatColor(normalized);
                pixels[i] = new Color32(
                    (byte)Mathf.RoundToInt(overlayColor.r * 255f),
                    (byte)Mathf.RoundToInt(overlayColor.g * 255f),
                    (byte)Mathf.RoundToInt(overlayColor.b * 255f),
                    255);
            }

            texture.SetPixels32(pixels);
            texture.Apply();
            TextureDebugger.SaveTexture(texture, "overlay_population_density");
            return texture;
        }

        private void PrewarmOverlayModeResolveCache(MapView.MapMode mode)
        {
            if (!IsModeResolveOverlay(mode) || styleMaterial == null)
                return;

            MapView.MapMode cacheKey = ResolveCacheKeyForMode(mode);
            if (modeColorResolveCacheRevisionByMode.TryGetValue(cacheKey, out int cachedRevision) &&
                cachedRevision == GetModeColorResolveRevision(cacheKey))
            {
                return;
            }

            MapView.MapMode previousMode = currentMapMode;
            currentMapMode = mode;
            RegenerateModeColorResolveTexture();
            currentMapMode = previousMode;

            if (IsModeResolveOverlay(previousMode))
            {
                if (!TryBindCachedModeColorResolveTexture(previousMode))
                    RegenerateModeColorResolveTexture();
            }
            else
            {
                styleMaterial.SetInt(UseModeColorResolveId, 0);
            }
        }

        private static Color LookupPaletteColor(Color[] palette, int id)
        {
            if (palette == null || palette.Length == 0)
                return Color.white;

            int idx = Mathf.Clamp(id, 0, palette.Length - 1);
            Color c = palette[idx];
            c.a = 1f;
            return c;
        }

        private static int ComputeCountyToMarketHash(Dictionary<int, int> countyToMarket)
        {
            if (countyToMarket == null || countyToMarket.Count == 0)
                return 0;

            unchecked
            {
                int hash = 17;
                var entries = new List<KeyValuePair<int, int>>(countyToMarket);
                entries.Sort((a, b) => a.Key.CompareTo(b.Key));
                hash = hash * 31 + entries.Count;
                for (int i = 0; i < entries.Count; i++)
                {
                    var entry = entries[i];
                    hash = hash * 31 + entry.Key;
                    hash = hash * 31 + entry.Value;
                }
                return hash;
            }
        }

        private static int ComputeRoadStateHash(RoadState roads)
        {
            if (roads == null)
                return 0;

            unchecked
            {
                int hash = 23;
                hash = hash * 31 + roads.EdgeTraffic.Count;
                hash = hash * 31 + BitConverter.SingleToInt32Bits(roads.PathThreshold);
                hash = hash * 31 + BitConverter.SingleToInt32Bits(roads.RoadThreshold);

                var entries = new List<KeyValuePair<(int, int), float>>(roads.EdgeTraffic);
                entries.Sort((a, b) =>
                {
                    int cmp = a.Key.Item1.CompareTo(b.Key.Item1);
                    if (cmp != 0) return cmp;
                    return a.Key.Item2.CompareTo(b.Key.Item2);
                });

                for (int i = 0; i < entries.Count; i++)
                {
                    var entry = entries[i];
                    hash = hash * 31 + entry.Key.Item1;
                    hash = hash * 31 + entry.Key.Item2;
                    hash = hash * 31 + BitConverter.SingleToInt32Bits(entry.Value);
                }

                return hash;
            }
        }

        /// <summary>
        /// Set road state for shader-based road rendering.
        /// Stores reference and regenerates the road distance texture.
        /// </summary>
        public void SetRoadState(RoadState roads)
        {
            roadState = roads;
            int roadHash = ComputeRoadStateHash(roads);
            bool canReuseCachedRoadTexture =
                roadDistTexture != null &&
                cachedRoadStateHash != 0 &&
                cachedRoadStateHash == roadHash;

            if (canReuseCachedRoadTexture)
            {
                Debug.Log("MapOverlayManager: Reused cached road mask texture");
            }
            else
            {
                RegenerateRoadDistTexture();
                cachedRoadStateHash = roadHash;
                overlayCacheDirty = true;
            }

            if (currentMapMode != MapView.MapMode.Market)
                pendingMarketModePrewarm = true;
        }

        /// <summary>
        /// Regenerate the road mask texture from current road state.
        /// Uses direct thick-line rasterization (same as river mask) — no chamfer transform.
        /// Only touches pixels near roads (~O(road_pixels)) instead of the entire grid.
        /// </summary>
        public void RegenerateRoadDistTexture()
        {
            if (roadDistTexture == null || roadState == null || mapData == null) return;

            var pixels = GenerateRoadMaskPixels();

            roadDistTexture.LoadRawTextureData(pixels);
            roadDistTexture.Apply();
        }

        /// <summary>
        /// Set runtime style values for path dash/gap/width and regenerate the path mask.
        /// </summary>
        public void SetPathStyle(float dashLength, float gapLength, float width)
        {
            if (!SupportsPathStyle())
                return;

            float clampedDash = Mathf.Max(0.1f, dashLength);
            float clampedGap = Mathf.Max(0.1f, gapLength);
            float clampedWidth = Mathf.Max(0.2f, width);

            styleMaterial.SetFloat(PathDashLengthId, clampedDash);
            styleMaterial.SetFloat(PathGapLengthId, clampedGap);
            styleMaterial.SetFloat(PathWidthId, clampedWidth);

            bool styleChanged =
                !Mathf.Approximately(cachedPathDashLength, clampedDash) ||
                !Mathf.Approximately(cachedPathGapLength, clampedGap) ||
                !Mathf.Approximately(cachedPathWidth, clampedWidth);

            if (styleChanged)
            {
                cachedPathDashLength = clampedDash;
                cachedPathGapLength = clampedGap;
                cachedPathWidth = clampedWidth;
            }

            if (styleChanged && roadState != null)
            {
                RegenerateRoadDistTexture();
                overlayCacheDirty = true;
            }
        }

        public void RunDeferredStartupWork()
        {
            if (pendingMarketModePrewarm && currentMapMode != MapView.MapMode.Market)
            {
                PrewarmOverlayModeResolveCache(MapView.MapMode.Market);
            }

            pendingMarketModePrewarm = false;
            FlushOverlayTextureCacheIfDirty();
        }

        public void FlushOverlayTextureCacheIfDirty()
        {
            if (!overlayCacheDirty || string.IsNullOrWhiteSpace(overlayTextureCacheDirectory))
                return;

            if (TrySaveOverlayTextureCache(overlayTextureCacheDirectory))
                overlayCacheDirty = false;
        }

        /// <summary>
        /// Pull path style from material and regenerate mask if values changed.
        /// Supports live tuning through the material inspector.
        /// </summary>
        public void RefreshPathStyleFromMaterial()
        {
            if (!SupportsPathStyle())
                return;

            float dash = GetMaterialFloatOr(PathDashLengthId, 1.8f);
            float gap = GetMaterialFloatOr(PathGapLengthId, 2.4f);
            float width = GetMaterialFloatOr(PathWidthId, 0.8f);
            SetPathStyle(dash, gap, width);
        }

        private bool SupportsPathStyle()
        {
            return styleMaterial != null &&
                   styleMaterial.HasProperty(PathDashLengthId) &&
                   styleMaterial.HasProperty(PathGapLengthId) &&
                   styleMaterial.HasProperty(PathWidthId);
        }

        private byte[] GenerateRoadMaskPixels()
        {
            var pixels = new byte[gridWidth * gridHeight];
            float scale = resolutionMultiplier;

            // Road width settings (in grid pixels)
            float configuredPathWidth = GetMaterialFloatOr(PathWidthId, 0.8f);
            float pathWidth = configuredPathWidth * resolutionMultiplier;
            float roadWidth = pathWidth * 1.8f;

            float configuredDashLength = GetMaterialFloatOr(PathDashLengthId, 1.8f);
            float configuredGapLength = GetMaterialFloatOr(PathGapLengthId, 2.4f);
            float dashLength = configuredDashLength * resolutionMultiplier;
            float gapLength = configuredGapLength * resolutionMultiplier;
            float patternLength = Mathf.Max(0.01f, dashLength + gapLength);

            var roads = roadState.GetAllRoads();

            // Subdivide each road segment and warp every intermediate point
            // so roads meander through the noise field like random walks
            const int substeps = 20;
            const float roadFreq = DomainWarp.Frequency;
            const float roadAmp = DomainWarp.Amplitude;

            foreach (var (cellA, cellB, tier) in roads)
            {
                if (!mapData.CellById.TryGetValue(cellA, out var dataA)) continue;
                if (!mapData.CellById.TryGetValue(cellB, out var dataB)) continue;

                float ax = dataA.Center.X * scale;
                float ay = dataA.Center.Y * scale;
                float bx = dataB.Center.X * scale;
                float by = dataB.Center.Y * scale;

                float width = tier == RoadTier.Road ? roadWidth : pathWidth;

                // Warp each substep point independently — creates organic meandering
                var (prevX, prevY) = DomainWarp.Warp(ax, ay, roadFreq, roadAmp);
                float dashProgress = 0f;
                for (int i = 1; i <= substeps; i++)
                {
                    float t = i / (float)substeps;
                    float mx = ax + (bx - ax) * t;
                    float my = ay + (by - ay) * t;
                    var (wx, wy) = DomainWarp.Warp(mx, my, roadFreq, roadAmp);

                    var start = new Vector2(prevX, prevY);
                    var end = new Vector2(wx, wy);
                    float segmentLength = Vector2.Distance(start, end);
                    if (segmentLength > 0.001f)
                    {
                        DrawDashedSegment(
                            pixels,
                            start,
                            end,
                            width,
                            dashLength,
                            patternLength,
                            ref dashProgress);
                    }

                    prevX = wx;
                    prevY = wy;
                }
            }

            return pixels;
        }

        private void DrawDashedSegment(
            byte[] pixels,
            Vector2 start,
            Vector2 end,
            float width,
            float dashLength,
            float patternLength,
            ref float dashProgress)
        {
            Vector2 delta = end - start;
            float segmentLength = delta.magnitude;
            if (segmentLength <= 0.001f)
                return;

            Vector2 dir = delta / segmentLength;
            float cursor = 0f;

            while (cursor < segmentLength)
            {
                float patternPos = Mathf.Repeat(dashProgress + cursor, patternLength);
                float step = Mathf.Min(patternLength - patternPos, segmentLength - cursor);

                if (patternPos < dashLength)
                {
                    float dashStart = cursor;
                    float dashEnd = Mathf.Min(cursor + step, cursor + (dashLength - patternPos));
                    if (dashEnd > dashStart + 0.001f)
                    {
                        Vector2 worldStart = start + dir * dashStart;
                        Vector2 worldEnd = start + dir * dashEnd;
                        DrawThickLine(pixels, worldStart, worldEnd, width);
                    }
                }

                cursor += step;
            }

            dashProgress += segmentLength;
        }

        private float GetMaterialFloatOr(int propertyId, float fallback)
        {
            if (styleMaterial == null)
                return fallback;

            if (!styleMaterial.HasProperty(propertyId))
                return fallback;

            float value = styleMaterial.GetFloat(propertyId);
            return float.IsNaN(value) || float.IsInfinity(value) ? fallback : value;
        }

        /// <summary>
        /// Set the current map mode for the shader.
        /// Mode: 1=political, 2=province, 3=county, 4=market,
        /// 6=biomes (vertex-blended), 7=channel-inspector, 8=local transport, 9=market transport
        /// </summary>
        public void SetMapMode(MapView.MapMode mode)
        {
            if (styleMaterial == null) return;
            bool modeChanged = currentMapMode != mode;
            currentMapMode = mode;
            if (mode == MapView.MapMode.Market)
                pendingMarketModePrewarm = false;

            int shaderMode;
            switch (mode)
            {
                case MapView.MapMode.Political:
                    shaderMode = 1;
                    break;
                case MapView.MapMode.Province:
                    shaderMode = 2;
                    break;
                case MapView.MapMode.County:
                    shaderMode = 3;
                    break;
                case MapView.MapMode.Market:
                    shaderMode = 4;
                    break;
                case MapView.MapMode.Biomes:
                    shaderMode = 6;
                    break;
                case MapView.MapMode.ChannelInspector:
                    shaderMode = 7;
                    break;
                case MapView.MapMode.TransportCost:
                    shaderMode = 8;
                    break;
                case MapView.MapMode.MarketAccess:
                    shaderMode = 9;
                    break;
                default:
                    shaderMode = 1;
                    break;
            }

            styleMaterial.SetInt(MapModeId, shaderMode);

            if (!IsModeResolveOverlay(mode))
            {
                styleMaterial.SetInt(UseModeColorResolveId, 0);
                return;
            }

            if (modeChanged && TryBindCachedModeColorResolveTexture(mode))
                return;

            RegenerateModeColorResolveTexture();
        }

        public void SetChannelDebugView(ChannelDebugView debugView)
        {
            if (styleMaterial == null) return;
            styleMaterial.SetInt(DebugViewId, (int)debugView);
        }

        /// <summary>
        /// Enable or disable height displacement in the shader.
        /// When enabled, the shader displaces vertices based on heightmap values.
        /// </summary>
        public void SetHeightDisplacementEnabled(bool enabled)
        {
            if (styleMaterial == null) return;
            styleMaterial.SetInt(UseHeightDisplacementId, enabled ? 1 : 0);
        }

        /// <summary>
        /// Set the height scale for terrain displacement.
        /// </summary>
        public void SetHeightScale(float scale)
        {
            if (styleMaterial == null) return;
            styleMaterial.SetFloat(HeightScaleId, scale);
        }

        /// <summary>
        /// Set sea-level threshold in world absolute height units for water detection.
        /// </summary>
        public void SetSeaLevel(float seaLevelAbsoluteHeight)
        {
            if (styleMaterial == null) return;
            styleMaterial.SetFloat(SeaLevelId, Elevation.NormalizeAbsolute01(seaLevelAbsoluteHeight, mapData.Info));
        }

        /// <summary>
        /// Clear all selection highlighting.
        /// </summary>
        public void ClearSelection()
        {
            if (styleMaterial == null) return;
            styleMaterial.SetFloat(SelectedRealmIdId, -1f);
            styleMaterial.SetFloat(SelectedProvinceIdId, -1f);
            styleMaterial.SetFloat(SelectedCountyIdId, -1f);
            styleMaterial.SetFloat(SelectedMarketIdId, -1f);
        }

        /// <summary>
        /// Set the currently selected realm for shader-based highlighting.
        /// Clears other selections.
        /// </summary>
        public void SetSelectedRealm(int realmId)
        {
            if (styleMaterial == null) return;
            float normalizedId = realmId < 0 ? -1f : realmId / 65535f;
            styleMaterial.SetFloat(SelectedRealmIdId, normalizedId);
            styleMaterial.SetFloat(SelectedProvinceIdId, -1f);
            styleMaterial.SetFloat(SelectedCountyIdId, -1f);
            styleMaterial.SetFloat(SelectedMarketIdId, -1f);
        }

        /// <summary>
        /// Set the currently selected province for shader-based highlighting.
        /// Clears other selections.
        /// </summary>
        public void SetSelectedProvince(int provinceId)
        {
            if (styleMaterial == null) return;
            styleMaterial.SetFloat(SelectedRealmIdId, -1f);
            float normalizedId = provinceId < 0 ? -1f : provinceId / 65535f;
            styleMaterial.SetFloat(SelectedProvinceIdId, normalizedId);
            styleMaterial.SetFloat(SelectedCountyIdId, -1f);
            styleMaterial.SetFloat(SelectedMarketIdId, -1f);
        }

        /// <summary>
        /// Set the currently selected county for shader-based highlighting.
        /// Clears other selections.
        /// Pass -1 to clear selection.
        /// </summary>
        public void SetSelectedCounty(int countyId)
        {
            if (styleMaterial == null) return;
            styleMaterial.SetFloat(SelectedRealmIdId, -1f);
            styleMaterial.SetFloat(SelectedProvinceIdId, -1f);
            float normalizedId = countyId < 0 ? -1f : countyId / 65535f;
            styleMaterial.SetFloat(SelectedCountyIdId, normalizedId);
            styleMaterial.SetFloat(SelectedMarketIdId, -1f);
        }

        /// <summary>
        /// Set the currently selected market for shader-based highlighting.
        /// Clears other selections.
        /// </summary>
        public void SetSelectedMarket(int marketId)
        {
            if (styleMaterial == null) return;
            styleMaterial.SetFloat(SelectedRealmIdId, -1f);
            styleMaterial.SetFloat(SelectedProvinceIdId, -1f);
            styleMaterial.SetFloat(SelectedCountyIdId, -1f);
            float normalizedId = marketId < 0 ? -1f : marketId / 65535f;
            styleMaterial.SetFloat(SelectedMarketIdId, normalizedId);
        }

        /// <summary>
        /// Clear all hover highlighting.
        /// </summary>
        public void ClearHover()
        {
            if (styleMaterial == null) return;
            styleMaterial.SetFloat(HoveredRealmIdId, -1f);
            styleMaterial.SetFloat(HoveredProvinceIdId, -1f);
            styleMaterial.SetFloat(HoveredCountyIdId, -1f);
            styleMaterial.SetFloat(HoveredMarketIdId, -1f);
        }

        /// <summary>
        /// Set the currently hovered realm for shader-based highlighting.
        /// Clears other hovers.
        /// </summary>
        public void SetHoveredRealm(int realmId)
        {
            if (styleMaterial == null) return;
            float normalizedId = realmId < 0 ? -1f : realmId / 65535f;
            styleMaterial.SetFloat(HoveredRealmIdId, normalizedId);
            styleMaterial.SetFloat(HoveredProvinceIdId, -1f);
            styleMaterial.SetFloat(HoveredCountyIdId, -1f);
            styleMaterial.SetFloat(HoveredMarketIdId, -1f);
        }

        /// <summary>
        /// Set the currently hovered province for shader-based highlighting.
        /// Clears other hovers.
        /// </summary>
        public void SetHoveredProvince(int provinceId)
        {
            if (styleMaterial == null) return;
            styleMaterial.SetFloat(HoveredRealmIdId, -1f);
            float normalizedId = provinceId < 0 ? -1f : provinceId / 65535f;
            styleMaterial.SetFloat(HoveredProvinceIdId, normalizedId);
            styleMaterial.SetFloat(HoveredCountyIdId, -1f);
            styleMaterial.SetFloat(HoveredMarketIdId, -1f);
        }

        /// <summary>
        /// Set the currently hovered county for shader-based highlighting.
        /// Clears other hovers.
        /// </summary>
        public void SetHoveredCounty(int countyId)
        {
            if (styleMaterial == null) return;
            styleMaterial.SetFloat(HoveredRealmIdId, -1f);
            styleMaterial.SetFloat(HoveredProvinceIdId, -1f);
            float normalizedId = countyId < 0 ? -1f : countyId / 65535f;
            styleMaterial.SetFloat(HoveredCountyIdId, normalizedId);
            styleMaterial.SetFloat(HoveredMarketIdId, -1f);
        }

        /// <summary>
        /// Set the currently hovered market for shader-based highlighting.
        /// Clears other hovers.
        /// </summary>
        public void SetHoveredMarket(int marketId)
        {
            if (styleMaterial == null) return;
            styleMaterial.SetFloat(HoveredRealmIdId, -1f);
            styleMaterial.SetFloat(HoveredProvinceIdId, -1f);
            styleMaterial.SetFloat(HoveredCountyIdId, -1f);
            float normalizedId = marketId < 0 ? -1f : marketId / 65535f;
            styleMaterial.SetFloat(HoveredMarketIdId, normalizedId);
        }

        /// <summary>
        /// Set the selection dimming factor (0 = black, 1 = no dimming).
        /// </summary>
        public void SetSelectionDimming(float dimming)
        {
            if (styleMaterial == null) return;
            styleMaterial.SetFloat(SelectionDimmingId, Mathf.Clamp01(dimming));
        }

        /// <summary>
        /// Set the selection desaturation factor (0 = full color, 1 = grayscale).
        /// </summary>
        public void SetSelectionDesaturation(float desaturation)
        {
            if (styleMaterial == null) return;
            styleMaterial.SetFloat(SelectionDesaturationId, Mathf.Clamp01(desaturation));
        }

        /// <summary>
        /// Set the hover intensity (0 = no effect, 1 = full effect).
        /// </summary>
        public void SetHoverIntensity(float intensity)
        {
            if (styleMaterial == null) return;
            styleMaterial.SetFloat(HoverIntensityId, Mathf.Clamp01(intensity));
        }

        /// <summary>
        /// Update political IDs for a specific cell. Useful for dynamic changes (conquests, etc.).
        /// </summary>
        public void UpdatePoliticalIds(int cellId, int? newRealmId = null, int? newProvinceId = null, int? newCountyId = null)
        {
            if (!mapData.CellById.TryGetValue(cellId, out var cell))
                return;

            if (politicalIdsPixels == null && politicalIdsTexture != null)
            {
                politicalIdsPixels = politicalIdsTexture.GetPixels();
            }

            if (newRealmId.HasValue)
                cell.RealmId = newRealmId.Value;
            if (newProvinceId.HasValue)
                cell.ProvinceId = newProvinceId.Value;
            if (newCountyId.HasValue)
                cell.CountyId = newCountyId.Value;

            if (cellId >= 0 && cellId < cellRealmIdById.Length)
            {
                if (newRealmId.HasValue)
                    cellRealmIdById[cellId] = newRealmId.Value;
                if (newProvinceId.HasValue)
                    cellProvinceIdById[cellId] = newProvinceId.Value;
                if (newCountyId.HasValue)
                    cellCountyIdById[cellId] = newCountyId.Value;
            }

            bool needsUpdate = false;
            float scale = resolutionMultiplier;

            // Update spatial grid positions belonging to this cell
            int cx = Mathf.RoundToInt(cell.Center.X * scale);
            int cy = Mathf.RoundToInt(cell.Center.Y * scale);
            int radius = 10 * resolutionMultiplier + (int)DomainWarp.Amplitude;

            for (int dy = -radius; dy <= radius; dy++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    int x = cx + dx;
                    int y = cy + dy;

                    if (x < 0 || x >= gridWidth || y < 0 || y >= gridHeight)
                        continue;

                    int gridIdx = y * gridWidth + x;

                    if (spatialGrid[gridIdx] == cellId)
                    {
                        Color political = politicalIdsPixels[gridIdx];

                        if (newRealmId.HasValue)
                        {
                            float normalized = newRealmId.Value / 65535f;
                            political.r = normalized;
                        }
                        if (newProvinceId.HasValue)
                        {
                            float normalized = newProvinceId.Value / 65535f;
                            political.g = normalized;
                        }
                        if (newCountyId.HasValue)
                        {
                            float normalized = newCountyId.Value / 65535f;
                            political.b = normalized;
                        }

                        politicalIdsPixels[gridIdx] = political;
                        needsUpdate = true;
                    }
                }
            }

            if (needsUpdate)
            {
                politicalIdsTexture.SetPixels(politicalIdsPixels);
                politicalIdsTexture.Apply();
                InvalidateModeColorResolveCache(MapView.MapMode.Political);
                InvalidateModeColorResolveCache(MapView.MapMode.Province);
                InvalidateModeColorResolveCache(MapView.MapMode.County);
                if (newCountyId.HasValue || newRealmId.HasValue)
                {
                    InvalidateModeColorResolveCache(MapView.MapMode.Market);
                    InvalidateModeColorResolveCache(MapView.MapMode.MarketAccess);
                }

                if (currentMapMode == MapView.MapMode.Political ||
                    currentMapMode == MapView.MapMode.Province ||
                    currentMapMode == MapView.MapMode.County ||
                    currentMapMode == MapView.MapMode.Market ||
                    currentMapMode == MapView.MapMode.MarketAccess)
                {
                    RegenerateModeColorResolveTexture();
                }
            }
        }

        /// <summary>
        /// Clean up textures when destroyed.
        /// </summary>
        public void Dispose()
        {
            var texturesToDestroy = new HashSet<Texture2D>();
            AddTextureForDestroy(texturesToDestroy, politicalIdsTexture);
            AddTextureForDestroy(texturesToDestroy, geographyBaseTexture);
            AddTextureForDestroy(texturesToDestroy, vegetationTexture);
            AddTextureForDestroy(texturesToDestroy, heightmapTexture);
            AddTextureForDestroy(texturesToDestroy, reliefNormalTexture);
            AddTextureForDestroy(texturesToDestroy, riverMaskTexture);
            AddTextureForDestroy(texturesToDestroy, realmPaletteTexture);
            AddTextureForDestroy(texturesToDestroy, marketPaletteTexture);
            AddTextureForDestroy(texturesToDestroy, biomePaletteTexture);
            AddTextureForDestroy(texturesToDestroy, cellToMarketTexture);
            AddTextureForDestroy(texturesToDestroy, realmBorderDistTexture);
            AddTextureForDestroy(texturesToDestroy, provinceBorderDistTexture);
            AddTextureForDestroy(texturesToDestroy, countyBorderDistTexture);
            AddTextureForDestroy(texturesToDestroy, marketBorderDistTexture);
            AddTextureForDestroy(texturesToDestroy, roadDistTexture);
            AddTextureForDestroy(texturesToDestroy, modeColorResolveTexture);
            foreach (var cachedResolve in modeColorResolveCacheByMode.Values)
                AddTextureForDestroy(texturesToDestroy, cachedResolve);
            foreach (var overlayTexture in overlayTextureCacheByLayer.Values)
                AddTextureForDestroy(texturesToDestroy, overlayTexture);

            foreach (var texture in texturesToDestroy)
                DestroyTexture(texture);

            modeColorResolveCacheByMode.Clear();
            modeColorResolveCacheRevisionByMode.Clear();
            modeColorResolveRevisionByKey.Clear();
            overlayTextureCacheByLayer.Clear();

            politicalIdsTexture = null;
            geographyBaseTexture = null;
            vegetationTexture = null;
            heightmapTexture = null;
            reliefNormalTexture = null;
            riverMaskTexture = null;
            realmPaletteTexture = null;
            marketPaletteTexture = null;
            biomePaletteTexture = null;
            cellToMarketTexture = null;
            realmBorderDistTexture = null;
            provinceBorderDistTexture = null;
            countyBorderDistTexture = null;
            marketBorderDistTexture = null;
            roadDistTexture = null;
            modeColorResolveTexture = null;
            riverMaskPixels = null;
        }

        private static void AddTextureForDestroy(HashSet<Texture2D> set, Texture2D texture)
        {
            if (texture != null)
                set.Add(texture);
        }

        private static void DestroyTexture(Texture2D texture)
        {
            if (Application.isPlaying)
                UnityEngine.Object.Destroy(texture);
            else
                UnityEngine.Object.DestroyImmediate(texture);
        }
    }
}
