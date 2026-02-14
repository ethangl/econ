using System;
using System.Collections.Generic;
using System.Text;
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
        [SerializeField] private float cellScale = 0.01f;  // Scale from map data pixels to Unity units
        public float CellScale => cellScale;
        [SerializeField] private Material terrainMaterial;
        [SerializeField] private bool renderLandOnly = false;
        private bool showRealmCapitalMarkers = true;
        private bool showMarketLocationMarkers = true;
        private float realmCapitalMarkerHeight = 0.1f;
        private float realmCapitalMarkerDiameter = 0.02f;
        private float realmCapitalMarkerBaseOffset = 0.02f;

        // Map mode (not serialized - always starts in Political per CLAUDE.md guidance)
        private MapMode currentMode = MapMode.Political;

        [Header("Shader Overlays")]
        private bool useShaderOverlays = true;
        [SerializeField] [Range(1, 8)] private int overlayResolutionMultiplier = 6;  // Higher = smoother borders, more memory

        // Grid mesh with height displacement (Phase 6c)
        // Non-serialized during development - see CLAUDE.md
        private bool useGridMesh = true;
        private int gridDivisor = 1;  // 1 = full source resolution, 2 = half, etc.
        private float gridHeightScale = 0.2f;

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
        private const float RealmZoomedInMax = 0.05f;
        private const float ProvinceZoomedInMax = 0.75f;
        private const float PoliticalZoomHysteresis = 0.02f;
        private const float DefaultModeHeightScale = 0f;
        private const float BiomesModeHeightScale = 0.3f;
        private const float HeightScaleTransitionSpeed = 2f;
        private float currentAnimatedHeightScale = DefaultModeHeightScale;
        private float targetHeightScale = DefaultModeHeightScale;

        /// <summary>Event fired when a cell is clicked. Passes cell ID (-1 if clicked on nothing).</summary>
        public event Action<int> OnCellClicked;

        /// <summary>Event fired after selection changes. Passes the selection depth.</summary>
        public event Action<SelectionDepth> OnSelectionChanged;

        private MapData mapData;
        private EconSim.Core.Economy.EconomyState economyState;
        private MeshFilter meshFilter;
        private MeshRenderer meshRenderer;
        private Mesh mesh;
        private MapOverlayManager overlayManager;
        private Transform realmCapitalMarkerRoot;
        private Transform marketLocationMarkerRoot;
        private Material realmCapitalMarkerMaterial;
        private bool marketLocationMarkersDirty = true;

        // Cell mesh data
        private List<Vector3> vertices = new List<Vector3>();
        private List<int> triangles = new List<int>();
        private List<Color32> colors = new List<Color32>();
        private List<Vector2> uv0 = new List<Vector2>();  // UVs for both heightmap and data texture (Y-up, unified)

        // Vertex heights (computed by averaging neighboring cells)
        private float[] vertexHeights;

        // Drill-down selection state
        public enum SelectionDepth { None, Realm, Province, County }
        private SelectionDepth selectionDepth = SelectionDepth.None;
        private int selectedRealmId = -1;
        private int selectedProvinceId = -1;
        private int selectedCountyId = -1;

        public SelectionDepth CurrentSelectionDepth => selectionDepth;
        public int SelectedRealmId => selectedRealmId;
        public int SelectedProvinceId => selectedProvinceId;
        public int SelectedCountyId => selectedCountyId;

        public enum MapMode
        {
            Political = 0,      // Colored by realm (zoomed-in 0-25%)
            Province = 1,       // Colored by province (zoomed-in 25-75%)
            County = 2,         // Colored by county/cell (zoomed-in 75-100%)
            Market = 3,         // Colored by market zone (key: 3)
            Biomes = 4,         // Biomes (vertex-blended) (key: 2)
            ChannelInspector = 5, // Debug channel visualization (key: 0)
            TransportCost = 6, // Local per-cell transport difficulty heatmap (key: 5)
            MarketAccess = 7 // Cell-to-assigned-market transport cost heatmap (key: 6)
        }

        public MapMode CurrentMode => currentMode;
        public string CurrentModeName => ModeNames[(int)currentMode];

        private static readonly string[] ModeNames =
        {
            "Political",
            "Province",
            "County",
            "Market",
            "Biomes",
            "Channel Inspector",
            "Transport Cost",
            "Market Access"
        };

        private static readonly MapOverlayManager.OverlayLayer[] NoOverlayCycle =
        {
            MapOverlayManager.OverlayLayer.None
        };

        private static readonly MapOverlayManager.OverlayLayer[] PoliticalOverlayCycle =
        {
            MapOverlayManager.OverlayLayer.None,
            MapOverlayManager.OverlayLayer.PopulationDensity
        };

        private static readonly Dictionary<MapMode, MapOverlayManager.OverlayLayer[]> OverlayCyclesByScope =
            new Dictionary<MapMode, MapOverlayManager.OverlayLayer[]>
            {
                { MapMode.Political, PoliticalOverlayCycle },
                { MapMode.Market, NoOverlayCycle },
                { MapMode.Biomes, NoOverlayCycle },
                { MapMode.ChannelInspector, NoOverlayCycle },
                { MapMode.TransportCost, NoOverlayCycle },
                { MapMode.MarketAccess, NoOverlayCycle }
            };

        private readonly Dictionary<MapMode, MapOverlayManager.OverlayLayer> selectedOverlayByScope =
            new Dictionary<MapMode, MapOverlayManager.OverlayLayer>();

        [Header("Debug Tooling")]
        private bool showIdProbe = false;
        private KeyCode cycleDebugChannelKey = KeyCode.O;
        private KeyCode toggleProbeKey = KeyCode.P;

        private MapOverlayManager.ChannelDebugView channelDebugView = MapOverlayManager.ChannelDebugView.PoliticalIdsR;
        private string probeText = "ID Probe: move cursor over land";
        private readonly StringBuilder probeBuilder = new StringBuilder(512);

        private void Awake()
        {
            meshFilter = GetComponent<MeshFilter>();
            meshRenderer = GetComponent<MeshRenderer>();
        }

        private void OnValidate()
        {
            // selectionDimmingTarget changes take effect through the animation loop.
            overlayManager?.RefreshPathStyleFromMaterial();
        }

        private void OnDestroy()
        {
            // Unsubscribe from shader selection
            OnCellClicked -= HandleShaderSelection;

            DestroyRealmCapitalMarkers();
            DestroyMarketLocationMarkers();
            DestroyRealmCapitalMarkerMaterial();

            // Clean up overlay manager textures
            overlayManager?.Dispose();
        }

        private void Update()
        {
            overlayManager?.RefreshPathStyleFromMaterial();

            // Map mode selection with number keys.
            if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1))
            {
                if (IsPoliticalFamilyMode(currentMode))
                {
                    CycleOverlayForMode(currentMode, "1");
                }
                else
                {
                    MapMode politicalBandMode = ResolveZoomDrivenPoliticalMode();
                    SetMapMode(politicalBandMode);
                    Debug.Log($"Map mode: {ModeNames[(int)politicalBandMode]} (1, zoom-driven)");
                }
            }
            else if (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2))
            {
                SetMapMode(MapMode.Biomes);
                Debug.Log("Map mode: Biomes (2)");
            }
            else if (Input.GetKeyDown(KeyCode.Alpha3) || Input.GetKeyDown(KeyCode.Keypad3))
            {
                SetMapMode(MapMode.Market);
                Debug.Log("Map mode: Market (3)");
            }
            else if (Input.GetKeyDown(KeyCode.Alpha5) || Input.GetKeyDown(KeyCode.Keypad5))
            {
                SetMapMode(MapMode.TransportCost);
                Debug.Log("Map mode: Transport Cost (5)");
            }
            else if (Input.GetKeyDown(KeyCode.Alpha6) || Input.GetKeyDown(KeyCode.Keypad6))
            {
                SetMapMode(MapMode.MarketAccess);
                Debug.Log("Map mode: Market Access (6)");
            }
            else if (Input.GetKeyDown(KeyCode.Alpha0) || Input.GetKeyDown(KeyCode.Keypad0))
            {
                SetMapMode(MapMode.ChannelInspector);
                Debug.Log($"Map mode: Channel Inspector (0), view={channelDebugView}");
            }

            ApplyZoomDrivenPoliticalMode();

            if (Input.GetKeyDown(cycleDebugChannelKey))
            {
                CycleChannelDebugView();
            }
            if (Input.GetKeyDown(toggleProbeKey))
            {
                showIdProbe = !showIdProbe;
                Debug.Log($"ID probe: {(showIdProbe ? "enabled" : "disabled")}");
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

            // Select on left-button release (unless that release ended a drag pan).
            if (Input.GetMouseButtonUp(0))
            {
                bool suppressSelection = mapCameraController != null
                    && mapCameraController.ConsumeSelectionReleaseSuppression();

                if (!suppressSelection && !IsPointerOverUI())
                {
                    HandleClick();
                }
            }

            // Animate selection dimming
            UpdateHeightScaleAnimation();
            UpdateDimmingAnimation();
            UpdateProbe();
        }

        private static bool IsPoliticalFamilyMode(MapMode mode)
        {
            return mode == MapMode.Political || mode == MapMode.Province || mode == MapMode.County;
        }

        private float ResolveHeightScaleForMode(MapMode mode)
        {
            return mode switch
            {
                MapMode.Biomes => BiomesModeHeightScale,
                _ => DefaultModeHeightScale
            };
        }

        private void SetHeightScaleTargetForMode(MapMode mode)
        {
            targetHeightScale = ResolveHeightScaleForMode(mode);
        }

        private void SetHeightScaleImmediateForMode(MapMode mode)
        {
            targetHeightScale = ResolveHeightScaleForMode(mode);
            currentAnimatedHeightScale = targetHeightScale;
            overlayManager?.SetHeightScale(currentAnimatedHeightScale);
        }

        private void UpdateHeightScaleAnimation()
        {
            if (overlayManager == null)
                return;

            currentAnimatedHeightScale = Mathf.MoveTowards(
                currentAnimatedHeightScale,
                targetHeightScale,
                HeightScaleTransitionSpeed * Time.deltaTime);

            overlayManager.SetHeightScale(currentAnimatedHeightScale);
        }

        private static MapMode ResolveOverlayScope(MapMode mode)
        {
            return IsPoliticalFamilyMode(mode) ? MapMode.Political : mode;
        }

        private static MapOverlayManager.OverlayLayer[] GetOverlayCycle(MapMode mode)
        {
            MapMode scope = ResolveOverlayScope(mode);
            if (OverlayCyclesByScope.TryGetValue(scope, out MapOverlayManager.OverlayLayer[] cycle) &&
                cycle != null &&
                cycle.Length > 0)
            {
                return cycle;
            }

            return NoOverlayCycle;
        }

        private void CycleOverlayForMode(MapMode mode, string triggerKey)
        {
            if (overlayManager == null)
                return;

            MapMode scope = ResolveOverlayScope(mode);
            MapOverlayManager.OverlayLayer[] cycle = GetOverlayCycle(scope);
            if (cycle.Length <= 1)
            {
                overlayManager.SetOverlay(cycle[0]);
                return;
            }

            if (!selectedOverlayByScope.TryGetValue(scope, out MapOverlayManager.OverlayLayer currentOverlay))
                currentOverlay = overlayManager.CurrentOverlay;

            int currentIndex = Array.IndexOf(cycle, currentOverlay);
            if (currentIndex < 0)
                currentIndex = 0;

            int nextIndex = (currentIndex + 1) % cycle.Length;
            MapOverlayManager.OverlayLayer nextOverlay = cycle[nextIndex];
            selectedOverlayByScope[scope] = nextOverlay;
            overlayManager.SetOverlay(nextOverlay);

            Debug.Log($"Overlay: {nextOverlay} ({triggerKey})");
        }

        private void ApplyOverlayForCurrentMode()
        {
            if (overlayManager == null)
                return;

            MapMode scope = ResolveOverlayScope(currentMode);
            MapOverlayManager.OverlayLayer[] cycle = GetOverlayCycle(scope);
            if (cycle == null || cycle.Length == 0)
                return;

            if (!selectedOverlayByScope.TryGetValue(scope, out MapOverlayManager.OverlayLayer selectedOverlay))
                selectedOverlay = cycle[0];

            if (Array.IndexOf(cycle, selectedOverlay) < 0)
                selectedOverlay = cycle[0];

            selectedOverlayByScope[scope] = selectedOverlay;
            overlayManager.SetOverlay(selectedOverlay);
        }

        private MapMode ResolveZoomDrivenPoliticalMode()
        {
            if (mapCameraController == null)
                return MapMode.Political;

            float zoomedIn01 = mapCameraController.GetZoomedIn01();
            if (zoomedIn01 < RealmZoomedInMax)
                return MapMode.Political;
            if (zoomedIn01 < ProvinceZoomedInMax)
                return MapMode.Province;
            return MapMode.County;
        }

        private static MapMode ResolveZoomDrivenPoliticalModeWithHysteresis(MapMode currentPoliticalMode, float zoomedIn01)
        {
            float realmLower = RealmZoomedInMax - PoliticalZoomHysteresis;
            float realmUpper = RealmZoomedInMax + PoliticalZoomHysteresis;
            float provinceLower = ProvinceZoomedInMax - PoliticalZoomHysteresis;
            float provinceUpper = ProvinceZoomedInMax + PoliticalZoomHysteresis;

            switch (currentPoliticalMode)
            {
                case MapMode.Political:
                    return zoomedIn01 >= realmUpper ? MapMode.Province : MapMode.Political;
                case MapMode.Province:
                    if (zoomedIn01 < realmLower)
                        return MapMode.Political;
                    if (zoomedIn01 >= provinceUpper)
                        return MapMode.County;
                    return MapMode.Province;
                case MapMode.County:
                    return zoomedIn01 < provinceLower ? MapMode.Province : MapMode.County;
                default:
                    if (zoomedIn01 < RealmZoomedInMax)
                        return MapMode.Political;
                    if (zoomedIn01 < ProvinceZoomedInMax)
                        return MapMode.Province;
                    return MapMode.County;
            }
        }

        private void ApplyZoomDrivenPoliticalMode()
        {
            if (!IsPoliticalFamilyMode(currentMode))
                return;

            if (mapCameraController == null)
                return;

            float zoomedIn01 = mapCameraController.GetZoomedIn01();
            MapMode zoomDrivenMode = ResolveZoomDrivenPoliticalModeWithHysteresis(currentMode, zoomedIn01);
            if (zoomDrivenMode != currentMode)
                SetMapMode(zoomDrivenMode);
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
                    overlayManager.SetHoveredRealm(cell.RealmId);
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
                case MapMode.Biomes:
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

            // Convert world position back to data coordinates
            Vector3 localPos = worldPos - transform.position;
            float dataX = localPos.x / cellScale;
            float dataY = localPos.z / cellScale;

            // Find the closest cell center
            float minDistSq = float.MaxValue;
            int closestCell = -1;

            foreach (var cell in mapData.Cells)
            {
                if (renderLandOnly && !cell.IsLand) continue;

                float dx = cell.Center.X - dataX;
                float dy = cell.Center.Y - dataY;
                float distSq = dx * dx + dy * dy;

                if (distSq < minDistSq)
                {
                    minDistSq = distSq;
                    closestCell = cell.Id;
                }
            }

            // Average cell radius is roughly 5-10 map units
            // If click is more than 15 units from nearest cell center, ignore it
            const float maxDistSq = 15f * 15f;
            if (minDistSq > maxDistSq)
            {
                return -1;
            }

            return closestCell;
        }

        public void Initialize(
            MapData data,
            string overlayTextureCacheDirectory = null,
            bool preferCachedOverlayTextures = false)
        {
            mapData = data;

            Profiler.Begin("GenerateMesh");
            GenerateMesh();
            Profiler.End();

            Profiler.Begin("InitializeOverlays");
            InitializeOverlays(overlayTextureCacheDirectory, preferCachedOverlayTextures);
            Profiler.End();

            BuildRealmCapitalMarkers();
            DestroyMarketLocationMarkers();
            marketLocationMarkersDirty = true;
            UpdateModeMarkerVisibility();

            // Subscribe to cell clicks for shader selection
            if (useShaderOverlays && overlayManager != null)
            {
                OnCellClicked += HandleShaderSelection;
            }
        }

        private void InitializeOverlays(string overlayTextureCacheDirectory, bool preferCachedOverlayTextures)
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

            overlayManager = new MapOverlayManager(
                mapData,
                mat,
                overlayResolutionMultiplier,
                overlayTextureCacheDirectory,
                preferCachedOverlayTextures);

            // Height displacement follows grid-mesh mode with map-mode-specific scale.
            overlayManager.SetHeightDisplacementEnabled(useGridMesh);
            SetHeightScaleImmediateForMode(currentMode);
            overlayManager.SetSeaLevel(Elevation.ResolveSeaLevel(mapData.Info));

            // Sync shader mode with current map mode
            overlayManager.SetMapMode(currentMode);
            overlayManager.SetChannelDebugView(channelDebugView);
            overlayManager.RefreshPathStyleFromMaterial();
            ApplyOverlayForCurrentMode();
        }

        /// <summary>
        /// Set road state for shader-based road rendering. Call after economy initialization.
        /// </summary>
        public void SetRoadState(EconSim.Core.Economy.RoadState roads)
        {
            overlayManager?.SetRoadState(roads);
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

            // Get the cell to look up its realm/province
            if (!mapData.CellById.TryGetValue(cellId, out var cell))
            {
                ClearDrillDownSelection();
                return;
            }

            // Water cells have no meaningful realm/province/county - clear selection
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

            // Non-political overlays - just select county, no drill-down
            if (currentMode == MapMode.Biomes ||
                currentMode == MapMode.TransportCost ||
                currentMode == MapMode.MarketAccess)
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
                    // No selection yet - select realm
                    SelectAtDepth(SelectionDepth.Realm, cell);
                    selectionBounds = GetRealmBounds(cell.RealmId);
                    break;

                case SelectionDepth.Realm:
                    if (cell.RealmId == selectedRealmId)
                    {
                        // Clicking same realm - drill down to province
                        SelectAtDepth(SelectionDepth.Province, cell);
                        selectionBounds = GetProvinceBounds(cell.ProvinceId);
                    }
                    else
                    {
                        // Clicking different realm - select new realm
                        SelectAtDepth(SelectionDepth.Realm, cell);
                        selectionBounds = GetRealmBounds(cell.RealmId);
                    }
                    break;

                case SelectionDepth.Province:
                    if (cell.ProvinceId == selectedProvinceId)
                    {
                        // Clicking same province - drill down to county
                        SelectAtDepth(SelectionDepth.County, cell);
                        selectionBounds = GetCountyBounds(cell.CountyId);
                    }
                    else if (cell.RealmId == selectedRealmId)
                    {
                        // Clicking different province in same realm - select new province
                        SelectAtDepth(SelectionDepth.Province, cell);
                        selectionBounds = GetProvinceBounds(cell.ProvinceId);
                    }
                    else
                    {
                        // Clicking different realm - reset to realm level
                        SelectAtDepth(SelectionDepth.Realm, cell);
                        selectionBounds = GetRealmBounds(cell.RealmId);
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
                    else if (cell.RealmId == selectedRealmId)
                    {
                        // Clicking different province in same realm - go back to province level
                        SelectAtDepth(SelectionDepth.Province, cell);
                        selectionBounds = GetProvinceBounds(cell.ProvinceId);
                    }
                    else
                    {
                        // Clicking different realm - reset to realm level
                        SelectAtDepth(SelectionDepth.Realm, cell);
                        selectionBounds = GetRealmBounds(cell.RealmId);
                    }
                    break;
            }

            // Pan camera to selection without changing zoom
            if (selectionBounds.HasValue && mapCameraController != null)
            {
                mapCameraController.FocusOn(selectionBounds.Value.center);
            }
        }

        /// <summary>
        /// Select at a specific depth and update shader uniforms.
        /// </summary>
        private void SelectAtDepth(SelectionDepth depth, Cell cell)
        {
            selectionDepth = depth;
            selectedRealmId = cell.RealmId;
            selectedProvinceId = cell.ProvinceId;
            selectedCountyId = cell.CountyId;
            SetSelectionActive(true);

            // Update shader selection based on depth
            overlayManager.ClearSelection();
            switch (depth)
            {
                case SelectionDepth.Realm:
                    overlayManager.SetSelectedRealm(cell.RealmId);
                    break;
                case SelectionDepth.Province:
                    overlayManager.SetSelectedProvince(cell.ProvinceId);
                    break;
                case SelectionDepth.County:
                    overlayManager.SetSelectedCounty(cell.CountyId);
                    break;
            }

            // Notify listeners
            OnSelectionChanged?.Invoke(depth);
        }

        /// <summary>
        /// Clear drill-down selection state.
        /// </summary>
        private void ClearDrillDownSelection()
        {
            selectionDepth = SelectionDepth.None;
            selectedRealmId = -1;
            selectedProvinceId = -1;
            selectedCountyId = -1;
            SetSelectionActive(false);
            overlayManager?.ClearSelection();

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
                mapCameraController.FocusOn(selectionBounds.Value.center);
            }
        }

        /// <summary>
        /// Convert data coordinates to world position.
        /// </summary>
        private Vector3 DataToWorld(float dataX, float dataY)
        {
            return transform.position + DataToLocal(dataX, dataY);
        }

        private Vector3 DataToLocal(float dataX, float dataY)
        {
            return new Vector3(dataX * cellScale, 0f, dataY * cellScale);
        }

        /// <summary>
        /// Get world-space centroid of a county.
        /// </summary>
        private Vector3? GetCountyCentroid(int countyId)
        {
            if (countyId <= 0) return null;
            if (!mapData.CountyById.TryGetValue(countyId, out var county)) return null;

            return DataToWorld(county.Centroid.X, county.Centroid.Y);
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
                return DataToWorld(centerCell.Center.X, centerCell.Center.Y);
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

            return DataToWorld(sumX / count, sumY / count);
        }

        /// <summary>
        /// Get world-space centroid of a realm.
        /// </summary>
        private Vector3? GetRealmCentroid(int realmId)
        {
            if (realmId <= 0) return null;
            if (!mapData.RealmById.TryGetValue(realmId, out var realm)) return null;

            // Use center cell if available
            if (realm.CenterCellId > 0 && mapData.CellById.TryGetValue(realm.CenterCellId, out var centerCell))
            {
                return DataToWorld(centerCell.Center.X, centerCell.Center.Y);
            }

            // Fall back to calculating from all cells with this realm ID
            float sumX = 0, sumY = 0;
            int count = 0;
            foreach (var cell in mapData.Cells)
            {
                if (cell.RealmId == realmId && cell.IsLand)
                {
                    sumX += cell.Center.X;
                    sumY += cell.Center.Y;
                    count++;
                }
            }
            if (count == 0) return null;

            return DataToWorld(sumX / count, sumY / count);
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

            return DataToWorld(sumX / count, sumY / count);
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
        /// Get world-space bounds of a realm.
        /// </summary>
        private Bounds? GetRealmBounds(int realmId)
        {
            if (realmId <= 0) return null;

            // Collect all cells belonging to this realm
            var cellIds = new List<int>();
            foreach (var cell in mapData.Cells)
            {
                if (cell.RealmId == realmId && cell.IsLand)
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

            // Convert data corners to world space
            Vector3 worldMin = DataToWorld(minX, minY);
            Vector3 worldMax = DataToWorld(maxX, maxY);

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
                    SetHeightScaleTargetForMode(mode);
                    overlayManager.SetChannelDebugView(channelDebugView);
                    ApplyOverlayForCurrentMode();

                    // Clear drill-down selection when changing modes
                    ClearDrillDownSelection();
                }

                if (mode == MapMode.Market)
                {
                    EnsureMarketLocationMarkersBuilt();
                }

                UpdateModeMarkerVisibility();
            }
        }

        private void BuildRealmCapitalMarkers()
        {
            DestroyRealmCapitalMarkers();

            if (!showRealmCapitalMarkers || mapData == null || mapData.Realms == null || mapData.Realms.Count == 0)
                return;

            if (mapData.CellById == null)
            {
                mapData.BuildLookups();
            }

            EnsureRealmCapitalMarkerMaterial();

            var burgById = new Dictionary<int, Burg>();
            if (mapData.Burgs != null)
            {
                foreach (var burg in mapData.Burgs)
                {
                    if (burg != null && burg.Id > 0)
                        burgById[burg.Id] = burg;
                }
            }

            var root = new GameObject("RealmCapitalMarkers");
            root.transform.SetParent(transform, false);
            realmCapitalMarkerRoot = root.transform;

            float markerHeight = Mathf.Max(0.1f, realmCapitalMarkerHeight);
            float markerDiameter = Mathf.Max(0.005f, realmCapitalMarkerDiameter);
            float markerScaleY = markerHeight * 0.5f; // Unity cylinder mesh has height 2 at scale.y = 1

            foreach (var realm in mapData.Realms)
            {
                if (realm == null || realm.Id <= 0)
                    continue;

                int capitalCellId = ResolveRealmCapitalCellId(realm, burgById);
                if (capitalCellId <= 0 || !mapData.CellById.TryGetValue(capitalCellId, out var capitalCell))
                    continue;

                if (!capitalCell.IsLand)
                    continue;

                GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                marker.name = $"RealmCapitalMarker_{realm.Id}";
                marker.transform.SetParent(realmCapitalMarkerRoot, false);

                float surfaceY = GetCellSurfaceY(capitalCell);
                marker.transform.localPosition = DataToLocal(capitalCell.Center.X, capitalCell.Center.Y) +
                    Vector3.up * (surfaceY + realmCapitalMarkerBaseOffset + markerHeight * 0.5f);
                marker.transform.localScale = new Vector3(markerDiameter, markerScaleY, markerDiameter);

                var collider = marker.GetComponent<Collider>();
                if (collider != null)
                    Destroy(collider);

                var renderer = marker.GetComponent<MeshRenderer>();
                if (renderer != null && realmCapitalMarkerMaterial != null)
                {
                    renderer.sharedMaterial = realmCapitalMarkerMaterial;
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                }
            }
        }

        private int ResolveRealmCapitalCellId(Realm realm, Dictionary<int, Burg> burgById)
        {
            if (realm.CapitalBurgId > 0 && burgById != null && burgById.TryGetValue(realm.CapitalBurgId, out var capitalBurg))
            {
                return capitalBurg.CellId;
            }

            return realm.CenterCellId;
        }

        private float GetCellSurfaceY(Cell cell)
        {
            if (cell == null)
                return 0f;

            if (useGridMesh && useShaderOverlays)
            {
                float normalizedSignedHeight = Elevation.GetNormalizedSignedHeight(cell, mapData.Info);
                return normalizedSignedHeight * gridHeightScale;
            }

            return GetCellHeight(cell);
        }

        private void UpdateRealmCapitalMarkersVisibility()
        {
            if (realmCapitalMarkerRoot == null)
                return;

            bool isVisible = showRealmCapitalMarkers && currentMode == MapMode.Political;
            if (realmCapitalMarkerRoot.gameObject.activeSelf != isVisible)
            {
                realmCapitalMarkerRoot.gameObject.SetActive(isVisible);
            }
        }

        private void BuildMarketLocationMarkers()
        {
            DestroyMarketLocationMarkers();

            if (!showMarketLocationMarkers || mapData == null || economyState?.Markets == null || economyState.Markets.Count == 0)
                return;

            if (mapData.CellById == null)
            {
                mapData.BuildLookups();
            }

            EnsureRealmCapitalMarkerMaterial();

            var root = new GameObject("MarketLocationMarkers");
            root.transform.SetParent(transform, false);
            marketLocationMarkerRoot = root.transform;

            float markerHeight = Mathf.Max(0.1f, realmCapitalMarkerHeight);
            float markerDiameter = Mathf.Max(0.005f, realmCapitalMarkerDiameter);
            float markerScaleY = markerHeight * 0.5f; // Unity cylinder mesh has height 2 at scale.y = 1

            foreach (var market in economyState.Markets.Values)
            {
                if (market == null || market.Id <= 0)
                    continue;

                int locationCellId = market.LocationCellId;
                if (locationCellId <= 0 || !mapData.CellById.TryGetValue(locationCellId, out var locationCell))
                    continue;

                if (!locationCell.IsLand)
                    continue;

                GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                marker.name = $"MarketLocationMarker_{market.Id}";
                marker.transform.SetParent(marketLocationMarkerRoot, false);

                float surfaceY = GetCellSurfaceY(locationCell);
                marker.transform.localPosition = DataToLocal(locationCell.Center.X, locationCell.Center.Y) +
                    Vector3.up * (surfaceY + realmCapitalMarkerBaseOffset + markerHeight * 0.5f);
                marker.transform.localScale = new Vector3(markerDiameter, markerScaleY, markerDiameter);

                var collider = marker.GetComponent<Collider>();
                if (collider != null)
                    Destroy(collider);

                var renderer = marker.GetComponent<MeshRenderer>();
                if (renderer != null && realmCapitalMarkerMaterial != null)
                {
                    renderer.sharedMaterial = realmCapitalMarkerMaterial;
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                }
            }
        }

        private void UpdateMarketLocationMarkersVisibility()
        {
            bool isVisible = showMarketLocationMarkers && currentMode == MapMode.Market;
            if (isVisible)
            {
                EnsureMarketLocationMarkersBuilt();
            }

            if (marketLocationMarkerRoot == null)
                return;

            if (marketLocationMarkerRoot.gameObject.activeSelf != isVisible)
            {
                marketLocationMarkerRoot.gameObject.SetActive(isVisible);
            }
        }

        private void EnsureMarketLocationMarkersBuilt()
        {
            if (!showMarketLocationMarkers || mapData == null || economyState?.Markets == null || economyState.Markets.Count == 0)
                return;

            if (!marketLocationMarkersDirty && marketLocationMarkerRoot != null)
                return;

            BuildMarketLocationMarkers();
            marketLocationMarkersDirty = false;
        }

        private void UpdateModeMarkerVisibility()
        {
            UpdateRealmCapitalMarkersVisibility();
            UpdateMarketLocationMarkersVisibility();
        }

        private void DestroyRealmCapitalMarkers()
        {
            if (realmCapitalMarkerRoot == null)
                return;

            if (Application.isPlaying)
            {
                Destroy(realmCapitalMarkerRoot.gameObject);
            }
            else
            {
                DestroyImmediate(realmCapitalMarkerRoot.gameObject);
            }

            realmCapitalMarkerRoot = null;
        }

        private void DestroyMarketLocationMarkers()
        {
            if (marketLocationMarkerRoot == null)
                return;

            if (Application.isPlaying)
            {
                Destroy(marketLocationMarkerRoot.gameObject);
            }
            else
            {
                DestroyImmediate(marketLocationMarkerRoot.gameObject);
            }

            marketLocationMarkerRoot = null;
        }

        private void EnsureRealmCapitalMarkerMaterial()
        {
            if (realmCapitalMarkerMaterial != null)
                return;

            Shader markerShader = Shader.Find("Unlit/Color");
            if (markerShader == null)
            {
                markerShader = Shader.Find("Universal Render Pipeline/Unlit");
            }
            if (markerShader == null)
            {
                markerShader = Shader.Find("Universal Render Pipeline/Lit");
            }
            if (markerShader == null)
            {
                markerShader = Shader.Find("Standard");
            }

            if (markerShader == null)
                return;

            realmCapitalMarkerMaterial = new Material(markerShader)
            {
                color = Color.white
            };

            if (realmCapitalMarkerMaterial.HasProperty("_Glossiness"))
            {
                realmCapitalMarkerMaterial.SetFloat("_Glossiness", 0f);
            }
        }

        private void DestroyRealmCapitalMarkerMaterial()
        {
            if (realmCapitalMarkerMaterial == null)
                return;

            if (Application.isPlaying)
            {
                Destroy(realmCapitalMarkerMaterial);
            }
            else
            {
                DestroyImmediate(realmCapitalMarkerMaterial);
            }

            realmCapitalMarkerMaterial = null;
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
        /// Single UV channel for both heightmap and data texture (Y-up coordinates).
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
            triangles.Clear();

            var oceanColor = new Color32(30, 50, 90, 255);

            // Generate vertices and UVs
            for (int y = 0; y <= gridHeight; y++)
            {
                for (int x = 0; x <= gridWidth; x++)
                {
                    float u = (float)x / gridWidth;
                    float v = (float)y / gridHeight;

                    // World position: X right, Z positive (Y-up data maps to Z-positive)
                    float worldX = u * worldWidth;
                    float worldZ = v * worldHeight;

                    vertices.Add(new Vector3(worldX, 0f, worldZ));
                    colors.Add(oceanColor);

                    // Single UV for both heightmap and data texture
                    uv0.Add(new Vector2(u, v));
                }
            }

            // Generate triangles (counter-clockwise winding for Z-positive top-down view)
            for (int y = 0; y < gridHeight; y++)
            {
                for (int x = 0; x < gridWidth; x++)
                {
                    int bl = y * vertCountX + x;
                    int br = bl + 1;
                    int tl = (y + 1) * vertCountX + x;
                    int tr = tl + 1;

                    // Triangle 1: BL, TL, TR (reversed from before)
                    triangles.Add(bl);
                    triangles.Add(tl);
                    triangles.Add(tr);

                    // Triangle 2: BL, TR, BR (reversed from before)
                    triangles.Add(bl);
                    triangles.Add(tr);
                    triangles.Add(br);
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
            mesh.SetUVs(0, uv0);
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
            uv0.Clear();

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

            // Set UV0 for shader overlay sampling
            if (useShaderOverlays && uv0.Count == vertices.Count)
            {
                mesh.SetUVs(0, uv0);
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
            var polyUVs = new List<Vector2>();  // Normalized data coordinates for texture sampling
            float heightSum = 0f;

            // Normalization factors (data coords -> 0-1)
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
                        pos2D.y * cellScale
                    ));

                    // UV: normalized data coordinates for texture sampling
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

            int centerIdx = vertices.Count;
            vertices.Add(center);
            colors.Add(cellColor);
            uv0.Add(centerUV);

            // Add polygon vertices and create triangles
            int firstPolyIdx = vertices.Count;
            for (int i = 0; i < polyVerts.Count; i++)
            {
                vertices.Add(polyVerts[i]);
                colors.Add(cellColor);
                uv0.Add(polyUVs[i]);
            }

            // Create fan triangles (reversed winding for Z-positive)
            for (int i = 0; i < polyVerts.Count; i++)
            {
                int next = (i + 1) % polyVerts.Count;
                triangles.Add(centerIdx);
                triangles.Add(firstPolyIdx + next);
                triangles.Add(firstPolyIdx + i);
            }
        }

        private float GetCellHeight(Cell cell)
        {
            // Use world-scale normalized signed height so displacement remains consistent across map scales.
            float normalizedSignedHeight = Elevation.GetNormalizedSignedHeight(cell, mapData.Info);
            return normalizedSignedHeight * heightScale;
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
                case MapMode.Market:
                    return GetMarketColor(cell);
                case MapMode.Biomes:
                    return GetTerrainColor(cell);  // Fallback; biome tint is shader-driven
                case MapMode.ChannelInspector:
                    return GetTerrainColor(cell);  // Shader debug visualization overrides this.
                case MapMode.TransportCost:
                case MapMode.MarketAccess:
                    return GetTerrainColor(cell);  // Transport heatmaps are shader-only.
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

            if (cell.RealmId > 0 && mapData.RealmById.TryGetValue(cell.RealmId, out var realm))
            {
                return realm.Color.ToUnity();
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
            return GetPoliticalColor(cell);  // Fall back to realm color
        }

        private Color32 GetCountyColor(Cell cell)
        {
            // Water cells
            if (!cell.IsLand)
            {
                return GetWaterColor(cell);
            }

            // Get base color from province (or realm if no province)
            Color baseColor;
            if (cell.ProvinceId > 0 && mapData.ProvinceById.TryGetValue(cell.ProvinceId, out var province))
            {
                baseColor = province.Color.ToUnity();
            }
            else if (cell.RealmId > 0 && mapData.RealmById.TryGetValue(cell.RealmId, out var realm))
            {
                baseColor = realm.Color.ToUnity();
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

            // Default ocean color - deep blue, varies by normalized depth in meters.
            float depthFactor = Elevation.GetNormalizedDepth01(cell, mapData.Info); // 0 at sea level, 1 at configured max depth
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

            transform.position = new Vector3(-halfWidth, 0, -halfHeight);
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

            marketLocationMarkersDirty = true;
            DestroyMarketLocationMarkers();
            if (currentMode == MapMode.Market)
            {
                EnsureMarketLocationMarkersBuilt();
            }
            UpdateModeMarkerVisibility();
        }

        public void RunDeferredStartupWork()
        {
            overlayManager?.RunDeferredStartupWork();
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

        private void CycleChannelDebugView()
        {
            if (currentMode != MapMode.ChannelInspector)
            {
                SetMapMode(MapMode.ChannelInspector);
            }

            int count = Enum.GetValues(typeof(MapOverlayManager.ChannelDebugView)).Length;
            int next = ((int)channelDebugView + 1) % count;
            channelDebugView = (MapOverlayManager.ChannelDebugView)next;

            overlayManager?.SetChannelDebugView(channelDebugView);
            Debug.Log($"Channel Inspector view: {channelDebugView}");
        }

        private void UpdateProbe()
        {
            if (mapData == null || mapData.CellById == null || !showIdProbe)
            {
                return;
            }

            var cam = selectionCamera != null ? selectionCamera : UnityEngine.Camera.main;
            if (cam == null)
            {
                probeText = "ID Probe: no camera";
                return;
            }

            Ray ray = cam.ScreenPointToRay(Input.mousePosition);
            Plane groundPlane = new Plane(Vector3.up, Vector3.zero);
            if (!groundPlane.Raycast(ray, out float distance))
            {
                probeText = "ID Probe: cursor off map";
                return;
            }

            Vector3 hitPoint = ray.GetPoint(distance);
            int cellId = FindCellAtPosition(hitPoint);
            if (cellId < 0 || !mapData.CellById.TryGetValue(cellId, out var cell))
            {
                probeText = "ID Probe: no cell";
                return;
            }

            probeBuilder.Clear();
            probeBuilder.Append("ID Probe");
            if (currentMode == MapMode.ChannelInspector)
            {
                probeBuilder.Append(" | Channel=").Append(channelDebugView);
            }
            probeBuilder.AppendLine();
            probeBuilder.Append("Mode=").Append(CurrentModeName).AppendLine();
            float absoluteHeight = Elevation.GetAbsoluteHeight(cell, mapData.Info);
            float seaRelativeHeight = Elevation.GetSeaRelativeHeight(cell, mapData.Info);
            float metersAboveSeaLevel = Elevation.GetMetersAboveSeaLevel(cell, mapData.Info);
            float signedMeters = Elevation.GetSignedMeters(cell, mapData.Info);

            probeBuilder.Append("Cell=").Append(cell.Id)
                .Append(" Land=").Append(cell.IsLand ? "Y" : "N")
                .Append(" AbsH=").Append(absoluteHeight.ToString("F1"))
                .Append(" SeaRel=").Append(seaRelativeHeight.ToString("F1"))
                .Append(" AboveSeaM=").Append(metersAboveSeaLevel.ToString("F0"))
                .Append(" SignedM=").Append(signedMeters.ToString("F0"))
                .AppendLine();

            switch (currentMode)
            {
                case MapMode.Political:
                case MapMode.Province:
                case MapMode.County:
                    probeBuilder.Append("Political: Realm=").Append(cell.RealmId)
                        .Append(" Province=").Append(cell.ProvinceId)
                        .Append(" County=").Append(cell.CountyId)
                        .AppendLine();
                    probeBuilder.Append("PoliticalIdsTex: R=").Append(FormatNorm(cell.RealmId))
                        .Append(" G=").Append(FormatNorm(cell.ProvinceId))
                        .Append(" B=").Append(FormatNorm(cell.CountyId))
                        .AppendLine();
                    break;

                case MapMode.Market:
                case MapMode.MarketAccess:
                    if (TryGetAssignedMarket(cell.Id, cell.CountyId, out int marketId, out var market))
                    {
                        probeBuilder.Append("Market: Id=").Append(marketId)
                            .Append(" Name=").Append(market?.Name ?? $"Market {marketId}")
                            .Append(" Type=").Append(market?.Type.ToString() ?? "Unknown");

                        if (market != null &&
                            market.ZoneCellCosts != null &&
                            market.ZoneCellCosts.TryGetValue(cell.Id, out float zoneCost))
                        {
                            probeBuilder.Append(" Cost=").Append(zoneCost.ToString("F2"));
                        }
                        else
                        {
                            probeBuilder.Append(" Cost=n/a");
                        }

                        probeBuilder.AppendLine();
                    }
                    else
                    {
                        probeBuilder.Append("Market: none").AppendLine();
                    }
                    break;

                case MapMode.Biomes:
                    string biomeName = "Unknown";
                    if (mapData.Biomes != null)
                    {
                        for (int i = 0; i < mapData.Biomes.Count; i++)
                        {
                            var biome = mapData.Biomes[i];
                            if (biome != null && biome.Id == cell.BiomeId)
                            {
                                biomeName = biome.Name;
                                break;
                            }
                        }
                    }
                    probeBuilder.Append("Biomes: Biome=").Append(biomeName)
                        .Append(" (").Append(cell.BiomeId).Append(")")
                        .Append(" Soil=").Append(cell.SoilId)
                        .Append(" VegType=").Append(cell.VegetationTypeId)
                        .Append(" VegDensity=").Append(cell.VegetationDensity.ToString("F3"))
                        .AppendLine();
                    probeBuilder.Append("GeographyBaseTex: R=").Append(FormatNorm(cell.BiomeId))
                        .Append(" G=").Append(FormatNorm(cell.SoilId))
                        .Append(" A=").Append(cell.IsLand ? "0.000000" : "1.000000")
                        .AppendLine();
                    break;

                case MapMode.ChannelInspector:
                    probeBuilder.Append("PoliticalIdsTex: R=").Append(FormatNorm(cell.RealmId))
                        .Append(" G=").Append(FormatNorm(cell.ProvinceId))
                        .Append(" B=").Append(FormatNorm(cell.CountyId))
                        .Append(" A=0.000000")
                        .AppendLine();
                    probeBuilder.Append("GeographyBaseTex: R=").Append(FormatNorm(cell.BiomeId))
                        .Append(" G=").Append(FormatNorm(cell.SoilId))
                        .Append(" B=0.000000")
                        .Append(" A=").Append(cell.IsLand ? "0.000000" : "1.000000")
                        .AppendLine();
                    probeBuilder.Append("VegetationTex: Type=").Append(cell.VegetationTypeId)
                        .Append(" Density=").Append(cell.VegetationDensity.ToString("F3"))
                        .AppendLine();
                    break;

                case MapMode.TransportCost:
                    probeBuilder.Append("TransportCost: ").Append(cell.MovementCost.ToString("F1"))
                        .AppendLine();
                    break;

                default:
                    probeBuilder.Append("Political: Realm=").Append(cell.RealmId)
                        .Append(" Province=").Append(cell.ProvinceId)
                        .Append(" County=").Append(cell.CountyId)
                        .AppendLine();
                    break;
            }

            probeText = probeBuilder.ToString();
        }

        private bool TryGetAssignedMarket(int cellId, int countyId, out int marketId, out EconSim.Core.Economy.Market market)
        {
            marketId = 0;
            market = null;

            if (economyState == null || economyState.Markets == null)
                return false;

            if (economyState.CellToMarket == null || !economyState.CellToMarket.TryGetValue(cellId, out marketId))
            {
                economyState.CountyToMarket?.TryGetValue(countyId, out marketId);
            }

            if (marketId <= 0)
                return false;

            return economyState.Markets.TryGetValue(marketId, out market);
        }


        private static string FormatNorm(int value)
        {
            return (value / 65535f).ToString("F6");
        }

        private void OnGUI()
        {
            if (!showIdProbe || mapData == null)
            {
                return;
            }

            const int width = 480;
            const int height = 110;
            GUI.Box(new Rect(10, 10, width, height), GUIContent.none);
            GUI.Label(
                new Rect(18, 18, width - 16, height - 16),
                probeText + "\nKeys: 2=Biomes, 5=Local Transport, 6=Market Transport, 0=Channel Inspector, O=Cycle Channel, P=Toggle Probe");
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

        [ContextMenu("Set Mode: Biomes")]
        private void SetModeBiomes() => SetMapMode(MapMode.Biomes);

        [ContextMenu("Set Mode: Market")]
        private void SetModeMarket() => SetMapMode(MapMode.Market);

        [ContextMenu("Set Mode: Transport Cost")]
        private void SetModeTransportCost() => SetMapMode(MapMode.TransportCost);

        [ContextMenu("Set Mode: Market Access")]
        private void SetModeMarketAccess() => SetMapMode(MapMode.MarketAccess);

        [ContextMenu("Set Mode: Channel Inspector")]
        private void SetModeChannelInspector() => SetMapMode(MapMode.ChannelInspector);

        [ContextMenu("Toggle Grid Mesh")]
        private void ToggleGridMesh()
        {
            useGridMesh = !useGridMesh;
            Debug.Log($"Grid mesh: {(useGridMesh ? "enabled" : "disabled")}");
            if (mapData != null)
            {
                GenerateMesh();
                BuildRealmCapitalMarkers();
                marketLocationMarkersDirty = true;
                if (currentMode == MapMode.Market)
                {
                    EnsureMarketLocationMarkersBuilt();
                }
                UpdateModeMarkerVisibility();
                if (overlayManager != null)
                {
                    overlayManager.SetHeightDisplacementEnabled(useGridMesh);
                }
            }
        }
#endif
    }
}
