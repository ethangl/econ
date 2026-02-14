using System;
using System.Collections;
using System.IO;
using UnityEngine;
using EconSim.Core.Data;
using EconSim.Core.Common;
using EconSim.Core.Import;
using EconSim.Core.Simulation;
using EconSim.Renderer;
using EconSim.Camera;
using MapGen.Core;
using Profiler = EconSim.Core.Common.StartupProfiler;

namespace EconSim.Core
{
    public enum MapGenerationMode
    {
        Default = 0
    }

    /// <summary>
    /// Main entry point. Generates map and wires together simulation and rendering.
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private MapView mapView;
        [SerializeField] private MapCamera mapCamera;
        [Header("Generation")]
        [SerializeField] private MapGenerationMode generationMode = MapGenerationMode.Default;

        public MapData MapData { get; private set; }
        public MapGenResult MapGenResult { get; private set; }
        public ISimulation Simulation => _simulation;
        public MapGenerationMode GenerationMode
        {
            get => generationMode;
            set => generationMode = value;
        }

        /// <summary>
        /// Fired when the map has finished loading and the simulation is ready.
        /// UI panels should subscribe to this to know when it's safe to access GameManager.Simulation.
        /// </summary>
        public static event Action OnMapReady;

        /// <summary>
        /// True after the map has been loaded and simulation initialized.
        /// </summary>
        public static bool IsMapReady { get; private set; }
        public static bool HasLastMapCache => File.Exists(GetLastMapPayloadPath());

        private ISimulation _simulation;
        private Coroutine deferredStartupWorkRoutine;

        public static GameManager Instance { get; private set; }

        private const int LastMapCacheVersion = 1;
        private const string LastMapCacheFolderName = "last-map";
        private const string LastMapPayloadFileName = "map_payload.json";
        private const string LastMapTexturesFolderName = "textures";

        [Serializable]
        private sealed class LastMapGenerationSettings
        {
            public int RootSeed;
            public int MapGenSeed;
            public int PopGenSeed;
            public int EconomySeed;
            public int SimulationSeed;
            public int CellCount;
            public float AspectRatio;
            public string Template;
            public string ContractVersion;
        }

        [Serializable]
        private sealed class LastMapCachePayload
        {
            public int Version;
            public string SavedAtUtc;
            public LastMapGenerationSettings Generation;
            public MapData MapData;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            // Initialize runtime domain logging sinks/filter defaults.
            DomainLoggingBootstrap.Initialize();
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
            IsMapReady = false;
            Profiler.Reset();
            Profiler.Begin("Total Startup");

            if (config == null)
            {
                config = new MapGenConfig
                {
                    CellCount = 60000,
                    Seed = UnityEngine.Random.Range(1, int.MaxValue)
                };
            }

            WorldGenerationContext generationContext = WorldGenerationContext.FromRootSeed(config.Seed);
            config.Seed = generationContext.MapGenSeed;

            Debug.Log(
                $"MapGen config: contract={generationContext.ContractVersion}, " +
                $"rootSeed={generationContext.RootSeed}, mapGenSeed={generationContext.MapGenSeed}, " +
                $"economySeed={generationContext.EconomySeed}, cells={config.CellCount}, template={config.Template}, " +
                $"riverThreshold={config.EffectiveRiverThreshold:0.0}, " +
                $"riverTrace={config.EffectiveRiverTraceThreshold:0.0}, " +
                $"minRiverVertices={config.EffectiveMinRiverVertices}");

            Profiler.Begin("MapGen Pipeline");
            var result = MapGenPipeline.Generate(config);
            Profiler.End();

            MapGenResult = result;

            Profiler.Begin("WorldGenImporter Convert");
            MapData = WorldGenImporter.Convert(result, generationContext);
            Profiler.End();
            LogMapGenSummary(result, MapData);

            InitializeWithMapData(
                generationContext,
                GetLastMapTexturesDirectory(),
                preferCachedOverlayTextures: false,
                preferCachedSimulationBootstrap: false);
            SaveLastMapCache(MapData, config, generationContext);

