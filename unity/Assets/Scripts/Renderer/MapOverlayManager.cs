using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using EconSim.Core.Data;
using EconSim.Core.Economy;
using EconSim.Core.Rendering;
using EconSim.Bridge;
using MapGen.Core;
using Profiler = EconSim.Core.Common.StartupProfiler;

namespace EconSim.Renderer
{
    /// <summary>
    /// Manages shader-based map overlays by generating data textures and controlling shader parameters.
    /// Provides infrastructure for borders, heat maps, and other visual overlays without mesh regeneration.
    /// </summary>
public class MapOverlayManager
{
        public enum ChannelDebugView
        {
            CellDataR = 0,
            CellDataG = 1,
            CellDataB = 2,
            CellDataA = 3,
            RealmBorderDist = 4,
            ProvinceBorderDist = 5,
            CountyBorderDist = 6,
            MarketBorderDist = 7,
            RiverMask = 8,
            Heightmap = 9,
            RoadMask = 10
        }

        // Shader property IDs (cached for performance)
        private static readonly int CellDataTexId = Shader.PropertyToID("_CellDataTex");
        private static readonly int HeightmapTexId = Shader.PropertyToID("_HeightmapTex");
        private static readonly int RiverMaskTexId = Shader.PropertyToID("_RiverMaskTex");
        private static readonly int RealmPaletteTexId = Shader.PropertyToID("_RealmPaletteTex");
        private static readonly int MarketPaletteTexId = Shader.PropertyToID("_MarketPaletteTex");
        private static readonly int BiomePaletteTexId = Shader.PropertyToID("_BiomePaletteTex");
        private static readonly int BiomeMatrixTexId = Shader.PropertyToID("_BiomeMatrixTex");
        private static readonly int CellToMarketTexId = Shader.PropertyToID("_CellToMarketTex");
        private static readonly int RealmBorderDistTexId = Shader.PropertyToID("_RealmBorderDistTex");
        private static readonly int ProvinceBorderDistTexId = Shader.PropertyToID("_ProvinceBorderDistTex");
        private static readonly int CountyBorderDistTexId = Shader.PropertyToID("_CountyBorderDistTex");
        private static readonly int MarketBorderDistTexId = Shader.PropertyToID("_MarketBorderDistTex");
        private static readonly int RoadMaskTexId = Shader.PropertyToID("_RoadMaskTex");
        private static readonly int PathDashLengthId = Shader.PropertyToID("_PathDashLength");
        private static readonly int PathGapLengthId = Shader.PropertyToID("_PathGapLength");
        private static readonly int PathWidthId = Shader.PropertyToID("_PathWidth");
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
        private static readonly int WaterDepthRangeId = Shader.PropertyToID("_WaterDepthRange");
        private static readonly int WaterShallowAlphaId = Shader.PropertyToID("_WaterShallowAlpha");
        private static readonly int WaterDeepAlphaId = Shader.PropertyToID("_WaterDeepAlpha");
        private static readonly int RiverDepthId = Shader.PropertyToID("_RiverDepth");
        private static readonly int RiverDarkenId = Shader.PropertyToID("_RiverDarken");
        private static readonly int ShimmerScaleId = Shader.PropertyToID("_ShimmerScale");
        private static readonly int ShimmerSpeedId = Shader.PropertyToID("_ShimmerSpeed");
        private static readonly int ShimmerIntensityId = Shader.PropertyToID("_ShimmerIntensity");

        private MapData mapData;
        private EconomyState economyState;
        private Material terrainMaterial;
        private readonly ElevationDomain elevationDomain;

        // Resolution multiplier for higher quality borders
        private int resolutionMultiplier;
        private int baseWidth;
        private int baseHeight;

        // Data textures
        private Texture2D cellDataTexture;      // RGBAFloat: RealmId, ProvinceId, BiomeId+WaterFlag, CountyId

        /// <summary>
        /// Public accessor for the cell data texture (for border masking).
        /// </summary>
        public Texture2D CellDataTexture => cellDataTexture;
        private Texture2D cellToMarketTexture;  // R16: CellId -> MarketId mapping (dynamic)
        private Texture2D heightmapTexture;     // RFloat: smoothed height values
        private Texture2D riverMaskTexture;     // R8: river mask (1 = river, 0 = not river)
        private Texture2D realmPaletteTexture;  // 256x1: realm colors
        private Texture2D marketPaletteTexture; // 256x1: market colors
        private Texture2D biomePaletteTexture;  // 256x1: biome colors
        private Texture2D biomeElevationMatrix; // 64x64: biome × elevation colors
        private Texture2D realmBorderDistTexture; // R8: distance to nearest realm boundary (texels)
        private Texture2D provinceBorderDistTexture; // R8: distance to nearest province boundary (texels)
        private Texture2D countyBorderDistTexture;   // R8: distance to nearest county boundary (texels)
        private Texture2D marketBorderDistTexture;   // R8: distance to nearest market zone boundary (texels, dynamic)
        private Texture2D roadDistTexture;             // R8: distance to nearest road centerline (texels, dynamic)

        // Road state (cached for regeneration)
        private RoadState roadState;
        private float cachedPathDashLength = -1f;
        private float cachedPathGapLength = -1f;
        private float cachedPathWidth = -1f;

        // Spatial lookup grid: maps data pixel coordinates to cell IDs
        private int[] spatialGrid;
        private int gridWidth;
        private int gridHeight;

        // Raw texture data for incremental updates
        private Color[] cellDataPixels;

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

