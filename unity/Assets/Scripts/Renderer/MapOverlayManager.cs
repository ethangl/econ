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
        // Shader property IDs (cached for performance)
        private static readonly int CellDataTexId = Shader.PropertyToID("_CellDataTex");
        private static readonly int HeightmapTexId = Shader.PropertyToID("_HeightmapTex");
        private static readonly int RiverMaskTexId = Shader.PropertyToID("_RiverMaskTex");
        private static readonly int StatePaletteTexId = Shader.PropertyToID("_StatePaletteTex");
        private static readonly int MarketPaletteTexId = Shader.PropertyToID("_MarketPaletteTex");
        private static readonly int BiomePaletteTexId = Shader.PropertyToID("_BiomePaletteTex");
        private static readonly int BiomeMatrixTexId = Shader.PropertyToID("_BiomeMatrixTex");
        private static readonly int CellToMarketTexId = Shader.PropertyToID("_CellToMarketTex");
        private static readonly int MapModeId = Shader.PropertyToID("_MapMode");
        private static readonly int UseHeightDisplacementId = Shader.PropertyToID("_UseHeightDisplacement");
        private static readonly int HeightScaleId = Shader.PropertyToID("_HeightScale");
        private static readonly int SeaLevelId = Shader.PropertyToID("_SeaLevel");
        private static readonly int SelectedStateIdId = Shader.PropertyToID("_SelectedStateId");
        private static readonly int SelectedProvinceIdId = Shader.PropertyToID("_SelectedProvinceId");
        private static readonly int SelectedCountyIdId = Shader.PropertyToID("_SelectedCountyId");
        private static readonly int SelectedMarketIdId = Shader.PropertyToID("_SelectedMarketId");
        private static readonly int SelectionBorderColorId = Shader.PropertyToID("_SelectionBorderColor");
        private static readonly int SelectionBorderWidthId = Shader.PropertyToID("_SelectionBorderWidth");
        private static readonly int HoveredStateIdId = Shader.PropertyToID("_HoveredStateId");
        private static readonly int HoveredProvinceIdId = Shader.PropertyToID("_HoveredProvinceId");
        private static readonly int HoveredCountyIdId = Shader.PropertyToID("_HoveredCountyId");
        private static readonly int HoveredMarketIdId = Shader.PropertyToID("_HoveredMarketId");
        private static readonly int HoverIntensityId = Shader.PropertyToID("_HoverIntensity");
        private static readonly int SelectionDimmingId = Shader.PropertyToID("_SelectionDimming");
        private static readonly int SelectionDesaturationId = Shader.PropertyToID("_SelectionDesaturation");

        private MapData mapData;
        private EconomyState economyState;
        private Material terrainMaterial;

        // Resolution multiplier for higher quality borders
        private int resolutionMultiplier;
        private int baseWidth;
        private int baseHeight;

        // Data textures
        private Texture2D cellDataTexture;      // RGBAFloat: StateId, ProvinceId, BiomeId+WaterFlag, CountyId

        /// <summary>
        /// Public accessor for the cell data texture (for border masking).
        /// </summary>
        public Texture2D CellDataTexture => cellDataTexture;
        private Texture2D cellToMarketTexture;  // R16: CellId -> MarketId mapping (dynamic)
        private Texture2D heightmapTexture;     // RFloat: smoothed height values
        private Texture2D riverMaskTexture;     // R8: river mask (1 = river, 0 = not river)
        private Texture2D statePaletteTexture;  // 256x1: state colors
        private Texture2D marketPaletteTexture; // 256x1: market colors
        private Texture2D biomePaletteTexture;  // 256x1: biome colors
        private Texture2D biomeElevationMatrix; // 64x64: biome × elevation colors

        // Spatial lookup grid: maps Azgaar pixel coordinates to cell IDs
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

            Profiler.Begin("GeneratePaletteTextures");
            GeneratePaletteTextures();
            Profiler.End();

            Profiler.Begin("GenerateBiomeElevationMatrix");
            GenerateBiomeElevationMatrix();
            Profiler.End();

            Profiler.Begin("ApplyTexturesToMaterial");
            ApplyTexturesToMaterial();
            Profiler.End();

            // Debug output
            TextureDebugger.SaveTexture(heightmapTexture, "heightmap");
            TextureDebugger.SaveTexture(cellDataTexture, "cell_data");
        }

        /// <summary>
        /// Build spatial lookup grid mapping Azgaar coordinates to cell IDs.
        /// Uses cell centers to determine ownership of each grid position.
        /// Applies domain warping for organic, meandering borders.
        /// Results are cached to disk for fast subsequent loads.
        /// </summary>
        private void BuildSpatialGrid()
        {
            // Try to load from cache first
            string cacheKey = ComputeSpatialGridCacheKey();
            string cachePath = GetSpatialGridCachePath(cacheKey);

            if (TryLoadSpatialGridFromCache(cachePath))
            {
                Debug.Log($"MapOverlayManager: Loaded spatial grid from cache ({gridWidth}x{gridHeight})");
                return;
            }

            // Build the grid from scratch
            BuildSpatialGridFromScratch();

            // Save to cache for next time
            SaveSpatialGridToCache(cachePath);
            Debug.Log($"MapOverlayManager: Built and cached spatial grid {gridWidth}x{gridHeight} ({resolutionMultiplier}x resolution)");
        }

        /// <summary>
        /// Compute a cache key based on parameters that affect the spatial grid.
        /// </summary>
        private string ComputeSpatialGridCacheKey()
        {
            // Include all parameters that affect the grid output
            string input = $"{mapData.Info.Name}_{mapData.Info.Width}x{mapData.Info.Height}_{mapData.Cells.Count}cells_{resolutionMultiplier}x_v2";

            // Simple hash
            int hash = input.GetHashCode();
            return $"spatial_grid_{hash:X8}";
        }

        /// <summary>
        /// Get the cache file path for a given cache key.
        /// </summary>
        private string GetSpatialGridCachePath(string cacheKey)
        {
            // Use Unity's persistent data path for cache
            string cacheDir = Path.Combine(Application.persistentDataPath, "MapCache");
            if (!Directory.Exists(cacheDir))
            {
                Directory.CreateDirectory(cacheDir);
            }
            return Path.Combine(cacheDir, $"{cacheKey}.bin");
        }

        /// <summary>
        /// Try to load the spatial grid from a cache file.
        /// </summary>
        private bool TryLoadSpatialGridFromCache(string cachePath)
        {
            if (!File.Exists(cachePath))
                return false;

            try
            {
                using (var stream = File.OpenRead(cachePath))
                using (var reader = new BinaryReader(stream))
                {
                    // Read and verify header
                    int cachedWidth = reader.ReadInt32();
                    int cachedHeight = reader.ReadInt32();

                    if (cachedWidth != gridWidth || cachedHeight != gridHeight)
                    {
                        Debug.LogWarning($"MapOverlayManager: Cache size mismatch ({cachedWidth}x{cachedHeight} vs {gridWidth}x{gridHeight}), rebuilding");
                        return false;
                    }

                    // Read grid data
                    int length = gridWidth * gridHeight;
                    spatialGrid = new int[length];

                    for (int i = 0; i < length; i++)
                    {
                        spatialGrid[i] = reader.ReadInt32();
                    }

                    return true;
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"MapOverlayManager: Failed to load cache: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Save the spatial grid to a cache file.
        /// </summary>
        private void SaveSpatialGridToCache(string cachePath)
        {
            try
            {
                using (var stream = File.Create(cachePath))
                using (var writer = new BinaryWriter(stream))
                {
                    // Write header
                    writer.Write(gridWidth);
                    writer.Write(gridHeight);

                    // Write grid data
                    for (int i = 0; i < spatialGrid.Length; i++)
                    {
                        writer.Write(spatialGrid[i]);
                    }
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"MapOverlayManager: Failed to save cache: {ex.Message}");
            }
        }

        /// <summary>
        /// Build the spatial grid from scratch (expensive operation).
        /// Uses parallel processing for performance.
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

            // Pre-compute cell bounding boxes and centers
            var cellInfos = new List<(Cell cell, int x0, int y0, int x1, int y1, float cx, float cy)>();

            foreach (var cell in mapData.Cells)
            {
                if (cell.VertexIndices == null || cell.VertexIndices.Count < 3)
                    continue;

                // Find bounding box of cell vertices (in scaled coordinates)
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

                // Clamp bounding box to grid
                int x0 = Mathf.Max(0, Mathf.FloorToInt(minX));
                int x1 = Mathf.Min(gridWidth - 1, Mathf.CeilToInt(maxX));
                int y0 = Mathf.Max(0, Mathf.FloorToInt(minY));
                int y1 = Mathf.Min(gridHeight - 1, Mathf.CeilToInt(maxY));

                float cx = cell.Center.X * scale;
                float cy = cell.Center.Y * scale;

                cellInfos.Add((cell, x0, y0, x1, y1, cx, cy));
            }

            // Process cells in parallel, using distance comparison for conflict resolution
            // Use striped locks (one per row) to reduce contention
            var rowLocks = new object[gridHeight];
            for (int i = 0; i < gridHeight; i++)
                rowLocks[i] = new object();

            Parallel.ForEach(cellInfos, info =>
            {
                var (cell, x0, y0, x1, y1, cx, cy) = info;

                for (int y = y0; y <= y1; y++)
                {
                    // Lock only this row
                    lock (rowLocks[y])
                    {
                        for (int x = x0; x <= x1; x++)
                        {
                            int gridIdx = y * gridWidth + x;

                            // Check if this grid position is closer to this cell
                            float dx = x - cx;
                            float dy = y - cy;
                            float distSq = dx * dx + dy * dy;

                            if (distSq < distanceSqGrid[gridIdx])
                            {
                                distanceSqGrid[gridIdx] = distSq;
                                spatialGrid[gridIdx] = cell.Id;
                            }
                        }
                    }
                }
            });
        }

        /// <summary>
        /// Generate the cell data texture from the spatial grid and cell data.
        /// Format: RGBAFloat with StateId, ProvinceId, BiomeId+WaterFlag, CountyId normalized to 0-1.
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
                        pixel.r = cell.StateId / 65535f;
                        pixel.g = cell.ProvinceId / 65535f;
                        // Pack biome ID and water flag: biomeId + (isWater ? 32768 : 0)
                        int packedBiome = cell.BiomeId + (cell.IsLand ? 0 : 32768);
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
            // 1. Sample raw heights from spatial grid and flip Y in one pass (parallelized)
            float[] flipped = new float[gridWidth * gridHeight];

            Parallel.For(0, gridHeight, y =>
            {
                int srcRow = (gridHeight - 1 - y) * gridWidth;
                int dstRow = y * gridWidth;

                for (int x = 0; x < gridWidth; x++)
                {
                    int srcIdx = srcRow + x;
                    int cellId = spatialGrid[srcIdx];

                    float height = 0f;
                    if (cellId >= 0 && mapData.CellById.TryGetValue(cellId, out var cell))
                    {
                        // Normalize 0-100 to 0-1
                        height = cell.Height / 100f;
                    }

                    flipped[dstRow + x] = height;
                }
            });

            // 2. Create texture
            heightmapTexture = new Texture2D(gridWidth, gridHeight, TextureFormat.RFloat, false);
            heightmapTexture.name = "HeightmapTexture";
            heightmapTexture.filterMode = FilterMode.Bilinear;
            heightmapTexture.wrapMode = TextureWrapMode.Clamp;
            heightmapTexture.SetPixelData(flipped, 0);
            heightmapTexture.Apply();

            Debug.Log($"MapOverlayManager: Generated heightmap {gridWidth}x{gridHeight}");
        }

        /// <summary>
        /// Generate river mask texture by rasterizing river paths.
        /// Rivers are "knocked out" of the land in the shader, showing water underneath.
        /// Results are cached to disk.
        /// </summary>
        private void GenerateRiverMaskTexture()
        {
            riverMaskTexture = new Texture2D(gridWidth, gridHeight, TextureFormat.R8, false);
            riverMaskTexture.name = "RiverMaskTexture";
            riverMaskTexture.filterMode = FilterMode.Bilinear;
            riverMaskTexture.wrapMode = TextureWrapMode.Clamp;

            // Try to load from cache
            string cacheKey = ComputeRiverMaskCacheKey();
            string cachePath = GetRiverMaskCachePath(cacheKey);

            if (TryLoadRiverMaskFromCache(cachePath))
            {
                Debug.Log($"MapOverlayManager: Loaded river mask from cache ({gridWidth}x{gridHeight})");
                return;
            }

            // Generate from scratch
            var pixels = GenerateRiverMaskPixels();

            // Upload to texture
            var colorPixels = new Color[pixels.Length];
            for (int i = 0; i < pixels.Length; i++)
            {
                float v = pixels[i] / 255f;
                colorPixels[i] = new Color(v, v, v, 1f);
            }
            riverMaskTexture.SetPixels(colorPixels);
            riverMaskTexture.Apply();

            // Save to cache
            SaveRiverMaskToCache(cachePath, pixels);

            TextureDebugger.SaveTexture(riverMaskTexture, "river_mask");
            Debug.Log($"MapOverlayManager: Generated and cached river mask {gridWidth}x{gridHeight} ({mapData.Rivers.Count} rivers)");
        }

        private string ComputeRiverMaskCacheKey()
        {
            // Include parameters that affect river mask output
            int riverHash = 0;
            foreach (var river in mapData.Rivers)
            {
                riverHash ^= river.Id * 31 + river.CellPath?.Count ?? 0;
            }
            string input = $"{mapData.Info.Name}_{gridWidth}x{gridHeight}_{mapData.Rivers.Count}rivers_{riverHash}";
            return $"river_mask_{input.GetHashCode():X8}";
        }

        private string GetRiverMaskCachePath(string cacheKey)
        {
            string cacheDir = Path.Combine(Application.persistentDataPath, "MapCache");
            if (!Directory.Exists(cacheDir))
                Directory.CreateDirectory(cacheDir);
            return Path.Combine(cacheDir, $"{cacheKey}.bin");
        }

        private bool TryLoadRiverMaskFromCache(string cachePath)
        {
            if (!File.Exists(cachePath))
                return false;

            try
            {
                var pixels = File.ReadAllBytes(cachePath);
                if (pixels.Length != gridWidth * gridHeight)
                    return false;

                var colorPixels = new Color[pixels.Length];
                for (int i = 0; i < pixels.Length; i++)
                {
                    float v = pixels[i] / 255f;
                    colorPixels[i] = new Color(v, v, v, 1f);
                }
                riverMaskTexture.SetPixels(colorPixels);
                riverMaskTexture.Apply();
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void SaveRiverMaskToCache(string cachePath, byte[] pixels)
        {
            try
            {
                File.WriteAllBytes(cachePath, pixels);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"Failed to save river mask cache: {ex.Message}");
            }
        }

        private byte[] GenerateRiverMaskPixels()
        {
            var pixels = new byte[gridWidth * gridHeight];

            float scale = resolutionMultiplier;

            // River width settings
            float baseWidth = 2.5f * resolutionMultiplier;  // Minimum river width in pixels
            float widthScale = 1.2f * resolutionMultiplier; // Scale factor for discharge-based width

            foreach (var river in mapData.Rivers)
            {
                if (river.CellPath == null || river.CellPath.Count < 2)
                    continue;

                // Get river path points (cell centers)
                var pathPoints = new List<Vector2>();
                foreach (int cellId in river.CellPath)
                {
                    if (mapData.CellById.TryGetValue(cellId, out var cell))
                    {
                        // Scale to texture coordinates and flip Y for Unity
                        float x = cell.Center.X * scale;
                        float y = (baseHeight - cell.Center.Y) * scale;  // Y-flip
                        pathPoints.Add(new Vector2(x, y));
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

                // Draw circular caps at river endpoints to fill gaps in gradient
                float sourceCapRadius = maxWidth * 0.4f;
                float mouthCapRadius = maxWidth * 0.5f;
                DrawFilledCircle(pixels, smoothedPoints[0], sourceCapRadius);
                DrawFilledCircle(pixels, smoothedPoints[smoothedPoints.Count - 1], mouthCapRadius);
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
        /// Draw a filled anti-aliased circle into the pixel buffer.
        /// </summary>
        private void DrawFilledCircle(byte[] pixels, Vector2 center, float radius)
        {
            int x0 = Mathf.Max(0, Mathf.FloorToInt(center.x - radius - 1));
            int x1 = Mathf.Min(gridWidth - 1, Mathf.CeilToInt(center.x + radius + 1));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(center.y - radius - 1));
            int y1 = Mathf.Min(gridHeight - 1, Mathf.CeilToInt(center.y + radius + 1));

            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    float dx = x + 0.5f - center.x;
                    float dy = y + 0.5f - center.y;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);

                    // Anti-aliased edge
                    float coverage = 1f - Mathf.Clamp01((dist - radius + 0.5f) / 1f);

                    if (coverage > 0)
                    {
                        int idx = y * gridWidth + x;
                        int newVal = Mathf.RoundToInt(coverage * 255);
                        if (newVal > pixels[idx])
                            pixels[idx] = (byte)newVal;
                    }
                }
            }
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
        /// Generate color palette textures for states, markets, and biomes.
        /// Province/county colors are derived from state colors in the shader.
        /// </summary>
        private void GeneratePaletteTextures()
        {
            // Generate state colors using HSV distribution
            politicalPalette = new PoliticalPalette(mapData);

            // State palette
            statePaletteTexture = new Texture2D(256, 1, TextureFormat.RGBA32, false);
            statePaletteTexture.name = "StatePalette";
            statePaletteTexture.filterMode = FilterMode.Point;
            statePaletteTexture.wrapMode = TextureWrapMode.Clamp;

            var stateColors = new Color[256];
            stateColors[0] = new Color(0.5f, 0.5f, 0.5f);  // Neutral/no state

            foreach (var state in mapData.States)
            {
                if (state.Id > 0 && state.Id < 256)
                {
                    var c = politicalPalette.GetStateColor(state.Id);
                    stateColors[state.Id] = new Color(c.R / 255f, c.G / 255f, c.B / 255f);
                }
            }
            statePaletteTexture.SetPixels(stateColors);
            statePaletteTexture.Apply();

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

            Debug.Log($"MapOverlayManager: Generated palette textures ({mapData.States.Count} states, {mapData.Biomes.Count} biomes)");
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
        /// Elevation uses Azgaar's absolute scale: 0 = sea level (height 20), 1 = max (height 100).
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
            terrainMaterial.SetTexture(StatePaletteTexId, statePaletteTexture);
            terrainMaterial.SetTexture(MarketPaletteTexId, marketPaletteTexture);
            terrainMaterial.SetTexture(BiomePaletteTexId, biomePaletteTexture);
            terrainMaterial.SetTexture(BiomeMatrixTexId, biomeElevationMatrix);

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

            // Default to political mode
            terrainMaterial.SetInt(MapModeId, 1);

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

            // Regenerate market palette based on hub state colors
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

            Debug.Log($"MapOverlayManager: Updated county-to-market texture ({economy.CountyToMarket.Count} counties mapped)");
        }


        /// <summary>
        /// Regenerate market palette based on hub state colors.
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

                // Get the hub cell's state color
                if (mapData.CellById.TryGetValue(market.LocationCellId, out var hubCell))
                {
                    var c = politicalPalette.GetStateColor(hubCell.StateId);
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
        /// Set the current map mode for the shader.
        /// Mode: 1=political, 2=province, 3=county, 4=market, 5=terrain/biome
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
                case MapView.MapMode.Terrain:
                default:
                    shaderMode = 5;  // Biome texture with elevation tinting
                    break;
            }

            terrainMaterial.SetInt(MapModeId, shaderMode);
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
            terrainMaterial.SetFloat(SelectedStateIdId, -1f);
            terrainMaterial.SetFloat(SelectedProvinceIdId, -1f);
            terrainMaterial.SetFloat(SelectedCountyIdId, -1f);
            terrainMaterial.SetFloat(SelectedMarketIdId, -1f);
        }

        /// <summary>
        /// Set the currently selected state for shader-based highlighting.
        /// Clears other selections.
        /// </summary>
        public void SetSelectedState(int stateId)
        {
            if (terrainMaterial == null) return;
            float normalizedId = stateId < 0 ? -1f : stateId / 65535f;
            terrainMaterial.SetFloat(SelectedStateIdId, normalizedId);
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
            terrainMaterial.SetFloat(SelectedStateIdId, -1f);
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
            terrainMaterial.SetFloat(SelectedStateIdId, -1f);
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
            terrainMaterial.SetFloat(SelectedStateIdId, -1f);
            terrainMaterial.SetFloat(SelectedProvinceIdId, -1f);
            terrainMaterial.SetFloat(SelectedCountyIdId, -1f);
            float normalizedId = marketId < 0 ? -1f : marketId / 65535f;
            terrainMaterial.SetFloat(SelectedMarketIdId, normalizedId);
        }

        /// <summary>
        /// Set the selection border color.
        /// </summary>
        public void SetSelectionBorderColor(Color color)
        {
            if (terrainMaterial == null) return;
            terrainMaterial.SetColor(SelectionBorderColorId, color);
        }

        /// <summary>
        /// Set the selection border width in screen pixels.
        /// </summary>
        public void SetSelectionBorderWidth(float pixels)
        {
            if (terrainMaterial == null) return;
            terrainMaterial.SetFloat(SelectionBorderWidthId, Mathf.Clamp(pixels, 1f, 6f));
        }

        /// <summary>
        /// Clear all hover highlighting.
        /// </summary>
        public void ClearHover()
        {
            if (terrainMaterial == null) return;
            terrainMaterial.SetFloat(HoveredStateIdId, -1f);
            terrainMaterial.SetFloat(HoveredProvinceIdId, -1f);
            terrainMaterial.SetFloat(HoveredCountyIdId, -1f);
            terrainMaterial.SetFloat(HoveredMarketIdId, -1f);
        }

        /// <summary>
        /// Set the currently hovered state for shader-based highlighting.
        /// Clears other hovers.
        /// </summary>
        public void SetHoveredState(int stateId)
        {
            if (terrainMaterial == null) return;
            float normalizedId = stateId < 0 ? -1f : stateId / 65535f;
            terrainMaterial.SetFloat(HoveredStateIdId, normalizedId);
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
            terrainMaterial.SetFloat(HoveredStateIdId, -1f);
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
            terrainMaterial.SetFloat(HoveredStateIdId, -1f);
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
            terrainMaterial.SetFloat(HoveredStateIdId, -1f);
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
        public void UpdateCellData(int cellId, int? newStateId = null, int? newProvinceId = null, int? newCountyId = null)
        {
            if (!mapData.CellById.TryGetValue(cellId, out var cell))
                return;

            bool needsUpdate = false;
            float scale = resolutionMultiplier;

            // Update spatial grid positions belonging to this cell
            int cx = Mathf.RoundToInt(cell.Center.X * scale);
            int cy = Mathf.RoundToInt(cell.Center.Y * scale);
            int radius = 10 * resolutionMultiplier;

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

                        if (newStateId.HasValue)
                            pixel.r = newStateId.Value / 65535f;
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
            if (statePaletteTexture != null)
                Object.Destroy(statePaletteTexture);
            if (marketPaletteTexture != null)
                Object.Destroy(marketPaletteTexture);
            if (biomePaletteTexture != null)
                Object.Destroy(biomePaletteTexture);
            if (biomeElevationMatrix != null)
                Object.Destroy(biomeElevationMatrix);
            if (cellToMarketTexture != null)
                Object.Destroy(cellToMarketTexture);
        }
    }
}