            Profiler.End();
            Profiler.LogResults();
        }

        public bool LoadLastMap()
        {
            IsMapReady = false;
            Profiler.Reset();
            Profiler.Begin("Load Cached Map");

            if (!TryLoadLastMapCache(out MapData cachedMapData, out WorldGenerationContext generationContext, out string error))
            {
                Debug.LogWarning(error);
                Profiler.End();
                return false;
            }

            MapGenResult = null;
            MapData = cachedMapData;
            Debug.Log($"Loading cached map: {GetLastMapPayloadPath()}");
            InitializeWithMapData(
                generationContext,
                GetLastMapTexturesDirectory(),
                preferCachedOverlayTextures: true,
                preferCachedSimulationBootstrap: true);

            Profiler.End();
            Profiler.LogResults();
            return true;
        }

        static void LogMapGenSummary(MapGenResult result, MapData runtimeMap)
        {
            if (result?.Elevation == null || result.Rivers == null)
                return;

            float landRatio = result.Elevation.LandRatio();
            int riverCount = result.Rivers.Rivers != null ? result.Rivers.Rivers.Length : 0;
            float p50Meters = Percentile(result.Elevation.ElevationMetersSigned, 0.5f);

            float runtimeLandRatio = 0f;
            if (runtimeMap?.Cells != null && runtimeMap.Cells.Count > 0)
                runtimeLandRatio = runtimeMap.Info.LandCells / (float)runtimeMap.Cells.Count;

            Debug.Log(
                $"MapGen summary: land={landRatio:0.000}, runtimeLand={runtimeLandRatio:0.000}, rivers={riverCount}, elevP50={p50Meters:0.0}m");
        }

        static float Percentile(float[] values, float q)
        {
            if (values == null || values.Length == 0)
                return 0f;

            var sorted = (float[])values.Clone();
            Array.Sort(sorted);

            if (q <= 0f) return sorted[0];
            if (q >= 1f) return sorted[sorted.Length - 1];

            float index = q * (sorted.Length - 1);
            int lo = (int)Mathf.Floor(index);
            int hi = (int)Mathf.Ceil(index);
            if (lo == hi)
                return sorted[lo];

            float t = index - lo;
            return sorted[lo] + (sorted[hi] - sorted[lo]) * t;
        }

        private void InitializeWithMapData(
            WorldGenerationContext generationContext,
            string overlayTextureCacheDirectory = null,
            bool preferCachedOverlayTextures = false,
            bool preferCachedSimulationBootstrap = false)
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
                mapView.Initialize(MapData, overlayTextureCacheDirectory, preferCachedOverlayTextures);
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
            _simulation = new SimulationRunner(
                MapData,
                generationContext,
                GetLastMapCacheDirectory(),
                preferCachedSimulationBootstrap);
            Profiler.End();
            _simulation.IsPaused = true;  // Start paused

            // Provide economy state to map view for market mode and roads
            if (mapView != null)
            {
                var economy = _simulation.GetState().Economy;
                mapView.SetEconomyState(economy);
                mapView.SetRoadState(economy.Roads);
            }

            Debug.Log("Simulation initialized (paused). Press Backspace to unpause, -/= to change speed.");

            // Mark map as ready and notify subscribers
            IsMapReady = true;
            OnMapReady?.Invoke();

