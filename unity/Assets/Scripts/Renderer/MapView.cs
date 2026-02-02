using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using EconSim.Core.Data;
using EconSim.Bridge;
using EconSim.Camera;

namespace EconSim.Renderer
{
    /// <summary>
    /// Main map renderer. Generates and displays the terrain mesh from map data.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class MapView : MonoBehaviour
    {
        [Header("Rendering Settings")]
        [SerializeField] private float heightScale = 15f;
        [SerializeField] private float cellScale = 0.01f;  // Scale from Azgaar pixels to Unity units
        [SerializeField] private Material terrainMaterial;
        [SerializeField] private bool renderLandOnly = false;

        [Header("Map Mode")]
        [SerializeField] private MapMode currentMode = MapMode.Political;

        [Header("Shader Overlays")]
        [SerializeField] private bool useShaderOverlays = true;
        [SerializeField] [Range(1, 4)] private int overlayResolutionMultiplier = 2;  // Higher = smoother borders, more memory

        [Header("Borders")]
        [SerializeField] private bool enableBorders = true;
        [SerializeField] private Material borderMaterial;

        [Header("Selection")]
        [SerializeField] private UnityEngine.Camera selectionCamera;
        [SerializeField] private MapCamera mapCameraController;

        /// <summary>Event fired when a cell is clicked. Passes cell ID (-1 if clicked on nothing).</summary>
        public event Action<int> OnCellClicked;

        private MapData mapData;
        private EconSim.Core.Economy.EconomyState economyState;
        private MeshFilter meshFilter;
        private MeshRenderer meshRenderer;
        private Mesh mesh;
        private BorderRenderer borderRenderer;
        private RiverRenderer riverRenderer;
        private RoadRenderer roadRenderer;
        private SelectionHighlight selectionHighlight;
        private MapOverlayManager overlayManager;

        // Cell mesh data
        private List<Vector3> vertices = new List<Vector3>();
        private List<int> triangles = new List<int>();
        private List<Color32> colors = new List<Color32>();
        private List<Vector2> uv1 = new List<Vector2>();  // Data texture UVs (Azgaar coords normalized)

        // Vertex heights (computed by averaging neighboring cells)
        private float[] vertexHeights;

        // Track last political mode for 1-key cycling
        private MapMode lastPoliticalMode = MapMode.Political;

        public enum MapMode
        {
            Political,  // Colored by state/country (key: 1, cycles with Province/County)
            Province,   // Colored by province (key: 1, cycles with Political/County)
            County,     // Colored by county/cell (key: 1, cycles with Political/Province)
            Terrain,    // Colored by biome (key: 2)
            Height,     // Colored by elevation (key: 3)
            Market      // Colored by market zone (key: 4)
        }

        public MapMode CurrentMode => currentMode;
        public string CurrentModeName => ModeNames[(int)currentMode];

        private static readonly string[] ModeNames = { "Political", "Province", "County", "Terrain", "Height", "Market" };

        private void Awake()
        {
            meshFilter = GetComponent<MeshFilter>();
            meshRenderer = GetComponent<MeshRenderer>();
        }

        private void OnDestroy()
        {
            // Clean up overlay manager textures
            overlayManager?.Dispose();
        }

        private void Update()
        {
            // Map mode selection with number keys 1-4
            if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1))
            {
                // Key 1 cycles between political modes (Political → Province → County)
                // From non-political mode, returns to last used political mode
                if (currentMode == MapMode.Political)
                {
                    SetMapMode(MapMode.Province);
                    lastPoliticalMode = MapMode.Province;
                    Debug.Log("Map mode: Province (1)");
                }
                else if (currentMode == MapMode.Province)
                {
                    SetMapMode(MapMode.County);
                    lastPoliticalMode = MapMode.County;
                    Debug.Log("Map mode: County (1)");
                }
                else if (currentMode == MapMode.County)
                {
                    SetMapMode(MapMode.Political);
                    lastPoliticalMode = MapMode.Political;
                    Debug.Log("Map mode: Political (1)");
                }
                else
                {
                    SetMapMode(lastPoliticalMode);
                    Debug.Log($"Map mode: {lastPoliticalMode} (1)");
                }
            }
            else if (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2))
            {
                SetMapMode(MapMode.Terrain);
                Debug.Log("Map mode: Terrain (2)");
            }
            else if (Input.GetKeyDown(KeyCode.Alpha3) || Input.GetKeyDown(KeyCode.Keypad3))
            {
                SetMapMode(MapMode.Height);
                Debug.Log("Map mode: Height (3)");
            }
            else if (Input.GetKeyDown(KeyCode.Alpha4) || Input.GetKeyDown(KeyCode.Keypad4))
            {
                SetMapMode(MapMode.Market);
                Debug.Log("Map mode: Market (4)");
            }

            // Click to select cell (but not when camera is in panning mode or over UI)
            if (Input.GetMouseButtonDown(0))
            {
                // Skip if pointer is over a UI Toolkit element
                if (IsPointerOverUI())
                    return;

                if (mapCameraController == null || !mapCameraController.IsPanningMode)
                {
                    HandleClick();
                }
            }
        }

        private void HandleClick()
        {
            if (mapData == null) return;

            var cam = selectionCamera != null ? selectionCamera : UnityEngine.Camera.main;
            if (cam == null) return;

            // Use a ground plane at y=0 instead of mesh collider (much faster)
            Ray ray = cam.ScreenPointToRay(Input.mousePosition);
            Plane groundPlane = new Plane(Vector3.up, Vector3.zero);

            if (groundPlane.Raycast(ray, out float distance))
            {
                Vector3 hitPoint = ray.GetPoint(distance);
                int cellId = FindCellAtPosition(hitPoint);
                OnCellClicked?.Invoke(cellId);
            }
            else
            {
                OnCellClicked?.Invoke(-1);
            }
        }

        /// <summary>
        /// Check if the mouse pointer is over any UI Toolkit panel element.
        /// </summary>
        private bool IsPointerOverUI()
        {
            Vector2 mousePos = Input.mousePosition;

            // Check all UIDocuments in the scene
            var docs = FindObjectsOfType<UIDocument>();
            foreach (var doc in docs)
            {
                var root = doc.rootVisualElement;
                if (root == null || root.panel == null) continue;

                // Convert screen position to panel position
                Vector2 panelPos = RuntimePanelUtils.ScreenToPanel(
                    root.panel,
                    new Vector2(mousePos.x, Screen.height - mousePos.y)
                );

                // Pick the topmost element at the panel position
                var picked = root.panel.Pick(panelPos);

                // If nothing picked or just the root's TemplateContainer, not over UI
                if (picked == null) continue;

                // Walk up to find if we're inside a real panel (has "panel" class or specific name)
                var current = picked;
                while (current != null)
                {
                    if (current.ClassListContains("panel") ||
                        current.name == "selection-panel" ||
                        current.name == "market-panel" ||
                        current.name == "economy-panel" ||
                        current.name == "time-control-panel")
                    {
                        return true;
                    }
                    current = current.parent;
                }
            }
            return false;
        }

        /// <summary>
        /// Find the cell that contains the given world position.
        /// Returns -1 if click is too far from any land cell.
        /// </summary>
        public int FindCellAtPosition(Vector3 worldPos)
        {
            if (mapData == null) return -1;

            // Convert world position back to Azgaar coordinates
            Vector3 localPos = worldPos - transform.position;
            float azgaarX = localPos.x / cellScale;
            float azgaarY = -localPos.z / cellScale;  // Z was negated during mesh generation

            // Find the closest cell center
            float minDistSq = float.MaxValue;
            int closestCell = -1;

            foreach (var cell in mapData.Cells)
            {
                if (renderLandOnly && !cell.IsLand) continue;

                float dx = cell.Center.X - azgaarX;
                float dy = cell.Center.Y - azgaarY;
                float distSq = dx * dx + dy * dy;

                if (distSq < minDistSq)
                {
                    minDistSq = distSq;
                    closestCell = cell.Id;
                }
            }

            // Average cell radius is roughly 5-10 Azgaar units
            // If click is more than 15 units from nearest cell center, ignore it
            const float maxDistSq = 15f * 15f;
            if (minDistSq > maxDistSq)
            {
                return -1;
            }

            return closestCell;
        }

        public void Initialize(MapData data)
        {
            mapData = data;
            GenerateMesh();
            InitializeOverlays();
            InitializeBorders();
            InitializeRivers();
            InitializeRoads();
            InitializeSelectionHighlight();
        }

        private void InitializeOverlays()
        {
            if (!useShaderOverlays || mapData == null)
                return;

            // Get the material from the MeshRenderer if not set in Inspector
            Material mat = terrainMaterial;
            if (mat == null && meshRenderer != null)
            {
                mat = meshRenderer.sharedMaterial;
            }

            if (mat == null)
                return;

            overlayManager = new MapOverlayManager(mapData, mat, overlayResolutionMultiplier);

            // Sync shader mode with current map mode
            overlayManager.SetMapMode(currentMode);
            UpdateOverlayVisibility();
        }

        private void InitializeBorders()
        {
            // Skip mesh-based borders if using shader overlays
            if (useShaderOverlays || !enableBorders || mapData == null) return;

            // Create or get BorderRenderer component
            borderRenderer = GetComponent<BorderRenderer>();
            if (borderRenderer == null)
            {
                borderRenderer = gameObject.AddComponent<BorderRenderer>();
            }

            // Set border material if we have one
            if (borderMaterial != null)
            {
                var borderMaterialField = typeof(BorderRenderer).GetField("borderMaterial",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                borderMaterialField?.SetValue(borderRenderer, borderMaterial);
            }

            borderRenderer.Initialize(mapData, cellScale, heightScale);

            // Apply initial border visibility based on current map mode
            UpdateBorderVisibility();
        }

        private void InitializeRivers()
        {
            if (mapData == null) return;

            // Create or get RiverRenderer component
            riverRenderer = GetComponent<RiverRenderer>();
            if (riverRenderer == null)
            {
                riverRenderer = gameObject.AddComponent<RiverRenderer>();
            }

            // Set the river material (reuse border material)
            if (borderMaterial != null)
            {
                var materialField = typeof(RiverRenderer).GetField("riverMaterial",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                materialField?.SetValue(riverRenderer, borderMaterial);
            }

            riverRenderer.Initialize(mapData, cellScale, heightScale);
        }

        private void InitializeRoads()
        {
            if (mapData == null) return;

            // Create or get RoadRenderer component
            roadRenderer = GetComponent<RoadRenderer>();
            if (roadRenderer == null)
            {
                roadRenderer = gameObject.AddComponent<RoadRenderer>();
            }

            // Set the road material (reuse border material)
            if (borderMaterial != null)
            {
                var materialField = typeof(RoadRenderer).GetField("roadMaterial",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                materialField?.SetValue(roadRenderer, borderMaterial);
            }

            // Note: RoadRenderer.Initialize will be called from SetRoadState once economy is ready
        }

        /// <summary>
        /// Set road state for road rendering. Call after economy initialization.
        /// </summary>
        public void SetRoadState(EconSim.Core.Economy.RoadState roads)
        {
            if (roadRenderer != null && mapData != null)
            {
                roadRenderer.Initialize(mapData, roads, cellScale, heightScale);
            }
        }

        /// <summary>
        /// Refresh road rendering. Call periodically to show new roads.
        /// </summary>
        public void RefreshRoads()
        {
            roadRenderer?.RefreshRoads();
        }

        private void InitializeSelectionHighlight()
        {
            if (mapData == null) return;

            // Create or get SelectionHighlight component
            selectionHighlight = GetComponent<SelectionHighlight>();
            if (selectionHighlight == null)
            {
                selectionHighlight = gameObject.AddComponent<SelectionHighlight>();
            }

            // Set the mapView reference via reflection (or make it public/serialized)
            var mapViewField = typeof(SelectionHighlight).GetField("mapView",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            mapViewField?.SetValue(selectionHighlight, this);

            // Set the outline material (reuse border material if available)
            if (borderMaterial != null)
            {
                var materialField = typeof(SelectionHighlight).GetField("outlineMaterial",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                materialField?.SetValue(selectionHighlight, borderMaterial);
            }

            selectionHighlight.Initialize(mapData, cellScale, heightScale);
        }

        public void SetMapMode(MapMode mode)
        {
            if (currentMode != mode)
            {
                currentMode = mode;
                UpdateColors();

                if (useShaderOverlays && overlayManager != null)
                {
                    overlayManager.SetMapMode(mode);
                    UpdateOverlayVisibility();
                }
                else
                {
                    UpdateBorderVisibility();
                }
            }
        }

        private void UpdateOverlayVisibility()
        {
            if (overlayManager == null) return;

            switch (currentMode)
            {
                case MapMode.Political:
                    overlayManager.SetStateBordersVisible(true);
                    overlayManager.SetProvinceBordersVisible(false);
                    overlayManager.SetMarketBordersVisible(false);
                    break;
                case MapMode.Province:
                    overlayManager.SetStateBordersVisible(true);
                    overlayManager.SetProvinceBordersVisible(true);
                    overlayManager.SetMarketBordersVisible(false);
                    break;
                case MapMode.County:
                    overlayManager.SetStateBordersVisible(true);
                    overlayManager.SetProvinceBordersVisible(true);
                    overlayManager.SetMarketBordersVisible(false);
                    break;
                case MapMode.Terrain:
                case MapMode.Height:
                    overlayManager.SetStateBordersVisible(false);
                    overlayManager.SetProvinceBordersVisible(false);
                    overlayManager.SetMarketBordersVisible(false);
                    break;
                case MapMode.Market:
                    overlayManager.SetStateBordersVisible(false);
                    overlayManager.SetProvinceBordersVisible(false);
                    overlayManager.SetMarketBordersVisible(true);
                    break;
            }
        }

        private void UpdateBorderVisibility()
        {
            if (borderRenderer == null) return;

            switch (currentMode)
            {
                case MapMode.Political:
                    borderRenderer.SetStateBordersVisible(true);
                    borderRenderer.SetProvinceBordersVisible(false);
                    break;
                case MapMode.Province:
                    borderRenderer.SetStateBordersVisible(true);
                    borderRenderer.SetProvinceBordersVisible(true);
                    break;
                case MapMode.County:
                    // Show province borders to group counties visually
                    borderRenderer.SetStateBordersVisible(true);
                    borderRenderer.SetProvinceBordersVisible(true);
                    break;
                case MapMode.Terrain:
                case MapMode.Height:
                    borderRenderer.SetStateBordersVisible(false);
                    borderRenderer.SetProvinceBordersVisible(false);
                    break;
                case MapMode.Market:
                    // Hide political borders, market zones speak for themselves
                    borderRenderer.SetStateBordersVisible(false);
                    borderRenderer.SetProvinceBordersVisible(false);
                    break;
            }
        }

        public void SetStateBordersVisible(bool visible)
        {
            if (useShaderOverlays && overlayManager != null)
            {
                overlayManager.SetStateBordersVisible(visible);
            }
            else if (borderRenderer != null)
            {
                borderRenderer.SetStateBordersVisible(visible);
            }
        }

        public void SetProvinceBordersVisible(bool visible)
        {
            if (useShaderOverlays && overlayManager != null)
            {
                overlayManager.SetProvinceBordersVisible(visible);
            }
            else if (borderRenderer != null)
            {
                borderRenderer.SetProvinceBordersVisible(visible);
            }
        }

        /// <summary>
        /// Compute height for each vertex by averaging the heights of all cells that share it.
        /// This creates smooth terrain instead of disconnected plateaus.
        /// </summary>
        private void ComputeVertexHeights()
        {
            int vertexCount = mapData.Vertices.Count;
            vertexHeights = new float[vertexCount];
            int[] vertexCellCounts = new int[vertexCount];

            // Accumulate heights from all cells that use each vertex
            foreach (var cell in mapData.Cells)
            {
                if (cell.VertexIndices == null) continue;

                float cellHeight = GetCellHeight(cell);

                foreach (int vIdx in cell.VertexIndices)
                {
                    if (vIdx >= 0 && vIdx < vertexCount)
                    {
                        vertexHeights[vIdx] += cellHeight;
                        vertexCellCounts[vIdx]++;
                    }
                }
            }

            // Average the heights
            for (int i = 0; i < vertexCount; i++)
            {
                if (vertexCellCounts[i] > 0)
                {
                    vertexHeights[i] /= vertexCellCounts[i];
                }
            }

            Debug.Log($"Computed heights for {vertexCount} vertices");
        }

        private void GenerateMesh()
        {
            if (mapData == null)
            {
                Debug.LogError("MapView: No map data to render");
                return;
            }

            Debug.Log($"Generating mesh for {mapData.Cells.Count} cells...");

            // Compute per-vertex heights by averaging neighboring cells
            ComputeVertexHeights();

            vertices.Clear();
            triangles.Clear();
            colors.Clear();
            uv1.Clear();

            // Generate triangulated polygons for each cell
            int cellsRendered = 0;
            foreach (var cell in mapData.Cells)
            {
                if (renderLandOnly && !cell.IsLand)
                    continue;

                GenerateCellMesh(cell);
                cellsRendered++;
            }

            Debug.Log($"Generated mesh: {vertices.Count} vertices, {triangles.Count / 3} triangles, {cellsRendered} cells");

            // Create or update mesh
            if (mesh == null)
            {
                mesh = new Mesh();
                mesh.name = "MapMesh";
            }
            mesh.Clear();

            // Use 32-bit indices for large meshes
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.SetColors(colors);

            // Set UV1 for shader overlay data texture sampling
            if (useShaderOverlays && uv1.Count == vertices.Count)
            {
                mesh.SetUVs(1, uv1);
            }

            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            meshFilter.mesh = mesh;

            if (terrainMaterial != null)
            {
                meshRenderer.material = terrainMaterial;
            }

            // Center the map
            CenterMap();
        }

        private void GenerateCellMesh(Cell cell)
        {
            if (cell.VertexIndices == null || cell.VertexIndices.Count < 3)
                return;

            // Get cell color based on current map mode
            Color32 cellColor = GetCellColor(cell);

            // Get vertex positions for this cell's polygon, using per-vertex heights
            var polyVerts = new List<Vector3>();
            var polyUVs = new List<Vector2>();  // Azgaar coordinates normalized for data texture
            float heightSum = 0f;

            // Normalization factors for UV1 (Azgaar coords -> 0-1)
            float invWidth = 1f / mapData.Info.Width;
            float invHeight = 1f / mapData.Info.Height;

            foreach (int vIdx in cell.VertexIndices)
            {
                if (vIdx >= 0 && vIdx < mapData.Vertices.Count)
                {
                    Vector2 pos2D = mapData.Vertices[vIdx].ToUnity();
                    float height = vertexHeights[vIdx];
                    heightSum += height;
                    polyVerts.Add(new Vector3(
                        pos2D.x * cellScale,
                        height,
                        -pos2D.y * cellScale  // Flip Y to Z, negate for Unity coords
                    ));

                    // UV1: normalized Azgaar coordinates for data texture sampling
                    polyUVs.Add(new Vector2(pos2D.x * invWidth, pos2D.y * invHeight));
                }
            }

            if (polyVerts.Count < 3)
                return;

            // Fan triangulation from cell center
            Vector3 center = Vector3.zero;
            Vector2 centerUV = Vector2.zero;
            foreach (var v in polyVerts)
                center += v;
            foreach (var uv in polyUVs)
                centerUV += uv;
            center /= polyVerts.Count;
            centerUV /= polyUVs.Count;

            // Center height is average of edge vertex heights (already computed in center.y from the sum)
            // No need to adjust - it's already correct from averaging polyVerts

            int centerIdx = vertices.Count;
            vertices.Add(center);
            colors.Add(cellColor);
            uv1.Add(centerUV);

            // Add polygon vertices and create triangles
            int firstPolyIdx = vertices.Count;
            for (int i = 0; i < polyVerts.Count; i++)
            {
                vertices.Add(polyVerts[i]);
                colors.Add(cellColor);
                uv1.Add(polyUVs[i]);
            }

            // Create fan triangles
            for (int i = 0; i < polyVerts.Count; i++)
            {
                int next = (i + 1) % polyVerts.Count;
                triangles.Add(centerIdx);
                triangles.Add(firstPolyIdx + i);
                triangles.Add(firstPolyIdx + next);
            }
        }

        private float GetCellHeight(Cell cell)
        {
            // Convert height (0-100, sea level 20) to world units
            float normalizedHeight = (cell.Height - mapData.Info.SeaLevel) / 80f;  // -0.25 to 1.0
            return normalizedHeight * heightScale;
        }

        private Color32 GetCellColor(Cell cell)
        {
            switch (currentMode)
            {
                case MapMode.Political:
                    return GetPoliticalColor(cell);
                case MapMode.Province:
                    return GetProvinceColor(cell);
                case MapMode.County:
                    return GetCountyColor(cell);
                case MapMode.Terrain:
                    return GetTerrainColor(cell);
                case MapMode.Height:
                    return GetHeightColor(cell);
                case MapMode.Market:
                    return GetMarketColor(cell);
                default:
                    return new Color32(128, 128, 128, 255);
            }
        }

        private Color32 GetPoliticalColor(Cell cell)
        {
            // Water cells
            if (!cell.IsLand)
            {
                return GetWaterColor(cell);
            }

            if (cell.StateId > 0 && mapData.StateById.TryGetValue(cell.StateId, out var state))
            {
                return state.Color.ToUnity();
            }
            return new Color32(200, 200, 200, 255);  // Neutral/unclaimed
        }

        private Color32 GetProvinceColor(Cell cell)
        {
            // Water cells
            if (!cell.IsLand)
            {
                return GetWaterColor(cell);
            }

            if (cell.ProvinceId > 0 && mapData.ProvinceById.TryGetValue(cell.ProvinceId, out var province))
            {
                return province.Color.ToUnity();
            }
            return GetPoliticalColor(cell);  // Fall back to state color
        }

        private Color32 GetCountyColor(Cell cell)
        {
            // Water cells
            if (!cell.IsLand)
            {
                return GetWaterColor(cell);
            }

            // Get base color from province (or state if no province)
            Color baseColor;
            if (cell.ProvinceId > 0 && mapData.ProvinceById.TryGetValue(cell.ProvinceId, out var province))
            {
                baseColor = province.Color.ToUnity();
            }
            else if (cell.StateId > 0 && mapData.StateById.TryGetValue(cell.StateId, out var state))
            {
                baseColor = state.Color.ToUnity();
            }
            else
            {
                baseColor = new Color(0.78f, 0.78f, 0.78f);  // Neutral gray
            }

            // Convert to HSV
            Color.RGBToHSV(baseColor, out float h, out float s, out float v);

            // Use cell ID hash to vary saturation and value
            uint hash = (uint)(cell.Id * 2654435761L);
            float satVar = ((hash & 0xFF) / 255f - 0.5f) * 0.3f;        // ±0.15
            float valVar = (((hash >> 8) & 0xFF) / 255f - 0.5f) * 0.3f; // ±0.15

            s = Mathf.Clamp01(s + satVar);
            v = Mathf.Clamp01(v + valVar);

            Color result = Color.HSVToRGB(h, s, v);
            return new Color32(
                (byte)(result.r * 255),
                (byte)(result.g * 255),
                (byte)(result.b * 255),
                255
            );
        }

        private Color32 GetTerrainColor(Cell cell)
        {
            // Water cells - use water colors instead of biome
            if (!cell.IsLand)
            {
                return GetWaterColor(cell);
            }

            if (cell.BiomeId >= 0 && cell.BiomeId < mapData.Biomes.Count)
            {
                return mapData.Biomes[cell.BiomeId].Color.ToUnity();
            }
            return new Color32(100, 100, 100, 255);
        }

        private Color32 GetHeightColor(Cell cell)
        {
            // Gradient from blue (low) to green (mid) to brown (high) to white (peaks)
            float t = cell.Height / 100f;

            if (t < 0.2f)  // Water
            {
                float waterT = t / 0.2f;
                return Color32.Lerp(
                    new Color32(0, 30, 80, 255),    // Deep water
                    new Color32(50, 100, 150, 255), // Shallow water
                    waterT
                );
            }
            else if (t < 0.4f)  // Lowlands
            {
                float landT = (t - 0.2f) / 0.2f;
                return Color32.Lerp(
                    new Color32(80, 160, 80, 255),  // Coastal green
                    new Color32(120, 180, 80, 255), // Grassland
                    landT
                );
            }
            else if (t < 0.7f)  // Hills
            {
                float hillT = (t - 0.4f) / 0.3f;
                return Color32.Lerp(
                    new Color32(120, 180, 80, 255), // Grassland
                    new Color32(139, 119, 101, 255), // Brown hills
                    hillT
                );
            }
            else  // Mountains
            {
                float mtT = (t - 0.7f) / 0.3f;
                return Color32.Lerp(
                    new Color32(139, 119, 101, 255), // Brown
                    new Color32(240, 240, 250, 255), // Snow caps
                    mtT
                );
            }
        }

        private Color32 GetMarketColor(Cell cell)
        {
            // Water cells
            if (!cell.IsLand)
            {
                return GetWaterColor(cell);
            }

            if (economyState == null)
            {
                return new Color32(100, 100, 100, 255);  // Gray - no economy data
            }

            if (economyState.CellToMarket.TryGetValue(cell.Id, out int marketId))
            {
                // Check if this cell is in the market hub's province (highlights the whole province)
                if (IsInMarketHubProvince(cell, out int hubMarketId))
                {
                    return MarketHubColor(hubMarketId);
                }
                return MarketIdToColor(marketId);
            }

            // Cell not assigned to any market - dim gray
            return new Color32(60, 60, 60, 255);
        }

        /// <summary>
        /// Get color for water cells based on feature type (ocean vs lake).
        /// </summary>
        private Color32 GetWaterColor(Cell cell)
        {
            // Check feature type to distinguish ocean from lake
            if (mapData.FeatureById != null && mapData.FeatureById.TryGetValue(cell.FeatureId, out var feature))
            {
                if (feature.IsLake)
                {
                    // Lakes - lighter cyan-blue
                    return new Color32(70, 130, 180, 255);  // Steel blue
                }
            }

            // Default ocean color - deep blue, varies slightly by depth
            float depthFactor = Mathf.Clamp01((20 - cell.Height) / 20f);  // 0 at sea level, 1 at deepest
            return Color32.Lerp(
                new Color32(50, 100, 150, 255),   // Shallow ocean
                new Color32(20, 50, 100, 255),    // Deep ocean
                depthFactor
            );
        }

        /// <summary>
        /// Check if a cell is a market hub (the cell where the market is located).
        /// </summary>
        public bool IsMarketHub(int cellId)
        {
            if (economyState?.Markets == null) return false;
            foreach (var market in economyState.Markets.Values)
            {
                if (market.LocationCellId == cellId) return true;
            }
            return false;
        }

        /// <summary>
        /// Check if a cell is in the same province as any market hub.
        /// Returns true and the market ID if so.
        /// </summary>
        private bool IsInMarketHubProvince(Cell cell, out int marketId)
        {
            marketId = 0;
            if (economyState?.Markets == null || mapData == null) return false;

            foreach (var market in economyState.Markets.Values)
            {
                if (mapData.CellById.TryGetValue(market.LocationCellId, out var hubCell))
                {
                    // Same province as market hub
                    if (cell.ProvinceId > 0 && cell.ProvinceId == hubCell.ProvinceId)
                    {
                        marketId = market.Id;
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Get the market located at a cell, or null if none.
        /// </summary>
        public EconSim.Core.Economy.Market GetMarketAtCell(int cellId)
        {
            // Invalid cell ID (e.g., clicked on ocean)
            if (cellId < 0) return null;
            if (economyState?.Markets == null) return null;
            foreach (var market in economyState.Markets.Values)
            {
                if (market.LocationCellId == cellId) return market;
            }
            return null;
        }

        // Predefined market zone colors - distinct, muted pastels for large areas
        private static readonly Color32[] MarketZoneColors = new Color32[]
        {
            new Color32(100, 149, 237, 255),  // Cornflower blue
            new Color32(144, 238, 144, 255),  // Light green
            new Color32(255, 182, 108, 255),  // Light orange
            new Color32(221, 160, 221, 255),  // Plum
            new Color32(240, 230, 140, 255),  // Khaki
            new Color32(127, 255, 212, 255),  // Aquamarine
            new Color32(255, 160, 122, 255),  // Light salmon
            new Color32(176, 196, 222, 255),  // Light steel blue
        };

        // Predefined market hub colors - vivid, high-contrast versions
        private static readonly Color32[] MarketHubColors = new Color32[]
        {
            new Color32(0, 71, 171, 255),     // Cobalt blue
            new Color32(0, 128, 0, 255),      // Green
            new Color32(255, 140, 0, 255),    // Dark orange
            new Color32(148, 0, 211, 255),    // Dark violet
            new Color32(184, 134, 11, 255),   // Dark goldenrod
            new Color32(0, 139, 139, 255),    // Dark cyan
            new Color32(220, 20, 60, 255),    // Crimson
            new Color32(70, 130, 180, 255),   // Steel blue
        };

        /// <summary>
        /// Get zone color for a market. Uses predefined palette for clarity.
        /// </summary>
        private Color32 MarketIdToColor(int marketId)
        {
            int index = (marketId - 1) % MarketZoneColors.Length;
            if (index < 0) index = 0;
            return MarketZoneColors[index];
        }

        /// <summary>
        /// Get hub color for a market. Much more vivid than zone color for contrast.
        /// </summary>
        private Color32 MarketHubColor(int marketId)
        {
            int index = (marketId - 1) % MarketHubColors.Length;
            if (index < 0) index = 0;
            return MarketHubColors[index];
        }

        private void CenterMap()
        {
            if (mapData == null) return;

            // Position the map so it's centered at origin
            float halfWidth = mapData.Info.Width * cellScale * 0.5f;
            float halfHeight = mapData.Info.Height * cellScale * 0.5f;

            transform.position = new Vector3(-halfWidth, 0, halfHeight);
        }

        private void UpdateColors()
        {
            if (mesh == null || mapData == null) return;

            colors.Clear();

            int vertexIndex = 0;
            foreach (var cell in mapData.Cells)
            {
                if (renderLandOnly && !cell.IsLand)
                    continue;

                if (cell.VertexIndices == null || cell.VertexIndices.Count < 3)
                    continue;

                Color32 cellColor = GetCellColor(cell);

                // Center vertex + polygon vertices
                int numVerts = 1 + cell.VertexIndices.Count;
                for (int i = 0; i < numVerts; i++)
                {
                    colors.Add(cellColor);
                }
            }

            mesh.SetColors(colors);
        }

        /// <summary>
        /// Initialize economy state reference for market map mode.
        /// Call this after economy initialization.
        /// </summary>
        public void SetEconomyState(EconSim.Core.Economy.EconomyState economy)
        {
            economyState = economy;

            // Forward to overlay manager for market zone visualization
            if (overlayManager != null)
            {
                overlayManager.SetEconomyState(economy);
            }
        }

        /// <summary>
        /// Get the world-space bounds of the land mass (rendered cells only).
        /// Returns the mesh bounds which represents the actual land area.
        /// </summary>
        public Bounds GetLandBounds()
        {
            if (mesh != null)
            {
                // Mesh bounds are in local space, transform to world space
                var localBounds = mesh.bounds;
                return new Bounds(
                    transform.TransformPoint(localBounds.center),
                    localBounds.size  // Size doesn't change for uniform scale
                );
            }

            // Fallback to full map bounds if mesh not ready
            float width = mapData?.Info.Width * cellScale ?? 14.4f;
            float height = mapData?.Info.Height * cellScale ?? 8.1f;
            return new Bounds(Vector3.zero, new Vector3(width, 0.1f, height));
        }

#if UNITY_EDITOR
        [ContextMenu("Regenerate Mesh")]
        private void RegenerateMesh()
        {
            if (mapData != null)
            {
                GenerateMesh();
            }
        }

        [ContextMenu("Set Mode: Political")]
        private void SetModePolitical() => SetMapMode(MapMode.Political);

        [ContextMenu("Set Mode: Province")]
        private void SetModeProvince() => SetMapMode(MapMode.Province);

        [ContextMenu("Set Mode: County")]
        private void SetModeCounty() => SetMapMode(MapMode.County);

        [ContextMenu("Set Mode: Terrain")]
        private void SetModeTerrain() => SetMapMode(MapMode.Terrain);

        [ContextMenu("Set Mode: Height")]
        private void SetModeHeight() => SetMapMode(MapMode.Height);

        [ContextMenu("Set Mode: Market")]
        private void SetModeMarket() => SetMapMode(MapMode.Market);

        [ContextMenu("Toggle State Borders")]
        private void ToggleStateBorders()
        {
            if (borderRenderer != null)
            {
                var field = typeof(BorderRenderer).GetField("showStateBorders",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (field != null)
                {
                    bool current = (bool)field.GetValue(borderRenderer);
                    borderRenderer.SetStateBordersVisible(!current);
                }
            }
        }

        [ContextMenu("Toggle Province Borders")]
        private void ToggleProvinceBorders()
        {
            if (borderRenderer != null)
            {
                var field = typeof(BorderRenderer).GetField("showProvinceBorders",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (field != null)
                {
                    bool current = (bool)field.GetValue(borderRenderer);
                    borderRenderer.SetProvinceBordersVisible(!current);
                }
            }
        }
#endif
    }
}
