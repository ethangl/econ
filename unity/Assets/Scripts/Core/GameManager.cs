using System;
using System.Collections;
using System.IO;
using UnityEngine;
using EconSim.Core.Data;
using EconSim.Core.Common;
using EconSim.Core.Import;
using EconSim.Core.Actors;
using EconSim.Core.Economy;
using EconSim.Core.Religious;
using EconSim.Core.Simulation;
using EconSim.Renderer;
using EconSim.Camera;
using MapGen.Core;
using WorldGen.Core;
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
        /// Fired when globe generation completes with a site selected.
        /// The startup screen should switch to site review mode.
        /// </summary>
        public static event Action<SiteContext> OnGlobeReady;

        /// <summary>
        /// Fired when the active site changes via cycling (Prev/Next).
        /// Args: site, currentIndex (0-based), totalCount.
        /// </summary>
        public static event Action<SiteContext, int, int> OnSiteChanged;

        /// <summary>
        /// True after the map has been loaded and simulation initialized.
        /// </summary>
        public static bool IsMapReady { get; private set; }
        public static bool HasLastMapCache => File.Exists(GetLastMapPayloadPath());

        /// <summary>
        /// The site context from the most recent globe generation, or null.
        /// </summary>
        public SiteContext CurrentSite { get; private set; }

        /// <summary>Number of candidate sites from last globe generation.</summary>
        public int SiteCount => _sites?.Count ?? 0;

        /// <summary>Current site index (0-based) within the candidates list.</summary>
        public int CurrentSiteIndex { get; private set; }

        private System.Collections.Generic.List<SiteContext> _sites;
        private ISimulation _simulation;
        private Coroutine deferredStartupWorkRoutine;
        private int _globeSeed;
        private GameObject _sphereViewObj;
        private GlobalTradeContext _pendingTradeContext;

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
        /// Generate a sphere (globe) using the WorldGen pipeline.
        /// Does NOT fire OnMapReady — fires OnGlobeReady instead so the startup
        /// screen can switch to site review mode.
        /// </summary>
        public void GenerateGlobe(int seed, float latitude = 50f)
        {
            _globeSeed = seed;

            // Map latitude (degrees from equator) to site selection band centered on it
            float latBandHalf = 15f;
            var config = new WorldGenConfig
            {
                Seed = seed,
                SiteLatitudeMin = Mathf.Max(0f, latitude - latBandHalf),
                SiteLatitudeMax = latitude + latBandHalf,
            };

            Debug.Log($"WorldGen: generating globe with seed={seed}, coarse={config.CoarseCellCount}, dense={config.DenseCellCount}, radius={config.Radius}");

            // Get or create SphereView (cached to survive SetActive(false))
            if (_sphereViewObj == null)
            {
                _sphereViewObj = new GameObject("SphereView");
                _sphereViewObj.AddComponent<MeshFilter>();
                _sphereViewObj.AddComponent<MeshRenderer>();
                _sphereViewObj.AddComponent<SphereView>();
            }
            _sphereViewObj.SetActive(true);

            var sphereView = _sphereViewObj.GetComponent<SphereView>();
            sphereView.Generate(config);

            // Store site candidates
            _sites = sphereView.Sites ?? new System.Collections.Generic.List<SiteContext>();
            CurrentSiteIndex = 0;
            CurrentSite = _sites.Count > 0 ? _sites[0] : null;

            // Hide flat map if visible
            if (mapView != null)
                mapView.gameObject.SetActive(false);

            // Switch camera: disable MapCamera, enable OrbitCamera
            if (mapCamera != null)
                mapCamera.enabled = false;

            var cam = UnityEngine.Camera.main;
            if (cam != null)
            {
                var orbitCam = cam.GetComponent<EconSim.Camera.OrbitCamera>();
                if (orbitCam == null)
                    orbitCam = cam.gameObject.AddComponent<EconSim.Camera.OrbitCamera>();
                orbitCam.enabled = true;
                orbitCam.Configure(Vector3.zero, sphereView.Radius);
            }

            // Signal globe ready (startup screen stays visible in review mode)
            OnGlobeReady?.Invoke(CurrentSite);
        }

        /// <summary>
        /// Generate a flat map from the currently selected globe site.
        /// Maps SiteType → HeightmapTemplateType, passes latitude through.
        /// </summary>
        public void GenerateMapFromSite()
        {
            if (CurrentSite == null)
            {
                Debug.LogWarning("GenerateMapFromSite: no site selected");
                return;
            }

            // Hide globe
            if (_sphereViewObj != null)
                _sphereViewObj.SetActive(false);

            // Re-enable flat map and camera
            if (mapView != null)
                mapView.gameObject.SetActive(true);
            if (mapCamera != null)
                mapCamera.enabled = true;

            var cam = UnityEngine.Camera.main;
            if (cam != null)
            {
                var orbitCam = cam.GetComponent<EconSim.Camera.OrbitCamera>();
                if (orbitCam != null)
                    orbitCam.enabled = false;

                // Reset clip planes from globe-scale back to map-scale defaults
                cam.nearClipPlane = 0.3f;
                cam.farClipPlane = 5000f;
            }

            _pendingTradeContext = BuildGlobalTradeContext(CurrentSite);

            var config = new MapGenConfig
            {
                Seed = _globeSeed,
                CellCount = 100000,
                AspectRatio = 1.5f,
                Template = MapTemplateForSiteType(CurrentSite.SiteType),
                Latitude = CurrentSite.Latitude,
                Longitude = CurrentSite.Longitude,
                Tectonics = BuildTectonicHints(CurrentSite),
            };

            Debug.Log($"GenerateMapFromSite: type={CurrentSite.SiteType} → template={config.Template}, " +
                $"lat={config.Latitude:F1}°, lng={config.Longitude:F1}°, " +
                $"convergence={config.Tectonics.ConvergenceMagnitude:F2}, coastDir=({config.Tectonics.CoastDirectionX:F2},{config.Tectonics.CoastDirectionY:F2}), " +
                $"boundaryHops={config.Tectonics.BoundaryDistanceHops}, oceanAnomaly={config.Tectonics.OceanCurrentAnomalyC:F1}°C, " +
                $"moistureBias={config.Tectonics.MoistureBias:F2}, wind=({config.Tectonics.WindDirectionX:F2},{config.Tectonics.WindDirectionY:F2}), " +
                $"continentalNeighbors={CurrentSite.ContinentalNeighbors?.Count ?? 0}");
            GenerateMap(config);
        }

        /// <summary>
        /// Cycle to the next or previous candidate site.
        /// delta=+1 for next, -1 for previous.
        /// </summary>
        public void CycleSite(int delta)
        {
            if (_sites == null || _sites.Count <= 1) return;

            CurrentSiteIndex = ((CurrentSiteIndex + delta) % _sites.Count + _sites.Count) % _sites.Count;
            CurrentSite = _sites[CurrentSiteIndex];

            // Update globe highlight
            if (_sphereViewObj != null)
            {
                var sphereView = _sphereViewObj.GetComponent<SphereView>();
                sphereView?.SetActiveSite(CurrentSite);
            }

            OnSiteChanged?.Invoke(CurrentSite, CurrentSiteIndex, _sites.Count);
        }

        private static HeightmapTemplateType MapTemplateForSiteType(SiteType siteType)
        {
            return siteType switch
            {
                SiteType.Volcanic => HeightmapTemplateType.Volcano,
                SiteType.HighIsland => HeightmapTemplateType.HighIsland,
                SiteType.LowIsland => HeightmapTemplateType.LowIsland,
                // Archipelago sites are tectonically interesting but the template
                // produces too little landmass for the economic sim. Use HighIsland.
                SiteType.Archipelago => HeightmapTemplateType.HighIsland,
                _ => HeightmapTemplateType.LowIsland,
            };
        }

        /// <summary>
        /// Project globe-space SiteContext into flat-map TectonicHints.
        /// CoastDirection (3D unit vector on sphere) is projected onto local
        /// east/north tangent-plane basis vectors at the site's lat/lng,
        /// then mapped to [0,1] where 0.5 = center = no bias.
        /// </summary>
        private static TectonicHints BuildTectonicHints(SiteContext site)
        {
            float latRad = site.Latitude * Mathf.Deg2Rad;
            float lngRad = site.Longitude * Mathf.Deg2Rad;
            float sinLat = Mathf.Sin(latRad);
            float cosLat = Mathf.Cos(latRad);
            float sinLng = Mathf.Sin(lngRad);
            float cosLng = Mathf.Cos(lngRad);

            // Local tangent-plane basis vectors at (lat, lng) on the unit sphere.
            // East: perpendicular to meridian, pointing east.
            var east = new WorldGen.Core.Vec3(-sinLng, 0f, cosLng);
            // North: tangent along meridian, pointing north.
            var north = new WorldGen.Core.Vec3(
                -sinLat * cosLng,
                cosLat,
                -sinLat * sinLng);

            var cd = site.CoastDirection;
            double coastE = cd.X * east.X + cd.Y * east.Y + cd.Z * east.Z;
            double coastN = cd.X * north.X + cd.Y * north.Y + cd.Z * north.Z;

            // Map to [0,1]: 0.5 = center (no bias), 0/1 = full bias left/right or bottom/top.
            float coastDirX = Mathf.Clamp01(0.5f + (float)coastE * 0.5f);
            float coastDirY = Mathf.Clamp01(0.5f + (float)coastN * 0.5f);

            return new TectonicHints
            {
                ConvergenceMagnitude = Math.Abs(site.BoundaryConvergence),
                CoastDirectionX = coastDirX,
                CoastDirectionY = coastDirY,
                BoundaryDistanceHops = site.BoundaryDistanceHops,
                OceanCurrentAnomalyC = site.OceanCurrentAnomaly,
                MoistureBias = site.MoistureBias,
                WindDirectionX = site.WindDirectionEast,
                WindDirectionY = site.WindDirectionNorth,
            };
        }

        private static GlobalTradeContext BuildGlobalTradeContext(SiteContext site)
        {
            int coastDist = site.CoastDistanceHops;
            float surcharge = Mathf.Clamp(0.01f + 0.005f * coastDist, 0.01f, 0.10f);

            int neighborCount = site.ContinentalNeighbors?.Count ?? 0;
            float volumeScale = 0.5f + 0.5f * Mathf.Min((float)neighborCount / 3f, 1f);

            int nearestHops = int.MaxValue;
            if (site.ContinentalNeighbors != null)
            {
                foreach (var cn in site.ContinentalNeighbors)
                {
                    if (cn.DistanceHops < nearestHops)
                        nearestHops = cn.DistanceHops;
                }
            }
            if (nearestHops == int.MaxValue)
                nearestHops = 0;

            Debug.Log($"GlobalTradeContext: surcharge={surcharge:F3}, volumeScale={volumeScale:F2}, " +
                $"nearestContinent={nearestHops}hops, neighborCount={neighborCount}");

            return new GlobalTradeContext
            {
                OverseasSurcharge = surcharge,
                TradeVolumeScale = volumeScale,
                NearestContinentHops = nearestHops,
                ContinentNeighborCount = neighborCount,
            };
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
                $"cells={config.CellCount}, template={config.Template}, " +
                $"riverThreshold={config.EffectiveRiverThreshold:0.0}, " +
                $"riverTrace={config.EffectiveRiverTraceThreshold:0.0}, " +
                $"minRiverVertices={config.EffectiveMinRiverVertices}");

            Profiler.Begin("MapGen Pipeline");
            var result = MapGenPipeline.Generate(config);
            Profiler.End();

            MapGenResult = result;

            Profiler.Begin("WorldGenImporter Convert");
            MapData = WorldGenImporter.Convert(result, generationContext);
            if (_pendingTradeContext != null)
            {
                MapData.Info.Trade = _pendingTradeContext;
                _pendingTradeContext = null;
            }
            Profiler.End();
            LogMapGenSummary(result, MapData);

            InitializeWithMapData(
                GetLastMapTexturesDirectory(),
                preferCachedOverlayTextures: false);
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
                GetLastMapTexturesDirectory(),
                preferCachedOverlayTextures: true);

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
            string overlayTextureCacheDirectory = null,
            bool preferCachedOverlayTextures = false)
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

            // Initialize simulation
            Profiler.Begin("Simulation Init");
            var runner = new SimulationRunner(MapData);
            runner.RegisterSystem(new ProductionSystem());
            runner.RegisterSystem(new ConsumptionSystem());
            runner.RegisterSystem(new FiscalSystem());
            runner.RegisterSystem(new InterRealmTradeSystem());

            runner.RegisterSystem(new PopulationSystem());
            runner.RegisterSystem(new SpoilageSystem());
            _simulation = runner;

            // Bootstrap actors and peerage
            var simStateForActors = _simulation.GetState();
            simStateForActors.Actors = ActorBootstrap.Generate(MapData, MapData.Info.PopGenSeed);
            SimLog.Log("Actors", $"Bootstrapped {simStateForActors.Actors.ActorCount} actors, {simStateForActors.Actors.TitleCount} titles");

            // Bootstrap religion state (adherence from cell data)
            int maxCountyIdForReligion = 0;
            foreach (var county in MapData.Counties)
                if (county.Id > maxCountyIdForReligion) maxCountyIdForReligion = county.Id;
            simStateForActors.Religion = ReligionInitializer.Initialize(MapData, maxCountyIdForReligion);
            ReligionBootstrap.Generate(simStateForActors.Religion, MapData, simStateForActors.Actors, MapData.Info.PopGenSeed);

            // Register religion spread and tithe collection after religion state is initialized
            runner.RegisterSystem(new ReligionSpreadSystem());
            runner.RegisterSystem(new TitheSystem());

            Profiler.End();
            _simulation.IsPaused = true;  // Start paused

            // Provide road state and economy state to map view for rendering
            if (mapView != null)
            {
                var simState = _simulation.GetState();
                mapView.SetRoadState(simState.Roads);
                mapView.SetEconomyState(simState.Economy, simState.Transport);
                mapView.SetReligionState(simState.Religion);
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
            if (EconSim.UI.StartupScreenPanel.IsOpen) return;

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
            foreach (var existing in FindObjectsByType<Light>(FindObjectsSortMode.None))
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
