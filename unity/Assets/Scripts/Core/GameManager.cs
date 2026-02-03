using UnityEngine;
using EconSim.Core.Import;
using EconSim.Core.Data;
using EconSim.Core.Common;
using EconSim.Core.Simulation;
using EconSim.Core.Simulation.Systems;
using EconSim.Renderer;
using EconSim.Camera;
using Profiler = EconSim.Core.Common.StartupProfiler;

namespace EconSim.Core
{
    /// <summary>
    /// Main entry point. Initializes map loading and wires together simulation and rendering.
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        [Header("Map Settings")]
        [SerializeField] private string mapFileName = "preston.json";
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
            Profiler.Reset();
            Profiler.Begin("Total Startup");
            LoadMap();
            Profiler.End();
            Profiler.LogResults();
        }

        private void LoadMap()
        {
            Debug.Log($"Loading map: {mapFileName}");

            if (loadFromResources)
            {
                // Load from Resources/Maps folder (no caching for embedded resources)
                string resourcePath = $"Maps/{System.IO.Path.GetFileNameWithoutExtension(mapFileName)}";
                var textAsset = Resources.Load<TextAsset>(resourcePath);

                if (textAsset == null)
                {
                    Debug.LogError($"Failed to load map from Resources: {resourcePath}");
                    Debug.Log("Trying to load from file path...");
                    LoadMapFromFile();
                    return;
                }

                Profiler.Begin("Parse JSON");
                var azgaarMap = AzgaarParser.Parse(textAsset.text);
                Profiler.End();

                Profiler.Begin("Convert Map");
                MapData = MapConverter.Convert(azgaarMap);
                Profiler.End();

                InitializeWithMapData();
            }
            else
            {
                LoadMapFromFile();
            }
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

            // Try to load from cache first
            string cacheDir = System.IO.Path.Combine(Application.persistentDataPath, "MapCache");

            Profiler.Begin("Load MapData");
            if (MapDataCache.TryLoad(filePath, cacheDir, out var cachedMapData))
            {
                Profiler.End();
                MapData = cachedMapData;
                InitializeWithMapData();
                return;
            }
            Profiler.End();

            // Cache miss - parse and convert
            Profiler.Begin("Parse JSON");
            var azgaarMap = AzgaarParser.ParseFile(filePath);
            Profiler.End();

            Profiler.Begin("Convert Map");
            MapData = MapConverter.Convert(azgaarMap);
            Profiler.End();

            // Save to cache for next time
            Profiler.Begin("Save MapData Cache");
            MapDataCache.Save(filePath, cacheDir, MapData);
            Profiler.End();

            InitializeWithMapData();
        }

        private void InitializeWithMapData()
        {
            Debug.Log($"Map loaded: {MapData.Info.Name}");
            Debug.Log($"  Dimensions: {MapData.Info.Width}x{MapData.Info.Height}");
            Debug.Log($"  Cells: {MapData.Cells.Count}");
            Debug.Log($"  States: {MapData.States.Count}");
            Debug.Log($"  Provinces: {MapData.Provinces.Count}");
            Debug.Log($"  Rivers: {MapData.Rivers.Count}");
            Debug.Log($"  Counties: {MapData.Counties.Count}");

            // Ensure we have a directional light for terrain relief
            EnsureDirectionalLight();

            // Initialize renderer
            if (mapView != null)
            {
                Profiler.Begin("MapView.Initialize");
                mapView.Initialize(MapData);
                Profiler.End();

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
            Profiler.Begin("Simulation Init");
            _simulation = new SimulationRunner(MapData);
            Profiler.End();
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
                // Day counter logging disabled for cleaner console
                // if (state.CurrentDay % 10 == 0)
                // {
                //     Debug.Log($"Day {state.CurrentDay}");
                // }

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