        /// <summary>
        /// Create overlay manager with specified resolution multiplier.
        /// </summary>
        /// <param name="mapData">Map data source</param>
        /// <param name="terrainMaterial">Material to apply textures to</param>
        /// <param name="resolutionMultiplier">Multiplier for data texture resolution (1=base, 2=2x, 3=3x). Higher = smoother borders but more memory.</param>
        public MapOverlayManager(MapData mapData, Material terrainMaterial, int resolutionMultiplier = 2)
        {
            this.mapData = mapData;
            this.terrainMaterial = terrainMaterial;
            this.resolutionMultiplier = Mathf.Clamp(resolutionMultiplier, 1, 8);
            elevationDomain = ElevationDomains.InferFromSeaLevel(mapData?.Info?.SeaLevel ?? ElevationDomains.Simulation.SeaLevel);

            baseWidth = mapData.Info.Width;
            baseHeight = mapData.Info.Height;
            gridWidth = baseWidth * this.resolutionMultiplier;
            gridHeight = baseHeight * this.resolutionMultiplier;

            Profiler.Begin("BuildSpatialGrid");
            BuildSpatialGrid();
            Profiler.End();

            Profiler.Begin("GenerateDataTextures");
            GenerateDataTextures();
            Profiler.End();

            Profiler.Begin("GenerateHeightmapTexture");
            GenerateHeightmapTexture();
            Profiler.End();

            Profiler.Begin("GenerateRiverMaskTexture");
            GenerateRiverMaskTexture();
            Profiler.End();

            Profiler.Begin("GenerateRealmBorderDistTexture");
            GenerateRealmBorderDistTexture();
            Profiler.End();

            Profiler.Begin("GenerateProvinceBorderDistTexture");
            GenerateProvinceBorderDistTexture();
            Profiler.End();

            Profiler.Begin("GenerateCountyBorderDistTexture");
            GenerateCountyBorderDistTexture();
            Profiler.End();

            Profiler.Begin("GeneratePaletteTextures");
            GeneratePaletteTextures();
            Profiler.End();

            Profiler.Begin("GenerateBiomeElevationMatrix");
            GenerateBiomeElevationMatrix();
            Profiler.End();



            Profiler.Begin("ApplyTexturesToMaterial");
            ApplyTexturesToMaterial();
            Profiler.End();
            SetSeaLevel(elevationDomain.SeaLevel / elevationDomain.Max);

            // Debug output
            TextureDebugger.SaveTexture(heightmapTexture, "heightmap");
            TextureDebugger.SaveTexture(cellDataTexture, "cell_data");
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
            spatialGrid = new int[gridWidth * gridHeight];
            var distanceSqGrid = new float[gridWidth * gridHeight];

            // Initialize grids
            Parallel.For(0, spatialGrid.Length, i =>
            {
                spatialGrid[i] = -1;
                distanceSqGrid[i] = float.MaxValue;
            });

            float scale = resolutionMultiplier;

            // PHASE 1: Fast Voronoi fill using cell centers (complete coverage, no gaps)
            var cellInfos = new List<(int cellId, int x0, int y0, int x1, int y1, float cx, float cy)>();

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

                cellInfos.Add((cell.Id, x0, y0, x1, y1, cx, cy));
            }

            var rowLocks = new object[gridHeight];
            for (int i = 0; i < gridHeight; i++)
                rowLocks[i] = new object();

            Parallel.ForEach(cellInfos, info =>
            {
                var (cellId, x0, y0, x1, y1, cx, cy) = info;

                for (int y = y0; y <= y1; y++)
                {
                    lock (rowLocks[y])
                    {
                        for (int x = x0; x <= x1; x++)
                        {
                            int gridIdx = y * gridWidth + x;
                            var (wx, wy) = DomainWarp.Warp(x, y);
                            float dx = wx - cx;
                            float dy = wy - cy;
                            float distSq = dx * dx + dy * dy;

                            if (distSq < distanceSqGrid[gridIdx])
                            {
                                distanceSqGrid[gridIdx] = distSq;
                                spatialGrid[gridIdx] = cellId;
                            }
                        }
                    }
                }
            });
        }

        /// <summary>
        /// Generate the cell data texture from the spatial grid and cell data.
        /// Format: RGBAFloat with RealmId, ProvinceId, BiomeId+WaterFlag, CountyId normalized to 0-1.
        /// B channel encodes: BiomeId in low bits, water flag in high bit (add 32768 if water)
        /// Uses 32-bit float for precise ID storage (half-precision caused banding artifacts).
        /// Parallelized for performance.
        /// </summary>
        private void GenerateDataTextures()
        {
            cellDataTexture = new Texture2D(gridWidth, gridHeight, TextureFormat.RGBAFloat, false);
            cellDataTexture.name = "CellDataTexture";
            cellDataTexture.filterMode = FilterMode.Point;  // No interpolation
            cellDataTexture.wrapMode = TextureWrapMode.Clamp;

            cellDataPixels = new Color[gridWidth * gridHeight];

            // Fill texture from spatial grid (parallelized by row)
            Parallel.For(0, gridHeight, y =>
            {
                for (int x = 0; x < gridWidth; x++)
                {
                    int gridIdx = y * gridWidth + x;
                    int cellId = spatialGrid[gridIdx];

                    Color pixel;

                    if (cellId >= 0 && mapData.CellById.TryGetValue(cellId, out var cell))
                    {
                        // Normalize IDs to 0-1 range (divide by 65535)
                        pixel.r = cell.RealmId / 65535f;
                        pixel.g = cell.ProvinceId / 65535f;
                        // Pack biome ID, soil ID, and water flag:
                        // Land: biomeId * 8 + soilId (max 63*8+7 = 511)
                        // Water: 32768 + biomeId
                        int packedBiome = cell.IsLand
                            ? cell.BiomeId * 8 + cell.SoilId
                            : 32768 + cell.BiomeId;
                        pixel.b = packedBiome / 65535f;
                        // County ID for county-level rendering (from grouped cells)
                        pixel.a = cell.CountyId / 65535f;
                    }
                    else
                    {
                        // No cell data - treat as water (areas outside map bounds)
                        pixel.r = 0;
                        pixel.g = 0;
                        pixel.b = 32768 / 65535f;  // Water flag set, biomeId = 0
                        pixel.a = 0;
                    }

                    cellDataPixels[gridIdx] = pixel;
                }
            });

            cellDataTexture.SetPixels(cellDataPixels);
            cellDataTexture.Apply();

            Debug.Log($"MapOverlayManager: Generated cell data texture {gridWidth}x{gridHeight}");
        }

