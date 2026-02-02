using System.Collections.Generic;
using UnityEngine;
using EconSim.Core.Data;
using EconSim.Core.Economy;

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

        private MapData mapData;
        private EconomyState economyState;
        private Material terrainMaterial;

        // Resolution multiplier for higher quality borders
        private int resolutionMultiplier;
        private int baseWidth;
        private int baseHeight;

        // Data textures
        private Texture2D cellDataTexture;      // RGBAHalf: StateId, ProvinceId, CellId, MarketId
        private Texture2D statePaletteTexture;  // 256x1: state colors
        private Texture2D provincePaletteTexture; // 256x1: province colors
        private Texture2D marketPaletteTexture; // 256x1: market colors

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
            GeneratePaletteTextures();
            ApplyTexturesToMaterial();
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
        }

        /// <summary>
        /// Set the current map mode for the shader.
        /// Mode: 0=vertex color (terrain/height), 1=political, 2=province, 3=county, 4=market
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
                default:
                    // Terrain, Height, or any future modes use vertex color
                    shaderMode = 0;
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
            if (statePaletteTexture != null)
                Object.Destroy(statePaletteTexture);
            if (provincePaletteTexture != null)
                Object.Destroy(provincePaletteTexture);
            if (marketPaletteTexture != null)
                Object.Destroy(marketPaletteTexture);
        }
    }
}
