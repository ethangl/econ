using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using EconSim.Core.Data;
using EconSim.Bridge;
using EconSim.Camera;
using Profiler = EconSim.Core.Common.StartupProfiler;

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

        // Map mode (not serialized - always starts in Political per CLAUDE.md guidance)
        private MapMode currentMode = MapMode.Political;

        [Header("Shader Overlays")]
        private bool useShaderOverlays = true;
        [SerializeField] [Range(1, 8)] private int overlayResolutionMultiplier = 6;  // Higher = smoother borders, more memory

        // Grid mesh with height displacement (Phase 6c)
        // Non-serialized during development - see CLAUDE.md
        private bool useGridMesh = true;
        private int gridDivisor = 1;  // 1 = full source resolution, 2 = half, etc.
        private float gridHeightScale = 3f;

        [Header("Borders")]
        private bool enableBorders = true;
        [SerializeField] private Material borderMaterial;

        [Header("Selection")]
        [SerializeField] private UnityEngine.Camera selectionCamera;
        [SerializeField] private MapCamera mapCameraController;

        [Header("Selection Settings")]
        [SerializeField] [Range(0f, 1f)] private float selectionDimmingTarget = 0.5f;
        [SerializeField] [Range(0f, 1f)] private float selectionDesaturationTarget = 0.8f;
        [SerializeField] private float dimmingAnimationSpeed = 8f;

        // Animation state (not serialized)
        private float currentSelectionDimming = 1f;
        private float currentSelectionDesaturation = 0f;
        private bool hasActiveSelection;
        private float currentHoverIntensity = 0f;
        private bool hasActiveHover;

        /// <summary>Event fired when a cell is clicked. Passes cell ID (-1 if clicked on nothing).</summary>
        public event Action<int> OnCellClicked;

        /// <summary>Event fired after selection changes. Passes the selection depth.</summary>
        public event Action<SelectionDepth> OnSelectionChanged;

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
        private RelaxedCellGeometry relaxedGeometry;

        // Cell mesh data
        private List<Vector3> vertices = new List<Vector3>();
        private List<int> triangles = new List<int>();
        private List<Color32> colors = new List<Color32>();
        private List<Vector2> uv0 = new List<Vector2>();  // Heightmap UVs (Unity coords, Y-flipped)
        private List<Vector2> uv1 = new List<Vector2>();  // Data texture UVs (Azgaar coords normalized)

        // Vertex heights (computed by averaging neighboring cells)
        private float[] vertexHeights;

        // Drill-down selection state
        public enum SelectionDepth { None, State, Province, County }
        private SelectionDepth selectionDepth = SelectionDepth.None;
        private int selectedStateId = -1;
        private int selectedProvinceId = -1;
        private int selectedCountyId = -1;

        public SelectionDepth CurrentSelectionDepth => selectionDepth;
        public int SelectedStateId => selectedStateId;
        public int SelectedProvinceId => selectedProvinceId;
        public int SelectedCountyId => selectedCountyId;

        public enum MapMode
        {
            Political,  // Colored by state/country (key: 1, cycles with Province/County)
            Province,   // Colored by province (key: 1, cycles with Political/County)
            County,     // Colored by county/cell (key: 1, cycles with Political/Province)
            Terrain,    // Colored by biome with elevation tinting (key: 2)
            Market      // Colored by market zone (key: 3)
        }

        public MapMode CurrentMode => currentMode;
        public string CurrentModeName => ModeNames[(int)currentMode];

        private static readonly string[] ModeNames = { "Political", "Province", "County", "Terrain", "Market" };

        private void Awake()
        {
            meshFilter = GetComponent<MeshFilter>();
            meshRenderer = GetComponent<MeshRenderer>();
        }

        private void OnValidate()
        {
            // selectionDimmingTarget changes take effect through the animation loop
        }

        private void OnDestroy()
        {
            // Unsubscribe from shader selection
            OnCellClicked -= HandleShaderSelection;

            // Clean up overlay manager textures
            overlayManager?.Dispose();
        }

        private void Update()
        {
            // Map mode selection with number keys 1-3
            if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1))
            {
                // Key 1 switches to Political mode (drill-down handles province/county)
                SetMapMode(MapMode.Political);
                Debug.Log("Map mode: Political (1)");
            }
            else if (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2))
            {
                SetMapMode(MapMode.Terrain);
                Debug.Log("Map mode: Terrain (2)");
            }
            else if (Input.GetKeyDown(KeyCode.Alpha3) || Input.GetKeyDown(KeyCode.Keypad3))
            {
                SetMapMode(MapMode.Market);
                Debug.Log("Map mode: Market (3)");
            }

            // Update hover state (but not when panning or over UI)
            if (mapCameraController == null || !mapCameraController.IsPanningMode)
            {
                if (!IsPointerOverUI())
                {
                    UpdateHover();
                }
                else
                {
                    // Clear hover when over UI (animation will fade out)
                    hasActiveHover = false;
                }
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

            // Animate selection dimming
            UpdateDimmingAnimation();
        }

        private void SetSelectionActive(bool active)
        {
            hasActiveSelection = active;
        }

        private void UpdateDimmingAnimation()
        {
            if (overlayManager == null) return;

            float dt = Time.deltaTime * dimmingAnimationSpeed;

            // Animate toward target (selectionDimmingTarget when selected, 1 when not)
            float dimmingTarget = hasActiveSelection ? selectionDimmingTarget : 1f;
            currentSelectionDimming = Mathf.Lerp(currentSelectionDimming, dimmingTarget, dt);
            overlayManager.SetSelectionDimming(currentSelectionDimming);

            // Animate desaturation (0 = full color, target when selected)
            float desatTarget = hasActiveSelection ? selectionDesaturationTarget : 0f;
            currentSelectionDesaturation = Mathf.Lerp(currentSelectionDesaturation, desatTarget, dt);
            overlayManager.SetSelectionDesaturation(currentSelectionDesaturation);

            // Animate hover intensity (1 when hovered, 0 when not)
            float hoverTarget = hasActiveHover ? 1f : 0f;
            currentHoverIntensity = Mathf.Lerp(currentHoverIntensity, hoverTarget, dt);
            overlayManager.SetHoverIntensity(currentHoverIntensity);

            // Only clear hover IDs once intensity has fully faded out
            if (!hasActiveHover && currentHoverIntensity < 0.01f)
            {
                overlayManager.ClearHover();
            }
        }

        private void UpdateHover()
        {
            if (overlayManager == null || mapData == null) return;

            var cam = selectionCamera != null ? selectionCamera : UnityEngine.Camera.main;
            if (cam == null) return;

            // Raycast to ground plane
            Ray ray = cam.ScreenPointToRay(Input.mousePosition);
            Plane groundPlane = new Plane(Vector3.up, Vector3.zero);

            if (!groundPlane.Raycast(ray, out float distance))
            {
                hasActiveHover = false;
                return;
            }

            Vector3 hitPoint = ray.GetPoint(distance);
            int cellId = FindCellAtPosition(hitPoint);

            if (cellId < 0)
            {
                hasActiveHover = false;
                return;
            }

            if (!mapData.CellById.TryGetValue(cellId, out var cell) || !cell.IsLand)
            {
                hasActiveHover = false;
                return;
            }

            // Set hover based on current map mode
            hasActiveHover = true;
            switch (currentMode)
            {
                case MapMode.Political:
                    overlayManager.SetHoveredState(cell.StateId);
                    break;
                case MapMode.Province:
                    overlayManager.SetHoveredProvince(cell.ProvinceId);
                    break;
                case MapMode.Market:
                    if (economyState != null && economyState.CellToMarket.TryGetValue(cellId, out int marketId))
                    {
                        overlayManager.SetHoveredMarket(marketId);
                    }
                    else
                    {
                        hasActiveHover = false;
                    }
                    break;
                case MapMode.County:
                case MapMode.Terrain:
                default:
                    overlayManager.SetHoveredCounty(cell.CountyId);
                    break;
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

            Profiler.Begin("GenerateMesh");
            GenerateMesh();
            Profiler.End();

            Profiler.Begin("InitializeRelaxedGeometry");
            InitializeRelaxedGeometry();
            Profiler.End();

            Profiler.Begin("InitializeOverlays");
            InitializeOverlays();
            Profiler.End();

            Profiler.Begin("InitializeBorders");
            InitializeBorders();
            Profiler.End();

            // InitializeRivers();  // Disabled - rivers now rendered via shader mask (Phase 8)

            Profiler.Begin("InitializeRoads");
            InitializeRoads();
            Profiler.End();

            Profiler.Begin("InitializeSelectionHighlight");
            InitializeSelectionHighlight();
            Profiler.End();
        }

        private void InitializeRelaxedGeometry()
        {
            if (mapData == null) return;

            // Build relaxed geometry for organic curved borders
            relaxedGeometry = new RelaxedCellGeometry
            {
                Amplitude = 1.2f,      // Perpendicular wobble distance (map units)
                Frequency = 0.36f,     // Control points per map unit
                SamplesPerSegment = 5  // Catmull-Rom smoothness
            };
            relaxedGeometry.Build(mapData);
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

            overlayManager = new MapOverlayManager(mapData, relaxedGeometry, mat, overlayResolutionMultiplier);

            // Height displacement is disabled (elevation is now shown via biome-elevation tinting)
            overlayManager.SetHeightDisplacementEnabled(false);

            // Sync shader mode with current map mode
            overlayManager.SetMapMode(currentMode);
            UpdateOverlayVisibility();
        }

        private void InitializeBorders()
        {
            if (!enableBorders || mapData == null || relaxedGeometry == null) return;

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

            borderRenderer.Initialize(mapData, relaxedGeometry, cellScale, heightScale);

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

            // Use shader-based selection when overlays are enabled
            if (useShaderOverlays && overlayManager != null)
            {
                // Subscribe to cell clicks for shader selection
                OnCellClicked += HandleShaderSelection;

                // Destroy any existing SelectionHighlight component (legacy)
                var oldHighlight = GetComponent<SelectionHighlight>();
                if (oldHighlight != null)
                {
                    Destroy(oldHighlight);
                }
                return;
            }

            // Legacy: Create or get SelectionHighlight component for non-shader mode
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

        private void HandleShaderSelection(int cellId)
        {
            if (overlayManager == null) return;

            // Clear selection if clicked on nothing
            if (cellId < 0)
            {
                ClearDrillDownSelection();
                return;
            }

            // Get the cell to look up its state/province
            if (!mapData.CellById.TryGetValue(cellId, out var cell))
            {
                ClearDrillDownSelection();
                return;
            }

            // Water cells have no meaningful state/province/county - clear selection
            if (!cell.IsLand)
            {
                ClearDrillDownSelection();
                return;
            }

            // Market mode has its own selection logic (no drill-down)
            if (currentMode == MapMode.Market)
            {
                HandleMarketSelection(cell, cellId);
                return;
            }

            // Terrain mode - just select county, no drill-down
            if (currentMode == MapMode.Terrain)
            {
                SelectAtDepth(SelectionDepth.County, cell);
                return;
            }

            // Political modes (Political, Province, County) - use drill-down logic
            HandleDrillDownSelection(cell);
        }

        /// <summary>
        /// Drill-down selection: clicking same entity drills deeper, clicking outside resets.
        /// </summary>
        private void HandleDrillDownSelection(Cell cell)
        {
            Bounds? selectionBounds = null;

            switch (selectionDepth)
            {
                case SelectionDepth.None:
                    // No selection yet - select state
                    SelectAtDepth(SelectionDepth.State, cell);
                    selectionBounds = GetStateBounds(cell.StateId);
                    break;

                case SelectionDepth.State:
                    if (cell.StateId == selectedStateId)
                    {
                        // Clicking same state - drill down to province
                        SelectAtDepth(SelectionDepth.Province, cell);
                        selectionBounds = GetProvinceBounds(cell.ProvinceId);
                    }
                    else
                    {
                        // Clicking different state - select new state
                        SelectAtDepth(SelectionDepth.State, cell);
                        selectionBounds = GetStateBounds(cell.StateId);
                    }
                    break;

                case SelectionDepth.Province:
                    if (cell.ProvinceId == selectedProvinceId)
                    {
                        // Clicking same province - drill down to county
                        SelectAtDepth(SelectionDepth.County, cell);
                        selectionBounds = GetCountyBounds(cell.CountyId);
                    }
                    else if (cell.StateId == selectedStateId)
                    {
                        // Clicking different province in same state - select new province
                        SelectAtDepth(SelectionDepth.Province, cell);
                        selectionBounds = GetProvinceBounds(cell.ProvinceId);
                    }
                    else
                    {
                        // Clicking different state - reset to state level
                        SelectAtDepth(SelectionDepth.State, cell);
                        selectionBounds = GetStateBounds(cell.StateId);
                    }
                    break;

                case SelectionDepth.County:
                    if (cell.CountyId == selectedCountyId)
                    {
                        // Already at deepest level, clicking same county - do nothing
                        return;
                    }
                    else if (cell.ProvinceId == selectedProvinceId)
                    {
                        // Clicking different county in same province - select new county
                        SelectAtDepth(SelectionDepth.County, cell);
                        selectionBounds = GetCountyBounds(cell.CountyId);
                    }
                    else if (cell.StateId == selectedStateId)
                    {
                        // Clicking different province in same state - go back to province level
                        SelectAtDepth(SelectionDepth.Province, cell);
                        selectionBounds = GetProvinceBounds(cell.ProvinceId);
                    }
                    else
                    {
                        // Clicking different state - reset to state level
                        SelectAtDepth(SelectionDepth.State, cell);
                        selectionBounds = GetStateBounds(cell.StateId);
                    }
                    break;
            }

            // Zoom and pan camera to frame selection
            if (selectionBounds.HasValue && mapCameraController != null)
            {
                mapCameraController.FocusOnBounds(selectionBounds.Value);
            }
        }

        /// <summary>
        /// Select at a specific depth and update shader uniforms.
        /// </summary>
        private void SelectAtDepth(SelectionDepth depth, Cell cell)
        {
            selectionDepth = depth;
            selectedStateId = cell.StateId;
            selectedProvinceId = cell.ProvinceId;
            selectedCountyId = cell.CountyId;
            SetSelectionActive(true);

            // Update shader selection based on depth
            overlayManager.ClearSelection();
            switch (depth)
            {
                case SelectionDepth.State:
                    overlayManager.SetSelectedState(cell.StateId);
                    break;
                case SelectionDepth.Province:
                    overlayManager.SetSelectedProvince(cell.ProvinceId);
                    break;
                case SelectionDepth.County:
                    overlayManager.SetSelectedCounty(cell.CountyId);
                    break;
            }

            // Update border visibility to match selection depth
            UpdateBordersForSelectionDepth();

            // Notify listeners
            OnSelectionChanged?.Invoke(depth);
        }

        /// <summary>
        /// Update border visibility based on current selection depth.
        /// </summary>
        private void UpdateBordersForSelectionDepth()
        {
            // Shader-based borders disabled - using mesh-based BorderRenderer
        }

        /// <summary>
        /// Clear drill-down selection state.
        /// </summary>
        private void ClearDrillDownSelection()
        {
            selectionDepth = SelectionDepth.None;
            selectedStateId = -1;
            selectedProvinceId = -1;
            selectedCountyId = -1;
            SetSelectionActive(false);
            overlayManager?.ClearSelection();
            UpdateOverlayVisibility();  // Restore borders based on map mode

            // Notify listeners
            OnSelectionChanged?.Invoke(SelectionDepth.None);
        }

        /// <summary>
        /// Handle market mode selection (no drill-down).
        /// </summary>
        private void HandleMarketSelection(Cell cell, int cellId)
        {
            Bounds? selectionBounds = null;

            if (economyState != null && economyState.CellToMarket.TryGetValue(cellId, out int marketId))
            {
                SetSelectionActive(true);
                overlayManager.SetSelectedMarket(marketId);
                selectionBounds = GetMarketBounds(marketId);
            }
            else
            {
                SetSelectionActive(false);
                overlayManager.ClearSelection();
            }

            if (selectionBounds.HasValue && mapCameraController != null)
            {
                mapCameraController.FocusOnBounds(selectionBounds.Value);
            }
        }

        /// <summary>
        /// Convert Azgaar coordinates to world position.
        /// </summary>
        private Vector3 AzgaarToWorld(float azgaarX, float azgaarY)
        {
            // World position matches mesh generation: X scaled, Z negated and scaled
            // transform.position offsets to center the map
            return new Vector3(
                azgaarX * cellScale + transform.position.x,
                0f,
                -azgaarY * cellScale + transform.position.z
            );
        }

        /// <summary>
        /// Get world-space centroid of a county.
        /// </summary>
        private Vector3? GetCountyCentroid(int countyId)
        {
            if (countyId <= 0) return null;
            if (!mapData.CountyById.TryGetValue(countyId, out var county)) return null;

            return AzgaarToWorld(county.Centroid.X, county.Centroid.Y);
        }

        /// <summary>
        /// Get world-space centroid of a province.
        /// </summary>
        private Vector3? GetProvinceCentroid(int provinceId)
        {
            if (provinceId <= 0) return null;
            if (!mapData.ProvinceById.TryGetValue(provinceId, out var province)) return null;

            // Use center cell if available
            if (province.CenterCellId > 0 && mapData.CellById.TryGetValue(province.CenterCellId, out var centerCell))
            {
                return AzgaarToWorld(centerCell.Center.X, centerCell.Center.Y);
            }

            // Fall back to calculating from cell list
            if (province.CellIds == null || province.CellIds.Count == 0) return null;

            float sumX = 0, sumY = 0;
            int count = 0;
            foreach (int cellId in province.CellIds)
            {
                if (mapData.CellById.TryGetValue(cellId, out var cell))
                {
                    sumX += cell.Center.X;
                    sumY += cell.Center.Y;
                    count++;
                }
            }
            if (count == 0) return null;

            return AzgaarToWorld(sumX / count, sumY / count);
        }

        /// <summary>
        /// Get world-space centroid of a state.
        /// </summary>
        private Vector3? GetStateCentroid(int stateId)
        {
            if (stateId <= 0) return null;
            if (!mapData.StateById.TryGetValue(stateId, out var state)) return null;

            // Use center cell if available
            if (state.CenterCellId > 0 && mapData.CellById.TryGetValue(state.CenterCellId, out var centerCell))
            {
                return AzgaarToWorld(centerCell.Center.X, centerCell.Center.Y);
            }

            // Fall back to calculating from all cells with this state ID
            float sumX = 0, sumY = 0;
            int count = 0;
            foreach (var cell in mapData.Cells)
            {
                if (cell.StateId == stateId && cell.IsLand)
                {
                    sumX += cell.Center.X;
                    sumY += cell.Center.Y;
                    count++;
                }
            }
            if (count == 0) return null;

            return AzgaarToWorld(sumX / count, sumY / count);
        }

        /// <summary>
        /// Get world-space centroid of a market zone.
        /// </summary>
        private Vector3? GetMarketCentroid(int marketId)
        {
            if (marketId <= 0 || economyState == null) return null;

            // Calculate from all cells assigned to this market
            float sumX = 0, sumY = 0;
            int count = 0;
            foreach (var kvp in economyState.CellToMarket)
            {
                if (kvp.Value == marketId && mapData.CellById.TryGetValue(kvp.Key, out var cell))
                {
                    sumX += cell.Center.X;
                    sumY += cell.Center.Y;
                    count++;
                }
            }
            if (count == 0) return null;

            return AzgaarToWorld(sumX / count, sumY / count);
        }

        /// <summary>
        /// Get world-space bounds of a county.
        /// </summary>
        private Bounds? GetCountyBounds(int countyId)
        {
            if (countyId <= 0) return null;
            if (!mapData.CountyById.TryGetValue(countyId, out var county)) return null;

            return GetCellListBounds(county.CellIds);
        }

        /// <summary>
        /// Get world-space bounds of a province.
        /// </summary>
        private Bounds? GetProvinceBounds(int provinceId)
        {
            if (provinceId <= 0) return null;
            if (!mapData.ProvinceById.TryGetValue(provinceId, out var province)) return null;

            return GetCellListBounds(province.CellIds);
        }

        /// <summary>
        /// Get world-space bounds of a state.
        /// </summary>
        private Bounds? GetStateBounds(int stateId)
        {
            if (stateId <= 0) return null;

            // Collect all cells belonging to this state
            var cellIds = new List<int>();
            foreach (var cell in mapData.Cells)
            {
                if (cell.StateId == stateId && cell.IsLand)
                {
                    cellIds.Add(cell.Id);
                }
            }

            return GetCellListBounds(cellIds);
        }

        /// <summary>
        /// Get world-space bounds of a market zone.
        /// </summary>
        private Bounds? GetMarketBounds(int marketId)
        {
            if (marketId <= 0 || economyState == null) return null;

            // Collect all cells assigned to this market
            var cellIds = new List<int>();
            foreach (var kvp in economyState.CellToMarket)
            {
                if (kvp.Value == marketId)
                {
                    cellIds.Add(kvp.Key);
                }
            }

            return GetCellListBounds(cellIds);
        }

        /// <summary>
        /// Calculate world-space bounds from a list of cell IDs.
        /// </summary>
        private Bounds? GetCellListBounds(IEnumerable<int> cellIds)
        {
            if (cellIds == null) return null;

            float minX = float.MaxValue, maxX = float.MinValue;
            float minY = float.MaxValue, maxY = float.MinValue;
            int count = 0;

            foreach (int cellId in cellIds)
            {
                if (mapData.CellById.TryGetValue(cellId, out var cell))
                {
                    minX = Mathf.Min(minX, cell.Center.X);
                    maxX = Mathf.Max(maxX, cell.Center.X);
                    minY = Mathf.Min(minY, cell.Center.Y);
                    maxY = Mathf.Max(maxY, cell.Center.Y);
                    count++;
                }
            }

            if (count == 0) return null;

            // Convert Azgaar corners to world space
            Vector3 worldMin = AzgaarToWorld(minX, maxY);  // maxY because Y is flipped
            Vector3 worldMax = AzgaarToWorld(maxX, minY);

            Vector3 center = (worldMin + worldMax) / 2f;
            Vector3 size = new Vector3(
                Mathf.Abs(worldMax.x - worldMin.x),
                0.1f,
                Mathf.Abs(worldMax.z - worldMin.z)
            );

            return new Bounds(center, size);
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

                    // Clear drill-down selection when changing modes
                    ClearDrillDownSelection();
                }
                else
                {
                    UpdateBorderVisibility();
                }
            }
        }

        private void UpdateOverlayVisibility()
        {
            // Shader-based borders disabled - using mesh-based BorderRenderer
        }

        private void UpdateBorderVisibility()
        {
            // Show all borders for now
        }

        public void SetProvinceBordersVisible(bool visible)
        {
            if (borderRenderer != null)
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

            if (useGridMesh && useShaderOverlays)
            {
                GenerateGridMesh();
            }
            else
            {
                GenerateVoronoiMesh();
            }
        }

        /// <summary>
        /// Generate a grid mesh for GPU height displacement.
        /// Uses dual UV channels: UV0 for heightmap (Y-flipped), UV1 for data texture (Azgaar coords).
        /// </summary>
        private void GenerateGridMesh()
        {
            // Use source resolution divided by divisor for clean texel alignment
            int gridWidth = mapData.Info.Width / gridDivisor;
            int gridHeight = mapData.Info.Height / gridDivisor;

            Debug.Log($"Generating grid mesh {gridWidth}x{gridHeight} (divisor {gridDivisor})...");

            float worldWidth = mapData.Info.Width * cellScale;
            float worldHeight = mapData.Info.Height * cellScale;

            int vertCountX = gridWidth + 1;
            int vertCountY = gridHeight + 1;
            int totalVerts = vertCountX * vertCountY;

            vertices.Clear();
            colors.Clear();
            uv0.Clear();
            uv1.Clear();
            triangles.Clear();

            var oceanColor = new Color32(30, 50, 90, 255);

            // Generate vertices and UVs
            for (int y = 0; y <= gridHeight; y++)
            {
                for (int x = 0; x <= gridWidth; x++)
                {
                    float u = (float)x / gridWidth;
                    float v = (float)y / gridHeight;

                    // World position: X right, Z negative (matches Voronoi convention)
                    float worldX = u * worldWidth;
                    float worldZ = -v * worldHeight;

                    vertices.Add(new Vector3(worldX, 0f, worldZ));
                    colors.Add(oceanColor);

                    // UV0 for heightmap: Y-flipped to match Unity texture coordinates
                    uv0.Add(new Vector2(u, 1f - v));

                    // UV1 for data texture: Azgaar coordinates (no flip)
                    uv1.Add(new Vector2(u, v));
                }
            }

            // Generate triangles (clockwise winding for top-down view)
            for (int y = 0; y < gridHeight; y++)
            {
                for (int x = 0; x < gridWidth; x++)
                {
                    int bl = y * vertCountX + x;
                    int br = bl + 1;
                    int tl = (y + 1) * vertCountX + x;
                    int tr = tl + 1;

                    // Triangle 1: BL, TR, TL
                    triangles.Add(bl);
                    triangles.Add(tr);
                    triangles.Add(tl);

                    // Triangle 2: BL, BR, TR
                    triangles.Add(bl);
                    triangles.Add(br);
                    triangles.Add(tr);
                }
            }

            Debug.Log($"Generated grid mesh: {totalVerts} vertices, {triangles.Count / 3} triangles");

            // Create or update mesh
            if (mesh == null)
            {
                mesh = new Mesh();
                mesh.name = "MapMesh";
            }
            mesh.Clear();
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

            mesh.SetVertices(vertices);
            mesh.SetColors(colors);
            mesh.SetUVs(0, uv0);  // UV0 for heightmap
            mesh.SetUVs(1, uv1);  // UV1 for data texture
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            meshFilter.mesh = mesh;

            if (terrainMaterial != null)
            {
                meshRenderer.sharedMaterial = terrainMaterial;
            }

            CenterMap();
        }

        /// <summary>
        /// Generate Voronoi mesh from cell polygons (original approach, no height displacement).
        /// </summary>
        private void GenerateVoronoiMesh()
        {
            Debug.Log($"Generating Voronoi mesh for {mapData.Cells.Count} cells...");

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

            Debug.Log($"Generated Voronoi mesh: {vertices.Count} vertices, {triangles.Count / 3} triangles, {cellsRendered} cells");

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
                meshRenderer.sharedMaterial = terrainMaterial;
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

            // Grid mesh uses shader for coloring, skip vertex color updates
            if (useGridMesh && useShaderOverlays)
                return;

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
            if (mapData != null)
            {
                float width = mapData.Info.Width * cellScale;
                float height = mapData.Info.Height * cellScale;
                return new Bounds(Vector3.zero, new Vector3(width, 0.1f, height));
            }
            // No map data yet - return empty bounds
            return new Bounds(Vector3.zero, Vector3.zero);
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

        [ContextMenu("Set Mode: Market")]
        private void SetModeMarket() => SetMapMode(MapMode.Market);

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

        [ContextMenu("Toggle Grid Mesh")]
        private void ToggleGridMesh()
        {
            useGridMesh = !useGridMesh;
            Debug.Log($"Grid mesh: {(useGridMesh ? "enabled" : "disabled")}");
            if (mapData != null)
            {
                GenerateMesh();
                if (overlayManager != null)
                {
                    overlayManager.SetHeightDisplacementEnabled(useGridMesh);
                }
            }
        }
#endif
    }
}
