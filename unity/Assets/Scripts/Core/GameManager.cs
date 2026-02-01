using UnityEngine;
using EconSim.Core.Import;
using EconSim.Core.Data;
using EconSim.Core.Common;
using EconSim.Core.Simulation;
using EconSim.Core.Simulation.Systems;
using EconSim.Renderer;
using EconSim.Camera;

namespace EconSim.Core
{
    /// <summary>
    /// Main entry point. Initializes map loading and wires together simulation and rendering.
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        [Header("Map Settings")]
        [SerializeField] private string mapFileName = "1234_low-island_40k_1440x810.json";
        [SerializeField] private bool loadFromResources = false;

        [Header("References")]
        [SerializeField] private MapView mapView;
        [SerializeField] private MapCamera mapCamera;

        public MapData MapData { get; private set; }
        public ISimulation Simulation => _simulation;

        private ISimulation _simulation;
        private int _lastLoggedDay;

        public static GameManager Instance { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            // Route simulation logs to Unity's console
            SimLog.LogAction = Debug.Log;
        }

        private void Start()
        {
            LoadMap();
        }

        private void LoadMap()
        {
            Debug.Log($"Loading map: {mapFileName}");

            AzgaarMap azgaarMap;

            if (loadFromResources)
            {
                // Load from Resources/Maps folder
                string resourcePath = $"Maps/{System.IO.Path.GetFileNameWithoutExtension(mapFileName)}";
                var textAsset = Resources.Load<TextAsset>(resourcePath);

                if (textAsset == null)
                {
                    Debug.LogError($"Failed to load map from Resources: {resourcePath}");
                    Debug.Log("Trying to load from file path...");
                    LoadMapFromFile();
                    return;
                }

                azgaarMap = AzgaarParser.Parse(textAsset.text);
            }
            else
            {
                LoadMapFromFile();
                return;
            }

            ConvertAndInitialize(azgaarMap);
        }

        private void LoadMapFromFile()
        {
            // Application.dataPath is unity/Assets, so go up two levels to reach project root
            string filePath = System.IO.Path.Combine(Application.dataPath, "..", "..", "reference", mapFileName);

            if (!System.IO.File.Exists(filePath))
            {
                Debug.LogError($"Map file not found: {filePath}");
                return;
            }

            var azgaarMap = AzgaarParser.ParseFile(filePath);
            ConvertAndInitialize(azgaarMap);
        }

        private void ConvertAndInitialize(AzgaarMap azgaarMap)
        {
            Debug.Log($"Parsed map: {azgaarMap.info.mapName}");
            Debug.Log($"  Dimensions: {azgaarMap.info.width}x{azgaarMap.info.height}");
            Debug.Log($"  Cells: {azgaarMap.pack.cells.Count}");
            Debug.Log($"  States: {azgaarMap.pack.states.Count}");
            Debug.Log($"  Provinces: {azgaarMap.pack.provinces.Count}");
            Debug.Log($"  Rivers: {azgaarMap.pack.rivers.Count}");
            Debug.Log($"  Burgs: {azgaarMap.pack.burgs.Count}");

            // Convert to simulation data
            MapData = MapConverter.Convert(azgaarMap);

            Debug.Log($"Converted map: {MapData.Info.Name}");
            Debug.Log($"  Land cells: {MapData.Info.LandCells} / {MapData.Info.TotalCells}");
            Debug.Log($"  States: {MapData.States.Count}");
            Debug.Log($"  Provinces: {MapData.Provinces.Count}");

            // Ensure we have a directional light for terrain relief
            EnsureDirectionalLight();

            // Initialize renderer
            if (mapView != null)
            {
                mapView.Initialize(MapData);

                // Fit camera to land bounds
                if (mapCamera != null)
                {
                    var landBounds = mapView.GetLandBounds();
                    mapCamera.FitToBounds(landBounds, 0.1f);  // 10% margin
                }
            }
            else
            {
                Debug.LogWarning("MapView not assigned to GameManager");
            }

            // Initialize simulation (auto-registers ProductionSystem + ConsumptionSystem)
            _simulation = new SimulationRunner(MapData);
            _simulation.IsPaused = true;  // Start paused

            // Provide economy state to map view for market mode and roads
            if (mapView != null)
            {
                var economy = _simulation.GetState().Economy;
                mapView.SetEconomyState(economy);
                mapView.SetRoadState(economy.Roads);
            }

            Debug.Log("Simulation initialized (paused). Press P to unpause, -/= to change speed.");
        }

