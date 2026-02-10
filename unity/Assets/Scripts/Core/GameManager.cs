using System;
using UnityEngine;
using EconSim.Core.Data;
using EconSim.Core.Common;
using EconSim.Core.Import;
using EconSim.Core.Simulation;
using EconSim.Core.Simulation.Systems;
using EconSim.Renderer;
using EconSim.Camera;
using MapGen.Core;
using Profiler = EconSim.Core.Common.StartupProfiler;

namespace EconSim.Core
{
    /// <summary>
    /// Main entry point. Generates map and wires together simulation and rendering.
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private MapView mapView;
        [SerializeField] private MapCamera mapCamera;

        public MapData MapData { get; private set; }
        public MapGenResult MapGenResult { get; private set; }
        public ISimulation Simulation => _simulation;

        /// <summary>
        /// Fired when the map has finished loading and the simulation is ready.
        /// UI panels should subscribe to this to know when it's safe to access GameManager.Simulation.
        /// </summary>
        public static event Action OnMapReady;

        /// <summary>
        /// True after the map has been loaded and simulation initialized.
        /// </summary>
        public static bool IsMapReady { get; private set; }

        private ISimulation _simulation;
        private int _lastLoggedDay;
        private int _lastRoadVisualRevision = -1;

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
            // Map generation is triggered by StartupScreenPanel
        }

        /// <summary>
        /// Generate a map procedurally using the MapGen pipeline.
        /// </summary>
        public void GenerateMap(MapGenConfig config = null)
        {
            Profiler.Reset();
            Profiler.Begin("Total Startup");

            config ??= new MapGenConfig
            {
                CellCount = 60000,
                Seed = UnityEngine.Random.Range(1, int.MaxValue)
            };

            Profiler.Begin("MapGen Pipeline");
            var result = MapGenPipeline.Generate(config);
            Profiler.End();

            MapGenResult = result;

            Profiler.Begin("MapGenAdapter Convert");
            MapData = MapGenAdapter.Convert(result);
            Profiler.End();

            // Update info with seed
            MapData.Info.Seed = config.Seed.ToString();

            InitializeWithMapData();

            Profiler.End();
            Profiler.LogResults();
        }

        private void InitializeWithMapData()
        {
            Debug.Log($"Map loaded: {MapData.Info.Name}");
            Debug.Log($"  Dimensions: {MapData.Info.Width}x{MapData.Info.Height}");
            Debug.Log($"  Cells: {MapData.Cells.Count}");
            Debug.Log($"  Realms: {MapData.Realms.Count}");
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
                _lastRoadVisualRevision = economy.Roads.Revision;
            }

            Debug.Log("Simulation initialized (paused). Press P to unpause, -/= to change speed.");

            // Mark map as ready and notify subscribers
            IsMapReady = true;
            OnMapReady?.Invoke();
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
            SimulationConfig.Speed.Ultra,
            SimulationConfig.Speed.Hyper
        };
        private static readonly string[] SpeedNames = { "Slow", "Normal", "Fast", "Ultra", "Hyper" };

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

                // Refresh road visuals only when committed road tiers changed.
                if (mapView != null)
                {
                    int currentRoadRevision = state.Economy.Roads.Revision;
                    if (currentRoadRevision != _lastRoadVisualRevision)
                    {
                        mapView.RefreshRoads();
                        _lastRoadVisualRevision = currentRoadRevision;
                    }
                }

                _lastLoggedDay = state.CurrentDay;
            }
        }

    }
}