        /// <summary>
        /// Generate heightmap texture from cell height data.
        /// Used for water depth coloring. 3D height displacement is currently disabled.
        /// Water detection for other modes uses the water flag in the data texture.
        /// Parallelized for performance.
        /// </summary>
        private void GenerateHeightmapTexture()
        {
            // Sample raw heights from spatial grid (Y-up matches texture row order, no flip needed)
            float[] heightData = new float[gridWidth * gridHeight];

            Parallel.For(0, gridHeight, y =>
            {
                int row = y * gridWidth;

                for (int x = 0; x < gridWidth; x++)
                {
                    int idx = row + x;
                    int cellId = spatialGrid[idx];

                    float height = 0f;
                    if (cellId >= 0 && mapData.CellById.TryGetValue(cellId, out var cell))
                    {
                        // Normalize active elevation domain to 0-1.
                        height = cell.Height / elevationDomain.Max;
                    }

                    heightData[idx] = height;
                }
            });

            // Create texture
            heightmapTexture = new Texture2D(gridWidth, gridHeight, TextureFormat.RFloat, false);
            heightmapTexture.name = "HeightmapTexture";
            heightmapTexture.filterMode = FilterMode.Bilinear;
            heightmapTexture.wrapMode = TextureWrapMode.Clamp;
            heightmapTexture.SetPixelData(heightData, 0);
            heightmapTexture.Apply();

            Debug.Log($"MapOverlayManager: Generated heightmap {gridWidth}x{gridHeight}");
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

            var pixels = GenerateRiverMaskPixels();

            var colorPixels = new Color[pixels.Length];
            for (int i = 0; i < pixels.Length; i++)
            {
                float v = pixels[i] / 255f;
                colorPixels[i] = new Color(v, v, v, 1f);
            }
            riverMaskTexture.SetPixels(colorPixels);
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

        /// <summary>
        /// Generate realm border distance texture using a chamfer distance transform.
        /// Stores the Euclidean distance (in texels) from each land pixel to the nearest
        /// realm boundary, capped at 255. Bilinear filtering gives smooth borders at any zoom.
        /// </summary>
        private void GenerateRealmBorderDistTexture()
        {
            realmBorderDistTexture = new Texture2D(gridWidth, gridHeight, TextureFormat.R8, false);
            realmBorderDistTexture.name = "RealmBorderDistTexture";
            realmBorderDistTexture.filterMode = FilterMode.Bilinear;
            realmBorderDistTexture.anisoLevel = 8;
            realmBorderDistTexture.wrapMode = TextureWrapMode.Clamp;

            var pixels = GenerateRealmBorderDistPixels();

            var colorPixels = new Color[pixels.Length];
            for (int i = 0; i < pixels.Length; i++)
            {
                float v = pixels[i] / 255f;
                colorPixels[i] = new Color(v, v, v, 1f);
            }
            realmBorderDistTexture.SetPixels(colorPixels);
            realmBorderDistTexture.Apply();

            TextureDebugger.SaveTexture(realmBorderDistTexture, "realm_border_dist");
            Debug.Log($"MapOverlayManager: Generated realm border distance texture {gridWidth}x{gridHeight}");
        }

        private byte[] GenerateRealmBorderDistPixels()
        {
            int size = gridWidth * gridHeight;

            int[] realmGrid = new int[size];
            bool[] isLand = new bool[size];

            for (int i = 0; i < size; i++)
            {
                int cellId = spatialGrid[i];
                if (cellId >= 0 && mapData.CellById.TryGetValue(cellId, out var cell) && cell.IsLand)
                {
                    realmGrid[i] = cell.RealmId;
                    isLand[i] = true;
                }
                else
                {
                    realmGrid[i] = -1;
                    isLand[i] = false;
                }
            }

            float[] dist = new float[size];
            for (int i = 0; i < size; i++)
                dist[i] = 255f;

            // Seed: land pixels adjacent to a different realm's land pixel
            for (int y = 0; y < gridHeight; y++)
            {
                for (int x = 0; x < gridWidth; x++)
                {
                    int idx = y * gridWidth + x;
                    if (!isLand[idx]) continue;

                    int realm = realmGrid[idx];
                    bool isBoundary = false;

                    for (int dy = -1; dy <= 1 && !isBoundary; dy++)
                    {
                        for (int dx = -1; dx <= 1 && !isBoundary; dx++)
                        {
                            if (dx == 0 && dy == 0) continue;
                            int nx = x + dx, ny = y + dy;
                            if (nx < 0 || nx >= gridWidth || ny < 0 || ny >= gridHeight) continue;
                            int nIdx = ny * gridWidth + nx;
                            if (isLand[nIdx] && realmGrid[nIdx] != realm)
                                isBoundary = true;
                        }
                    }

                    if (isBoundary)
                        dist[idx] = 0f;
                }
            }

            RunChamferTransform(dist, isLand);
            return DistToBytes(dist);
        }

        /// <summary>
        /// Generate province border distance texture using a chamfer distance transform.
        /// Stores the Euclidean distance (in texels) from each land pixel to the nearest
        /// province boundary (same realm, different province), capped at 255.
        /// </summary>
        private void GenerateProvinceBorderDistTexture()
        {
            provinceBorderDistTexture = new Texture2D(gridWidth, gridHeight, TextureFormat.R8, false);
            provinceBorderDistTexture.name = "ProvinceBorderDistTexture";
            provinceBorderDistTexture.filterMode = FilterMode.Bilinear;
            provinceBorderDistTexture.anisoLevel = 8;
            provinceBorderDistTexture.wrapMode = TextureWrapMode.Clamp;

            var pixels = GenerateProvinceBorderDistPixels();

            var colorPixels = new Color[pixels.Length];
            for (int i = 0; i < pixels.Length; i++)
            {
                float v = pixels[i] / 255f;
                colorPixels[i] = new Color(v, v, v, 1f);
            }
            provinceBorderDistTexture.SetPixels(colorPixels);
            provinceBorderDistTexture.Apply();

            TextureDebugger.SaveTexture(provinceBorderDistTexture, "province_border_dist");
            Debug.Log($"MapOverlayManager: Generated province border distance texture {gridWidth}x{gridHeight}");
        }

        private byte[] GenerateProvinceBorderDistPixels()
        {
            int size = gridWidth * gridHeight;

            int[] realmGrid = new int[size];
            int[] provinceGrid = new int[size];
            bool[] isLand = new bool[size];

            for (int i = 0; i < size; i++)
            {
                int cellId = spatialGrid[i];
                if (cellId >= 0 && mapData.CellById.TryGetValue(cellId, out var cell) && cell.IsLand)
                {
                    realmGrid[i] = cell.RealmId;
                    provinceGrid[i] = cell.ProvinceId;
                    isLand[i] = true;
                }
                else
                {
                    realmGrid[i] = -1;
                    provinceGrid[i] = -1;
                    isLand[i] = false;
                }
            }

            float[] dist = new float[size];
            for (int i = 0; i < size; i++)
                dist[i] = 255f;

            // Seed: land pixels adjacent to land pixel with same realm, different province
            for (int y = 0; y < gridHeight; y++)
            {
                for (int x = 0; x < gridWidth; x++)
                {
                    int idx = y * gridWidth + x;
                    if (!isLand[idx]) continue;

                    int realm = realmGrid[idx];
                    int province = provinceGrid[idx];
                    bool isBoundary = false;

                    for (int dy = -1; dy <= 1 && !isBoundary; dy++)
                    {
                        for (int dx = -1; dx <= 1 && !isBoundary; dx++)
                        {
                            if (dx == 0 && dy == 0) continue;
                            int nx = x + dx, ny = y + dy;
                            if (nx < 0 || nx >= gridWidth || ny < 0 || ny >= gridHeight) continue;
                            int nIdx = ny * gridWidth + nx;
                            if (isLand[nIdx] && realmGrid[nIdx] == realm && provinceGrid[nIdx] != province)
                                isBoundary = true;
                        }
                    }

                    if (isBoundary)
                        dist[idx] = 0f;
                }
            }

            RunChamferTransform(dist, isLand);
            return DistToBytes(dist);
        }

        /// <summary>
        /// Generate county border distance texture using a chamfer distance transform.
        /// Stores the Euclidean distance (in texels) from each land pixel to the nearest
        /// county boundary (same province, different county), capped at 255.
        /// </summary>
        private void GenerateCountyBorderDistTexture()
        {
            countyBorderDistTexture = new Texture2D(gridWidth, gridHeight, TextureFormat.R8, false);
            countyBorderDistTexture.name = "CountyBorderDistTexture";
            countyBorderDistTexture.filterMode = FilterMode.Bilinear;
            countyBorderDistTexture.anisoLevel = 8;
            countyBorderDistTexture.wrapMode = TextureWrapMode.Clamp;

            var pixels = GenerateCountyBorderDistPixels();

            var colorPixels = new Color[pixels.Length];
            for (int i = 0; i < pixels.Length; i++)
            {
                float v = pixels[i] / 255f;
                colorPixels[i] = new Color(v, v, v, 1f);
            }
            countyBorderDistTexture.SetPixels(colorPixels);
            countyBorderDistTexture.Apply();

            TextureDebugger.SaveTexture(countyBorderDistTexture, "county_border_dist");
            Debug.Log($"MapOverlayManager: Generated county border distance texture {gridWidth}x{gridHeight}");
        }

        private byte[] GenerateCountyBorderDistPixels()
        {
            int size = gridWidth * gridHeight;

            int[] realmGrid = new int[size];
            int[] provinceGrid = new int[size];
            int[] countyGrid = new int[size];
            bool[] isLand = new bool[size];

            for (int i = 0; i < size; i++)
            {
                int cellId = spatialGrid[i];
                if (cellId >= 0 && mapData.CellById.TryGetValue(cellId, out var cell) && cell.IsLand)
                {
                    realmGrid[i] = cell.RealmId;
                    provinceGrid[i] = cell.ProvinceId;
                    countyGrid[i] = cell.CountyId;
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

            float[] dist = new float[size];
            for (int i = 0; i < size; i++)
                dist[i] = 255f;

            // Seed: land pixels adjacent to land pixel with same province, different county
            for (int y = 0; y < gridHeight; y++)
            {
                for (int x = 0; x < gridWidth; x++)
                {
                    int idx = y * gridWidth + x;
                    if (!isLand[idx]) continue;

                    int realm = realmGrid[idx];
                    int province = provinceGrid[idx];
                    int county = countyGrid[idx];
                    bool isBoundary = false;

                    for (int dy = -1; dy <= 1 && !isBoundary; dy++)
                    {
                        for (int dx = -1; dx <= 1 && !isBoundary; dx++)
                        {
                            if (dx == 0 && dy == 0) continue;
                            int nx = x + dx, ny = y + dy;
                            if (nx < 0 || nx >= gridWidth || ny < 0 || ny >= gridHeight) continue;
                            int nIdx = ny * gridWidth + nx;
                            if (isLand[nIdx] && realmGrid[nIdx] == realm && provinceGrid[nIdx] == province && countyGrid[nIdx] != county)
                                isBoundary = true;
                        }
                    }

                    if (isBoundary)
                        dist[idx] = 0f;
                }
            }

            RunChamferTransform(dist, isLand);
            return DistToBytes(dist);
        }

        /// <summary>
        /// Generate color palette textures for realms, markets, and biomes.
        /// Province/county colors are derived from realm colors in the shader.
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
        /// Generate the biome-elevation matrix texture (64x64).
        /// X axis = biome ID (0-63), Y axis = normalized elevation (0-1).
        /// Applies elevation-based color modifications to biome base colors.
        /// </summary>
        private void GenerateBiomeElevationMatrix()
        {
            const int MatrixSize = 64;

            biomeElevationMatrix = new Texture2D(MatrixSize, MatrixSize, TextureFormat.RGBA32, false);
            biomeElevationMatrix.name = "BiomeElevationMatrix";
            biomeElevationMatrix.filterMode = FilterMode.Bilinear;
            biomeElevationMatrix.wrapMode = TextureWrapMode.Clamp;

            var pixels = new Color[MatrixSize * MatrixSize];

            // For each biome column
            for (int biomeIdx = 0; biomeIdx < MatrixSize; biomeIdx++)
            {
                // Get biome color from map data, fall back to neutral green
                Color baseColor = new Color(0.4f, 0.6f, 0.4f);

                var biome = mapData.Biomes.Find(b => b.Id == biomeIdx);
                if (biome != null)
                {
                    baseColor = new Color(
                        biome.Color.R / 255f,
                        biome.Color.G / 255f,
                        biome.Color.B / 255f
                    );
                }

                Color.RGBToHSV(baseColor, out float h, out float s, out float v);

                for (int elevIdx = 0; elevIdx < MatrixSize; elevIdx++)
                {
                    float elevation = elevIdx / (float)(MatrixSize - 1);
                    Color finalColor = ApplyElevationZone(h, s, v, elevation);
                    pixels[elevIdx * MatrixSize + biomeIdx] = finalColor;
                }
            }

            biomeElevationMatrix.SetPixels(pixels);
            biomeElevationMatrix.Apply();

            TextureDebugger.SaveTexture(biomeElevationMatrix, "biome_elevation_matrix");
            Debug.Log($"MapOverlayManager: Generated biome-elevation matrix {MatrixSize}x{MatrixSize}");
        }

        /// <summary>
        /// Apply elevation-based color modifications to biome color.
        /// Elevation is normalized land height: 0 = sea level, 1 = domain max.
        /// Brightness gradient from 0.4 (coastal) to 1.0 (high), snow blend above 85%.
        /// </summary>
        private Color ApplyElevationZone(float h, float s, float v, float elevation)
        {
            if (elevation < 0.85f)
            {
                // Continuous brightness gradient: darker at low elevation, brighter at high
                float brightness = 0.4f + elevation * 0.7f;
                return Color.HSVToRGB(h, s, v * brightness);
            }
            else
            {
                // Snow zone (Azgaar height 88-100): blend to white
                float t = (elevation - 0.85f) / 0.15f;
                Color baseCol = Color.HSVToRGB(h, s, v);
                Color snow = new Color(0.95f, 0.95f, 0.98f);
                return Color.Lerp(baseCol, snow, t);
            }
        }

        /// <summary>
        /// Apply all textures and initial settings to the terrain material.
        /// </summary>
        private void ApplyTexturesToMaterial()
        {
            if (terrainMaterial == null) return;

            terrainMaterial.SetTexture(CellDataTexId, cellDataTexture);
            terrainMaterial.SetTexture(HeightmapTexId, heightmapTexture);
            terrainMaterial.SetTexture(RiverMaskTexId, riverMaskTexture);
            terrainMaterial.SetTexture(RealmPaletteTexId, realmPaletteTexture);
            terrainMaterial.SetTexture(MarketPaletteTexId, marketPaletteTexture);
            terrainMaterial.SetTexture(BiomePaletteTexId, biomePaletteTexture);
            terrainMaterial.SetTexture(BiomeMatrixTexId, biomeElevationMatrix);
            terrainMaterial.SetTexture(RealmBorderDistTexId, realmBorderDistTexture);
            terrainMaterial.SetTexture(ProvinceBorderDistTexId, provinceBorderDistTexture);
            terrainMaterial.SetTexture(CountyBorderDistTexId, countyBorderDistTexture);

            // Create market border dist texture (initially all-white = no borders, regenerated when economy is set)
            marketBorderDistTexture = new Texture2D(gridWidth, gridHeight, TextureFormat.R8, false);
            marketBorderDistTexture.name = "MarketBorderDistTexture";
            marketBorderDistTexture.filterMode = FilterMode.Bilinear;
            marketBorderDistTexture.anisoLevel = 8;
            marketBorderDistTexture.wrapMode = TextureWrapMode.Clamp;
            var whitePixels = new Color[gridWidth * gridHeight];
            for (int i = 0; i < whitePixels.Length; i++)
                whitePixels[i] = Color.white;
            marketBorderDistTexture.SetPixels(whitePixels);
            marketBorderDistTexture.Apply();
            terrainMaterial.SetTexture(MarketBorderDistTexId, marketBorderDistTexture);

            // Create road mask texture (initially all-black = no roads, regenerated when road state is set)
            roadDistTexture = new Texture2D(gridWidth, gridHeight, TextureFormat.R8, false);
            roadDistTexture.name = "RoadMaskTexture";
            roadDistTexture.filterMode = FilterMode.Bilinear;
            roadDistTexture.anisoLevel = 8;
            roadDistTexture.wrapMode = TextureWrapMode.Clamp;
            // R8 textures initialize to 0 (black = no roads), just apply
            var roadEmptyPixels = new Color[gridWidth * gridHeight];
            roadDistTexture.SetPixels(roadEmptyPixels);
            roadDistTexture.Apply();
            terrainMaterial.SetTexture(RoadMaskTexId, roadDistTexture);

            // Create cell-to-market texture (16384 cells max, updated when economy is set)
            cellToMarketTexture = new Texture2D(16384, 1, TextureFormat.RHalf, false);
            cellToMarketTexture.name = "CellToMarketTexture";
            cellToMarketTexture.filterMode = FilterMode.Point;
            cellToMarketTexture.wrapMode = TextureWrapMode.Clamp;
            // Initialize to 0 (no market)
            var emptyMarkets = new Color[16384];
            cellToMarketTexture.SetPixels(emptyMarkets);
            cellToMarketTexture.Apply();
            terrainMaterial.SetTexture(CellToMarketTexId, cellToMarketTexture);

            // Water layer properties are set via shader defaults + material Inspector
            // (not overwritten here so Inspector tweaks persist)

            // Default to political mode
            terrainMaterial.SetInt(MapModeId, 1);
            terrainMaterial.SetInt(DebugViewId, (int)ChannelDebugView.CellDataR);

            // Clear any persisted selection/hover from previous play session
            ClearSelection();
            ClearHover();
        }

        /// <summary>
        /// Set the economy state to enable market-related overlays.
        /// Updates the county-to-market texture (indexed by countyId) and regenerates market palette.
        /// </summary>
        public void SetEconomyState(EconomyState economy)
        {
            economyState = economy;

            if (economy == null || economy.CountyToMarket == null)
                return;

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

            // Regenerate market border distance texture now that zone assignments are known
            RegenerateMarketBorderDistTexture(economy);

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
            var colorPixels = new Color[size];
            for (int i = 0; i < size; i++)
            {
                float v = pixels[i] / 255f;
                colorPixels[i] = new Color(v, v, v, 1f);
            }
            marketBorderDistTexture.SetPixels(colorPixels);
            marketBorderDistTexture.Apply();

            TextureDebugger.SaveTexture(marketBorderDistTexture, "market_border_dist");
            Debug.Log($"MapOverlayManager: Generated market border distance texture {gridWidth}x{gridHeight}");
        }

        /// <summary>
        /// Set road state for shader-based road rendering.
        /// Stores reference and regenerates the road distance texture.
        /// </summary>
        public void SetRoadState(RoadState roads)
        {
            roadState = roads;
            RegenerateRoadDistTexture();
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
            if (terrainMaterial == null) return;
            float clampedDash = Mathf.Max(0.1f, dashLength);
            float clampedGap = Mathf.Max(0.1f, gapLength);
            float clampedWidth = Mathf.Max(0.2f, width);

            terrainMaterial.SetFloat(PathDashLengthId, clampedDash);
            terrainMaterial.SetFloat(PathGapLengthId, clampedGap);
            terrainMaterial.SetFloat(PathWidthId, clampedWidth);

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
            }
        }

        /// <summary>
        /// Pull path style from material and regenerate mask if values changed.
        /// Supports live tuning through the material inspector.
        /// </summary>
        public void RefreshPathStyleFromMaterial()
        {
            if (terrainMaterial == null)
                return;

            float dash = GetMaterialFloatOr(PathDashLengthId, 1.8f);
            float gap = GetMaterialFloatOr(PathGapLengthId, 2.4f);
            float width = GetMaterialFloatOr(PathWidthId, 0.8f);
            SetPathStyle(dash, gap, width);
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
            if (terrainMaterial == null)
                return fallback;

            float value = terrainMaterial.GetFloat(propertyId);
            return value > 0f ? value : fallback;
        }

        /// <summary>
        /// Set the current map mode for the shader.
        /// Mode: 1=political, 2=province, 3=county, 4=market, 5=terrain/biome, 6=soil, 7=channel-inspector
        /// </summary>
        public void SetMapMode(MapView.MapMode mode)
        {
            if (terrainMaterial == null) return;

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
                case MapView.MapMode.Soil:
                    shaderMode = 6;
                    break;
                case MapView.MapMode.ChannelInspector:
                    shaderMode = 7;
                    break;
                case MapView.MapMode.Terrain:
                default:
                    shaderMode = 5;  // Biome texture with elevation tinting
                    break;
            }

            terrainMaterial.SetInt(MapModeId, shaderMode);
        }

        public void SetChannelDebugView(ChannelDebugView debugView)
        {
            if (terrainMaterial == null) return;
            terrainMaterial.SetInt(DebugViewId, (int)debugView);
        }

        /// <summary>
        /// Enable or disable height displacement in the shader.
        /// When enabled, the shader displaces vertices based on heightmap values.
        /// </summary>
        public void SetHeightDisplacementEnabled(bool enabled)
        {
            if (terrainMaterial == null) return;
            terrainMaterial.SetInt(UseHeightDisplacementId, enabled ? 1 : 0);
        }

        /// <summary>
        /// Set the height scale for terrain displacement.
        /// </summary>
        public void SetHeightScale(float scale)
        {
            if (terrainMaterial == null) return;
            terrainMaterial.SetFloat(HeightScaleId, scale);
        }

        /// <summary>
        /// Set the sea level threshold for water detection.
        /// </summary>
        public void SetSeaLevel(float level)
        {
            if (terrainMaterial == null) return;
            terrainMaterial.SetFloat(SeaLevelId, level);
        }

        /// <summary>
        /// Clear all selection highlighting.
        /// </summary>
        public void ClearSelection()
        {
            if (terrainMaterial == null) return;
            terrainMaterial.SetFloat(SelectedRealmIdId, -1f);
            terrainMaterial.SetFloat(SelectedProvinceIdId, -1f);
            terrainMaterial.SetFloat(SelectedCountyIdId, -1f);
            terrainMaterial.SetFloat(SelectedMarketIdId, -1f);
        }

        /// <summary>
        /// Set the currently selected realm for shader-based highlighting.
        /// Clears other selections.
        /// </summary>
        public void SetSelectedRealm(int realmId)
        {
            if (terrainMaterial == null) return;
            float normalizedId = realmId < 0 ? -1f : realmId / 65535f;
            terrainMaterial.SetFloat(SelectedRealmIdId, normalizedId);
            terrainMaterial.SetFloat(SelectedProvinceIdId, -1f);
            terrainMaterial.SetFloat(SelectedCountyIdId, -1f);
            terrainMaterial.SetFloat(SelectedMarketIdId, -1f);
        }

        /// <summary>
        /// Set the currently selected province for shader-based highlighting.
        /// Clears other selections.
        /// </summary>
        public void SetSelectedProvince(int provinceId)
        {
            if (terrainMaterial == null) return;
            terrainMaterial.SetFloat(SelectedRealmIdId, -1f);
            float normalizedId = provinceId < 0 ? -1f : provinceId / 65535f;
            terrainMaterial.SetFloat(SelectedProvinceIdId, normalizedId);
            terrainMaterial.SetFloat(SelectedCountyIdId, -1f);
            terrainMaterial.SetFloat(SelectedMarketIdId, -1f);
        }

        /// <summary>
        /// Set the currently selected county for shader-based highlighting.
        /// Clears other selections.
        /// Pass -1 to clear selection.
        /// </summary>
        public void SetSelectedCounty(int countyId)
        {
            if (terrainMaterial == null) return;
            terrainMaterial.SetFloat(SelectedRealmIdId, -1f);
            terrainMaterial.SetFloat(SelectedProvinceIdId, -1f);
            float normalizedId = countyId < 0 ? -1f : countyId / 65535f;
            terrainMaterial.SetFloat(SelectedCountyIdId, normalizedId);
            terrainMaterial.SetFloat(SelectedMarketIdId, -1f);
        }

        /// <summary>
        /// Set the currently selected market for shader-based highlighting.
        /// Clears other selections.
        /// </summary>
        public void SetSelectedMarket(int marketId)
        {
            if (terrainMaterial == null) return;
            terrainMaterial.SetFloat(SelectedRealmIdId, -1f);
            terrainMaterial.SetFloat(SelectedProvinceIdId, -1f);
            terrainMaterial.SetFloat(SelectedCountyIdId, -1f);
            float normalizedId = marketId < 0 ? -1f : marketId / 65535f;
            terrainMaterial.SetFloat(SelectedMarketIdId, normalizedId);
        }

        /// <summary>
        /// Clear all hover highlighting.
        /// </summary>
        public void ClearHover()
        {
            if (terrainMaterial == null) return;
            terrainMaterial.SetFloat(HoveredRealmIdId, -1f);
            terrainMaterial.SetFloat(HoveredProvinceIdId, -1f);
            terrainMaterial.SetFloat(HoveredCountyIdId, -1f);
            terrainMaterial.SetFloat(HoveredMarketIdId, -1f);
        }

        /// <summary>
        /// Set the currently hovered realm for shader-based highlighting.
        /// Clears other hovers.
        /// </summary>
        public void SetHoveredRealm(int realmId)
        {
            if (terrainMaterial == null) return;
            float normalizedId = realmId < 0 ? -1f : realmId / 65535f;
            terrainMaterial.SetFloat(HoveredRealmIdId, normalizedId);
            terrainMaterial.SetFloat(HoveredProvinceIdId, -1f);
            terrainMaterial.SetFloat(HoveredCountyIdId, -1f);
            terrainMaterial.SetFloat(HoveredMarketIdId, -1f);
        }

        /// <summary>
        /// Set the currently hovered province for shader-based highlighting.
        /// Clears other hovers.
        /// </summary>
        public void SetHoveredProvince(int provinceId)
        {
            if (terrainMaterial == null) return;
            terrainMaterial.SetFloat(HoveredRealmIdId, -1f);
            float normalizedId = provinceId < 0 ? -1f : provinceId / 65535f;
            terrainMaterial.SetFloat(HoveredProvinceIdId, normalizedId);
            terrainMaterial.SetFloat(HoveredCountyIdId, -1f);
            terrainMaterial.SetFloat(HoveredMarketIdId, -1f);
        }

        /// <summary>
        /// Set the currently hovered county for shader-based highlighting.
        /// Clears other hovers.
        /// </summary>
        public void SetHoveredCounty(int countyId)
        {
            if (terrainMaterial == null) return;
            terrainMaterial.SetFloat(HoveredRealmIdId, -1f);
            terrainMaterial.SetFloat(HoveredProvinceIdId, -1f);
            float normalizedId = countyId < 0 ? -1f : countyId / 65535f;
            terrainMaterial.SetFloat(HoveredCountyIdId, normalizedId);
            terrainMaterial.SetFloat(HoveredMarketIdId, -1f);
        }

        /// <summary>
        /// Set the currently hovered market for shader-based highlighting.
        /// Clears other hovers.
        /// </summary>
        public void SetHoveredMarket(int marketId)
        {
            if (terrainMaterial == null) return;
            terrainMaterial.SetFloat(HoveredRealmIdId, -1f);
            terrainMaterial.SetFloat(HoveredProvinceIdId, -1f);
            terrainMaterial.SetFloat(HoveredCountyIdId, -1f);
            float normalizedId = marketId < 0 ? -1f : marketId / 65535f;
            terrainMaterial.SetFloat(HoveredMarketIdId, normalizedId);
        }

        /// <summary>
        /// Set the selection dimming factor (0 = black, 1 = no dimming).
        /// </summary>
        public void SetSelectionDimming(float dimming)
        {
            if (terrainMaterial == null) return;
            terrainMaterial.SetFloat(SelectionDimmingId, Mathf.Clamp01(dimming));
        }

        /// <summary>
        /// Set the selection desaturation factor (0 = full color, 1 = grayscale).
        /// </summary>
        public void SetSelectionDesaturation(float desaturation)
        {
            if (terrainMaterial == null) return;
            terrainMaterial.SetFloat(SelectionDesaturationId, Mathf.Clamp01(desaturation));
        }

        /// <summary>
        /// Set the hover intensity (0 = no effect, 1 = full effect).
        /// </summary>
        public void SetHoverIntensity(float intensity)
        {
            if (terrainMaterial == null) return;
            terrainMaterial.SetFloat(HoverIntensityId, Mathf.Clamp01(intensity));
        }

        /// <summary>
        /// Update cell data for a specific cell. Useful for dynamic changes (conquests, etc.).
        /// </summary>
        public void UpdateCellData(int cellId, int? newRealmId = null, int? newProvinceId = null, int? newCountyId = null)
        {
            if (!mapData.CellById.TryGetValue(cellId, out var cell))
                return;

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
                        Color pixel = cellDataPixels[gridIdx];

                        if (newRealmId.HasValue)
                            pixel.r = newRealmId.Value / 65535f;
                        if (newProvinceId.HasValue)
                            pixel.g = newProvinceId.Value / 65535f;
                        if (newCountyId.HasValue)
                            pixel.a = newCountyId.Value / 65535f;

                        cellDataPixels[gridIdx] = pixel;
                        needsUpdate = true;
                    }
                }
            }

            if (needsUpdate)
            {
                cellDataTexture.SetPixels(cellDataPixels);
                cellDataTexture.Apply();
            }
        }

        /// <summary>
        /// Clean up textures when destroyed.
        /// </summary>
        public void Dispose()
        {
            if (cellDataTexture != null)
                Object.Destroy(cellDataTexture);
            if (heightmapTexture != null)
                Object.Destroy(heightmapTexture);
            if (riverMaskTexture != null)
                Object.Destroy(riverMaskTexture);
            if (realmPaletteTexture != null)
                Object.Destroy(realmPaletteTexture);
            if (marketPaletteTexture != null)
                Object.Destroy(marketPaletteTexture);
            if (biomePaletteTexture != null)
                Object.Destroy(biomePaletteTexture);
            if (biomeElevationMatrix != null)
                Object.Destroy(biomeElevationMatrix);
            if (cellToMarketTexture != null)
                Object.Destroy(cellToMarketTexture);
            if (realmBorderDistTexture != null)
                Object.Destroy(realmBorderDistTexture);
            if (provinceBorderDistTexture != null)
                Object.Destroy(provinceBorderDistTexture);
            if (countyBorderDistTexture != null)
                Object.Destroy(countyBorderDistTexture);
            if (marketBorderDistTexture != null)
                Object.Destroy(marketBorderDistTexture);
            if (roadDistTexture != null)
                Object.Destroy(roadDistTexture);
        }
    }
}