        private void Update()
        {
            HandleInput();
            _simulation?.Tick(Time.deltaTime);
            LogDayChange();
        }

        private void HandleInput()
        {
            if (_simulation == null) return;

            // P: Toggle pause
            if (Input.GetKeyDown(KeyCode.P))
            {
                _simulation.IsPaused = !_simulation.IsPaused;
                Debug.Log(_simulation.IsPaused ? "Paused" : $"Running (speed: {_simulation.TimeScale}x)");
            }

            // -/=: Decrease/increase speed
            if (Input.GetKeyDown(KeyCode.Minus))
            {
                DecreaseSpeed();
            }
            if (Input.GetKeyDown(KeyCode.Equals))
            {
                IncreaseSpeed();
            }
        }

        private static readonly float[] SpeedPresets =
        {
            SimulationConfig.Speed.Slow,
            SimulationConfig.Speed.Normal,
            SimulationConfig.Speed.Fast,
            SimulationConfig.Speed.Ultra
        };
        private static readonly string[] SpeedNames = { "Slow", "Normal", "Fast", "Ultra" };

        private void DecreaseSpeed()
        {
            int index = System.Array.IndexOf(SpeedPresets, _simulation.TimeScale);
            if (index > 0)
            {
                _simulation.TimeScale = SpeedPresets[index - 1];
                Debug.Log($"Speed: {SpeedNames[index - 1]} ({SpeedPresets[index - 1]} days/sec)");
            }
        }

        private void IncreaseSpeed()
        {
            int index = System.Array.IndexOf(SpeedPresets, _simulation.TimeScale);
            if (index < SpeedPresets.Length - 1)
            {
                _simulation.TimeScale = SpeedPresets[index + 1];
                Debug.Log($"Speed: {SpeedNames[index + 1]} ({SpeedPresets[index + 1]} days/sec)");
            }
        }

        /// <summary>
        /// Ensures a directional light exists for terrain relief shadows.
        /// Uses a fixed low angle for consistent relief shading.
        /// </summary>
        private void EnsureDirectionalLight()
        {
            // Set ambient light
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = Color.black;
            RenderSettings.ambientIntensity = 0f;

            // Check if a directional light already exists
            foreach (var existing in FindObjectsOfType<Light>())
            {
                if (existing.type == LightType.Directional)
                {
                    Debug.Log("Using existing directional light");
                    return;
                }
            }

            // Create a fixed directional light with low angle for terrain relief
            var lightObj = new GameObject("Sun");
            lightObj.transform.rotation = Quaternion.Euler(20f, 45f, 0f);  // Low angle from northwest

            var sun = lightObj.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.97f, 0.9f);
            sun.intensity = 1.5f;
            sun.shadows = LightShadows.Soft;

            Debug.Log("Created directional light for terrain relief");
        }

        private void LogDayChange()
        {
            if (_simulation == null) return;

            var state = _simulation.GetState();
            if (state.CurrentDay != _lastLoggedDay)
            {
                // Log every 10 days to avoid spam
                if (state.CurrentDay % 10 == 0)
                {
                    Debug.Log($"Day {state.CurrentDay}");
                }

                // Refresh road rendering weekly (same frequency as trade)
                if (state.CurrentDay % 7 == 0 && mapView != null)
                {
                    mapView.RefreshRoads();
                }

                _lastLoggedDay = state.CurrentDay;
            }
        }
    }
}
