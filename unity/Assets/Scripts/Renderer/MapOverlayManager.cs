using System.Collections.Generic;
using UnityEngine;
using EconSim.Core.Data;
using EconSim.Core.Economy;
using EconSim.Bridge;

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
        private static readonly int BiomeTexId = Shader.PropertyToID("_BiomeTex");
        private static readonly int StateColorTexId = Shader.PropertyToID("_StateColorTex");
        private static readonly int ProvinceColorTexId = Shader.PropertyToID("_ProvinceColorTex");
        private static readonly int MarketColorTexId = Shader.PropertyToID("_MarketColorTex");
        private static readonly int StatePaletteTexId = Shader.PropertyToID("_StatePaletteTex");
        private static readonly int ProvincePaletteTexId = Shader.PropertyToID("_ProvincePaletteTex");
        private static readonly int MarketPaletteTexId = Shader.PropertyToID("_MarketPaletteTex");
        private static readonly int MapModeId = Shader.PropertyToID("_MapMode");
        private static readonly int ShowStateBordersId = Shader.PropertyToID("_ShowStateBorders");
        private static readonly int ShowProvinceBordersId = Shader.PropertyToID("_ShowProvinceBorders");
        private static readonly int ShowMarketBordersId = Shader.PropertyToID("_ShowMarketBorders");
        private static readonly int StateBorderWidthId = Shader.PropertyToID("_StateBorderWidth");
        private static readonly int ProvinceBorderWidthId = Shader.PropertyToID("_ProvinceBorderWidth");
        private static readonly int MarketBorderWidthId = Shader.PropertyToID("_MarketBorderWidth");
        private static readonly int UseHeightDisplacementId = Shader.PropertyToID("_UseHeightDisplacement");
        private static readonly int HeightScaleId = Shader.PropertyToID("_HeightScale");
        private static readonly int SeaLevelId = Shader.PropertyToID("_SeaLevel");

        // Heightmap generation constants
        private const int HeightmapBlurRadius = 15;  // Smooth cell boundaries
        private const float SeaLevel = 0.2f;  // Azgaar sea level = 20/100
        private const float LandMinHeight = 0.25f;  // Margin above sea level to survive blur

        private MapData mapData;
        private EconomyState economyState;
        private Material terrainMaterial;

        // Resolution multiplier for higher quality borders
        private int resolutionMultiplier;
        private int baseWidth;
        private int baseHeight;

        // Data textures
        private Texture2D cellDataTexture;      // RGBAHalf: StateId, ProvinceId, CellId, MarketId
        private Texture2D heightmapTexture;     // RFloat: smoothed height values
        private Texture2D biomeTexture;         // RGB: biome colors (blurred)
        private Texture2D stateColorTexture;    // RGB: state colors (blurred)
        private Texture2D provinceColorTexture; // RGB: province colors (blurred)
        private Texture2D marketColorTexture;   // RGB: market colors (blurred)
        private Texture2D statePaletteTexture;  // 256x1: state colors (legacy)
        private Texture2D provincePaletteTexture; // 256x1: province colors (legacy)
        private Texture2D marketPaletteTexture; // 256x1: market colors (legacy)

        // Spatial lookup grid: maps Azgaar pixel coordinates to cell IDs
        private int[] spatialGrid;
        private int gridWidth;
        private int gridHeight;

        // Raw texture data for incremental updates
        private Color[] cellDataPixels;

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
            this.resolutionMultiplier = Mathf.Clamp(resolutionMultiplier, 1, 4);

            baseWidth = mapData.Info.Width;
            baseHeight = mapData.Info.Height;
            gridWidth = baseWidth * this.resolutionMultiplier;
            gridHeight = baseHeight * this.resolutionMultiplier;

            BuildSpatialGrid();
            GenerateDataTextures();
            GenerateHeightmapTexture();
            GenerateBlurredColorTextures();  // Biome, state, province at base resolution with blur
            GeneratePaletteTextures();       // Legacy palettes (kept for fallback)
            ApplyTexturesToMaterial();

            // Debug output
            TextureDebugger.SaveTexture(heightmapTexture, "heightmap");
            TextureDebugger.SaveTexture(cellDataTexture, "cell_data");
        }

        /// <summary>
        /// Build spatial lookup grid mapping Azgaar coordinates to cell IDs.
        /// Uses cell centers to determine ownership of each grid position.
        /// </summary>
        private void BuildSpatialGrid()
        {
            spatialGrid = new int[gridWidth * gridHeight];

            // Initialize to -1 (no cell)
            for (int i = 0; i < spatialGrid.Length; i++)
            {
                spatialGrid[i] = -1;
            }

            float scale = resolutionMultiplier;

            // For each land cell, fill its approximate area in the grid
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

                // Expand bounding box slightly and clamp to grid
                int x0 = Mathf.Max(0, Mathf.FloorToInt(minX) - 1);
                int x1 = Mathf.Min(gridWidth - 1, Mathf.CeilToInt(maxX) + 1);
                int y0 = Mathf.Max(0, Mathf.FloorToInt(minY) - 1);
                int y1 = Mathf.Min(gridHeight - 1, Mathf.CeilToInt(maxY) + 1);

                // Fill grid positions within bounding box
                // Use distance to cell center to resolve conflicts
                float cx = cell.Center.X * scale;
                float cy = cell.Center.Y * scale;

                for (int y = y0; y <= y1; y++)
                {
                    for (int x = x0; x <= x1; x++)
                    {
                        int gridIdx = y * gridWidth + x;

                        // Check if this grid position is closer to this cell
                        float dx = x - cx;
                        float dy = y - cy;
                        float distSq = dx * dx + dy * dy;

                        int existingCellId = spatialGrid[gridIdx];
                        if (existingCellId < 0)
                        {
                            // No cell assigned yet
                            spatialGrid[gridIdx] = cell.Id;
                        }
                        else if (mapData.CellById.TryGetValue(existingCellId, out var existingCell))
                        {
                            // Compare distances (scale existing cell center too)
                            float edx = x - existingCell.Center.X * scale;
                            float edy = y - existingCell.Center.Y * scale;
                            float existingDistSq = edx * edx + edy * edy;

                            if (distSq < existingDistSq)
                            {
                                spatialGrid[gridIdx] = cell.Id;
                            }
                        }
                    }
                }
            }

            Debug.Log($"MapOverlayManager: Built spatial grid {gridWidth}x{gridHeight} ({resolutionMultiplier}x resolution)");
        }

        /// <summary>
        /// Generate the cell data texture from the spatial grid and cell data.
        /// Format: RGBAHalf with StateId, ProvinceId, CellId, MarketId normalized to 0-1.
        /// </summary>
        private void GenerateDataTextures()
        {
            cellDataTexture = new Texture2D(gridWidth, gridHeight, TextureFormat.RGBAHalf, false);
            cellDataTexture.name = "CellDataTexture";
            cellDataTexture.filterMode = FilterMode.Point;  // No interpolation
            cellDataTexture.wrapMode = TextureWrapMode.Clamp;

            cellDataPixels = new Color[gridWidth * gridHeight];

            // Fill texture from spatial grid
            for (int y = 0; y < gridHeight; y++)
            {
                for (int x = 0; x < gridWidth; x++)
                {
                    int gridIdx = y * gridWidth + x;
                    int cellId = spatialGrid[gridIdx];

                    Color pixel = Color.clear;

                    if (cellId >= 0 && mapData.CellById.TryGetValue(cellId, out var cell))
                    {
                        // Normalize IDs to 0-1 range (divide by 65535 to fit in half precision)
                        pixel.r = cell.StateId / 65535f;
                        pixel.g = cell.ProvinceId / 65535f;
                        pixel.b = cellId / 65535f;
                        pixel.a = 0;  // Market ID will be set later if economy exists
                    }

                    cellDataPixels[gridIdx] = pixel;
                }
            }

            cellDataTexture.SetPixels(cellDataPixels);
            cellDataTexture.Apply();

            Debug.Log($"MapOverlayManager: Generated cell data texture {gridWidth}x{gridHeight}");
        }

        /// <summary>
        /// Generate smoothed heightmap texture from cell height data.
        /// Uses Gaussian blur to smooth the blocky cell boundaries.
        /// </summary>
        private void GenerateHeightmapTexture()
        {
            // 1. Sample raw heights from spatial grid (blocky cell-based)
            float[] rawHeights = new float[gridWidth * gridHeight];

            for (int i = 0; i < spatialGrid.Length; i++)
            {
                int cellId = spatialGrid[i];
                if (cellId >= 0 && mapData.CellById.TryGetValue(cellId, out var cell))
                {
                    float h = cell.Height / 100f;  // Normalize 0-100 to 0-1

                    // Clamp land heights above sea level to prevent
                    // blur from pushing coastal land below water
                    if (cell.IsLand && h < LandMinHeight)
                        h = LandMinHeight;

                    rawHeights[i] = h;
                }
                else
                {
                    rawHeights[i] = 0f;  // Default to sea floor
                }
            }

            // 2. Gaussian blur for smooth transitions
            float[] smoothed = GaussianBlur(rawHeights, gridWidth, gridHeight, HeightmapBlurRadius);

            // 3. Flip Y axis: Azgaar uses Y=0 at top (screen coords), Unity textures use Y=0 at bottom
            // NOTE: If we switch to generating our own maps, use Unity coords natively and remove this flip.
            // See CLAUDE.md "Coordinate Systems" for details.
            float[] flipped = new float[smoothed.Length];
            for (int y = 0; y < gridHeight; y++)
            {
                int srcRow = (gridHeight - 1 - y) * gridWidth;
                int dstRow = y * gridWidth;
                System.Array.Copy(smoothed, srcRow, flipped, dstRow, gridWidth);
            }

            // 4. Create texture
            heightmapTexture = new Texture2D(gridWidth, gridHeight, TextureFormat.RFloat, false);
            heightmapTexture.name = "HeightmapTexture";
            heightmapTexture.filterMode = FilterMode.Bilinear;
            heightmapTexture.wrapMode = TextureWrapMode.Clamp;
            heightmapTexture.SetPixelData(flipped, 0);
            heightmapTexture.Apply();

            Debug.Log($"MapOverlayManager: Generated heightmap {gridWidth}x{gridHeight}, blur radius {HeightmapBlurRadius}");
        }

        /// <summary>
        /// Generate all blurred color textures at base resolution.
        /// Base resolution + bilateral blur + bilinear filtering = smooth edges without aliasing.
        /// </summary>
        private void GenerateBlurredColorTextures()
        {
            // Use base resolution (not multiplied) for faster blur
            int texWidth = baseWidth;
            int texHeight = baseHeight;
            int blurRadius = 3;
            float colorSigma = 30f;

            Color32 waterColor = new Color32(30, 50, 90, 255);
            Color32 neutralColor = new Color32(128, 128, 128, 255);

            // Build a base-resolution spatial grid for texture generation
            int[] baseGrid = new int[texWidth * texHeight];
            for (int i = 0; i < baseGrid.Length; i++)
                baseGrid[i] = -1;

            foreach (var cell in mapData.Cells)
            {
                if (cell.VertexIndices == null || cell.VertexIndices.Count < 3)
                    continue;

                int cx = Mathf.RoundToInt(cell.Center.X);
                int cy = Mathf.RoundToInt(cell.Center.Y);
                int radius = 8;

                for (int dy = -radius; dy <= radius; dy++)
                {
                    for (int dx = -radius; dx <= radius; dx++)
                    {
                        int x = cx + dx;
                        int y = cy + dy;
                        if (x < 0 || x >= texWidth || y < 0 || y >= texHeight)
                            continue;

                        int idx = y * texWidth + x;
                        if (baseGrid[idx] < 0)
                        {
                            baseGrid[idx] = cell.Id;
                        }
                        else if (mapData.CellById.TryGetValue(baseGrid[idx], out var existing))
                        {
                            float edx = x - existing.Center.X;
                            float edy = y - existing.Center.Y;
                            float existDist = edx * edx + edy * edy;
                            float newDist = dx * dx + dy * dy;
                            if (newDist < existDist)
                                baseGrid[idx] = cell.Id;
                        }
                    }
                }
            }

            // Generate biome texture
            Color32[] biomePixels = new Color32[texWidth * texHeight];
            Color32[] statePixels = new Color32[texWidth * texHeight];
            Color32[] provincePixels = new Color32[texWidth * texHeight];

            for (int y = 0; y < texHeight; y++)
            {
                int srcY = texHeight - 1 - y;  // Y-flip for Unity coordinates

                for (int x = 0; x < texWidth; x++)
                {
                    int srcIdx = srcY * texWidth + x;
                    int dstIdx = y * texWidth + x;
                    int cellId = baseGrid[srcIdx];

                    if (cellId >= 0 && mapData.CellById.TryGetValue(cellId, out var cell))
                    {
                        if (!cell.IsLand)
                        {
                            float depthFactor = Mathf.Clamp01((20 - cell.Height) / 20f);
                            Color32 water = Color32.Lerp(
                                new Color32(50, 100, 150, 255),
                                new Color32(20, 50, 100, 255),
                                depthFactor
                            );
                            biomePixels[dstIdx] = water;
                            statePixels[dstIdx] = water;
                            provincePixels[dstIdx] = water;
                        }
                        else
                        {
                            // Biome
                            if (cell.BiomeId >= 0 && cell.BiomeId < mapData.Biomes.Count)
                                biomePixels[dstIdx] = mapData.Biomes[cell.BiomeId].Color.ToUnity();
                            else
                                biomePixels[dstIdx] = new Color32(100, 150, 100, 255);

                            // State
                            if (cell.StateId > 0 && mapData.StateById.TryGetValue(cell.StateId, out var state))
                                statePixels[dstIdx] = state.Color.ToUnity();
                            else
                                statePixels[dstIdx] = neutralColor;

                            // Province
                            if (cell.ProvinceId > 0 && mapData.ProvinceById.TryGetValue(cell.ProvinceId, out var province))
                                provincePixels[dstIdx] = province.Color.ToUnity();
                            else if (cell.StateId > 0 && mapData.StateById.TryGetValue(cell.StateId, out var st))
                                provincePixels[dstIdx] = st.Color.ToUnity();
                            else
                                provincePixels[dstIdx] = neutralColor;
                        }
                    }
                    else
                    {
                        biomePixels[dstIdx] = waterColor;
                        statePixels[dstIdx] = waterColor;
                        provincePixels[dstIdx] = waterColor;
                    }
                }
            }

            // Apply bilateral blur to each
            biomePixels = BilateralBlur(biomePixels, texWidth, texHeight, blurRadius, colorSigma);
            statePixels = BilateralBlur(statePixels, texWidth, texHeight, blurRadius, colorSigma);
            provincePixels = BilateralBlur(provincePixels, texWidth, texHeight, blurRadius, colorSigma);

            // Create textures
            biomeTexture = CreateColorTexture("BiomeTexture", biomePixels, texWidth, texHeight);
            stateColorTexture = CreateColorTexture("StateColorTexture", statePixels, texWidth, texHeight);
            provinceColorTexture = CreateColorTexture("ProvinceColorTexture", provincePixels, texWidth, texHeight);

            // Market texture generated later when economy state is set
            marketColorTexture = CreateColorTexture("MarketColorTexture", new Color32[texWidth * texHeight], texWidth, texHeight);

            Debug.Log($"MapOverlayManager: Generated blurred color textures {texWidth}x{texHeight}");
        }

        private Texture2D CreateColorTexture(string name, Color32[] pixels, int width, int height)
        {
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            tex.name = name;
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.SetPixels32(pixels);
            tex.Apply();
            return tex;
        }

        /// <summary>
        /// Bilateral blur - edge-preserving smoothing.
        /// Smooths similar colors, preserves edges between different colors.
        /// </summary>
        private Color32[] BilateralBlur(Color32[] input, int width, int height, int radius, float colorSigma)
        {
            Color32[] output = new Color32[input.Length];
            float spatialSigma = radius / 2.5f;
            float colorSigmaSq2 = 2f * colorSigma * colorSigma;
            float spatialSigmaSq2 = 2f * spatialSigma * spatialSigma;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int centerIdx = y * width + x;
                    Color32 centerColor = input[centerIdx];

                    float sumR = 0, sumG = 0, sumB = 0;
                    float weightSum = 0;

                    for (int ky = -radius; ky <= radius; ky++)
                    {
                        int sy = Mathf.Clamp(y + ky, 0, height - 1);

                        for (int kx = -radius; kx <= radius; kx++)
                        {
                            int sx = Mathf.Clamp(x + kx, 0, width - 1);
                            int sampleIdx = sy * width + sx;
                            Color32 sampleColor = input[sampleIdx];

                            // Spatial weight (Gaussian based on distance)
                            float distSq = kx * kx + ky * ky;
                            float spatialWeight = Mathf.Exp(-distSq / spatialSigmaSq2);

                            // Color weight (Gaussian based on color difference)
                            float dr = centerColor.r - sampleColor.r;
                            float dg = centerColor.g - sampleColor.g;
                            float db = centerColor.b - sampleColor.b;
                            float colorDistSq = dr * dr + dg * dg + db * db;
                            float colorWeight = Mathf.Exp(-colorDistSq / colorSigmaSq2);

                            // Combined weight
                            float weight = spatialWeight * colorWeight;

                            sumR += sampleColor.r * weight;
                            sumG += sampleColor.g * weight;
                            sumB += sampleColor.b * weight;
                            weightSum += weight;
                        }
                    }

                    output[centerIdx] = new Color32(
                        (byte)Mathf.Clamp(sumR / weightSum, 0, 255),
                        (byte)Mathf.Clamp(sumG / weightSum, 0, 255),
                        (byte)Mathf.Clamp(sumB / weightSum, 0, 255),
                        255
                    );
                }
            }

            return output;
        }

        /// <summary>
        /// Apply separable Gaussian blur to a 2D array of float values.
        /// </summary>
        private float[] GaussianBlur(float[] input, int width, int height, int radius)
        {
            float[] kernel = GenerateGaussianKernel(radius);
            float[] temp = new float[width * height];
            float[] output = new float[width * height];

            // Horizontal pass
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float sum = 0, weightSum = 0;
                    for (int k = -radius; k <= radius; k++)
                    {
                        int sx = Mathf.Clamp(x + k, 0, width - 1);
                        float w = kernel[k + radius];
                        sum += input[y * width + sx] * w;
                        weightSum += w;
                    }
                    temp[y * width + x] = sum / weightSum;
                }
            }

            // Vertical pass
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float sum = 0, weightSum = 0;
                    for (int k = -radius; k <= radius; k++)
                    {
                        int sy = Mathf.Clamp(y + k, 0, height - 1);
                        float w = kernel[k + radius];
                        sum += temp[sy * width + x] * w;
                        weightSum += w;
                    }
                    output[y * width + x] = sum / weightSum;
                }
            }

            return output;
        }

        /// <summary>
        /// Generate a 1D Gaussian kernel for the given radius.
        /// </summary>
        private float[] GenerateGaussianKernel(int radius)
        {
            int size = radius * 2 + 1;
            float[] kernel = new float[size];
            float sigma = radius / 2.5f;
            float sum = 0;

            for (int i = 0; i < size; i++)
            {
                int x = i - radius;
                kernel[i] = Mathf.Exp(-(x * x) / (2 * sigma * sigma));
                sum += kernel[i];
            }

            for (int i = 0; i < size; i++)
                kernel[i] /= sum;

            return kernel;
        }

        /// <summary>
        /// Generate color palette textures for states, provinces, and markets.
        /// </summary>
        private void GeneratePaletteTextures()
        {
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
                    stateColors[state.Id] = new Color(
                        state.Color.R / 255f,
                        state.Color.G / 255f,
                        state.Color.B / 255f
                    );
                }
            }
            statePaletteTexture.SetPixels(stateColors);
            statePaletteTexture.Apply();

            // Province palette
            provincePaletteTexture = new Texture2D(256, 1, TextureFormat.RGBA32, false);
            provincePaletteTexture.name = "ProvincePalette";
            provincePaletteTexture.filterMode = FilterMode.Point;
            provincePaletteTexture.wrapMode = TextureWrapMode.Clamp;

            var provinceColors = new Color[256];
            provinceColors[0] = new Color(0.5f, 0.5f, 0.5f);  // No province

            foreach (var province in mapData.Provinces)
            {
                if (province.Id > 0 && province.Id < 256)
                {
                    provinceColors[province.Id] = new Color(
                        province.Color.R / 255f,
                        province.Color.G / 255f,
                        province.Color.B / 255f
                    );
                }
            }
            provincePaletteTexture.SetPixels(provinceColors);
            provincePaletteTexture.Apply();

            // Market palette (will be populated when economy state is set)
            marketPaletteTexture = new Texture2D(256, 1, TextureFormat.RGBA32, false);
            marketPaletteTexture.name = "MarketPalette";
            marketPaletteTexture.filterMode = FilterMode.Point;
            marketPaletteTexture.wrapMode = TextureWrapMode.Clamp;

            var marketColors = new Color[256];
            marketColors[0] = new Color(0.6f, 0.6f, 0.6f);  // No market - light gray

            // Pre-populate with zone colors
            for (int i = 1; i < 256; i++)
            {
                int colorIdx = (i - 1) % MarketZoneColors.Length;
                marketColors[i] = MarketZoneColors[colorIdx];
            }
            marketPaletteTexture.SetPixels(marketColors);
            marketPaletteTexture.Apply();

            Debug.Log($"MapOverlayManager: Generated palette textures");
        }

        /// <summary>
        /// Apply all textures and initial settings to the terrain material.
        /// </summary>
        private void ApplyTexturesToMaterial()
        {
            if (terrainMaterial == null) return;

            terrainMaterial.SetTexture(CellDataTexId, cellDataTexture);
            terrainMaterial.SetTexture(HeightmapTexId, heightmapTexture);
            terrainMaterial.SetTexture(BiomeTexId, biomeTexture);
            terrainMaterial.SetTexture(StateColorTexId, stateColorTexture);
            terrainMaterial.SetTexture(ProvinceColorTexId, provinceColorTexture);
            terrainMaterial.SetTexture(MarketColorTexId, marketColorTexture);
            terrainMaterial.SetTexture(StatePaletteTexId, statePaletteTexture);
            terrainMaterial.SetTexture(ProvincePaletteTexId, provincePaletteTexture);
            terrainMaterial.SetTexture(MarketPaletteTexId, marketPaletteTexture);

            // Default to vertex color mode with state borders
            terrainMaterial.SetInt(MapModeId, 0);
            terrainMaterial.SetInt(ShowStateBordersId, 1);
            terrainMaterial.SetInt(ShowProvinceBordersId, 0);
            terrainMaterial.SetInt(ShowMarketBordersId, 0);
        }

        /// <summary>
        /// Set the economy state to enable market-related overlays.
        /// Updates the cell data texture with market IDs.
        /// </summary>
        public void SetEconomyState(EconomyState economy)
        {
            economyState = economy;

            if (economy == null || economy.CellToMarket == null)
                return;

            float scale = resolutionMultiplier;

            // Update cell data texture with market IDs
            bool needsUpdate = false;

            foreach (var kvp in economy.CellToMarket)
            {
                int cellId = kvp.Key;
                int marketId = kvp.Value;

                if (!mapData.CellById.TryGetValue(cellId, out var cell))
                    continue;

                // Find grid positions for this cell and update market ID
                // Scale cell center by resolution multiplier
                int cx = Mathf.RoundToInt(cell.Center.X * scale);
                int cy = Mathf.RoundToInt(cell.Center.Y * scale);

                // Update a region around the center (scaled radius)
                int radius = 5 * resolutionMultiplier;
                for (int dy = -radius; dy <= radius; dy++)
                {
                    for (int dx = -radius; dx <= radius; dx++)
                    {
                        int x = cx + dx;
                        int y = cy + dy;

                        if (x < 0 || x >= gridWidth || y < 0 || y >= gridHeight)
                            continue;

                        int gridIdx = y * gridWidth + x;

                        // Only update if this grid position belongs to this cell
                        if (spatialGrid[gridIdx] == cellId)
                        {
                            Color pixel = cellDataPixels[gridIdx];
                            pixel.a = marketId / 65535f;
                            cellDataPixels[gridIdx] = pixel;
                            needsUpdate = true;
                        }
                    }
                }
            }

            if (needsUpdate)
            {
                cellDataTexture.SetPixels(cellDataPixels);
                cellDataTexture.Apply();
                Debug.Log($"MapOverlayManager: Updated market IDs in cell data texture");
            }

            // Generate blurred market color texture
            GenerateMarketColorTexture(economy);
        }

        /// <summary>
        /// Generate blurred market color texture from economy state.
        /// Uses spatial grid for O(pixels) instead of O(pixels × cells).
        /// </summary>
        private void GenerateMarketColorTexture(EconomyState economy)
        {
            int texWidth = baseWidth;
            int texHeight = baseHeight;

            Color32 waterColor = new Color32(30, 50, 90, 255);
            Color32 noMarketColor = new Color32(60, 60, 60, 255);

            Color32[] pixels = new Color32[texWidth * texHeight];

            // Use the high-res spatial grid, sampling at base resolution
            float scale = (float)gridWidth / texWidth;

            for (int y = 0; y < texHeight; y++)
            {
                int srcY = texHeight - 1 - y;  // Y-flip

                for (int x = 0; x < texWidth; x++)
                {
                    int dstIdx = y * texWidth + x;

                    // Sample from high-res grid
                    int gx = Mathf.Clamp(Mathf.RoundToInt(x * scale), 0, gridWidth - 1);
                    int gy = Mathf.Clamp(Mathf.RoundToInt(srcY * scale), 0, gridHeight - 1);
                    int cellId = spatialGrid[gy * gridWidth + gx];

                    if (cellId >= 0 && mapData.CellById.TryGetValue(cellId, out var cell))
                    {
                        if (!cell.IsLand)
                        {
                            pixels[dstIdx] = waterColor;
                        }
                        else if (economy.CellToMarket.TryGetValue(cellId, out int marketId))
                        {
                            int colorIdx = (marketId - 1) % MarketZoneColors.Length;
                            if (colorIdx < 0) colorIdx = 0;
                            pixels[dstIdx] = new Color32(
                                (byte)(MarketZoneColors[colorIdx].r * 255),
                                (byte)(MarketZoneColors[colorIdx].g * 255),
                                (byte)(MarketZoneColors[colorIdx].b * 255),
                                255
                            );
                        }
                        else
                        {
                            pixels[dstIdx] = noMarketColor;
                        }
                    }
                    else
                    {
                        pixels[dstIdx] = waterColor;
                    }
                }
            }

            // Apply bilateral blur
            pixels = BilateralBlur(pixels, texWidth, texHeight, 3, 30f);

            // Update texture
            if (marketColorTexture != null)
                Object.Destroy(marketColorTexture);

            marketColorTexture = CreateColorTexture("MarketColorTexture", pixels, texWidth, texHeight);
            terrainMaterial.SetTexture(MarketColorTexId, marketColorTexture);

            Debug.Log($"MapOverlayManager: Generated blurred market color texture");
        }

        /// <summary>
        /// Set the current map mode for the shader.
        /// Mode: 0=vertex color (terrain/height), 1=political, 2=province, 3=county, 4=market
        /// </summary>
        /// <summary>
        /// Set the current map mode for the shader.
        /// Mode: 0=height gradient, 1=political, 2=province, 3=county, 4=market, 5=terrain/biome
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
                    shaderMode = 5;  // Biome texture
                    break;
                case MapView.MapMode.Height:
                default:
                    shaderMode = 0;  // Height gradient
                    break;
            }

            terrainMaterial.SetInt(MapModeId, shaderMode);
        }

        /// <summary>
        /// Set state border visibility.
        /// </summary>
        public void SetStateBordersVisible(bool visible)
        {
            if (terrainMaterial == null) return;
            terrainMaterial.SetInt(ShowStateBordersId, visible ? 1 : 0);
        }

        /// <summary>
        /// Set province border visibility.
        /// </summary>
        public void SetProvinceBordersVisible(bool visible)
        {
            if (terrainMaterial == null) return;
            terrainMaterial.SetInt(ShowProvinceBordersId, visible ? 1 : 0);
        }

        /// <summary>
        /// Set market border visibility.
        /// </summary>
        public void SetMarketBordersVisible(bool visible)
        {
            if (terrainMaterial == null) return;
            terrainMaterial.SetInt(ShowMarketBordersId, visible ? 1 : 0);
        }

        /// <summary>
        /// Set state border width in screen pixels.
        /// </summary>
        public void SetStateBorderWidth(float pixels)
        {
            if (terrainMaterial == null) return;
            terrainMaterial.SetFloat(StateBorderWidthId, Mathf.Clamp(pixels, 0.5f, 5f));
        }

        /// <summary>
        /// Set province border width in screen pixels.
        /// </summary>
        public void SetProvinceBorderWidth(float pixels)
        {
            if (terrainMaterial == null) return;
            terrainMaterial.SetFloat(ProvinceBorderWidthId, Mathf.Clamp(pixels, 0.5f, 3f));
        }

        /// <summary>
        /// Set market border width in screen pixels.
        /// </summary>
        public void SetMarketBorderWidth(float pixels)
        {
            if (terrainMaterial == null) return;
            terrainMaterial.SetFloat(MarketBorderWidthId, Mathf.Clamp(pixels, 0.5f, 3f));
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
        /// Update cell data for a specific cell. Useful for dynamic changes (conquests, etc.).
        /// </summary>
        public void UpdateCellData(int cellId, int? newStateId = null, int? newProvinceId = null, int? newMarketId = null)
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
                        if (newMarketId.HasValue)
                            pixel.a = newMarketId.Value / 65535f;

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
            if (biomeTexture != null)
                Object.Destroy(biomeTexture);
            if (stateColorTexture != null)
                Object.Destroy(stateColorTexture);
            if (provinceColorTexture != null)
                Object.Destroy(provinceColorTexture);
            if (marketColorTexture != null)
                Object.Destroy(marketColorTexture);
            if (statePaletteTexture != null)
                Object.Destroy(statePaletteTexture);
            if (provincePaletteTexture != null)
                Object.Destroy(provincePaletteTexture);
            if (marketPaletteTexture != null)
                Object.Destroy(marketPaletteTexture);
        }
    }
}