            ScheduleDeferredStartupWork(preferCachedOverlayTextures);
        }


        private void Update()
        {
            HandleInput();
            _simulation?.Tick(Time.deltaTime);
        }

        private void HandleInput()
        {
            if (_simulation == null) return;

            // Backspace: Toggle pause
            if (Input.GetKeyDown(KeyCode.Backspace))
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

        private static string GetLastMapCacheDirectory()
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", "debug", LastMapCacheFolderName));
        }

        private static string GetLastMapPayloadPath()
        {
            return Path.Combine(GetLastMapCacheDirectory(), LastMapPayloadFileName);
        }

        private static string GetLastMapTexturesDirectory()
        {
            return Path.Combine(GetLastMapCacheDirectory(), LastMapTexturesFolderName);
        }

        private static void SaveLastMapCache(MapData mapData, MapGenConfig config, WorldGenerationContext generationContext)
        {
            if (mapData == null)
                return;

            try
            {
                Directory.CreateDirectory(GetLastMapCacheDirectory());

                var payload = new LastMapCachePayload
                {
                    Version = LastMapCacheVersion,
                    SavedAtUtc = DateTime.UtcNow.ToString("O"),
                    Generation = new LastMapGenerationSettings
                    {
                        RootSeed = generationContext.RootSeed,
                        MapGenSeed = generationContext.MapGenSeed,
                        PopGenSeed = generationContext.PopGenSeed,
                        EconomySeed = generationContext.EconomySeed,
                        SimulationSeed = generationContext.SimulationSeed,
                        CellCount = config != null ? config.CellCount : 0,
                        AspectRatio = config != null ? config.AspectRatio : 0f,
                        Template = config != null ? config.Template.ToString() : string.Empty,
                        ContractVersion = generationContext.ContractVersion
                    },
                    MapData = mapData
                };

                string payloadJson = JsonUtility.ToJson(payload, false);
                string payloadPath = GetLastMapPayloadPath();
                File.WriteAllText(payloadPath, payloadJson);
                Debug.Log($"Saved last map cache: {payloadPath}");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Failed to save last map cache: {ex.Message}");
            }
        }

        private static bool TryLoadLastMapCache(out MapData mapData, out WorldGenerationContext generationContext, out string error)
        {
            mapData = null;
            generationContext = default;
            error = null;

            string payloadPath = GetLastMapPayloadPath();
            if (!File.Exists(payloadPath))
            {
                error = $"No cached map exists at {payloadPath}";
                return false;
            }

            try
            {
                string payloadJson = File.ReadAllText(payloadPath);
                LastMapCachePayload payload = JsonUtility.FromJson<LastMapCachePayload>(payloadJson);
                if (payload == null || payload.MapData == null)
                {
                    error = $"Cached map payload is invalid: {payloadPath}";
                    return false;
                }

                mapData = payload.MapData;
                mapData.BuildLookups();

                int rootSeed = ResolveCachedRootSeed(payload, mapData);
                generationContext = WorldGenerationContext.FromRootSeed(rootSeed);
                return true;
            }
            catch (Exception ex)
            {
                error = $"Failed to load cached map from {payloadPath}: {ex.Message}";
                return false;
            }
        }

        private static int ResolveCachedRootSeed(LastMapCachePayload payload, MapData mapData)
        {
            int rootSeed = 0;
            if (payload?.Generation != null)
                rootSeed = payload.Generation.RootSeed;

            if (rootSeed <= 0 && mapData?.Info != null)
            {
                rootSeed = mapData.Info.RootSeed;
                if (rootSeed <= 0)
                {
                    int.TryParse(mapData.Info.Seed, out rootSeed);
                }
            }

            return rootSeed > 0 ? rootSeed : 1;
        }

        private void ScheduleDeferredStartupWork(bool validateCachedMap)
        {
            if (deferredStartupWorkRoutine != null)
            {
                StopCoroutine(deferredStartupWorkRoutine);
            }

            deferredStartupWorkRoutine = StartCoroutine(RunDeferredStartupWork(MapData, validateCachedMap));
        }

        private IEnumerator RunDeferredStartupWork(MapData loadedMap, bool validateCachedMap)
        {
            yield return null;

            if (loadedMap == null || loadedMap != MapData)
            {
                deferredStartupWorkRoutine = null;
                yield break;
            }

            if (mapView != null)
                yield return mapView.RunDeferredStartupWork();

            if (validateCachedMap)
            {
                try
                {
                    loadedMap.AssertElevationInvariants();
                    loadedMap.AssertWorldInvariants();
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"Deferred cached map invariant check failed: {ex.Message}");
                }
            }

            deferredStartupWorkRoutine = null;
        }

    }
}
