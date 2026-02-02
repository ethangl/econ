using System.Collections.Generic;
using UnityEngine;
using EconSim.Core.Data;
using EconSim.Core.Economy;
using EconSim.Core.Rendering;
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
        private static readonly int StatePaletteTexId = Shader.PropertyToID("_StatePaletteTex");
        private static readonly int MarketPaletteTexId = Shader.PropertyToID("_MarketPaletteTex");
        private static readonly int BiomePaletteTexId = Shader.PropertyToID("_BiomePaletteTex");
        private static readonly int BiomeMatrixTexId = Shader.PropertyToID("_BiomeMatrixTex");
        private static readonly int CellToMarketTexId = Shader.PropertyToID("_CellToMarketTex");
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
        private static readonly int SelectedStateIdId = Shader.PropertyToID("_SelectedStateId");
        private static readonly int SelectedProvinceIdId = Shader.PropertyToID("_SelectedProvinceId");
        private static readonly int SelectedCellIdId = Shader.PropertyToID("_SelectedCellId");
        private static readonly int SelectedMarketIdId = Shader.PropertyToID("_SelectedMarketId");
        private static readonly int SelectionBorderColorId = Shader.PropertyToID("_SelectionBorderColor");
        private static readonly int SelectionBorderWidthId = Shader.PropertyToID("_SelectionBorderWidth");
        private static readonly int SelectionFillAlphaId = Shader.PropertyToID("_SelectionFillAlpha");

        private MapData mapData;
        private EconomyState economyState;
        private Material terrainMaterial;

        // Resolution multiplier for higher quality borders
        private int resolutionMultiplier;
        private int baseWidth;
        private int baseHeight;

        // Data textures
        private Texture2D cellDataTexture;      // RGBAFloat: StateId, ProvinceId, BiomeId+WaterFlag, CellId
        private Texture2D cellToMarketTexture;  // R16: CellId -> MarketId mapping (dynamic)
        private Texture2D heightmapTexture;     // RFloat: smoothed height values
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
            this.resolutionMultiplier = Mathf.Clamp(resolutionMultiplier, 1, 4);

            baseWidth = mapData.Info.Width;
            baseHeight = mapData.Info.Height;
            gridWidth = baseWidth * this.resolutionMultiplier;
            gridHeight = baseHeight * this.resolutionMultiplier;

            BuildSpatialGrid();
            GenerateDataTextures();
            GenerateHeightmapTexture();
            GeneratePaletteTextures();
            GenerateBiomeElevationMatrix();
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
        /// Format: RGBAFloat with StateId, ProvinceId, BiomeId+WaterFlag, CellId normalized to 0-1.
        /// B channel encodes: BiomeId in low bits, water flag in high bit (add 32768 if water)
        /// Uses 32-bit float for precise cell ID storage (half-precision caused banding artifacts).
        /// </summary>
        private void GenerateDataTextures()
        {
            cellDataTexture = new Texture2D(gridWidth, gridHeight, TextureFormat.RGBAFloat, false);
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

                    Color pixel;

                    if (cellId >= 0 && mapData.CellById.TryGetValue(cellId, out var cell))
                    {
                        // Normalize IDs to 0-1 range (divide by 65535)
                        pixel.r = cell.StateId / 65535f;
                        pixel.g = cell.ProvinceId / 65535f;
                        // Pack biome ID and water flag: biomeId + (isWater ? 32768 : 0)
                        int packedBiome = cell.BiomeId + (cell.IsLand ? 0 : 32768);
                        pixel.b = packedBiome / 65535f;
                        pixel.a = cellId / 65535f;  // Cell ID for county-level rendering
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
            }

            cellDataTexture.SetPixels(cellDataPixels);
            cellDataTexture.Apply();

            Debug.Log($"MapOverlayManager: Generated cell data texture {gridWidth}x{gridHeight}");
        }

        /// <summary>
        /// Generate heightmap texture from cell height data.
        /// Used for water depth coloring. 3D height displacement is currently disabled.
        /// Water detection for other modes uses the water flag in the data texture.
        /// </summary>
        private void GenerateHeightmapTexture()
        {
            // 1. Sample raw heights from spatial grid
            float[] rawHeights = new float[gridWidth * gridHeight];

            for (int i = 0; i < spatialGrid.Length; i++)
            {
                int cellId = spatialGrid[i];
                if (cellId >= 0 && mapData.CellById.TryGetValue(cellId, out var cell))
                {
                    // Normalize 0-100 to 0-1
                    rawHeights[i] = cell.Height / 100f;
                }
                else
                {
                    rawHeights[i] = 0f;  // Default to sea floor
                }
            }

            // 2. Flip Y axis: Azgaar uses Y=0 at top (screen coords), Unity textures use Y=0 at bottom
            // NOTE: If we switch to generating our own maps, use Unity coords natively and remove this flip.
            // See CLAUDE.md "Coordinate Systems" for details.
            float[] flipped = new float[rawHeights.Length];
            for (int y = 0; y < gridHeight; y++)
            {
                int srcRow = (gridHeight - 1 - y) * gridWidth;
                int dstRow = y * gridWidth;
                System.Array.Copy(rawHeights, srcRow, flipped, dstRow, gridWidth);
            }

            // 3. Create texture
            heightmapTexture = new Texture2D(gridWidth, gridHeight, TextureFormat.RFloat, false);
            heightmapTexture.name = "HeightmapTexture";
            heightmapTexture.filterMode = FilterMode.Bilinear;
            heightmapTexture.wrapMode = TextureWrapMode.Clamp;
            heightmapTexture.SetPixelData(flipped, 0);
            heightmapTexture.Apply();

            Debug.Log($"MapOverlayManager: Generated heightmap {gridWidth}x{gridHeight}");
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

            // Default to political mode with state borders
            terrainMaterial.SetInt(MapModeId, 1);
            terrainMaterial.SetInt(ShowStateBordersId, 1);
            terrainMaterial.SetInt(ShowProvinceBordersId, 0);
            terrainMaterial.SetInt(ShowMarketBordersId, 0);

            // Clear any persisted selection from previous play session
            ClearSelection();
        }

        /// <summary>
        /// Set the economy state to enable market-related overlays.
        /// Updates the cell-to-market texture and regenerates market palette.
        /// </summary>
        public void SetEconomyState(EconomyState economy)
        {
            economyState = economy;

            if (economy == null || economy.CellToMarket == null)
                return;

            // Regenerate market palette based on hub state colors
            RegenerateMarketPalette(economy);

            // Update cell-to-market lookup texture
            var marketPixels = new Color[16384];
            foreach (var kvp in economy.CellToMarket)
            {
                int cellId = kvp.Key;
                int marketId = kvp.Value;

                if (cellId >= 0 && cellId < 16384)
                {
                    marketPixels[cellId] = new Color(marketId / 65535f, 0, 0, 1);
                }
            }

            cellToMarketTexture.SetPixels(marketPixels);
            cellToMarketTexture.Apply();

            Debug.Log($"MapOverlayManager: Updated cell-to-market texture ({economy.CellToMarket.Count} cells mapped)");
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
        /// Clear all selection highlighting.
        /// </summary>
        public void ClearSelection()
        {
            if (terrainMaterial == null) return;
            terrainMaterial.SetFloat(SelectedStateIdId, -1f);
            terrainMaterial.SetFloat(SelectedProvinceIdId, -1f);
            terrainMaterial.SetFloat(SelectedCellIdId, -1f);
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
            terrainMaterial.SetFloat(SelectedCellIdId, -1f);
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
            terrainMaterial.SetFloat(SelectedCellIdId, -1f);
            terrainMaterial.SetFloat(SelectedMarketIdId, -1f);
        }

        /// <summary>
        /// Set the currently selected cell for shader-based highlighting.
        /// Clears other selections.
        /// Pass -1 to clear selection.
        /// </summary>
        public void SetSelectedCell(int cellId)
        {
            if (terrainMaterial == null) return;
            terrainMaterial.SetFloat(SelectedStateIdId, -1f);
            terrainMaterial.SetFloat(SelectedProvinceIdId, -1f);
            float normalizedId = cellId < 0 ? -1f : cellId / 65535f;
            terrainMaterial.SetFloat(SelectedCellIdId, normalizedId);
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
            terrainMaterial.SetFloat(SelectedCellIdId, -1f);
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
        /// Set the selection fill alpha (0 = no fill, 0.5 = max fill).
        /// </summary>
        public void SetSelectionFillAlpha(float alpha)
        {
            if (terrainMaterial == null) return;
            terrainMaterial.SetFloat(SelectionFillAlphaId, Mathf.Clamp(alpha, 0f, 0.5f));
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
