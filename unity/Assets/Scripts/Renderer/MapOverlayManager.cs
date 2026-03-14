using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using EconSim.Core.Data;
using EconSim.Core.Rendering;
using EconSim.Core.Transport;
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
        private static readonly int ColormapTexId = Shader.PropertyToID("_ColormapTex");
        private static readonly int HeightmapTexId = Shader.PropertyToID("_HeightmapTex");
        private static readonly int ReliefNormalTexId = Shader.PropertyToID("_ReliefNormalTex");
        private static readonly int RiverMaskTexId = Shader.PropertyToID("_RiverMaskTex");
        private static readonly int RiverWidthId = Shader.PropertyToID("_RiverWidth");
        private static readonly int RiverMinWidthId = Shader.PropertyToID("_RiverMinWidth");
        private static readonly int RealmPaletteTexId = Shader.PropertyToID("_RealmPaletteTex");
        private static readonly int BiomePaletteTexId = Shader.PropertyToID("_BiomePaletteTex");
        private static readonly int RealmBorderDistTexId = Shader.PropertyToID("_RealmBorderDistTex");
        private static readonly int ProvinceBorderDistTexId = Shader.PropertyToID("_ProvinceBorderDistTex");
        private static readonly int CountyBorderDistTexId = Shader.PropertyToID("_CountyBorderDistTex");
        private static readonly int RoadMaskTexId = Shader.PropertyToID("_RoadMaskTex");
        private static readonly int PathDashLengthId = Shader.PropertyToID("_PathDashLength");
        private static readonly int PathGapLengthId = Shader.PropertyToID("_PathGapLength");
        private static readonly int PathWidthId = Shader.PropertyToID("_PathWidth");
        private static readonly int PathOpacityId = Shader.PropertyToID("_PathOpacity");
        private static readonly int EdgeWidthId = Shader.PropertyToID("_EdgeWidth");
        private static readonly int EdgeDarkeningId = Shader.PropertyToID("_EdgeDarkening");
        private static readonly int RealmBorderWidthId = Shader.PropertyToID("_RealmBorderWidth");
        private static readonly int RealmBorderDarkeningId = Shader.PropertyToID("_RealmBorderDarkening");
        private static readonly int ProvinceBorderWidthId = Shader.PropertyToID("_ProvinceBorderWidth");
        private static readonly int ProvinceBorderDarkeningId = Shader.PropertyToID("_ProvinceBorderDarkening");
        private static readonly int CountyBorderWidthId = Shader.PropertyToID("_CountyBorderWidth");
        private static readonly int CountyBorderDarkeningId = Shader.PropertyToID("_CountyBorderDarkening");
        private static readonly int MapModeId = Shader.PropertyToID("_MapMode");
        private static readonly int DebugViewId = Shader.PropertyToID("_DebugView");
        private static readonly int UseHeightDisplacementId = Shader.PropertyToID("_UseHeightDisplacement");
        private static readonly int HeightScaleId = Shader.PropertyToID("_HeightScale");
        private static readonly int SeaLevelId = Shader.PropertyToID("_SeaLevel");
        private static readonly int SelectedRealmIdId = Shader.PropertyToID("_SelectedRealmId");
        private static readonly int SelectedProvinceIdId = Shader.PropertyToID("_SelectedProvinceId");
        private static readonly int SelectedCountyIdId = Shader.PropertyToID("_SelectedCountyId");
        private static readonly int HoveredRealmIdId = Shader.PropertyToID("_HoveredRealmId");
        private static readonly int HoveredProvinceIdId = Shader.PropertyToID("_HoveredProvinceId");
        private static readonly int HoveredCountyIdId = Shader.PropertyToID("_HoveredCountyId");
        private static readonly int HoverIntensityId = Shader.PropertyToID("_HoverIntensity");
        private static readonly int SelectionDimmingId = Shader.PropertyToID("_SelectionDimming");
        private static readonly int SelectionDesaturationId = Shader.PropertyToID("_SelectionDesaturation");
        private static readonly int MarketPaletteTexId = Shader.PropertyToID("_MarketPaletteTex");
        private static readonly int MarketBorderDistTexId = Shader.PropertyToID("_MarketBorderDistTex");
        private static readonly int SelectedMarketIdId = Shader.PropertyToID("_SelectedMarketId");
        private static readonly int HoveredMarketIdId = Shader.PropertyToID("_HoveredMarketId");
        private static readonly int BorderTexelScaleId = Shader.PropertyToID("_BorderTexelScale");

        // Water layer property IDs
        private static readonly int WaterDeepColorId = Shader.PropertyToID("_WaterDeepColor");
        private static readonly int ShimmerScaleId = Shader.PropertyToID("_ShimmerScale");
        private static readonly int ShimmerSpeedId = Shader.PropertyToID("_ShimmerSpeed");
        private static readonly int ShimmerIntensityId = Shader.PropertyToID("_ShimmerIntensity");

        private MapData mapData;
        private Material styleMaterial;

        // Resolution multiplier for higher quality borders
        private int resolutionMultiplier;
        private int baseWidth;
        private int baseHeight;

        // Border resolution scale (border textures at higher resolution than main grid)
        private float borderResolutionScale;
        private int borderWidth;
        private int borderHeight;

        // Data textures
        private Texture2D politicalIdsTexture;  // RGBAFloat: RealmId, ProvinceId, CountyId, reserved
        private Texture2D geographyBaseTexture; // RGBAFloat: BiomeId, SoilId, reserved, WaterFlag
        private Texture2D vegetationTexture;    // RGFloat: VegetationTypeId, VegetationDensity

        /// <summary>
        /// Accessor for the political IDs texture (realm/province/county channels).
        /// </summary>
        public Texture2D PoliticalIdsTexture => politicalIdsTexture;
        public Texture2D HeightmapTexture => heightmapTexture;
        private Texture2D heightmapTexture;     // RFloat: smoothed height values
        private Texture2D reliefNormalTexture;  // RGBA32: normal map derived from visual height
        private Texture2D riverMaskTexture;     // RG16: R=distance from river, G=normalized flux
        private Texture2D realmPaletteTexture;  // 256x1: realm colors
        private Texture2D biomePaletteTexture;  // 256x1: biome colors
        private Texture2D realmBorderDistTexture; // R8: distance to nearest realm boundary (texels)
        private Texture2D provinceBorderDistTexture; // R8: distance to nearest province boundary (texels)
        private Texture2D countyBorderDistTexture;   // R8: distance to nearest county boundary (texels)
        private Texture2D roadDistTexture;             // R8: distance to nearest road centerline (texels, dynamic)
        private Texture2D modeColorResolveTexture;     // RGBA32: resolved per-mode color overlay
        private byte[] riverMaskPixels;                // Cached to drive relief synthesis near rivers.
        private string overlayTextureCacheDirectory;
        private int cachedRoadStateHash;
        private bool overlayCacheDirty;
        private bool[] cellIsLandById;
        private float[] cellHeight01ById;
        private int[] cellRealmIdById;
        private int[] cellProvinceIdById;
        private int[] cellCountyIdById;
        private int[] cellBiomeIdById;
        private int[] cellSoilIdById;
        private int[] cellVegetationTypeById;
        private float[] cellVegetationDensityById;
        private float[] cellPrecipitationById;
        private Texture2D colormapTexture;         // 64x64: elevation × moisture → terrain color

        // Market overlay data
        private EconSim.Core.Economy.EconomyState economyState;
        private EconSim.Core.Transport.TransportGraph transportGraph;
        private Texture2D marketPaletteTexture;       // 256x1: market zone colors
        private Texture2D marketBorderDistTexture;    // gridW×gridH R8: distance to nearest market boundary
        private int[] cellMarketIdById;               // cellId → marketId lookup

        // Religion overlay data
        private EconSim.Core.Religious.ReligionState religionState;
        private Color[] faithPaletteColors;            // faithIndex → color
        private int[] cellParishIdById;                // cellId → parishId (majority faith)
        private int[] cellDioceseIdById;               // cellId → dioceseId
        private int[] cellArchdioceseIdById;           // cellId → archdioceseId
        private Dictionary<int, int> parishToDioceseId;    // parishId → dioceseId
        private Dictionary<int, int> dioceseToArchdioceseId; // dioceseId → archdioceseId

        // Drill-down (expand/collapse) state — set by MapView
        private HashSet<int> drillExpandedRealmIds = new HashSet<int>();
        private HashSet<int> drillExpandedProvinceIds = new HashSet<int>();
        private HashSet<int> drillExpandedArchdioceseIds = new HashSet<int>();
        private HashSet<int> drillExpandedDioceseIds = new HashSet<int>();

        // Precomputed per-cell colors for drill-down (avoids rebuilding graph-colored maps each drill change)
        private Color[] cachedRealmColorByCell;      // cellId → realm color
        private Color[] cachedProvinceColorByCell;   // cellId → graph-colored province color
        private Color[] cachedCountyColorByCell;     // cellId → graph-colored county color
        private Color[] cachedArchdioceseColorByCell; // cellId → archdiocese color
        private Color[] cachedDioceseColorByCell;    // cellId → diocese color
        private Color[] cachedParishColorByCell;     // cellId → parish color

        // Cached river mask per grid pixel (avoids GetPixels() allocation on every resolve)
        private bool[] cachedRiverMaskGrid;
        // Reusable pixel buffer for mode color resolve (avoids allocation per rebuild)
        private Color[] resolvePixelBuffer;
        private Texture2D archdioceseBorderDistTexture;
        private Texture2D dioceseBorderDistTexture;
        private Texture2D parishBorderDistTexture;
        private static readonly int ArchdioceseBorderDistTexId = Shader.PropertyToID("_ArchdioceseBorderDistTex");
        private static readonly int DioceseBorderDistTexId = Shader.PropertyToID("_DioceseBorderDistTex");
        private static readonly int ParishBorderDistTexId = Shader.PropertyToID("_ParishBorderDistTex");

        // Visual relief synthesis parameters (visual-only; gameplay elevation remains authoritative).
        private const int ReliefBlurRadius = 4;
        /// <summary>
        /// Max river half-width in distance-field texels. The distance field is offset by this
        /// value so that pixels inside rivers have dist &lt; MaxRiverHalfWidth.
        /// Used by both shader (_RiverWidth) and C# mode-color resolve.
        /// </summary>
        /// <summary>
        /// Max river half-width in distance-field texels (for the largest rivers).
        /// </summary>
        private const float MaxRiverHalfWidth = 0.5f;
        /// <summary>Min half-width for the smallest visible tributaries (texels).</summary>
        private const float MinRiverHalfWidth = 0.0f;

        private const float ReliefBlurCrossClassWeight = 0.25f;
        private const float ReliefChannelRadiusTexels = 2.6f;
        private const float ReliefChannelStrength = 0.022f;
        private const float ReliefBankErosionRadiusTexels = 6.5f;
        private const float ReliefBankErosionStrength = 0.015f;
        private const float ReliefBankSharpness = 2.4f;
        private const float ReliefValleyErosionRadiusTexels = 22f;
        private const float ReliefValleyErosionStrength = 0.016f;
        private const float ReliefValleySharpness = 3.0f;
        private const float ReliefLocalReliefNormalization = 0.02f;
        private const float ReliefLocalReliefInfluence = 1.25f;
        private const float ReliefValleyHeightExponent = 0.70f;
        private const float ReliefLandMinAboveSea = 0.001f;
        private const int ReliefNormalPreBlurPasses = 0;
        private const float ReliefNormalDerivativeScale = 0.45f;

        // Terrain detail noise parameters (sub-cell variation for sharper normals).
        private const float DetailNoiseAmplitude = 0.012f;      // Max height perturbation in 0-1 space
        private const float DetailNoiseFrequency = 0.035f;       // Base frequency (texels^-1)
        private const int DetailNoiseOctaves = 4;
        private const float DetailNoiseLacunarity = 2.17f;
        private const float DetailNoisePersistence = 0.48f;
        private const float DetailNoiseRiverFadeRadius = 18f;    // Suppress noise near rivers (texels)
        private const float DetailNoiseRiverFadeMin = 0.15f;     // Minimum noise scale near rivers
        private const float DetailNoiseHighElevationBoost = 1.8f; // Extra noise at high elevations
        private static readonly float[] ReliefGaussianKernel = { 1f, 8f, 28f, 56f, 70f, 56f, 28f, 8f, 1f };

        // Road state (cached for regeneration)
        private RoadState roadState;
        private float cachedPathDashLength = -1f;
        private float cachedPathGapLength = -1f;
        private float cachedPathWidth = -1f;
        private NoisyEdgeStyle noisyEdgeStyle = NoisyEdgeStyle.Default;
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
        private const float DefaultNoisyEdgeSampleSpacingPx = 2.5f;
        private const int DefaultNoisyEdgeMaxSamples = 96;
        private const float DefaultNoisyEdgeRoughness = 0.58f;
        private const float DefaultNoisyEdgeAmplitudePerResolution = 0.9f;
        private const float DefaultNoisyEdgeAmplitudeCap = 8.0f;
        private const float DefaultNoisyEdgeBandPaddingPx = 1.5f;

        private const int OverlayTextureCacheVersion = 11;
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
            public int RoadStateHash;
            public float BorderResolutionScale;
            public int BorderWidth;
            public int BorderHeight;
            public float NoisyEdgeSampleSpacingPx;
            public int NoisyEdgeMaxSamples;
            public float NoisyEdgeRoughness;
            public float NoisyEdgeAmplitudePerResolution;
            public float NoisyEdgeAmplitudeCap;
            public float NoisyEdgeBandPaddingPx;
        }

        public readonly struct NoisyEdgeStyle : IEquatable<NoisyEdgeStyle>
        {
            public readonly float SampleSpacingPx;
            public readonly int MaxSamples;
            public readonly float Roughness;
            public readonly float AmplitudePerResolution;
            public readonly float AmplitudeCap;
            public readonly float BandPaddingPx;

            public NoisyEdgeStyle(
                float sampleSpacingPx,
                int maxSamples,
                float roughness,
                float amplitudePerResolution,
                float amplitudeCap,
                float bandPaddingPx)
            {
                SampleSpacingPx = sampleSpacingPx;
                MaxSamples = maxSamples;
                Roughness = roughness;
                AmplitudePerResolution = amplitudePerResolution;
                AmplitudeCap = amplitudeCap;
                BandPaddingPx = bandPaddingPx;
            }

            public static NoisyEdgeStyle Default => new NoisyEdgeStyle(
                DefaultNoisyEdgeSampleSpacingPx,
                DefaultNoisyEdgeMaxSamples,
                DefaultNoisyEdgeRoughness,
                DefaultNoisyEdgeAmplitudePerResolution,
                DefaultNoisyEdgeAmplitudeCap,
                DefaultNoisyEdgeBandPaddingPx);

            public bool Equals(NoisyEdgeStyle other)
            {
                return Mathf.Approximately(SampleSpacingPx, other.SampleSpacingPx) &&
                       MaxSamples == other.MaxSamples &&
                       Mathf.Approximately(Roughness, other.Roughness) &&
                       Mathf.Approximately(AmplitudePerResolution, other.AmplitudePerResolution) &&
                       Mathf.Approximately(AmplitudeCap, other.AmplitudeCap) &&
                       Mathf.Approximately(BandPaddingPx, other.BandPaddingPx);
            }

            public override bool Equals(object obj)
            {
                return obj is NoisyEdgeStyle other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = 17;
                    hash = hash * 31 + SampleSpacingPx.GetHashCode();
                    hash = hash * 31 + MaxSamples;
                    hash = hash * 31 + Roughness.GetHashCode();
                    hash = hash * 31 + AmplitudePerResolution.GetHashCode();
                    hash = hash * 31 + AmplitudeCap.GetHashCode();
                    hash = hash * 31 + BandPaddingPx.GetHashCode();
                    return hash;
                }
            }
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

        private readonly struct PendingVoronoiEdge
        {
            public readonly int CellId;
            public readonly int V0;
            public readonly int V1;
            public readonly Vector2 Center;

            public PendingVoronoiEdge(int cellId, int v0, int v1, Vector2 center)
            {
                CellId = cellId;
                V0 = v0;
                V1 = v1;
                Center = center;
            }
        }

        private readonly struct SharedVoronoiEdge
        {
            public readonly int CellA;
            public readonly int CellB;
            public readonly Vector2 CenterA;
            public readonly Vector2 CenterB;
            public readonly Vector2 V0;
            public readonly Vector2 V1;
            public readonly uint Seed;

            public SharedVoronoiEdge(
                int cellA,
                int cellB,
                Vector2 centerA,
                Vector2 centerB,
                Vector2 v0,
                Vector2 v1,
                uint seed)
            {
                CellA = cellA;
                CellB = cellB;
                CenterA = centerA;
                CenterB = centerB;
                V0 = v0;
                V1 = v1;
                Seed = seed;
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
            bool preferCachedOverlayTextures = false,
            NoisyEdgeStyle? initialNoisyEdgeStyle = null,
            float borderResolutionScale = 1.0f)
        {
            this.mapData = mapData;
            this.styleMaterial = styleMaterial;
            this.resolutionMultiplier = Mathf.Clamp(resolutionMultiplier, 1, 8);
            this.borderResolutionScale = Mathf.Clamp(borderResolutionScale, 1.0f, 3.0f);
            this.overlayTextureCacheDirectory = overlayTextureCacheDirectory;
            noisyEdgeStyle = ClampNoisyEdgeStyle(initialNoisyEdgeStyle ?? NoisyEdgeStyle.Default);

            baseWidth = mapData.Info.Width;
            baseHeight = mapData.Info.Height;
            gridWidth = baseWidth * this.resolutionMultiplier;
            gridHeight = baseHeight * this.resolutionMultiplier;
            borderWidth = (int)(gridWidth * this.borderResolutionScale);
            borderHeight = (int)(gridHeight * this.borderResolutionScale);
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
                int[] borderGrid = BuildBorderSpatialGrid();
                GenerateAdministrativeBorderDistTextures(borderGrid);
                Profiler.End();
            }

            Profiler.Begin("GeneratePaletteTextures");
            GeneratePaletteTextures();
            Profiler.End();

            Profiler.Begin("GenerateVegetationTexture");
            GenerateVegetationTexture();
            Profiler.End();

            GenerateColormapTexture();

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
            cellPrecipitationById = new float[lookupSize];

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
                float precip = float.IsNaN(cell.Precipitation) || float.IsInfinity(cell.Precipitation)
                    ? 0f : cell.Precipitation;
                cellPrecipitationById[cellId] = Mathf.Clamp01(precip);
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
                    TextureFormat.RG16,
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
                    borderWidth,
                    borderHeight,
                    TextureFormat.R8,
                    FilterMode.Bilinear,
                    TextureWrapMode.Clamp,
                    anisoLevel: 8);

                Texture2D loadedProvinceBorder = LoadTextureFromRaw(
                    Path.Combine(cacheDirectory, CacheProvinceBorderFile),
                    borderWidth,
                    borderHeight,
                    TextureFormat.R8,
                    FilterMode.Bilinear,
                    TextureWrapMode.Clamp,
                    anisoLevel: 8);

                Texture2D loadedCountyBorder = LoadTextureFromRaw(
                    Path.Combine(cacheDirectory, CacheCountyBorderFile),
                    borderWidth,
                    borderHeight,
                    TextureFormat.R8,
                    FilterMode.Bilinear,
                    TextureWrapMode.Clamp,
                    anisoLevel: 8);

                Texture2D loadedRoadDist = LoadTextureFromRaw(
                    Path.Combine(cacheDirectory, CacheRoadDistFile),
                    borderWidth,
                    borderHeight,
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
                roadDistTexture = loadedRoadDist;
                riverMaskPixels = loadedRiverMaskPixels;
                politicalIdsPixels = null;
                geographyBasePixels = null;
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
                    RoadStateHash = cachedRoadStateHash,
                    BorderResolutionScale = borderResolutionScale,
                    BorderWidth = borderWidth,
                    BorderHeight = borderHeight,
                    NoisyEdgeSampleSpacingPx = noisyEdgeStyle.SampleSpacingPx,
                    NoisyEdgeMaxSamples = noisyEdgeStyle.MaxSamples,
                    NoisyEdgeRoughness = noisyEdgeStyle.Roughness,
                    NoisyEdgeAmplitudePerResolution = noisyEdgeStyle.AmplitudePerResolution,
                    NoisyEdgeAmplitudeCap = noisyEdgeStyle.AmplitudeCap,
                    NoisyEdgeBandPaddingPx = noisyEdgeStyle.BandPaddingPx
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

            if (!CacheFloatMatches(metadata.BorderResolutionScale, borderResolutionScale) ||
                metadata.BorderWidth != borderWidth ||
                metadata.BorderHeight != borderHeight)
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

            if (metadata.NoisyEdgeMaxSamples != noisyEdgeStyle.MaxSamples)
                return false;
            if (!CacheFloatMatches(metadata.NoisyEdgeSampleSpacingPx, noisyEdgeStyle.SampleSpacingPx))
                return false;
            if (!CacheFloatMatches(metadata.NoisyEdgeRoughness, noisyEdgeStyle.Roughness))
                return false;
            if (!CacheFloatMatches(metadata.NoisyEdgeAmplitudePerResolution, noisyEdgeStyle.AmplitudePerResolution))
                return false;
            if (!CacheFloatMatches(metadata.NoisyEdgeAmplitudeCap, noisyEdgeStyle.AmplitudeCap))
                return false;
            if (!CacheFloatMatches(metadata.NoisyEdgeBandPaddingPx, noisyEdgeStyle.BandPaddingPx))
                return false;

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
        /// Uses noisy shared Voronoi edges for organic, meandering borders.
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
            spatialGrid = new int[gridWidth * gridHeight];
            BuildSpatialGridInto(spatialGrid, gridWidth, gridHeight, resolutionMultiplier);
        }

        /// <summary>
        /// Build a border spatial grid by upsampling the main spatialGrid to border resolution.
        /// This ensures border detection aligns exactly with province/county color fills.
        /// Returns the main spatialGrid if border scale is 1.0 (no extra work).
        /// </summary>
        private int[] BuildBorderSpatialGrid()
        {
            if (borderResolutionScale <= 1.001f)
                return spatialGrid;

            Profiler.Begin("BuildBorderSpatialGrid");
            int[] borderGrid = new int[borderWidth * borderHeight];

            // Nearest-neighbor upsample: map each border pixel back to the main grid.
            // The chamfer distance transform will smooth the staircase edges into
            // continuous distance gradients at the higher resolution.
            float invScaleX = (float)gridWidth / borderWidth;
            float invScaleY = (float)gridHeight / borderHeight;

            Parallel.For(0, borderHeight, y =>
            {
                int srcY = Mathf.Min((int)(y * invScaleY), gridHeight - 1);
                int srcRow = srcY * gridWidth;
                int dstRow = y * borderWidth;
                for (int x = 0; x < borderWidth; x++)
                {
                    int srcX = Mathf.Min((int)(x * invScaleX), gridWidth - 1);
                    borderGrid[dstRow + x] = spatialGrid[srcRow + srcX];
                }
            });

            Profiler.End();
            Debug.Log($"MapOverlayManager: Upsampled border spatial grid {borderWidth}x{borderHeight} from {gridWidth}x{gridHeight}");
            return borderGrid;
        }

        /// <summary>
        /// Core Voronoi fill + noisy edge logic targeting an arbitrary grid.
        /// </summary>
        private void BuildSpatialGridInto(int[] target, int targetWidth, int targetHeight, float scale)
        {
            int size = targetWidth * targetHeight;
            var distanceSqGrid = new float[size];

            // Initialize grids
            Parallel.For(0, size, i =>
            {
                target[i] = -1;
                distanceSqGrid[i] = float.MaxValue;
            });

            // PHASE 1: Base Voronoi fill using cell centers.
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

                int x0 = Mathf.Max(0, Mathf.FloorToInt(minX) - 1);
                int x1 = Mathf.Min(targetWidth - 1, Mathf.CeilToInt(maxX) + 1);
                int y0 = Mathf.Max(0, Mathf.FloorToInt(minY) - 1);
                int y1 = Mathf.Min(targetHeight - 1, Mathf.CeilToInt(maxY) + 1);

                float cx = cell.Center.X * scale;
                float cy = cell.Center.Y * scale;

                cellInfos.Add(new SpatialCellInfo(cell.Id, x0, y0, x1, y1, cx, cy));
            }

            // Partition by row stripes so each worker owns exclusive output rows (no locks).
            const int stripeHeight = 32;
            int stripeCount = (targetHeight + stripeHeight - 1) / stripeHeight;
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
                int stripeY1 = Mathf.Min(targetHeight - 1, stripeY0 + stripeHeight - 1);

                for (int i = 0; i < cellsInStripe.Count; i++)
                {
                    SpatialCellInfo info = cellInfos[cellsInStripe[i]];
                    int y0 = info.Y0 > stripeY0 ? info.Y0 : stripeY0;
                    int y1 = info.Y1 < stripeY1 ? info.Y1 : stripeY1;

                    for (int y = y0; y <= y1; y++)
                    {
                        int row = y * targetWidth;
                        for (int x = info.X0; x <= info.X1; x++)
                        {
                            int gridIdx = row + x;
                            float dx = x - info.Cx;
                            float dy = y - info.Cy;
                            float distSq = dx * dx + dy * dy;

                            if (distSq < distanceSqGrid[gridIdx])
                            {
                                distanceSqGrid[gridIdx] = distSq;
                                target[gridIdx] = info.CellId;
                            }
                        }
                    }
                }
            });

            // PHASE 2: Replace straight shared edges with deterministic noisy edges.
            ApplyNoisyVoronoiEdges(target, targetWidth, targetHeight, scale);
        }

        private void ApplyNoisyVoronoiEdges(int[] target, int targetWidth, int targetHeight, float scale)
        {
            if (mapData?.Cells == null || mapData.Vertices == null || mapData.Vertices.Count == 0)
                return;

            var pendingByEdge = new Dictionary<ulong, PendingVoronoiEdge>(mapData.Cells.Count * 3);
            var sharedEdges = new List<SharedVoronoiEdge>(mapData.Cells.Count * 2);
            int seed = mapData.Info?.MapGenSeed ?? 0;

            for (int i = 0; i < mapData.Cells.Count; i++)
            {
                Cell cell = mapData.Cells[i];
                if (cell?.VertexIndices == null)
                    continue;

                int count = cell.VertexIndices.Count;
                if (count < 2)
                    continue;

                Vector2 center = new Vector2(cell.Center.X * scale, cell.Center.Y * scale);

                for (int e = 0; e < count; e++)
                {
                    int v0 = cell.VertexIndices[e];
                    int v1 = cell.VertexIndices[(e + 1) % count];
                    if (v0 < 0 || v1 < 0 || v0 >= mapData.Vertices.Count || v1 >= mapData.Vertices.Count || v0 == v1)
                        continue;

                    ulong key = MakeUndirectedEdgeKey(v0, v1);
                    if (!pendingByEdge.TryGetValue(key, out PendingVoronoiEdge pending))
                    {
                        pendingByEdge[key] = new PendingVoronoiEdge(cell.Id, v0, v1, center);
                        continue;
                    }

                    // Edge has two owning cells (shared Voronoi edge).
                    pendingByEdge.Remove(key);
                    if (pending.CellId == cell.Id)
                        continue;

                    int ev0 = pending.V0;
                    int ev1 = pending.V1;
                    if (ev0 < 0 || ev1 < 0 || ev0 >= mapData.Vertices.Count || ev1 >= mapData.Vertices.Count)
                        continue;

                    var p0 = mapData.Vertices[ev0];
                    var p1 = mapData.Vertices[ev1];
                    Vector2 edgeV0 = new Vector2(p0.X * scale, p0.Y * scale);
                    Vector2 edgeV1 = new Vector2(p1.X * scale, p1.Y * scale);
                    uint edgeSeed = BuildUnorderedPairSeed((uint)seed, pending.CellId, cell.Id);
                    edgeSeed = BuildUnorderedPairSeed(edgeSeed, ev0, ev1);

                    sharedEdges.Add(new SharedVoronoiEdge(
                        pending.CellId,
                        cell.Id,
                        pending.Center,
                        center,
                        edgeV0,
                        edgeV1,
                        edgeSeed));
                }
            }

            int[] baseGrid = (int[])target.Clone();
            float effectiveResMultiplier = scale;
            for (int i = 0; i < sharedEdges.Count; i++)
                RasterizeNoisyEdge(sharedEdges[i], baseGrid, target, targetWidth, targetHeight, effectiveResMultiplier);
        }

        private void RasterizeNoisyEdge(SharedVoronoiEdge edge, int[] baseGrid, int[] target, int targetWidth, int targetHeight, float effectiveResMultiplier)
        {
            Vector2 delta = edge.V1 - edge.V0;
            float length = delta.magnitude;
            if (length < 1e-4f)
                return;

            Vector2 dir = delta / length;
            Vector2 lineNormal = new Vector2(-dir.y, dir.x);
            Vector2 mid = (edge.V0 + edge.V1) * 0.5f;
            float signToA = Vector2.Dot(edge.CenterA - mid, lineNormal);
            Vector2 orientedNormal = signToA >= 0f ? lineNormal : -lineNormal;

            float centerDistance = Vector2.Distance(edge.CenterA, edge.CenterB);
            float baseAmplitude = GetNoisyEdgeBaseAmplitudePixels(effectiveResMultiplier);
            float amplitude = Mathf.Min(baseAmplitude, length * 0.22f, centerDistance * 0.35f);
            if (amplitude < 0.75f)
                return;

            int sampleCount = Mathf.Clamp(Mathf.CeilToInt(length / noisyEdgeStyle.SampleSpacingPx), 4, noisyEdgeStyle.MaxSamples);
            var offsets = new float[sampleCount + 1];
            offsets[0] = 0f;
            offsets[sampleCount] = 0f;
            BuildNoisyEdgeOffsets(offsets, 0, sampleCount, amplitude, edge.Seed);

            float band = amplitude + noisyEdgeStyle.BandPaddingPx;
            int minX = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(edge.V0.x, edge.V1.x) - band));
            int maxX = Mathf.Min(targetWidth - 1, Mathf.CeilToInt(Mathf.Max(edge.V0.x, edge.V1.x) + band));
            int minY = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(edge.V0.y, edge.V1.y) - band));
            int maxY = Mathf.Min(targetHeight - 1, Mathf.CeilToInt(Mathf.Max(edge.V0.y, edge.V1.y) + band));

            for (int y = minY; y <= maxY; y++)
            {
                int row = y * targetWidth;
                for (int x = minX; x <= maxX; x++)
                {
                    int idx = row + x;
                    int currentCell = baseGrid[idx];
                    if (currentCell != edge.CellA && currentCell != edge.CellB)
                        continue;

                    Vector2 p = new Vector2(x, y);
                    float along = Vector2.Dot(p - edge.V0, dir);
                    if (along < 0f || along > length)
                        continue;

                    Vector2 closest = edge.V0 + dir * along;
                    float lineDistance = Vector2.Dot(p - closest, lineNormal);
                    if (Mathf.Abs(lineDistance) > band)
                        continue;

                    float t = along / length;
                    float shift = SampleNoisyEdgeOffset(offsets, t);
                    float orientedDistance = Vector2.Dot(p - closest, orientedNormal);
                    target[idx] = orientedDistance >= shift ? edge.CellA : edge.CellB;
                }
            }
        }

        private float GetNoisyEdgeBaseAmplitudePixels()
        {
            return GetNoisyEdgeBaseAmplitudePixels(resolutionMultiplier);
        }

        private float GetNoisyEdgeBaseAmplitudePixels(float effectiveResMultiplier)
        {
            return Mathf.Min(noisyEdgeStyle.AmplitudeCap, Mathf.Max(1.0f, effectiveResMultiplier * noisyEdgeStyle.AmplitudePerResolution));
        }

        private void BuildNoisyEdgeOffsets(float[] offsets, int start, int end, float amplitude, uint seed)
        {
            if (end - start <= 1)
                return;

            int mid = (start + end) >> 1;
            float center = 0.5f * (offsets[start] + offsets[end]);
            float jitter = HashSigned(seed, start, mid, end) * amplitude;
            offsets[mid] = center + jitter;

            float nextAmplitude = amplitude * noisyEdgeStyle.Roughness;
            BuildNoisyEdgeOffsets(offsets, start, mid, nextAmplitude, seed);
            BuildNoisyEdgeOffsets(offsets, mid, end, nextAmplitude, seed);
        }

        private static float SampleNoisyEdgeOffset(float[] offsets, float t)
        {
            if (offsets == null || offsets.Length == 0)
                return 0f;

            float clampedT = Mathf.Clamp01(t);
            float pos = clampedT * (offsets.Length - 1);
            int i0 = Mathf.FloorToInt(pos);
            int i1 = Mathf.Min(offsets.Length - 1, i0 + 1);
            float frac = pos - i0;
            return Mathf.Lerp(offsets[i0], offsets[i1], frac);
        }

        private List<Vector2> BuildNoisyPolyline(List<Vector2> controlPoints, uint seed, float amplitudeScale = 1f)
        {
            if (controlPoints == null || controlPoints.Count < 2)
                return controlPoints;

            var result = new List<Vector2>(Mathf.Max(controlPoints.Count * 4, controlPoints.Count + 1))
            {
                controlPoints[0]
            };

            for (int i = 0; i < controlPoints.Count - 1; i++)
            {
                uint segmentSeed = BuildSegmentSeed(seed, i);
                AppendNoisySegment(result, controlPoints[i], controlPoints[i + 1], segmentSeed, amplitudeScale);
            }

            return result;
        }

        private List<Vector2> BuildNoisyRasterPath(
            List<Vector2> controlPoints,
            uint seed,
            float amplitudeScale,
            int smoothSamplesPerSegment)
        {
            List<Vector2> noisyPath = BuildNoisyPolyline(controlPoints, seed, amplitudeScale);
            if (noisyPath == null || noisyPath.Count < 2)
                return noisyPath;

            if (smoothSamplesPerSegment <= 0)
                return noisyPath;

            return SmoothPath(noisyPath, smoothSamplesPerSegment);
        }

        private void AppendNoisySegment(List<Vector2> output, Vector2 a, Vector2 b, uint seed, float amplitudeScale)
        {
            Vector2 delta = b - a;
            float length = delta.magnitude;
            if (length < 1e-4f)
            {
                output.Add(b);
                return;
            }

            Vector2 dir = delta / length;
            Vector2 normal = new Vector2(-dir.y, dir.x);
            if (HashSigned(seed, 11, 17, 29) < 0f)
                normal = -normal;

            int sampleCount = Mathf.Clamp(Mathf.CeilToInt(length / noisyEdgeStyle.SampleSpacingPx), 2, noisyEdgeStyle.MaxSamples);
            float maxAmplitude = GetNoisyEdgeBaseAmplitudePixels() * Mathf.Max(0f, amplitudeScale);
            float amplitude = Mathf.Min(maxAmplitude, length * 0.2f);

            if (amplitude < 0.35f)
            {
                output.Add(b);
                return;
            }

            var offsets = new float[sampleCount + 1];
            offsets[0] = 0f;
            offsets[sampleCount] = 0f;
            BuildNoisyEdgeOffsets(offsets, 0, sampleCount, amplitude, seed);

            for (int s = 1; s <= sampleCount; s++)
            {
                float t = s / (float)sampleCount;
                Vector2 point = a + dir * (length * t);
                float offset = s == sampleCount ? 0f : SampleNoisyEdgeOffset(offsets, t);
                output.Add(point + normal * offset);
            }
        }

        private uint BuildMapSeed(int entityId)
        {
            return MixHash((uint)(mapData.Info?.MapGenSeed ?? 0), (uint)entityId);
        }

        private static uint BuildSegmentSeed(uint baseSeed, int segmentIndex)
        {
            return MixHash(baseSeed, (uint)segmentIndex);
        }

        private static uint BuildUnorderedPairSeed(uint rootSeed, int a, int b)
        {
            uint lo = (uint)Mathf.Min(a, b);
            uint hi = (uint)Mathf.Max(a, b);
            return MixHash(MixHash(rootSeed, lo), hi);
        }

        private static ulong MakeUndirectedEdgeKey(int a, int b)
        {
            uint lo = (uint)Mathf.Min(a, b);
            uint hi = (uint)Mathf.Max(a, b);
            return ((ulong)hi << 32) | lo;
        }

        private static uint MixHash(uint state, uint value)
        {
            uint x = state ^ (value + 0x9e3779b9u + (state << 6) + (state >> 2));
            x ^= x >> 16;
            x *= 0x7feb352du;
            x ^= x >> 15;
            x *= 0x846ca68bu;
            x ^= x >> 16;
            return x;
        }

        private static float HashSigned(uint seed, int a, int b, int c)
        {
            uint h = MixHash(seed, (uint)a);
            h = MixHash(h, (uint)b);
            h = MixHash(h, (uint)c);
            float unit = h / (float)uint.MaxValue;
            return unit * 2f - 1f;
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
            vegetationTexture = new Texture2D(gridWidth, gridHeight, TextureFormat.RGBAFloat, false);
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
                            cellPrecipitationById[cellId],
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
        /// Generate a 64×64 colormap texture for elevation × moisture → terrain color lookup.
        /// Same procedural gradient as mapgen4: X = elevation (-1 to +1, sea level at center),
        /// Y = moisture (0 to 1). Land blends from sandy/brown (dry) to green (wet), fading
        /// to white at high elevation. Water deepens with depth.
        /// </summary>
        private void GenerateColormapTexture()
        {
            const int size = 64;
            var pixels = new Color32[size * size];
            int p = 0;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    // e: -1 (deep ocean) to +1 (mountain peak), 0 = sea level
                    float e = 2f * x / size - 1f;
                    // m: 0 (dry) to 1 (wet)
                    float m = (float)y / size;

                    float r, g, b;

                    if (x == size / 2 - 1)
                    {
                        r = 48; g = 120; b = 160;
                    }
                    else if (x == size / 2 - 2)
                    {
                        r = 48; g = 100; b = 150;
                    }
                    else if (x == size / 2 - 3)
                    {
                        r = 48; g = 80; b = 140;
                    }
                    else if (e < 0f)
                    {
                        r = 48 + 48 * e;
                        g = 64 + 64 * e;
                        b = 127 + 127 * e;
                    }
                    else
                    {
                        m *= 1f - e;
                        r = 210 - 100 * m;
                        g = 185 - 45 * m;
                        b = 139 - 45 * m;
                        r = 255 * e + r * (1f - e);
                        g = 255 * e + g * (1f - e);
                        b = 255 * e + b * (1f - e);
                    }

                    pixels[p++] = new Color32(
                        (byte)Mathf.Clamp(r, 0, 255),
                        (byte)Mathf.Clamp(g, 0, 255),
                        (byte)Mathf.Clamp(b, 0, 255),
                        255);
                }
            }

            colormapTexture = new Texture2D(size, size, TextureFormat.RGBA32, false, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                name = "TerrainColormap"
            };
            colormapTexture.SetPixels32(pixels);
            colormapTexture.Apply();
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
            ApplyFluvialErosion(heightData, isLand, riverDistance, seaLevel01);

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

            // Inject sub-cell detail noise for sharper normals. This creates a separate
            // "normal-only" heightfield so the visual heightmap (water shading, displacement)
            // stays clean while the normal map captures fine terrain detail.
            float[] normalHeightData = ApplyDetailNoise(heightData, isLand, riverDistance, seaLevel01);
            GenerateReliefNormalTexture(normalHeightData, isLand);

            // Create texture
            heightmapTexture = new Texture2D(gridWidth, gridHeight, TextureFormat.RFloat, false);
            heightmapTexture.name = "HeightmapTexture";
            heightmapTexture.filterMode = FilterMode.Bilinear;
            heightmapTexture.wrapMode = TextureWrapMode.Clamp;
            heightmapTexture.SetPixelData(heightData, 0);
            heightmapTexture.Apply();

            Debug.Log($"MapOverlayManager: Generated heightmap {gridWidth}x{gridHeight} with relief synthesis");
        }

        /// <summary>
        /// Add multi-octave noise to a copy of the heightfield for normal map derivation.
        /// Noise is suppressed near rivers (valleys are smooth/eroded) and boosted at high
        /// elevation (ridges/mountains have more rugged terrain).
        /// </summary>
        private float[] ApplyDetailNoise(float[] heightData, bool[] isLand, float[] riverDistance, float seaLevel01)
        {
            float[] result = (float[])heightData.Clone();
            float invLandSpan = 1f / Mathf.Max(0.0001f, 1f - seaLevel01);
            int seed = mapData?.Info?.RootSeed ?? 0;
            float offsetX = (seed * 7919) % 10000;
            float offsetY = (seed * 6271) % 10000;

            Parallel.For(0, gridHeight, y =>
            {
                int row = y * gridWidth;
                for (int x = 0; x < gridWidth; x++)
                {
                    int idx = row + x;
                    if (!isLand[idx])
                        continue;

                    // River proximity suppression: smooth valleys, rugged ridges.
                    float riverFade = 1f;
                    if (riverDistance != null && idx < riverDistance.Length)
                    {
                        float dist = riverDistance[idx];
                        float t = Mathf.Clamp01(dist / DetailNoiseRiverFadeRadius);
                        riverFade = Mathf.Lerp(DetailNoiseRiverFadeMin, 1f, t * t);
                    }

                    // Elevation boost: more noise at higher elevations (mountains are rugged).
                    float landHeight01 = Mathf.Clamp01((heightData[idx] - seaLevel01) * invLandSpan);
                    float elevationScale = Mathf.Lerp(0.5f, DetailNoiseHighElevationBoost, landHeight01);

                    // Multi-octave noise (value noise via hash, deterministic).
                    float noiseVal = 0f;
                    float freq = DetailNoiseFrequency;
                    float amp = 1f;
                    float ampSum = 0f;
                    for (int o = 0; o < DetailNoiseOctaves; o++)
                    {
                        float nx = (x + offsetX) * freq;
                        float ny = (y + offsetY) * freq;
                        noiseVal += GradientNoise2D(nx, ny) * amp;
                        ampSum += amp;
                        freq *= DetailNoiseLacunarity;
                        amp *= DetailNoisePersistence;
                    }

                    noiseVal /= ampSum; // Normalized to roughly -1..1

                    float displacement = noiseVal * DetailNoiseAmplitude * riverFade * elevationScale;
                    result[idx] = Mathf.Clamp(result[idx] + displacement, seaLevel01 + ReliefLandMinAboveSea, 1f);
                }
            });

            return result;
        }

        /// <summary>
        /// 2D gradient noise (smooth, deterministic). Returns value in approximately -1..1 range.
        /// Uses bitwise hash for speed in Parallel.For context.
        /// </summary>
        private static float GradientNoise2D(float x, float y)
        {
            int ix = Mathf.FloorToInt(x);
            int iy = Mathf.FloorToInt(y);
            float fx = x - ix;
            float fy = y - iy;

            // Smoothstep interpolation for C1 continuity
            float sx = fx * fx * (3f - 2f * fx);
            float sy = fy * fy * (3f - 2f * fy);

            float n00 = GradDot(ix, iy, fx, fy);
            float n10 = GradDot(ix + 1, iy, fx - 1f, fy);
            float n01 = GradDot(ix, iy + 1, fx, fy - 1f);
            float n11 = GradDot(ix + 1, iy + 1, fx - 1f, fy - 1f);

            float nx0 = n00 + sx * (n10 - n00);
            float nx1 = n01 + sx * (n11 - n01);
            return nx0 + sy * (nx1 - nx0);
        }

        private static float GradDot(int ix, int iy, float dx, float dy)
        {
            // Hash to pick a gradient direction
            int h = HashCoord(ix, iy) & 3;
            switch (h)
            {
                case 0: return dx + dy;
                case 1: return -dx + dy;
                case 2: return dx - dy;
                default: return -dx - dy;
            }
        }

        private static int HashCoord(int x, int y)
        {
            int h = x * 374761393 + y * 668265263;
            h = (h ^ (h >> 13)) * 1274126177;
            return h ^ (h >> 16);
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

        private void ApplyFluvialErosion(float[] heightData, bool[] isLand, float[] riverDistance, float seaLevel01)
        {
            if (riverDistance == null || riverDistance.Length != heightData.Length)
                return;

            float[] localRelief = BuildLocalReliefField(heightData, isLand);
            float minLandHeight = seaLevel01 + ReliefLandMinAboveSea;
            float invLandSpan = 1f / Mathf.Max(0.0001f, 1f - seaLevel01);

            Parallel.For(0, gridHeight, y =>
            {
                int row = y * gridWidth;
                for (int x = 0; x < gridWidth; x++)
                {
                    int idx = row + x;
                    if (!isLand[idx])
                        continue;

                    float distance = riverDistance[idx];
                    if (distance >= ReliefValleyErosionRadiusTexels)
                        continue;

                    float channelT = 1f - Mathf.Clamp01(distance / ReliefChannelRadiusTexels);
                    float channelCarve = channelT * ReliefChannelStrength;

                    float bankT = 1f - Mathf.Clamp01(distance / ReliefBankErosionRadiusTexels);
                    float bankCarve = Mathf.Pow(bankT, ReliefBankSharpness) * ReliefBankErosionStrength;

                    float valleyT = 1f - Mathf.Clamp01(distance / ReliefValleyErosionRadiusTexels);
                    float reliefFactor = 0.55f + 0.45f * Mathf.Clamp01(localRelief[idx] * ReliefLocalReliefInfluence);
                    float landHeight01 = Mathf.Clamp01((heightData[idx] - seaLevel01) * invLandSpan);
                    float heightFactor = 0.40f + 0.60f * Mathf.Pow(landHeight01, ReliefValleyHeightExponent);
                    float valleyCarve = Mathf.Pow(valleyT, ReliefValleySharpness) * ReliefValleyErosionStrength * reliefFactor * heightFactor;

                    heightData[idx] = Mathf.Max(minLandHeight, heightData[idx] - (channelCarve + bankCarve + valleyCarve));
                }
            });
        }

        private float[] BuildLocalReliefField(float[] heightData, bool[] isLand)
        {
            int size = heightData.Length;
            var localRelief = new float[size];

            Parallel.For(0, gridHeight, y =>
            {
                int row = y * gridWidth;
                for (int x = 0; x < gridWidth; x++)
                {
                    int idx = row + x;
                    if (!isLand[idx])
                    {
                        localRelief[idx] = 0f;
                        continue;
                    }

                    float minH = heightData[idx];
                    float maxH = minH;

                    for (int oy = -1; oy <= 1; oy++)
                    {
                        int sy = Mathf.Clamp(y + oy, 0, gridHeight - 1);
                        int sRow = sy * gridWidth;
                        for (int ox = -1; ox <= 1; ox++)
                        {
                            int sx = Mathf.Clamp(x + ox, 0, gridWidth - 1);
                            int sampleIdx = sRow + sx;
                            if (!isLand[sampleIdx])
                                continue;

                            float sampleH = heightData[sampleIdx];
                            if (sampleH < minH) minH = sampleH;
                            if (sampleH > maxH) maxH = sampleH;
                        }
                    }

                    localRelief[idx] = Mathf.Clamp01((maxH - minH) / ReliefLocalReliefNormalization);
                }
            });

            return localRelief;
        }

        private float[] BuildRiverDistanceField(bool[] isLand)
        {
            // RG16 river mask: R = distance from centerline, G = normalized flux.
            // Raw bytes are interleaved [R0, G0, R1, G1, ...].
            // For erosion we only need the R channel (distance).
            if (riverMaskPixels == null || riverMaskPixels.Length != isLand.Length * 2)
                return null;

            int size = isLand.Length;
            var dist = new float[size];
            bool hasRiver = false;

            for (int i = 0; i < size; i++)
            {
                dist[i] = riverMaskPixels[i * 2]; // R channel
                if (dist[i] < 1f && isLand[i])
                    hasRiver = true;
            }

            return hasRiver ? dist : null;
        }

        /// <summary>
        /// Generate river mask texture by rasterizing river paths.
        /// Rivers are "knocked out" of the land in the shader, showing water underneath.
        /// </summary>
        private void GenerateRiverMaskTexture()
        {
            riverMaskTexture = new Texture2D(gridWidth, gridHeight, TextureFormat.RG16, false);
            riverMaskTexture.name = "RiverMaskTexture";
            riverMaskTexture.filterMode = FilterMode.Bilinear;
            riverMaskTexture.wrapMode = TextureWrapMode.Clamp;

            riverMaskPixels = GenerateRiverMaskPixels();
            riverMaskTexture.LoadRawTextureData(riverMaskPixels);
            riverMaskTexture.Apply();

            TextureDebugger.SaveTexture(riverMaskTexture, "river_mask");
            int edgeCount = mapData.EdgeRiverFlux != null ? mapData.EdgeRiverFlux.Count : 0;
            Debug.Log($"MapOverlayManager: Generated river distance field {gridWidth}x{gridHeight} ({edgeCount} river edges)");
        }

        private byte[] GenerateRiverMaskPixels()
        {
            // RG8 river texture:
            //   R = chamfer distance from nearest river centerline (0 = on river, 255 = far)
            //   G = normalized river width at nearest river pixel (0 = thinnest, 255 = widest)
            // Shader uses both: isRiver = dist < width * MaxRiverHalfWidth
            var edgeFlux = mapData.EdgeRiverFlux;
            float visualThreshold = mapData.RiverTraceFluxThreshold;
            float majorThreshold = mapData.RiverFluxThreshold;
            if (edgeFlux == null || edgeFlux.Count == 0)
            {
                // R=255 (max distance = no river), G=0
                byte[] empty = new byte[gridWidth * gridHeight * 2];
                for (int i = 0; i < empty.Length; i += 2)
                    empty[i] = 255;
                return empty;
            }

            // Compute log-flux range for width interpolation
            float logMax = Mathf.Log(majorThreshold + 1f);
            foreach (var kv in edgeFlux)
                if (kv.Value > majorThreshold)
                    logMax = Mathf.Max(logMax, Mathf.Log(kv.Value + 1f));
            float logTrace = Mathf.Log(visualThreshold + 1f);
            float logRange = Mathf.Max(0.01f, logMax - logTrace);

            int size = gridWidth * gridHeight;
            float[] dist = new float[size];
            byte[] fluxNorm = new byte[size]; // normalized flux → width
            Array.Fill(dist, 255f);
            bool[] isLand = new bool[size];

            for (int i = 0; i < size; i++)
            {
                int cellId = spatialGrid[i];
                if (cellId >= 0 && cellId < cellIsLandById.Length && cellIsLandById[cellId])
                    isLand[i] = true;
            }

            // Seed scan: mark pixels adjacent to river edges as distance 0
            // and record normalized flux for width modulation
            Parallel.For(0, gridHeight, y =>
            {
                int row = y * gridWidth;
                for (int x = 0; x < gridWidth; x++)
                {
                    int idx = row + x;
                    if (!isLand[idx])
                        continue;

                    int cellId = spatialGrid[idx];
                    float bestFlux = -1f;

                    for (int dy = -1; dy <= 1; dy++)
                    {
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            if (dx == 0 && dy == 0)
                                continue;

                            int nx = x + dx;
                            int ny = y + dy;
                            if (nx < 0 || nx >= gridWidth || ny < 0 || ny >= gridHeight)
                                continue;

                            int nIdx = ny * gridWidth + nx;
                            int neighborCellId = spatialGrid[nIdx];
                            if (neighborCellId == cellId || neighborCellId < 0)
                                continue;

                            var key = cellId < neighborCellId
                                ? (cellId, neighborCellId)
                                : (neighborCellId, cellId);
                            if (edgeFlux.TryGetValue(key, out float flux) && flux >= visualThreshold)
                            {
                                if (flux > bestFlux)
                                    bestFlux = flux;
                            }
                        }
                    }

                    if (bestFlux >= 0f)
                    {
                        dist[idx] = 0f;
                        float t = (Mathf.Log(bestFlux + 1f) - logTrace) / logRange;
                        t = Mathf.Clamp01(t);
                        fluxNorm[idx] = (byte)Mathf.RoundToInt(t * 255f);
                    }
                }
            });

            // Propagate flux values from seed pixels to neighbors via nearest-seed.
            // We run the chamfer and simultaneously track which seed each pixel is closest to.
            // Simpler approach: after chamfer, flood-fill flux from nearest seed.
            RunChamferTransform(dist, isLand, gridWidth, gridHeight);
            PropagateNearestFlux(dist, fluxNorm, isLand);

            // Interleave into RG8
            byte[] pixels = new byte[size * 2];
            for (int i = 0; i < size; i++)
            {
                pixels[i * 2] = (byte)Mathf.Min(255, Mathf.RoundToInt(dist[i]));
                pixels[i * 2 + 1] = fluxNorm[i];
            }
            return pixels;
        }

        private List<Vector2> BuildRiverControlPoints(River river, float scale)
        {
            var pathPoints = new List<Vector2>();
            if (river.Points != null && river.Points.Count >= 2)
            {
                foreach (var pt in river.Points)
                    pathPoints.Add(new Vector2(pt.X * scale, pt.Y * scale));
            }
            else if (river.CellPath != null && river.CellPath.Count >= 2)
            {
                foreach (int cellId in river.CellPath)
                {
                    if (mapData.CellById.TryGetValue(cellId, out var cell))
                        pathPoints.Add(new Vector2(cell.Center.X * scale, cell.Center.Y * scale));
                }
            }

            return pathPoints;
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
        private static void DrawThickLine(byte[] pixels, Vector2 start, Vector2 end, float width, int gridW, int gridH)
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
            int x1 = Mathf.Min(gridW - 1, Mathf.CeilToInt(maxX));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(minY));
            int y1 = Mathf.Min(gridH - 1, Mathf.CeilToInt(maxY));

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
                        int idx = y * gridW + x;
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
        private static void RunChamferTransform(float[] dist, bool[] isLand, int width, int height)
        {
            const float orthCost = 1f;
            const float diagCost = 1.414f;

            // Forward pass: top-to-bottom, left-to-right
            for (int y = 1; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int idx = y * width + x;
                    if (!isLand[idx]) continue;

                    if (x > 0) dist[idx] = Mathf.Min(dist[idx], dist[idx - 1] + orthCost);
                    if (x > 0) dist[idx] = Mathf.Min(dist[idx], dist[(y - 1) * width + x - 1] + diagCost);
                    dist[idx] = Mathf.Min(dist[idx], dist[(y - 1) * width + x] + orthCost);
                    if (x < width - 1) dist[idx] = Mathf.Min(dist[idx], dist[(y - 1) * width + x + 1] + diagCost);
                }
            }

            // Backward pass: bottom-to-top, right-to-left
            for (int y = height - 2; y >= 0; y--)
            {
                for (int x = width - 1; x >= 0; x--)
                {
                    int idx = y * width + x;
                    if (!isLand[idx]) continue;

                    if (x < width - 1) dist[idx] = Mathf.Min(dist[idx], dist[idx + 1] + orthCost);
                    if (x < width - 1) dist[idx] = Mathf.Min(dist[idx], dist[(y + 1) * width + x + 1] + diagCost);
                    dist[idx] = Mathf.Min(dist[idx], dist[(y + 1) * width + x] + orthCost);
                    if (x > 0) dist[idx] = Mathf.Min(dist[idx], dist[(y + 1) * width + x - 1] + diagCost);
                }
            }
        }

        /// <summary>
        /// Check if a pixel from the RG16 river mask texture represents a river.
        /// R = distance from centerline (0-1 → 0-255 texels), G = normalized flux (0-1).
        /// River if distance &lt; flux-scaled half-width.
        /// </summary>
        private static bool IsRiverPixel(Color riverPixel)
        {
            float dist = riverPixel.r * 255f;
            float width = Mathf.Lerp(MinRiverHalfWidth, MaxRiverHalfWidth, riverPixel.g);
            return dist < width;
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

        /// <summary>
        /// Propagate flux values from seed pixels (dist=0) to all other pixels,
        /// so each pixel inherits the flux of its nearest river edge.
        /// Uses the same two-pass chamfer pattern as the distance transform.
        /// </summary>
        private void PropagateNearestFlux(float[] dist, byte[] fluxNorm, bool[] isLand)
        {
            const float orthCost = 1f;
            const float diagCost = 1.414f;

            // Forward pass: top-to-bottom, left-to-right
            for (int y = 1; y < gridHeight; y++)
            {
                for (int x = 0; x < gridWidth; x++)
                {
                    int idx = y * gridWidth + x;
                    if (!isLand[idx] || dist[idx] < 0.5f) continue; // skip seeds and water

                    // Check neighbors that were already processed; adopt flux from nearest
                    float bestDist = dist[idx];
                    int bestIdx = idx;

                    if (x > 0 && dist[idx - 1] + orthCost <= bestDist)
                    { bestDist = dist[idx - 1] + orthCost; bestIdx = idx - 1; }

                    int prevRow = (y - 1) * gridWidth;
                    if (x > 0 && dist[prevRow + x - 1] + diagCost <= bestDist)
                    { bestDist = dist[prevRow + x - 1] + diagCost; bestIdx = prevRow + x - 1; }

                    if (dist[prevRow + x] + orthCost <= bestDist)
                    { bestDist = dist[prevRow + x] + orthCost; bestIdx = prevRow + x; }

                    if (x < gridWidth - 1 && dist[prevRow + x + 1] + diagCost <= bestDist)
                    { bestIdx = prevRow + x + 1; }

                    if (bestIdx != idx)
                        fluxNorm[idx] = fluxNorm[bestIdx];
                }
            }

            // Backward pass: bottom-to-top, right-to-left
            for (int y = gridHeight - 2; y >= 0; y--)
            {
                for (int x = gridWidth - 1; x >= 0; x--)
                {
                    int idx = y * gridWidth + x;
                    if (!isLand[idx] || dist[idx] < 0.5f) continue;

                    float bestDist = dist[idx];
                    int bestIdx = idx;

                    if (x < gridWidth - 1 && dist[idx + 1] + orthCost <= bestDist)
                    { bestDist = dist[idx + 1] + orthCost; bestIdx = idx + 1; }

                    int nextRow = (y + 1) * gridWidth;
                    if (x < gridWidth - 1 && dist[nextRow + x + 1] + diagCost <= bestDist)
                    { bestDist = dist[nextRow + x + 1] + diagCost; bestIdx = nextRow + x + 1; }

                    if (dist[nextRow + x] + orthCost <= bestDist)
                    { bestDist = dist[nextRow + x] + orthCost; bestIdx = nextRow + x; }

                    if (x > 0 && dist[nextRow + x - 1] + diagCost <= bestDist)
                    { bestIdx = nextRow + x - 1; }

                    if (bestIdx != idx)
                        fluxNorm[idx] = fluxNorm[bestIdx];
                }
            }
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
        private void GenerateAdministrativeBorderDistTextures(int[] sourceGrid)
        {
            Profiler.Begin("GenerateAdministrativeBorderDistPixels");
            AdministrativeBorderDistPixels pixels = GenerateAdministrativeBorderDistPixels(sourceGrid, borderWidth, borderHeight);
            Profiler.End();

            realmBorderDistTexture = CreateBorderDistTexture("RealmBorderDistTexture", pixels.Realm, "realm_border_dist", borderWidth, borderHeight);
            provinceBorderDistTexture = CreateBorderDistTexture("ProvinceBorderDistTexture", pixels.Province, "province_border_dist", borderWidth, borderHeight);
            countyBorderDistTexture = CreateBorderDistTexture("CountyBorderDistTexture", pixels.County, "county_border_dist", borderWidth, borderHeight);

            Debug.Log($"MapOverlayManager: Generated administrative border distance textures {borderWidth}x{borderHeight}");
        }

        private AdministrativeBorderDistPixels GenerateAdministrativeBorderDistPixels(int[] sourceGrid, int gridW, int gridH)
        {
            int size = gridW * gridH;

            int[] realmGrid = new int[size];
            int[] provinceGrid = new int[size];
            int[] countyGrid = new int[size];
            bool[] isLand = new bool[size];

            for (int i = 0; i < size; i++)
            {
                int cellId = sourceGrid[i];
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
            Parallel.For(0, gridH, y =>
            {
                int row = y * gridW;
                for (int x = 0; x < gridW; x++)
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
                            if (nx < 0 || nx >= gridW || ny < 0 || ny >= gridH)
                            {
                                // Grid edge counts as realm boundary (coastline at map edge).
                                realmBoundary = true;
                                continue;
                            }

                            int nIdx = ny * gridW + nx;
                            if (!isLand[nIdx])
                            {
                                // Water/river neighbor — realm edge (coast, lake, river).
                                realmBoundary = true;
                                continue;
                            }

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
            Task realmTask = Task.Run(() => RunChamferTransform(realmDist, isLand, gridW, gridH));
            Task provinceTask = Task.Run(() => RunChamferTransform(provinceDist, isLand, gridW, gridH));
            Task countyTask = Task.Run(() => RunChamferTransform(countyDist, isLand, gridW, gridH));
            Task.WaitAll(realmTask, provinceTask, countyTask);
            Profiler.End();

            return new AdministrativeBorderDistPixels(
                DistToBytes(realmDist),
                DistToBytes(provinceDist),
                DistToBytes(countyDist));
        }

        private Texture2D CreateBorderDistTexture(string textureName, byte[] pixels, string debugName, int texW, int texH)
        {
            var texture = new Texture2D(texW, texH, TextureFormat.R8, false);
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
        /// Generate color palette textures for realms and biomes.
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
        /// Ensure the road mask texture exists before binding.
        /// </summary>
        private void EnsureRoadMaskTexture()
        {
            if (roadDistTexture != null)
                return;

            roadDistTexture = new Texture2D(borderWidth, borderHeight, TextureFormat.R8, false);
            roadDistTexture.name = "RoadMaskTexture";
            roadDistTexture.filterMode = FilterMode.Bilinear;
            roadDistTexture.anisoLevel = 8;
            roadDistTexture.wrapMode = TextureWrapMode.Clamp;
            // R8 textures initialize to 0 (black = no roads), just apply.
            roadDistTexture.Apply();
        }

        /// <summary>
        /// Bake the mesh's vertex-interpolated elevation into a high-resolution RenderTexture.
        /// This replaces the cell-resolution heightmap for slope lighting, giving smooth gradients
        /// across cell boundaries via GPU interpolation.
        /// </summary>
        public void BakeElevationFromMesh(MeshFilter sourceMeshFilter, Transform meshTransform)
        {
            if (sourceMeshFilter == null || sourceMeshFilter.sharedMesh == null)
            {
                Debug.LogWarning("MapOverlayManager: Cannot bake elevation - no mesh");
                return;
            }

            const int bakeSize = 4096;
            Shader bakeShader = Shader.Find("EconSim/ElevationBake");
            if (bakeShader == null)
            {
                Debug.LogError("MapOverlayManager: ElevationBake shader not found");
                return;
            }

            var bakeMaterial = new Material(bakeShader) { hideFlags = HideFlags.DontSave };

            // Create a temporary RenderTexture for the bake
            var bakeRT = new RenderTexture(bakeSize, bakeSize, 24, RenderTextureFormat.RFloat, RenderTextureReadWrite.Linear)
            {
                name = "ElevationBakeRT",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                useMipMap = false,
                autoGenerateMips = false
            };
            bakeRT.Create();

            // Set up a temporary orthographic camera looking straight down at the mesh.
            // The mesh is in world space: X = [0, worldWidth], Z = [0, worldHeight], Y = up.
            Bounds meshBounds = sourceMeshFilter.sharedMesh.bounds;
            Vector3 worldCenter = meshTransform.TransformPoint(meshBounds.center);
            Vector3 worldSize = meshBounds.size;

            // The mesh lies on the XZ plane. Camera looks down -Y.
            var cameraGO = new GameObject("ElevationBakeCamera");
            cameraGO.hideFlags = HideFlags.HideAndDontSave;
            var bakeCam = cameraGO.AddComponent<UnityEngine.Camera>();
            bakeCam.enabled = false;
            bakeCam.orthographic = true;
            // Ortho size = half the Z extent (height in world space)
            bakeCam.orthographicSize = worldSize.z * 0.5f;
            bakeCam.aspect = worldSize.x / worldSize.z;
            bakeCam.transform.position = new Vector3(worldCenter.x, worldCenter.y + 100f, worldCenter.z);
            bakeCam.transform.rotation = UnityEngine.Quaternion.Euler(90f, 0f, 0f);
            bakeCam.nearClipPlane = 0.1f;
            bakeCam.farClipPlane = 500f;
            bakeCam.clearFlags = CameraClearFlags.SolidColor;
            bakeCam.backgroundColor = Color.black;
            bakeCam.targetTexture = bakeRT;
            bakeCam.cullingMask = 0; // We'll render manually

            // Render the mesh with the bake material using CommandBuffer
            bakeCam.Render(); // Clear
            var cmd = new UnityEngine.Rendering.CommandBuffer { name = "ElevationBake" };
            cmd.SetRenderTarget(bakeRT);
            cmd.ClearRenderTarget(true, true, Color.black);
            cmd.SetViewProjectionMatrices(bakeCam.worldToCameraMatrix, bakeCam.projectionMatrix);
            cmd.DrawMesh(sourceMeshFilter.sharedMesh, meshTransform.localToWorldMatrix, bakeMaterial, 0, 0);
            UnityEngine.Graphics.ExecuteCommandBuffer(cmd);
            cmd.Dispose();

            // Read back into a Texture2D to replace the cell-resolution heightmap
            RenderTexture previousActive = RenderTexture.active;
            RenderTexture.active = bakeRT;
            var bakedTexture = new Texture2D(bakeSize, bakeSize, TextureFormat.RFloat, false)
            {
                name = "BakedElevationTexture",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            bakedTexture.ReadPixels(new Rect(0, 0, bakeSize, bakeSize), 0, 0);
            bakedTexture.Apply();
            RenderTexture.active = previousActive;

            // Clean up
            UnityEngine.Object.DestroyImmediate(cameraGO);
            UnityEngine.Object.DestroyImmediate(bakeMaterial);
            bakeRT.Release();
            UnityEngine.Object.DestroyImmediate(bakeRT);

            // Replace the heightmap texture
            if (heightmapTexture != null)
                UnityEngine.Object.DestroyImmediate(heightmapTexture);
            heightmapTexture = bakedTexture;

            // Re-bind to material
            if (styleMaterial != null)
                styleMaterial.SetTexture(HeightmapTexId, heightmapTexture);

            Debug.Log($"MapOverlayManager: Baked elevation {bakeSize}x{bakeSize} from mesh (was {gridWidth}x{gridHeight})");
        }

        private void BindGeneratedTexturesToMaterial()
        {
            if (styleMaterial == null)
                return;

            styleMaterial.SetTexture(PoliticalIdsTexId, politicalIdsTexture);
            styleMaterial.SetTexture(GeographyBaseTexId, geographyBaseTexture);
            styleMaterial.SetTexture(VegetationTexId, vegetationTexture);
            styleMaterial.SetTexture(HeightmapTexId, heightmapTexture);
            styleMaterial.SetTexture(ReliefNormalTexId, reliefNormalTexture);
            if (colormapTexture != null)
                styleMaterial.SetTexture(ColormapTexId, colormapTexture);
            styleMaterial.SetTexture(RiverMaskTexId, riverMaskTexture);
            styleMaterial.SetFloat(RiverWidthId, MaxRiverHalfWidth);
            styleMaterial.SetFloat(RiverMinWidthId, MinRiverHalfWidth);
            styleMaterial.SetTexture(RealmPaletteTexId, realmPaletteTexture);
            styleMaterial.SetTexture(BiomePaletteTexId, biomePaletteTexture);
            styleMaterial.SetTexture(RealmBorderDistTexId, realmBorderDistTexture);
            styleMaterial.SetTexture(ProvinceBorderDistTexId, provinceBorderDistTexture);
            styleMaterial.SetTexture(CountyBorderDistTexId, countyBorderDistTexture);
            styleMaterial.SetTexture(RoadMaskTexId, roadDistTexture);
            styleMaterial.SetTexture(MarketPaletteTexId, marketPaletteTexture);
            styleMaterial.SetTexture(MarketBorderDistTexId, marketBorderDistTexture);
            if (archdioceseBorderDistTexture != null)
                styleMaterial.SetTexture(ArchdioceseBorderDistTexId, archdioceseBorderDistTexture);
            if (dioceseBorderDistTexture != null)
                styleMaterial.SetTexture(DioceseBorderDistTexId, dioceseBorderDistTexture);
            if (parishBorderDistTexture != null)
                styleMaterial.SetTexture(ParishBorderDistTexId, parishBorderDistTexture);
            styleMaterial.SetFloat(BorderTexelScaleId, borderResolutionScale);
        }

        /// <summary>
        /// Apply all textures and initial settings to the terrain material.
        /// </summary>
        private void ApplyTexturesToMaterial()
        {
            if (styleMaterial == null) return;

            EnsureRoadMaskTexture();
            BindGeneratedTexturesToMaterial();
            styleMaterial.SetTexture(OverlayTexId, politicalIdsTexture);
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

        private static bool IsModeResolveOverlay(MapView.MapMode mode)
        {
            return mode == MapView.MapMode.Political ||
                   mode == MapView.MapMode.Province ||
                   mode == MapView.MapMode.County ||
                   mode == MapView.MapMode.Market ||
                   mode == MapView.MapMode.TransportCost ||
                   mode == MapView.MapMode.MarketAccess ||
                   mode == MapView.MapMode.Religion ||
                   mode == MapView.MapMode.ReligionDiocese ||
                   mode == MapView.MapMode.ReligionParish;
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
            if (resolvePixelBuffer == null || resolvePixelBuffer.Length != size)
                resolvePixelBuffer = new Color[size];
            else
                System.Array.Clear(resolvePixelBuffer, 0, size);
            var resolved = resolvePixelBuffer;

            Color[] realmPalette = realmPaletteTexture.GetPixels();

            bool isMarketMode = currentMapMode == MapView.MapMode.Market;
            bool isMarketAccessMode = currentMapMode == MapView.MapMode.MarketAccess;
            bool isLocalTransportMode = currentMapMode == MapView.MapMode.TransportCost;

            if (isMarketMode)
            {
                Color[] marketPalette = marketPaletteTexture != null ? marketPaletteTexture.GetPixels() : null;

                for (int i = 0; i < size; i++)
                {
                    int cellId = spatialGrid[i];
                    if (cellId < 0 || !mapData.CellById.TryGetValue(cellId, out var cell))
                        continue;

                    if (!cell.IsLand)
                        continue;

                    int marketId = (cellMarketIdById != null && cellId < cellMarketIdById.Length)
                        ? cellMarketIdById[cellId] : 0;

                    Color marketColor = (marketPalette != null && marketId >= 0 && marketId < marketPalette.Length)
                        ? marketPalette[marketId] : LookupPaletteColor(realmPalette, cell.RealmId);

                    // Pack marketId in alpha for shader selection/hover.
                    // Resolve texture is RGBA32 (8-bit), so normalize by 255 not 65535.
                    marketColor.a = marketId / 255f;
                    resolved[i] = marketColor;
                }
            }
            else if (isMarketAccessMode)
            {
                Color[] marketPalette = marketPaletteTexture != null ? marketPaletteTexture.GetPixels() : null;

                // Compute transport costs from each market hub to all cells in its zone
                var costValues = new float[size];
                for (int i = 0; i < size; i++)
                    costValues[i] = float.NaN;

                float minCost = float.MaxValue;
                float maxCost = float.MinValue;

                if (economyState?.Markets != null && transportGraph != null)
                {
                    // For each market, run Dijkstra from hub and collect costs for assigned cells
                    for (int m = 1; m < economyState.Markets.Length; m++)
                    {
                        var market = economyState.Markets[m];
                        if (market.HubCellId <= 0)
                            continue;

                        // Collect all reachable cells from hub
                        var reachable = transportGraph.FindReachable(market.HubCellId, float.MaxValue);

                        for (int i = 0; i < size; i++)
                        {
                            int cellId = spatialGrid[i];
                            if (cellId < 0) continue;
                            if (cellId >= cellIsLandById.Length || !cellIsLandById[cellId]) continue;

                            int cellMarketId = (cellMarketIdById != null && cellId < cellMarketIdById.Length)
                                ? cellMarketIdById[cellId] : 0;
                            if (cellMarketId != m) continue;

                            if (reachable.TryGetValue(cellId, out float cost))
                            {
                                costValues[i] = cost;
                                if (cost < minCost) minCost = cost;
                                if (cost > maxCost) maxCost = cost;
                            }
                        }
                    }
                }

                bool minFinite = !float.IsNaN(minCost) && !float.IsInfinity(minCost);
                bool maxFinite = !float.IsNaN(maxCost) && !float.IsInfinity(maxCost);
                if (!minFinite || !maxFinite || maxCost <= minCost)
                {
                    minCost = 0f;
                    maxCost = 1f;
                }

                float range = Mathf.Max(0.0001f, maxCost - minCost);
                for (int i = 0; i < size; i++)
                {
                    int cellId = spatialGrid[i];
                    if (cellId < 0) continue;
                    if (cellId >= cellIsLandById.Length || !cellIsLandById[cellId]) continue;

                    float cost = costValues[i];
                    if (float.IsNaN(cost) || float.IsInfinity(cost))
                    {
                        Color missing = HeatMissingColor;
                        int noMarket = (cellMarketIdById != null && cellId < cellMarketIdById.Length)
                            ? cellMarketIdById[cellId] : 0;
                        missing.a = noMarket / 255f;
                        resolved[i] = missing;
                        continue;
                    }

                    float normalized = Mathf.Clamp01((cost - minCost) / range);
                    Color heat = EvaluateHeatColor(normalized);

                    int marketId = (cellMarketIdById != null && cellId < cellMarketIdById.Length)
                        ? cellMarketIdById[cellId] : 0;
                    heat.a = marketId / 255f;
                    resolved[i] = heat;
                }
            }
            else if (isLocalTransportMode)
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

                    if (!cell.IsLand)
                        continue;

                    float value = ComputeTransportCost(cell);
                    bool hasValue = true;

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

                    if (!cell.IsLand)
                        continue;

                    float value = values[i];
                    if (float.IsNaN(value) || float.IsInfinity(value))
                    {
                        Color missing = HeatMissingColor;
                        missing.a = 0f;
                        resolved[i] = missing;
                        continue;
                    }

                    float normalized = Mathf.Clamp01((value - minValue) / range);
                    Color heat = EvaluateHeatColor(normalized);
                    heat.a = 0f;
                    resolved[i] = heat;
                }
            }
            else if (currentMapMode == MapView.MapMode.Religion ||
                     currentMapMode == MapView.MapMode.ReligionDiocese ||
                     currentMapMode == MapView.MapMode.ReligionParish)
            {
                // Religion modes with drill-down: use precomputed per-cell colors
                EnsureReligionCellColors();
                bool hasDrill = drillExpandedArchdioceseIds.Count > 0;

                for (int i = 0; i < size; i++)
                {
                    int cellId = spatialGrid[i];
                    if (cellId < 0) continue;
                    if (cellId >= cellIsLandById.Length || !cellIsLandById[cellId]) continue;

                    int archId = (cellArchdioceseIdById != null && cellId < cellArchdioceseIdById.Length) ? cellArchdioceseIdById[cellId] : 0;
                    int dioId = (cellDioceseIdById != null && cellId < cellDioceseIdById.Length) ? cellDioceseIdById[cellId] : 0;
                    int parId = (cellParishIdById != null && cellId < cellParishIdById.Length) ? cellParishIdById[cellId] : 0;

                    int displayLevel = 1;
                    int territoryId = archId;
                    Color territoryColor;

                    if (cachedArchdioceseColorByCell != null && cellId < cachedArchdioceseColorByCell.Length)
                    {
                        territoryColor = cachedArchdioceseColorByCell[cellId];

                        if (hasDrill && archId > 0 && drillExpandedArchdioceseIds.Contains(archId))
                        {
                            displayLevel = 2;
                            territoryId = dioId;
                            territoryColor = cachedDioceseColorByCell[cellId];

                            if (dioId > 0 && drillExpandedDioceseIds.Contains(dioId))
                            {
                                displayLevel = 3;
                                territoryId = parId;
                                territoryColor = cachedParishColorByCell[cellId];
                            }
                        }
                    }
                    else
                    {
                        territoryColor = GetCellFaithColor(cellId);
                    }

                    int packedAlpha = ((displayLevel & 0x3) << 6) | (territoryId & 0x3F);
                    territoryColor.a = packedAlpha / 255f;
                    resolved[i] = territoryColor;
                }
            }
            else
            {
                // Political modes with drill-down: use precomputed per-cell colors
                EnsurePoliticalCellColors();
                bool hasDrill = drillExpandedRealmIds.Count > 0;

                for (int i = 0; i < size; i++)
                {
                    int cellId = spatialGrid[i];
                    if (cellId < 0) continue;
                    if (cellId >= cellIsLandById.Length || !cellIsLandById[cellId]) continue;

                    int displayLevel = 1;
                    Color politicalColor;

                    if (hasDrill && cachedRealmColorByCell != null && cellId < cachedRealmColorByCell.Length)
                    {
                        int realmId = (cellRealmIdById != null && cellId < cellRealmIdById.Length) ? cellRealmIdById[cellId] : 0;
                        int provinceId = (cellProvinceIdById != null && cellId < cellProvinceIdById.Length) ? cellProvinceIdById[cellId] : 0;

                        if (drillExpandedRealmIds.Contains(realmId))
                        {
                            displayLevel = 2;
                            politicalColor = cachedProvinceColorByCell[cellId];

                            if (drillExpandedProvinceIds.Contains(provinceId))
                            {
                                displayLevel = 3;
                                politicalColor = cachedCountyColorByCell[cellId];
                            }
                        }
                        else
                        {
                            politicalColor = cachedRealmColorByCell[cellId];
                        }
                    }
                    else if (cachedRealmColorByCell != null && cellId < cachedRealmColorByCell.Length)
                    {
                        politicalColor = cachedRealmColorByCell[cellId];
                    }
                    else
                    {
                        politicalColor = LookupPaletteColor(realmPalette, cellRealmIdById != null && cellId < cellRealmIdById.Length ? cellRealmIdById[cellId] : 0);
                    }

                    politicalColor.a = displayLevel / 255f;
                    resolved[i] = politicalColor;
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

        /// <summary>
        /// Precompute per-pixel river mask from river distance texture.
        /// Called once; avoids GetPixels() allocation on every resolve.
        /// </summary>
        private void EnsureRiverMaskGrid()
        {
            if (cachedRiverMaskGrid != null) return;
            if (riverMaskTexture == null) return;

            Color[] rivers = riverMaskTexture.GetPixels();
            cachedRiverMaskGrid = new bool[rivers.Length];
            for (int i = 0; i < rivers.Length; i++)
                cachedRiverMaskGrid[i] = IsRiverPixel(rivers[i]);
        }

        /// <summary>
        /// Precompute per-cell colors for all three political levels (realm/province/county).
        /// Called once; results cached for fast drill-down switching.
        /// </summary>
        private void EnsurePoliticalCellColors()
        {
            if (cachedRealmColorByCell != null) return; // already computed

            Color[] realmPalette = realmPaletteTexture?.GetPixels();
            if (realmPalette == null || mapData?.CellById == null) return;

            int maxCellId = 0;
            foreach (var cell in mapData.Cells)
                if (cell.Id > maxCellId) maxCellId = cell.Id;

            cachedRealmColorByCell = new Color[maxCellId + 1];
            cachedProvinceColorByCell = new Color[maxCellId + 1];
            cachedCountyColorByCell = new Color[maxCellId + 1];

            var provinceColorById = BuildProvinceColorOverrides(realmPalette);
            var countyColorById = BuildCountyColorOverrides(provinceColorById, realmPalette);

            foreach (var cell in mapData.Cells)
            {
                if (!cell.IsLand) continue;
                int id = cell.Id;

                Color realmColor = LookupPaletteColor(realmPalette, cell.RealmId);
                cachedRealmColorByCell[id] = realmColor;

                if (provinceColorById.TryGetValue(cell.ProvinceId, out Color pc))
                    cachedProvinceColorByCell[id] = pc;
                else
                    cachedProvinceColorByCell[id] = DeriveProvinceColorFromRealm(realmColor, cell.ProvinceId);

                if (countyColorById.TryGetValue(cell.CountyId, out Color cc))
                    cachedCountyColorByCell[id] = cc;
                else
                    cachedCountyColorByCell[id] = DeriveCountyColorFromProvince(cachedProvinceColorByCell[id], cell.CountyId);
            }
        }

        /// <summary>
        /// Precompute per-cell colors for all three religion levels (archdiocese/diocese/parish).
        /// Called once; results cached for fast drill-down switching.
        /// </summary>
        private void EnsureReligionCellColors()
        {
            if (cachedArchdioceseColorByCell != null) return;
            if (religionState == null || faithPaletteColors == null || mapData?.CellById == null) return;

            int maxCellId = 0;
            foreach (var cell in mapData.Cells)
                if (cell.Id > maxCellId) maxCellId = cell.Id;

            cachedArchdioceseColorByCell = new Color[maxCellId + 1];
            cachedDioceseColorByCell = new Color[maxCellId + 1];
            cachedParishColorByCell = new Color[maxCellId + 1];

            var archdioceseColorById = BuildArchdioceseColors();
            var dioceseColorById = BuildDioceseColors(archdioceseColorById);
            var parishColorById = BuildParishColors(dioceseColorById, archdioceseColorById);

            foreach (var cell in mapData.Cells)
            {
                if (!cell.IsLand) continue;
                int id = cell.Id;

                int archId = (cellArchdioceseIdById != null && id < cellArchdioceseIdById.Length) ? cellArchdioceseIdById[id] : 0;
                int dioId = (cellDioceseIdById != null && id < cellDioceseIdById.Length) ? cellDioceseIdById[id] : 0;
                int parId = (cellParishIdById != null && id < cellParishIdById.Length) ? cellParishIdById[id] : 0;

                Color fallback = GetCellFaithColor(id);

                cachedArchdioceseColorByCell[id] = (archId > 0 && archdioceseColorById.TryGetValue(archId, out Color ac)) ? ac : fallback;
                cachedDioceseColorByCell[id] = (dioId > 0 && dioceseColorById.TryGetValue(dioId, out Color dc)) ? dc : fallback;
                cachedParishColorByCell[id] = (parId > 0 && parishColorById.TryGetValue(parId, out Color pc)) ? pc : fallback;
            }
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

            for (int i = 0; i < size; i++)
            {
                int cellId = spatialGrid[i];
                if (cellId < 0 || cellId >= cellIsLandById.Length || !cellIsLandById[cellId])
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

        private Color GetCellFaithColor(int cellId)
        {
            if (religionState == null || faithPaletteColors == null)
                return Color.gray;

            int countyId = (cellId >= 0 && cellId < cellCountyIdById.Length) ? cellCountyIdById[cellId] : 0;
            if (countyId <= 0 || countyId >= religionState.Adherence.Length)
                return Color.gray;

            var adh = religionState.Adherence[countyId];
            if (adh == null)
                return Color.gray;

            // Blend colors by adherence weight
            float r = 0f, g = 0f, b = 0f;
            float totalAdh = 0f;
            for (int f = 0; f < religionState.FaithCount; f++)
            {
                if (adh[f] > 0.01f)
                {
                    var fc = faithPaletteColors[f];
                    r += fc.r * adh[f];
                    g += fc.g * adh[f];
                    b += fc.b * adh[f];
                    totalAdh += adh[f];
                }
            }

            if (totalAdh < 0.01f)
                return Color.gray; // unaffiliated

            // Normalize
            r /= totalAdh;
            g /= totalAdh;
            b /= totalAdh;

            // Desaturate contested counties (max adherence < 50%)
            float maxAdh = 0f;
            for (int f = 0; f < religionState.FaithCount; f++)
                if (adh[f] > maxAdh) maxAdh = adh[f];

            if (maxAdh < 0.50f)
            {
                float sat = Mathf.Lerp(0.3f, 1f, maxAdh / 0.50f);
                float gray = r * 0.299f + g * 0.587f + b * 0.114f;
                r = Mathf.Lerp(gray, r, sat);
                g = Mathf.Lerp(gray, g, sat);
                b = Mathf.Lerp(gray, b, sat);
            }

            return new Color(r, g, b, 1f);
        }

        /// <summary>Build a color for each archdiocese (base color from faith palette).</summary>
        private Dictionary<int, Color> BuildArchdioceseColors()
        {
            var colors = new Dictionary<int, Color>();
            if (religionState == null || faithPaletteColors == null) return colors;

            for (int a = 1; a < religionState.Archdioceses.Length; a++)
            {
                var arch = religionState.Archdioceses[a];
                if (arch == null) continue;

                Color baseColor = (arch.FaithIndex >= 0 && arch.FaithIndex < faithPaletteColors.Length)
                    ? faithPaletteColors[arch.FaithIndex]
                    : Color.gray;

                // Vary per archdiocese to distinguish same-faith archdioceses (matches realm color variance)
                Color.RGBToHSV(baseColor, out float h, out float s, out float v);
                float hShift = ((a * 0.618034f) % 1f - 0.5f) * 0.06f; // golden ratio spread
                h = (h + hShift + 1f) % 1f;
                // S/V variance matching PoliticalPalette (±0.08)
                float sHash = ((a * 2654435761u) & 0xFFFF) / 65535f; // simple hash 0-1
                float vHash = (((a + 1000) * 2654435761u) & 0xFFFF) / 65535f;
                s = Mathf.Clamp(s + (sHash - 0.5f) * 2f * 0.08f, 0.28f, 0.55f);
                v = Mathf.Clamp(v + (vHash - 0.5f) * 2f * 0.08f, 0.58f, 0.85f);
                colors[arch.Id] = Color.HSVToRGB(h, s, v);
            }

            return colors;
        }

        /// <summary>Build a color for each diocese, varied from its archdiocese base.</summary>
        private Dictionary<int, Color> BuildDioceseColors(Dictionary<int, Color> archdioceseColorById)
        {
            var colors = new Dictionary<int, Color>();
            if (religionState == null) return colors;

            for (int d = 1; d < religionState.Dioceses.Length; d++)
            {
                var diocese = religionState.Dioceses[d];
                if (diocese == null) continue;

                Color parentColor = Color.gray;
                if (dioceseToArchdioceseId != null &&
                    dioceseToArchdioceseId.TryGetValue(diocese.Id, out int archId) &&
                    archdioceseColorById != null &&
                    archdioceseColorById.TryGetValue(archId, out Color ac))
                {
                    parentColor = ac;
                }
                else if (diocese.FaithIndex >= 0 && diocese.FaithIndex < faithPaletteColors.Length)
                {
                    parentColor = faithPaletteColors[diocese.FaithIndex];
                }

                colors[diocese.Id] = DeriveProvinceColorFromRealm(parentColor, diocese.Id);
            }

            return colors;
        }

        /// <summary>Build a color for each parish, varied from its diocese base.</summary>
        private Dictionary<int, Color> BuildParishColors(
            Dictionary<int, Color> dioceseColorById,
            Dictionary<int, Color> archdioceseColorById)
        {
            var colors = new Dictionary<int, Color>();
            if (religionState == null) return colors;

            for (int p = 1; p < religionState.Parishes.Length; p++)
            {
                var parish = religionState.Parishes[p];
                if (parish == null) continue;

                Color parentColor = Color.gray;
                if (parishToDioceseId != null &&
                    parishToDioceseId.TryGetValue(parish.Id, out int dioId) &&
                    dioceseColorById != null &&
                    dioceseColorById.TryGetValue(dioId, out Color dc))
                {
                    parentColor = dc;
                }
                else if (parish.FaithIndex >= 0 && parish.FaithIndex < faithPaletteColors.Length)
                {
                    parentColor = faithPaletteColors[parish.FaithIndex];
                }

                colors[parish.Id] = DeriveCountyColorFromProvince(parentColor, parish.Id);
            }

            return colors;
        }

        /// <summary>Set selection highlight for a religious territory (uses market ID alpha channel).</summary>
        public void SetSelectedReligiousTerritory(int territoryId)
        {
            if (styleMaterial == null) return;
            styleMaterial.SetFloat(SelectedRealmIdId, -1f);
            styleMaterial.SetFloat(SelectedProvinceIdId, -1f);
            styleMaterial.SetFloat(SelectedCountyIdId, -1f);
            float normalizedId = territoryId < 0 ? -1f : territoryId / 255f;
            styleMaterial.SetFloat(SelectedMarketIdId, normalizedId);
        }

        /// <summary>Set hover highlight for a religious territory (uses market ID alpha channel).</summary>
        public void SetHoveredReligiousTerritory(int territoryId)
        {
            if (styleMaterial == null) return;
            styleMaterial.SetFloat(HoveredRealmIdId, -1f);
            styleMaterial.SetFloat(HoveredProvinceIdId, -1f);
            styleMaterial.SetFloat(HoveredCountyIdId, -1f);
            float normalizedId = territoryId < 0 ? -1f : territoryId / 255f;
            styleMaterial.SetFloat(HoveredMarketIdId, normalizedId);
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
        }

        /// <summary>
        /// Regenerate the road mask texture from current road state.
        /// Uses direct thick-line rasterization (same as river mask) — no chamfer transform.
        /// Only touches pixels near roads (~O(road_pixels)) instead of the entire grid.
        /// </summary>
        public void RegenerateRoadDistTexture()
        {
            if (roadDistTexture == null || roadState == null || mapData == null) return;

            var pixels = GenerateRoadMaskPixels(borderWidth, borderHeight, resolutionMultiplier * borderResolutionScale);

            // Resize texture if needed (border resolution may have changed)
            if (roadDistTexture.width != borderWidth || roadDistTexture.height != borderHeight)
            {
                DestroyTexture(roadDistTexture);
                roadDistTexture = new Texture2D(borderWidth, borderHeight, TextureFormat.R8, false);
                roadDistTexture.name = "RoadMaskTexture";
                roadDistTexture.filterMode = FilterMode.Bilinear;
                roadDistTexture.anisoLevel = 8;
                roadDistTexture.wrapMode = TextureWrapMode.Clamp;
            }

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

        /// <summary>
        /// Set economy state for market mode rendering.
        /// Builds cellMarketIdById lookup, market palette, and market border distance textures.
        /// </summary>
        public void SetEconomyState(EconSim.Core.Economy.EconomyState econ, EconSim.Core.Transport.TransportGraph transport)
        {
            economyState = econ;
            transportGraph = transport;

            if (econ?.CountyToMarket == null || econ.Markets == null || mapData == null)
                return;

            // Build cellMarketIdById lookup
            int lookupSize = cellIsLandById != null ? cellIsLandById.Length : 0;
            cellMarketIdById = new int[lookupSize];
            for (int cellId = 0; cellId < lookupSize; cellId++)
            {
                if (!cellIsLandById[cellId]) continue;
                int countyId = (cellId < cellCountyIdById.Length) ? cellCountyIdById[cellId] : 0;
                if (countyId > 0 && countyId < econ.CountyToMarket.Length)
                    cellMarketIdById[cellId] = econ.CountyToMarket[countyId];
            }

            // Generate market palette (256x1): each market gets its hub realm's palette color
            if (marketPaletteTexture != null) DestroyTexture(marketPaletteTexture);
            marketPaletteTexture = new Texture2D(256, 1, TextureFormat.RGBA32, false);
            marketPaletteTexture.name = "MarketPaletteTexture";
            marketPaletteTexture.filterMode = FilterMode.Point;
            marketPaletteTexture.wrapMode = TextureWrapMode.Clamp;

            Color[] realmPalette = realmPaletteTexture != null ? realmPaletteTexture.GetPixels() : null;
            var marketPalettePixels = new Color[256];
            for (int m = 1; m < econ.Markets.Length && m < 256; m++)
            {
                var market = econ.Markets[m];
                marketPalettePixels[m] = LookupPaletteColor(realmPalette, market.HubRealmId);
            }
            marketPaletteTexture.SetPixels(marketPalettePixels);
            marketPaletteTexture.Apply();

            // Generate market border distance texture at border resolution
            int[] borderGrid = BuildBorderSpatialGrid();
            GenerateMarketBorderDistTexture(borderGrid, borderWidth, borderHeight);

            // Bind textures to material
            if (styleMaterial != null)
            {
                styleMaterial.SetTexture(MarketPaletteTexId, marketPaletteTexture);
                styleMaterial.SetTexture(MarketBorderDistTexId, marketBorderDistTexture);
            }

            // Invalidate mode caches for market modes
            InvalidateModeColorResolveCache(MapView.MapMode.Market);
            InvalidateModeColorResolveCache(MapView.MapMode.MarketAccess);

            Debug.Log($"MapOverlayManager: Set economy state with {econ.Markets.Length - 1} markets");
        }

        public void SetReligionState(EconSim.Core.Religious.ReligionState religion)
        {
            religionState = religion;

            if (religion == null || religion.FaithCount == 0)
                return;

            // Generate faith palette: evenly spaced hues, high saturation
            faithPaletteColors = new Color[religion.FaithCount];
            for (int f = 0; f < religion.FaithCount; f++)
            {
                float hue = (float)f / religion.FaithCount;
                faithPaletteColors[f] = Color.HSVToRGB(hue, 0.33f, 0.77f);
            }

            // Build reverse lookups: parish→diocese, diocese→archdiocese
            parishToDioceseId = new Dictionary<int, int>();
            dioceseToArchdioceseId = new Dictionary<int, int>();

            for (int d = 1; d < religion.Dioceses.Length; d++)
            {
                var diocese = religion.Dioceses[d];
                if (diocese == null) continue;
                foreach (int pid in diocese.ParishIds)
                    parishToDioceseId[pid] = diocese.Id;
            }

            for (int a = 1; a < religion.Archdioceses.Length; a++)
            {
                var arch = religion.Archdioceses[a];
                if (arch == null) continue;
                foreach (int did in arch.DioceseIds)
                    dioceseToArchdioceseId[did] = arch.Id;
            }

            // Build cell→territory lookup arrays
            int maxCellId = cellCountyIdById != null ? cellCountyIdById.Length : 0;
            cellParishIdById = new int[maxCellId];
            cellDioceseIdById = new int[maxCellId];
            cellArchdioceseIdById = new int[maxCellId];

            for (int cellId = 0; cellId < maxCellId; cellId++)
            {
                int countyId = cellCountyIdById[cellId];
                if (countyId <= 0 || countyId >= religion.CountyParishes.Length)
                    continue;

                var parishes = religion.CountyParishes[countyId];
                if (parishes == null || parishes.Count == 0)
                    continue;

                // Pick parish matching majority faith for this county
                int majorityFaith = (countyId < religion.MajorityFaith.Length) ? religion.MajorityFaith[countyId] : 0;
                int majorityFaithIndex = -1;
                if (majorityFaith > 0 && religion.ReligionToFaithIndex != null)
                    religion.ReligionToFaithIndex.TryGetValue(majorityFaith, out majorityFaithIndex);

                int parishId = 0;
                foreach (int pid in parishes)
                {
                    if (pid <= 0 || pid >= religion.Parishes.Length) continue;
                    var parish = religion.Parishes[pid];
                    if (parish == null) continue;
                    if (parishId == 0) parishId = pid; // fallback to first
                    if (parish.FaithIndex == majorityFaithIndex)
                    {
                        parishId = pid;
                        break;
                    }
                }

                cellParishIdById[cellId] = parishId;

                if (parishId > 0 && parishToDioceseId.TryGetValue(parishId, out int dioId))
                {
                    cellDioceseIdById[cellId] = dioId;
                    if (dioceseToArchdioceseId.TryGetValue(dioId, out int archId))
                        cellArchdioceseIdById[cellId] = archId;
                }
            }

            // Generate religious territory border distance textures at border resolution
            int[] religBorderGrid = BuildBorderSpatialGrid();
            GenerateReligiousBorderDistTextures(religBorderGrid, borderWidth, borderHeight);

            // Bind border textures to material
            if (styleMaterial != null)
            {
                styleMaterial.SetTexture(ArchdioceseBorderDistTexId, archdioceseBorderDistTexture);
                styleMaterial.SetTexture(DioceseBorderDistTexId, dioceseBorderDistTexture);
                styleMaterial.SetTexture(ParishBorderDistTexId, parishBorderDistTexture);
            }

            InvalidateModeColorResolveCache(MapView.MapMode.Religion);
            InvalidateModeColorResolveCache(MapView.MapMode.ReligionDiocese);
            InvalidateModeColorResolveCache(MapView.MapMode.ReligionParish);

            Debug.Log($"MapOverlayManager: Set religion state with {religion.FaithCount} faiths, " +
                $"{religion.Parishes.Length - 1} parishes, {religion.Dioceses.Length - 1} dioceses, " +
                $"{religion.Archdioceses.Length - 1} archdioceses");
        }

        public int GetCellParishId(int cellId)
        {
            if (cellParishIdById == null || cellId < 0 || cellId >= cellParishIdById.Length) return 0;
            return cellParishIdById[cellId];
        }

        public int GetCellDioceseId(int cellId)
        {
            if (cellDioceseIdById == null || cellId < 0 || cellId >= cellDioceseIdById.Length) return 0;
            return cellDioceseIdById[cellId];
        }

        public int GetCellArchdioceseId(int cellId)
        {
            if (cellArchdioceseIdById == null || cellId < 0 || cellId >= cellArchdioceseIdById.Length) return 0;
            return cellArchdioceseIdById[cellId];
        }

        public int GetDioceseArchdioceseId(int dioceseId)
        {
            if (dioceseToArchdioceseId != null && dioceseToArchdioceseId.TryGetValue(dioceseId, out int archId))
                return archId;
            return 0;
        }

        public void SetDrillState(HashSet<int> expandedRealms, HashSet<int> expandedProvinces,
            HashSet<int> expandedArchdioceses, HashSet<int> expandedDioceses)
        {
            drillExpandedRealmIds = expandedRealms ?? new HashSet<int>();
            drillExpandedProvinceIds = expandedProvinces ?? new HashSet<int>();
            drillExpandedArchdioceseIds = expandedArchdioceses ?? new HashSet<int>();
            drillExpandedDioceseIds = expandedDioceses ?? new HashSet<int>();
        }

        public void InvalidatePoliticalOverlay()
        {
            InvalidateModeColorResolveCache(MapView.MapMode.Political);
            InvalidateModeColorResolveCache(MapView.MapMode.Province);
            InvalidateModeColorResolveCache(MapView.MapMode.County);
        }

        /// <summary>
        /// Force recomputation of cached per-cell political colors (e.g. after political map changes).
        /// </summary>
        public void InvalidatePoliticalCellColors()
        {
            cachedRealmColorByCell = null;
            cachedProvinceColorByCell = null;
            cachedCountyColorByCell = null;
            InvalidatePoliticalOverlay();
        }

        public void InvalidateReligionOverlay()
        {
            InvalidateModeColorResolveCache(MapView.MapMode.Religion);
            InvalidateModeColorResolveCache(MapView.MapMode.ReligionDiocese);
            InvalidateModeColorResolveCache(MapView.MapMode.ReligionParish);
        }

        /// <summary>
        /// Force recomputation of cached per-cell religion colors (e.g. after adherence spread).
        /// </summary>
        public void InvalidateReligionCellColors()
        {
            cachedArchdioceseColorByCell = null;
            cachedDioceseColorByCell = null;
            cachedParishColorByCell = null;
            InvalidateReligionOverlay();
        }

        private void GenerateMarketBorderDistTexture(int[] sourceGrid, int gridW, int gridH)
        {
            if (marketBorderDistTexture != null) DestroyTexture(marketBorderDistTexture);
            marketBorderDistTexture = new Texture2D(gridW, gridH, TextureFormat.R8, false);
            marketBorderDistTexture.name = "MarketBorderDistTexture";
            marketBorderDistTexture.filterMode = FilterMode.Bilinear;
            marketBorderDistTexture.wrapMode = TextureWrapMode.Clamp;

            int size = gridW * gridH;

            // Build market grid from spatial grid
            int[] marketGrid = new int[size];
            bool[] isLand = new bool[size];

            for (int i = 0; i < size; i++)
            {
                int cellId = sourceGrid[i];
                if (cellId >= 0 && cellId < cellIsLandById.Length && cellIsLandById[cellId])
                {
                    isLand[i] = true;
                    marketGrid[i] = (cellMarketIdById != null && cellId < cellMarketIdById.Length)
                        ? cellMarketIdById[cellId] : 0;
                }
                else
                {
                    marketGrid[i] = -1;
                    isLand[i] = false;
                }
            }

            // Seed boundaries: find pixels adjacent to different market zone
            float[] marketDist = new float[size];
            Array.Fill(marketDist, 255f);

            System.Threading.Tasks.Parallel.For(0, gridH, y =>
            {
                int row = y * gridW;
                for (int x = 0; x < gridW; x++)
                {
                    int idx = row + x;
                    if (!isLand[idx]) continue;

                    int myMarket = marketGrid[idx];
                    bool isBoundary = false;

                    for (int dy = -1; dy <= 1 && !isBoundary; dy++)
                    {
                        for (int dx = -1; dx <= 1 && !isBoundary; dx++)
                        {
                            if (dx == 0 && dy == 0) continue;
                            int nx = x + dx;
                            int ny = y + dy;
                            if (nx < 0 || nx >= gridW || ny < 0 || ny >= gridH)
                            {
                                isBoundary = true;
                                continue;
                            }
                            int nIdx = ny * gridW + nx;
                            if (!isLand[nIdx])
                            {
                                isBoundary = true;
                                continue;
                            }
                            if (marketGrid[nIdx] != myMarket)
                                isBoundary = true;
                        }
                    }

                    if (isBoundary)
                        marketDist[idx] = 0f;
                }
            });

            // Chamfer transform
            RunChamferTransform(marketDist, isLand, gridW, gridH);

            // Convert to R8
            byte[] pixels = new byte[size];
            for (int i = 0; i < size; i++)
                pixels[i] = (byte)Mathf.Clamp(Mathf.RoundToInt(marketDist[i]), 0, 255);

            marketBorderDistTexture.LoadRawTextureData(pixels);
            marketBorderDistTexture.Apply();
        }

        private void GenerateReligiousBorderDistTextures(int[] sourceGrid, int gridW, int gridH)
        {
            if (archdioceseBorderDistTexture != null) DestroyTexture(archdioceseBorderDistTexture);
            if (dioceseBorderDistTexture != null) DestroyTexture(dioceseBorderDistTexture);
            if (parishBorderDistTexture != null) DestroyTexture(parishBorderDistTexture);

            int size = gridW * gridH;

            // Build per-pixel grids from spatial grid
            int[] archGrid = new int[size];
            int[] dioGrid = new int[size];
            int[] parGrid = new int[size];
            bool[] isLand = new bool[size];

            for (int i = 0; i < size; i++)
            {
                int cellId = sourceGrid[i];
                if (cellId >= 0 && cellId < cellIsLandById.Length && cellIsLandById[cellId])
                {
                    isLand[i] = true;
                    archGrid[i] = (cellArchdioceseIdById != null && cellId < cellArchdioceseIdById.Length)
                        ? cellArchdioceseIdById[cellId] : 0;
                    dioGrid[i] = (cellDioceseIdById != null && cellId < cellDioceseIdById.Length)
                        ? cellDioceseIdById[cellId] : 0;
                    parGrid[i] = (cellParishIdById != null && cellId < cellParishIdById.Length)
                        ? cellParishIdById[cellId] : 0;
                }
                else
                {
                    archGrid[i] = -1;
                    dioGrid[i] = -1;
                    parGrid[i] = -1;
                    isLand[i] = false;
                }
            }

            // Seed boundaries and run chamfer transforms
            float[] archDist = new float[size];
            float[] dioDist = new float[size];
            float[] parDist = new float[size];
            Array.Fill(archDist, 255f);
            Array.Fill(dioDist, 255f);
            Array.Fill(parDist, 255f);

            System.Threading.Tasks.Parallel.For(0, gridH, y =>
            {
                int row = y * gridW;
                for (int x = 0; x < gridW; x++)
                {
                    int idx = row + x;
                    if (!isLand[idx]) continue;

                    int myArch = archGrid[idx];
                    int myDio = dioGrid[idx];
                    int myPar = parGrid[idx];
                    bool archBoundary = false;
                    bool dioBoundary = false;
                    bool parBoundary = false;

                    for (int dy = -1; dy <= 1 && !(archBoundary && dioBoundary && parBoundary); dy++)
                    {
                        for (int dx = -1; dx <= 1 && !(archBoundary && dioBoundary && parBoundary); dx++)
                        {
                            if (dx == 0 && dy == 0) continue;
                            int nx = x + dx;
                            int ny = y + dy;
                            if (nx < 0 || nx >= gridW || ny < 0 || ny >= gridH)
                            {
                                archBoundary = true;
                                continue;
                            }
                            int nIdx = ny * gridW + nx;
                            if (!isLand[nIdx])
                            {
                                // Water/river neighbor — archdiocese edge (coast, lake, river).
                                archBoundary = true;
                                continue;
                            }

                            if (!archBoundary && archGrid[nIdx] != myArch)
                                archBoundary = true;
                            if (!dioBoundary && dioGrid[nIdx] != myDio)
                                dioBoundary = true;
                            if (!parBoundary && parGrid[nIdx] != myPar)
                                parBoundary = true;
                        }
                    }

                    if (archBoundary) archDist[idx] = 0f;
                    if (dioBoundary) dioDist[idx] = 0f;
                    if (parBoundary) parDist[idx] = 0f;
                }
            });

            Task archTask = Task.Run(() => RunChamferTransform(archDist, isLand, gridW, gridH));
            Task dioTask = Task.Run(() => RunChamferTransform(dioDist, isLand, gridW, gridH));
            Task parTask = Task.Run(() => RunChamferTransform(parDist, isLand, gridW, gridH));
            Task.WaitAll(archTask, dioTask, parTask);

            archdioceseBorderDistTexture = CreateBorderDistTexture("ArchdioceseBorderDistTexture", DistToBytes(archDist), "archdiocese_border_dist", gridW, gridH);
            dioceseBorderDistTexture = CreateBorderDistTexture("DioceseBorderDistTexture", DistToBytes(dioDist), "diocese_border_dist", gridW, gridH);
            parishBorderDistTexture = CreateBorderDistTexture("ParishBorderDistTexture", DistToBytes(parDist), "parish_border_dist", gridW, gridH);

            Debug.Log($"MapOverlayManager: Generated religious border distance textures {gridW}x{gridH}");
        }

        public IEnumerator RunDeferredStartupWork()
        {
            // Warm up political hierarchy modes after initial load so first user switch is instant.
            if (currentMapMode != MapView.MapMode.Province)
            {
                PrewarmOverlayModeResolveCache(MapView.MapMode.Province);
                yield return null;
            }

            if (currentMapMode != MapView.MapMode.County)
            {
                PrewarmOverlayModeResolveCache(MapView.MapMode.County);
                yield return null;
            }

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

        public void SetNoisyEdgeStyle(
            float sampleSpacingPx,
            int maxSamples,
            float roughness,
            float amplitudePerResolution,
            float amplitudeCap,
            float bandPaddingPx,
            bool rebuildSpatialTextures = true)
        {
            SetNoisyEdgeStyle(
                new NoisyEdgeStyle(
                    sampleSpacingPx,
                    maxSamples,
                    roughness,
                    amplitudePerResolution,
                    amplitudeCap,
                    bandPaddingPx),
                rebuildSpatialTextures);
        }

        public void SetNoisyEdgeStyle(NoisyEdgeStyle style, bool rebuildSpatialTextures = true)
        {
            NoisyEdgeStyle clampedStyle = ClampNoisyEdgeStyle(style);
            if (noisyEdgeStyle.Equals(clampedStyle))
                return;

            noisyEdgeStyle = clampedStyle;

            if (rebuildSpatialTextures)
                RebuildSpatialTexturesForNoisyEdgeStyle();
        }

        public NoisyEdgeStyle GetNoisyEdgeStyle()
        {
            return noisyEdgeStyle;
        }

        private static NoisyEdgeStyle ClampNoisyEdgeStyle(NoisyEdgeStyle style)
        {
            return new NoisyEdgeStyle(
                Mathf.Clamp(style.SampleSpacingPx, 0.5f, 12f),
                Mathf.Clamp(style.MaxSamples, 8, 512),
                Mathf.Clamp(style.Roughness, 0.2f, 0.95f),
                Mathf.Clamp(style.AmplitudePerResolution, 0.1f, 4f),
                Mathf.Clamp(style.AmplitudeCap, 0.5f, 64f),
                Mathf.Clamp(style.BandPaddingPx, 0f, 8f));
        }

        private void RebuildSpatialTexturesForNoisyEdgeStyle()
        {
            Profiler.Begin("RebuildSpatialGridForNoisyEdges");
            BuildSpatialGrid();
            Profiler.End();

            Profiler.Begin("RegenerateSpatialDataTextures");
            DestroyTexture(politicalIdsTexture);
            DestroyTexture(geographyBaseTexture);
            DestroyTexture(vegetationTexture);
            DestroyTexture(riverMaskTexture);
            DestroyTexture(heightmapTexture);
            DestroyTexture(reliefNormalTexture);
            DestroyTexture(colormapTexture);
            DestroyTexture(realmBorderDistTexture);
            DestroyTexture(provinceBorderDistTexture);
            DestroyTexture(countyBorderDistTexture);
            DestroyTexture(archdioceseBorderDistTexture);
            DestroyTexture(dioceseBorderDistTexture);
            DestroyTexture(parishBorderDistTexture);
            GenerateDataTextures();
            GenerateRiverMaskTexture();
            GenerateHeightmapTexture();
            int[] rebuildBorderGrid = BuildBorderSpatialGrid();
            GenerateAdministrativeBorderDistTextures(rebuildBorderGrid);
            GenerateVegetationTexture();
            if (roadState != null)
                RegenerateRoadDistTexture();
            Profiler.End();

            if (economyState?.CountyToMarket != null && economyState.Markets != null)
                GenerateMarketBorderDistTexture(rebuildBorderGrid, borderWidth, borderHeight);

            if (cellParishIdById != null)
                GenerateReligiousBorderDistTextures(rebuildBorderGrid, borderWidth, borderHeight);

            if (overlayTextureCacheByLayer.Count > 0)
            {
                foreach (Texture2D overlayTexture in overlayTextureCacheByLayer.Values)
                    DestroyTexture(overlayTexture);
                overlayTextureCacheByLayer.Clear();
            }

            if (styleMaterial != null)
            {
                EnsureRoadMaskTexture();
                BindGeneratedTexturesToMaterial();
            }

            // Clear all cached cell colors and mode color resolve caches
            cachedRealmColorByCell = null;
            cachedProvinceColorByCell = null;
            cachedCountyColorByCell = null;
            cachedArchdioceseColorByCell = null;
            cachedDioceseColorByCell = null;
            cachedParishColorByCell = null;

            InvalidateModeColorResolveCache(MapView.MapMode.Political);
            InvalidateModeColorResolveCache(MapView.MapMode.Province);
            InvalidateModeColorResolveCache(MapView.MapMode.County);
            InvalidateModeColorResolveCache(MapView.MapMode.Market);
            InvalidateModeColorResolveCache(MapView.MapMode.TransportCost);
            InvalidateModeColorResolveCache(MapView.MapMode.MarketAccess);
            InvalidateModeColorResolveCache(MapView.MapMode.Religion);
            InvalidateModeColorResolveCache(MapView.MapMode.ReligionDiocese);
            InvalidateModeColorResolveCache(MapView.MapMode.ReligionParish);

            if (currentOverlayLayer == OverlayLayer.None)
            {
                if (styleMaterial != null)
                    styleMaterial.SetTexture(OverlayTexId, politicalIdsTexture);
            }
            else
            {
                ApplyOverlayToMaterial();
            }

            RegenerateModeColorResolveTexture();
            overlayCacheDirty = true;
        }

        private bool SupportsPathStyle()
        {
            return styleMaterial != null &&
                   styleMaterial.HasProperty(PathDashLengthId) &&
                   styleMaterial.HasProperty(PathGapLengthId) &&
                   styleMaterial.HasProperty(PathWidthId);
        }

        private byte[] GenerateRoadMaskPixels(int gridW, int gridH, float effectiveMultiplier)
        {
            var pixels = new byte[gridW * gridH];
            float scale = effectiveMultiplier;

            // Road width settings (in grid pixels)
            float configuredPathWidth = GetMaterialFloatOr(PathWidthId, 0.8f);
            float pathWidth = configuredPathWidth * effectiveMultiplier;
            float roadWidth = pathWidth * 1.8f;

            float configuredDashLength = GetMaterialFloatOr(PathDashLengthId, 1.8f);
            float configuredGapLength = GetMaterialFloatOr(PathGapLengthId, 2.4f);
            float dashLength = configuredDashLength * effectiveMultiplier;
            float gapLength = configuredGapLength * effectiveMultiplier;
            float patternLength = Mathf.Max(0.01f, dashLength + gapLength);

            var roads = roadState.GetAllRoads();

            // Apply the same noisy-edge technique used for zone/rivers.
            // Roads are slightly straighter than paths.
            uint roadNetworkSeed = BuildMapSeed(911);
            const float roadAmplitudeScale = 0.65f;
            const float pathAmplitudeScale = 0.9f;

            foreach (var (cellA, cellB, tier) in roads)
            {
                if (!mapData.CellById.TryGetValue(cellA, out var dataA)) continue;
                if (!mapData.CellById.TryGetValue(cellB, out var dataB)) continue;

                float ax = dataA.Center.X * scale;
                float ay = dataA.Center.Y * scale;
                float bx = dataB.Center.X * scale;
                float by = dataB.Center.Y * scale;

                float width = tier == RoadTier.Road ? roadWidth : pathWidth;
                uint roadSeed = BuildUnorderedPairSeed(roadNetworkSeed, cellA, cellB);
                float amplitudeScale = tier == RoadTier.Road ? roadAmplitudeScale : pathAmplitudeScale;
                var controlPoints = new List<Vector2>(2)
                {
                    new Vector2(ax, ay),
                    new Vector2(bx, by)
                };
                List<Vector2> noisyPath = BuildNoisyRasterPath(controlPoints, roadSeed, amplitudeScale, smoothSamplesPerSegment: 0);
                RasterizeDashedPath(pixels, noisyPath, width, dashLength, patternLength, gridW, gridH);
            }

            return pixels;
        }

        private void RasterizeSolidPath(byte[] pixels, List<Vector2> path, Func<int, int, float> widthForSegment, int gridW, int gridH)
        {
            if (pixels == null || path == null || path.Count < 2 || widthForSegment == null)
                return;

            int segmentCount = path.Count - 1;
            for (int i = 0; i < segmentCount; i++)
            {
                float width = widthForSegment(i, segmentCount);
                if (width <= 0f)
                    continue;

                DrawThickLine(pixels, path[i], path[i + 1], width, gridW, gridH);
            }
        }

        private void RasterizeDashedPath(byte[] pixels, List<Vector2> path, float width, float dashLength, float patternLength, int gridW, int gridH)
        {
            if (pixels == null || path == null || path.Count < 2 || width <= 0f)
                return;

            float dashProgress = 0f;
            for (int i = 0; i < path.Count - 1; i++)
            {
                Vector2 start = path[i];
                Vector2 end = path[i + 1];
                float segmentLength = Vector2.Distance(start, end);
                if (segmentLength <= 0.001f)
                    continue;

                DrawDashedSegment(
                    pixels,
                    start,
                    end,
                    width,
                    dashLength,
                    patternLength,
                    ref dashProgress,
                    gridW,
                    gridH);
            }
        }

        private static void DrawDashedSegment(
            byte[] pixels,
            Vector2 start,
            Vector2 end,
            float width,
            float dashLength,
            float patternLength,
            ref float dashProgress,
            int gridW,
            int gridH)
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
                        DrawThickLine(pixels, worldStart, worldEnd, width, gridW, gridH);
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
        /// Mode: 1=political, 2=province, 3=county,
        /// 6=biomes (vertex-blended), 7=channel-inspector, 8=local transport
        /// </summary>
        public void SetMapMode(MapView.MapMode mode)
        {
            if (styleMaterial == null) return;
            bool modeChanged = currentMapMode != mode;
            currentMapMode = mode;

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
                case MapView.MapMode.Religion:
                    shaderMode = 10;
                    break;
                case MapView.MapMode.ReligionDiocese:
                    shaderMode = 11;
                    break;
                case MapView.MapMode.ReligionParish:
                    shaderMode = 12;
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
        }

        public void SetSelectedMarket(int marketId)
        {
            if (styleMaterial == null) return;
            styleMaterial.SetFloat(SelectedRealmIdId, -1f);
            styleMaterial.SetFloat(SelectedProvinceIdId, -1f);
            styleMaterial.SetFloat(SelectedCountyIdId, -1f);
            float normalizedId = marketId < 0 ? -1f : marketId / 255f;
            styleMaterial.SetFloat(SelectedMarketIdId, normalizedId);
        }

        public void SetHoveredMarket(int marketId)
        {
            if (styleMaterial == null) return;
            styleMaterial.SetFloat(HoveredRealmIdId, -1f);
            styleMaterial.SetFloat(HoveredProvinceIdId, -1f);
            styleMaterial.SetFloat(HoveredCountyIdId, -1f);
            float normalizedId = marketId < 0 ? -1f : marketId / 255f;
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
            int radius = 10 * resolutionMultiplier + Mathf.CeilToInt(GetNoisyEdgeBaseAmplitudePixels());

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
                cachedRealmColorByCell = null;
                cachedProvinceColorByCell = null;
                cachedCountyColorByCell = null;
                InvalidateModeColorResolveCache(MapView.MapMode.Political);
                InvalidateModeColorResolveCache(MapView.MapMode.Province);
                InvalidateModeColorResolveCache(MapView.MapMode.County);

                if (currentMapMode == MapView.MapMode.Political ||
                    currentMapMode == MapView.MapMode.Province ||
                    currentMapMode == MapView.MapMode.County)
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
            AddTextureForDestroy(texturesToDestroy, biomePaletteTexture);
            AddTextureForDestroy(texturesToDestroy, realmBorderDistTexture);
            AddTextureForDestroy(texturesToDestroy, provinceBorderDistTexture);
            AddTextureForDestroy(texturesToDestroy, countyBorderDistTexture);
            AddTextureForDestroy(texturesToDestroy, roadDistTexture);
            AddTextureForDestroy(texturesToDestroy, marketPaletteTexture);
            AddTextureForDestroy(texturesToDestroy, marketBorderDistTexture);
            AddTextureForDestroy(texturesToDestroy, archdioceseBorderDistTexture);
            AddTextureForDestroy(texturesToDestroy, dioceseBorderDistTexture);
            AddTextureForDestroy(texturesToDestroy, parishBorderDistTexture);
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
            colormapTexture = null;
            riverMaskTexture = null;
            realmPaletteTexture = null;
            biomePaletteTexture = null;
            realmBorderDistTexture = null;
            provinceBorderDistTexture = null;
            countyBorderDistTexture = null;
            roadDistTexture = null;
            marketPaletteTexture = null;
            marketBorderDistTexture = null;
            archdioceseBorderDistTexture = null;
            dioceseBorderDistTexture = null;
            parishBorderDistTexture = null;
            modeColorResolveTexture = null;
            riverMaskPixels = null;
            cachedRiverMaskGrid = null;
            resolvePixelBuffer = null;
            cachedRealmColorByCell = null;
            cachedProvinceColorByCell = null;
            cachedCountyColorByCell = null;
            cachedArchdioceseColorByCell = null;
            cachedDioceseColorByCell = null;
            cachedParishColorByCell = null;
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
