using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using EconSim.Core.Common;
using EconSim.Core.Data;
using EconSim.Core.Economy;
using EconSim.Core.Simulation.Systems;
using EconSim.Core.Transport;
using Profiler = EconSim.Core.Common.StartupProfiler;

namespace EconSim.Core.Simulation
{
    /// <summary>
    /// Main simulation runner. Handles the tick loop and system execution.
    /// </summary>
    public class SimulationRunner : ISimulation
    {
        private const int BootstrapCacheVersion = 10;
        private const int BootstrapCacheHeaderMagic = unchecked((int)0xEC0A571C);
        private const string BootstrapCacheFileName = "simulation_bootstrap.bin";
        private static readonly int BootstrapCacheSchemaHash = ComputeBootstrapCacheSchemaHash();

        private readonly MapData _mapData;
        private readonly SimulationState _state;
        private readonly List<ITickSystem> _systems;
        private readonly float _marketZoneMaxTransportCost;
        private readonly string _bootstrapCachePath;
        private readonly int _cacheRootSeed;
        private readonly int _cacheMapGenSeed;
        private readonly int _cacheEconomySeed;
        private float _accumulator;
        private static readonly double TimestampTicksToMs = 1000d / Stopwatch.Frequency;

        public float TimeScale
        {
            get => _state.TimeScale;
            set => _state.TimeScale = value;
        }

        public bool IsPaused
        {
            get => !_state.IsRunning;
            set => _state.IsRunning = !value;
        }

        public SimulationRunner(MapData mapData)
            : this(mapData, null, null, false, mapData?.Info?.RootSeed ?? 0, mapData?.Info?.MapGenSeed ?? 0)
        {
        }

        public SimulationRunner(MapData mapData, WorldGenerationContext generationContext)
            : this(mapData, generationContext.EconomySeed, null, false, generationContext.RootSeed, generationContext.MapGenSeed)
        {
        }

        public SimulationRunner(
            MapData mapData,
            WorldGenerationContext generationContext,
            string bootstrapCacheDirectory,
            bool preferBootstrapCache = false)
            : this(
                mapData,
                generationContext.EconomySeed,
                bootstrapCacheDirectory,
                preferBootstrapCache,
                generationContext.RootSeed,
                generationContext.MapGenSeed)
        {
        }

        public SimulationRunner(MapData mapData, int? economySeed)
            : this(mapData, economySeed, null, false, mapData?.Info?.RootSeed ?? 0, mapData?.Info?.MapGenSeed ?? 0)
        {
        }

        public SimulationRunner(
            MapData mapData,
            int? economySeed,
            string bootstrapCacheDirectory,
            bool preferBootstrapCache = false)
            : this(mapData, economySeed, bootstrapCacheDirectory, preferBootstrapCache, mapData?.Info?.RootSeed ?? 0, mapData?.Info?.MapGenSeed ?? 0)
        {
        }

        private SimulationRunner(
            MapData mapData,
            int? economySeed,
            string bootstrapCacheDirectory,
            bool preferBootstrapCache,
            int rootSeed,
            int mapGenSeed)
        {
            _mapData = mapData;
            _state = new SimulationState();
            _systems = new List<ITickSystem>();
            _accumulator = 0f;

            _bootstrapCachePath = string.IsNullOrWhiteSpace(bootstrapCacheDirectory)
                ? null
                : Path.Combine(bootstrapCacheDirectory, BootstrapCacheFileName);
            _cacheRootSeed = rootSeed > 0 ? rootSeed : mapData?.Info?.RootSeed ?? 0;
            _cacheMapGenSeed = mapGenSeed > 0 ? mapGenSeed : mapData?.Info?.MapGenSeed ?? 0;
            _cacheEconomySeed = ResolveEconomySeedForCache(mapData, economySeed);
            _state.EconomySeed = _cacheEconomySeed;
            _state.EconomyRng = new Random(_cacheEconomySeed);

            // Initialize economy
            Profiler.Begin("EconomyInitializer");
            _state.Economy = EconomyInitializer.Initialize(mapData, economySeed);
            Profiler.End();

            // Initialize transport graph
            Profiler.Begin("TransportGraph");
            _state.Transport = new TransportGraph(mapData);
            _state.Transport.SetRoadState(_state.Economy.Roads);
            Profiler.End();
            SimLog.Log("Transport", "Transport graph initialized");

            _marketZoneMaxTransportCost = MarketPlacer.ResolveMarketZoneMaxTransportCost(_mapData);
            SimLog.Log("Market", $"Market zone transport budget: {_marketZoneMaxTransportCost:F1}");

            bool loadedBootstrapCache = false;
            if (preferBootstrapCache && !string.IsNullOrWhiteSpace(_bootstrapCachePath))
            {
                Profiler.Begin("LoadSimulationBootstrapCache");
                loadedBootstrapCache = TryLoadSimulationBootstrapCache();
                Profiler.End();
            }

            if (!loadedBootstrapCache)
            {
                // Place markets (requires transport for accessibility scoring)
                Profiler.Begin("InitializeMarkets");
                InitializeMarkets();
                Profiler.End();

                if (SimulationConfig.Roads.BuildStaticNetworkAtInit)
                {
                    Profiler.Begin("StaticTransportBackbone");
                    var stats = StaticTransportBackboneBuilder.Build(_state, _mapData);
                    RecomputeMarketZones();
                    Profiler.End();
                    SimLog.Log("Roads",
                        $"Static backbone: majors={stats.MajorCountyCount}/{stats.CandidateCountyCount}, " +
                        $"pairs={stats.RoutedPairCount}/{stats.RoutePairCount}, missing={stats.MissingPairCount}, " +
                        $"edges={stats.EdgeCount}, thresholds(path={stats.PathThreshold:F2}, road={stats.RoadThreshold:F2})");
                }

                TrySaveSimulationBootstrapCache(SimulationConfig.Roads.BuildStaticNetworkAtInit);
            }
            else
            {
                SimLog.Log("Bootstrap", $"Loaded simulation bootstrap cache: {_bootstrapCachePath}");
                LogMarketAssignmentSummary();

                if (SimulationConfig.Roads.BuildStaticNetworkAtInit)
                {
                    SimLog.Log(
                        "Roads",
                        $"Loaded static backbone cache: edges={_state.Economy.Roads.EdgeTraffic.Count}, " +
                        $"thresholds(path={_state.Economy.Roads.PathThreshold:F2}, road={_state.Economy.Roads.RoadThreshold:F2})");
                }
            }

            EconomyInitializer.BootstrapV2(_state, _mapData);
            RegisterSystem(new MarketSystem());
            RegisterSystem(new ProductionSystem());
            RegisterSystem(new OrderSystem());
            RegisterSystem(new WageSystem());
            RegisterSystem(new PriceSystem());
            RegisterSystem(new LaborSystem());
            RegisterSystem(new OffMapSupplySystem());
            RegisterSystem(new MigrationSystem());
            RegisterSystem(new TelemetrySystem());
        }

        /// <summary>
        /// Register a tick system. Systems execute in registration order.
        /// </summary>
        public void RegisterSystem(ITickSystem system)
        {
            system.Initialize(_state, _mapData);
            _systems.Add(system);
        }

        public void Tick(float deltaTime)
        {
            if (!_state.IsRunning || _state.TimeScale <= 0f)
                return;

            // Accumulator-based fixed timestep
            // Each "tick" represents one day
            float secondsPerDay = 1f / _state.TimeScale;
            _accumulator += deltaTime;
            int ticksProcessedThisFrame = 0;
            int tickBudget = SimulationConfig.MaxTicksPerFrame > 0
                ? SimulationConfig.MaxTicksPerFrame
                : int.MaxValue;

            while (_accumulator >= secondsPerDay && ticksProcessedThisFrame < tickBudget)
            {
                _accumulator -= secondsPerDay;
                ProcessTick();
                ticksProcessedThisFrame++;
            }
        }

        private void ProcessTick()
        {
            long tickStart = Stopwatch.GetTimestamp();
            _state.CurrentDay++;
            _state.TotalTicksProcessed++;

            // Run each system if it's time for it to tick
            foreach (var system in _systems)
            {
                if (_state.CurrentDay % system.TickInterval == 0)
                {
                    long systemStart = Stopwatch.GetTimestamp();
                    system.Tick(_state, _mapData);
                    float systemMs = ElapsedMs(systemStart);
                    _state.Performance.RecordSystem(system.Name, system.TickInterval, systemMs);
                }
            }

            _state.Performance.RecordTick(ElapsedMs(tickStart));
        }

        private static float ElapsedMs(long startTimestamp)
        {
            return (float)((Stopwatch.GetTimestamp() - startTimestamp) * TimestampTicksToMs);
        }

        public MapData GetMapData() => _mapData;
        public SimulationState GetState() => _state;

        private bool TryLoadSimulationBootstrapCache()
        {
            if (string.IsNullOrWhiteSpace(_bootstrapCachePath) || !File.Exists(_bootstrapCachePath))
                return false;

            try
            {
                using var stream = File.Open(_bootstrapCachePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var reader = new BinaryReader(stream);

                int version = reader.ReadInt32();
                if (version != BootstrapCacheVersion)
                    return false;

                int rootSeed;
                int markerOrRootSeed = reader.ReadInt32();
                if (markerOrRootSeed == BootstrapCacheHeaderMagic)
                {
                    int schemaHash = reader.ReadInt32();
                    if (schemaHash != BootstrapCacheSchemaHash)
                        return false;
                    rootSeed = reader.ReadInt32();
                }
                else
                {
                    // Legacy v7 cache files did not include header magic/schema hash.
                    rootSeed = markerOrRootSeed;
                }

                int mapGenSeed = reader.ReadInt32();
                int economySeed = reader.ReadInt32();
                int countyCount = reader.ReadInt32();
                int cellCount = reader.ReadInt32();
                float latitudeSouth = reader.ReadSingle();
                float latitudeNorth = reader.ReadSingle();
                bool staticNetworkBuilt = reader.ReadBoolean();

                if (!IsSimulationBootstrapCacheCompatible(
                    rootSeed,
                    mapGenSeed,
                    economySeed,
                    countyCount,
                    cellCount,
                    latitudeSouth,
                    latitudeNorth,
                    staticNetworkBuilt))
                    return false;

                _state.Economy.Markets.Clear();

                int marketCount = reader.ReadInt32();
                for (int i = 0; i < marketCount; i++)
                {
                    int marketId = reader.ReadInt32();
                    int locationCellId = reader.ReadInt32();
                    string name = reader.ReadString();
                    int typeRaw = reader.ReadInt32();
                    if (marketId < 0)
                        continue;

                    MarketType type = typeRaw == (int)MarketType.OffMap
                        ? MarketType.OffMap
                        : MarketType.Legitimate;

                    var market = new Market
                    {
                        Id = marketId,
                        LocationCellId = locationCellId,
                        Name = string.IsNullOrWhiteSpace(name) ? $"Market {marketId}" : name,
                        Type = type
                    };

                    int zoneEntryCount = reader.ReadInt32();
                    if (zoneEntryCount > 0)
                    {
                        for (int zoneIndex = 0; zoneIndex < zoneEntryCount; zoneIndex++)
                        {
                            int zoneCellId = reader.ReadInt32();
                            float zoneCost = reader.ReadSingle();
                            market.ZoneCellIds.Add(zoneCellId);
                            market.ZoneCellCosts[zoneCellId] = zoneCost;
                        }
                    }

                    // Read OffMap market metadata (must come before InitializeMarketGoods)
                    if (market.Type == MarketType.OffMap)
                    {
                        market.OffMapPriceMultiplier = reader.ReadSingle();
                        int goodCount = reader.ReadInt32();
                        market.OffMapGoodIds = new HashSet<string>(goodCount);
                        for (int gi = 0; gi < goodCount; gi++)
                            market.OffMapGoodIds.Add(reader.ReadString());
                    }

                    InitializeMarketGoods(market);
                    _state.Economy.Markets[market.Id] = market;
                }

                _state.Economy.CountyToMarket.Clear();

                int countyToMarketCount = reader.ReadInt32();
                for (int i = 0; i < countyToMarketCount; i++)
                {
                    int countyId = reader.ReadInt32();
                    int marketId = reader.ReadInt32();

                    if (countyId <= 0 || marketId <= 0)
                        continue;

                    if (_state.Economy.Counties.ContainsKey(countyId) && _state.Economy.Markets.ContainsKey(marketId))
                    {
                        _state.Economy.CountyToMarket[countyId] = marketId;
                    }
                }
                _state.Economy.RebuildCellToMarketFromCountyLookup();

                float pathThreshold = reader.ReadSingle();
                float roadThreshold = reader.ReadSingle();

                int roadEdgeCount = reader.ReadInt32();
                var edgeTraffic = new Dictionary<(int, int), float>(Math.Max(roadEdgeCount, 0));
                for (int i = 0; i < roadEdgeCount; i++)
                {
                    int cellA = reader.ReadInt32();
                    int cellB = reader.ReadInt32();
                    float traffic = reader.ReadSingle();

                    if (!SimulationConfig.Roads.BuildStaticNetworkAtInit || !staticNetworkBuilt)
                        continue;

                    if (cellA <= 0 || cellB <= 0 || cellA == cellB || traffic <= 0f)
                        continue;

                    var key = RoadState.NormalizeKey(cellA, cellB);
                    if (edgeTraffic.TryGetValue(key, out float existing))
                        edgeTraffic[key] = existing + traffic;
                    else
                        edgeTraffic[key] = traffic;
                }

                if (SimulationConfig.Roads.BuildStaticNetworkAtInit && staticNetworkBuilt)
                {
                    _state.Economy.Roads.ApplyStaticTraffic(edgeTraffic, pathThreshold, roadThreshold);
                }
                else
                {
                    _state.Economy.Roads.ApplyStaticTraffic(new Dictionary<(int, int), float>(), 1f, 2f);
                }

                _state.Transport.SetRoadState(_state.Economy.Roads);
                return true;
            }
            catch (Exception ex)
            {
                SimLog.Log("Bootstrap", $"Failed to load simulation bootstrap cache: {ex.Message}");
                return false;
            }
        }

        private bool IsSimulationBootstrapCacheCompatible(
            int rootSeed,
            int mapGenSeed,
            int economySeed,
            int countyCount,
            int cellCount,
            float latitudeSouth,
            float latitudeNorth,
            bool staticNetworkBuilt)
        {
            if (countyCount != (_mapData?.Counties?.Count ?? 0))
                return false;

            if (cellCount != (_mapData?.Cells?.Count ?? 0))
                return false;

            if (_cacheRootSeed > 0 && rootSeed > 0 && _cacheRootSeed != rootSeed)
                return false;

            if (_cacheMapGenSeed > 0 && mapGenSeed > 0 && _cacheMapGenSeed != mapGenSeed)
                return false;

            if (_cacheEconomySeed > 0 && economySeed > 0 && _cacheEconomySeed != economySeed)
                return false;

            if (!CacheLatitudeMatches(latitudeSouth, _mapData?.Info?.World != null ? _mapData.Info.World.LatitudeSouth : float.NaN))
                return false;

            if (!CacheLatitudeMatches(latitudeNorth, _mapData?.Info?.World != null ? _mapData.Info.World.LatitudeNorth : float.NaN))
                return false;

            if (SimulationConfig.Roads.BuildStaticNetworkAtInit != staticNetworkBuilt)
                return false;

            return true;
        }

        private void TrySaveSimulationBootstrapCache(bool staticNetworkBuilt)
        {
            if (string.IsNullOrWhiteSpace(_bootstrapCachePath))
                return;

            try
            {
                string directory = Path.GetDirectoryName(_bootstrapCachePath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                using var stream = File.Open(_bootstrapCachePath, FileMode.Create, FileAccess.Write, FileShare.None);
                using var writer = new BinaryWriter(stream);

                writer.Write(BootstrapCacheVersion);
                writer.Write(BootstrapCacheHeaderMagic);
                writer.Write(BootstrapCacheSchemaHash);
                WriteBootstrapCachePayload(writer, staticNetworkBuilt);

                SimLog.Log("Bootstrap", $"Saved simulation bootstrap cache: {_bootstrapCachePath}");
            }
            catch (Exception ex)
            {
                SimLog.Log("Bootstrap", $"Failed to save simulation bootstrap cache: {ex.Message}");
            }
        }

        private void WriteBootstrapCachePayload(BinaryWriter writer, bool staticNetworkBuilt)
        {
            writer.Write(_cacheRootSeed);
            writer.Write(_cacheMapGenSeed);
            writer.Write(_cacheEconomySeed);
            writer.Write(_mapData?.Counties?.Count ?? 0);
            writer.Write(_mapData?.Cells?.Count ?? 0);
            writer.Write(_mapData?.Info?.World != null ? _mapData.Info.World.LatitudeSouth : float.NaN);
            writer.Write(_mapData?.Info?.World != null ? _mapData.Info.World.LatitudeNorth : float.NaN);
            writer.Write(staticNetworkBuilt);

            writer.Write(_state.Economy.Markets.Count);
            foreach (var market in _state.Economy.Markets.Values)
            {
                writer.Write(market.Id);
                writer.Write(market.LocationCellId);
                writer.Write(market.Name ?? string.Empty);
                writer.Write((int)market.Type);
                int zoneEntryCount = market.ZoneCellCosts?.Count ?? 0;
                writer.Write(zoneEntryCount);
                if (zoneEntryCount > 0)
                {
                    foreach (var zoneEntry in market.ZoneCellCosts)
                    {
                        writer.Write(zoneEntry.Key);
                        writer.Write(zoneEntry.Value);
                    }
                }

                // OffMap market metadata
                if (market.Type == MarketType.OffMap)
                {
                    writer.Write(market.OffMapPriceMultiplier);
                    int goodCount = market.OffMapGoodIds?.Count ?? 0;
                    writer.Write(goodCount);
                    if (market.OffMapGoodIds != null)
                    {
                        foreach (var goodId in market.OffMapGoodIds)
                            writer.Write(goodId);
                    }
                }
            }

            writer.Write(_state.Economy.CountyToMarket.Count);
            foreach (var kvp in _state.Economy.CountyToMarket)
            {
                writer.Write(kvp.Key);
                writer.Write(kvp.Value);
            }

            writer.Write(_state.Economy.Roads.PathThreshold);
            writer.Write(_state.Economy.Roads.RoadThreshold);

            if (staticNetworkBuilt)
            {
                writer.Write(_state.Economy.Roads.EdgeTraffic.Count);
                foreach (var kvp in _state.Economy.Roads.EdgeTraffic)
                {
                    writer.Write(kvp.Key.Item1);
                    writer.Write(kvp.Key.Item2);
                    writer.Write(kvp.Value);
                }
            }
            else
            {
                writer.Write(0);
            }
        }

        private static int ComputeBootstrapCacheSchemaHash()
        {
            MethodInfo payloadWriter = typeof(SimulationRunner).GetMethod(
                nameof(WriteBootstrapCachePayload),
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (payloadWriter == null)
                return 0;

            MethodBody body = payloadWriter.GetMethodBody();
            if (body == null)
                return 0;

            byte[] ilBytes = body.GetILAsByteArray();
            if (ilBytes == null || ilBytes.Length == 0)
                return 0;

            unchecked
            {
                uint hash = 2166136261u;
                for (int i = 0; i < ilBytes.Length; i++)
                {
                    hash ^= ilBytes[i];
                    hash *= 16777619u;
                }

                return (int)hash;
            }
        }

        private static bool CacheLatitudeMatches(float cachedValue, float expectedValue)
        {
            bool cachedIsFinite = IsFinite(cachedValue);
            bool expectedIsFinite = IsFinite(expectedValue);

            if (cachedIsFinite != expectedIsFinite)
                return false;

            if (!cachedIsFinite)
                return true;

            return Math.Abs(cachedValue - expectedValue) <= 0.0001f;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static int ResolveEconomySeedForCache(MapData mapData, int? explicitSeed)
        {
            if (explicitSeed.HasValue)
                return explicitSeed.Value;

            if (SimulationConfig.EconomySeedOverride > 0)
                return SimulationConfig.EconomySeedOverride;

            if (mapData?.Info != null)
            {
                if (mapData.Info.EconomySeed > 0)
                    return mapData.Info.EconomySeed;

                if (mapData.Info.RootSeed > 0)
                    return WorldSeeds.FromRoot(mapData.Info.RootSeed).EconomySeed;

                if (!string.IsNullOrWhiteSpace(mapData.Info.Seed) && int.TryParse(mapData.Info.Seed, out int parsed))
                    return WorldSeeds.FromRoot(parsed).EconomySeed;
            }

            return 42;
        }

        private void InitializeMarkets()
        {
            _state.Economy.Markets.Clear();

            // Simplified model: single legitimate market for the whole world.
            int cellId = -1;
            string marketName = "World Market";
            for (int i = 0; i < _mapData.Realms.Count; i++)
            {
                var realm = _mapData.Realms[i];
                if (realm.Id <= 0)
                    continue;

                int candidateCellId = -1;
                if (realm.CapitalBurgId > 0)
                {
                    var capitalBurg = _mapData.Burgs.Find(b => b.Id == realm.CapitalBurgId);
                    if (capitalBurg != null)
                        candidateCellId = capitalBurg.CellId;
                }
                if (candidateCellId < 0)
                    candidateCellId = realm.CenterCellId;
                if (candidateCellId < 0 || !_mapData.CellById.ContainsKey(candidateCellId))
                    continue;

                cellId = candidateCellId;
                var cell = _mapData.CellById[cellId];
                var burg = cell.HasBurg ? _mapData.Burgs.Find(b => b.Id == cell.BurgId) : null;
                marketName = burg?.Name ?? realm.Name;
                break;
            }

            if (cellId < 0)
            {
                for (int i = 0; i < _mapData.Cells.Count; i++)
                {
                    var cell = _mapData.Cells[i];
                    if (!cell.IsLand)
                        continue;
                    cellId = cell.Id;
                    break;
                }
            }

            if (cellId > 0)
            {
                var market = new Market
                {
                    Id = 1,
                    LocationCellId = cellId,
                    Name = marketName
                };

                InitializeMarketGoods(market);
                MarketPlacer.ComputeMarketZone(market, _mapData, _state.Transport, maxTransportCost: _marketZoneMaxTransportCost);
                _state.Economy.Markets[market.Id] = market;
                SimLog.Log("Market", $"Placed single world market '{market.Name}' at cell {cellId}");
            }

            // Place off-map virtual markets at map edges
            int nextId = 1;
            foreach (var existing in _state.Economy.Markets.Values)
            {
                if (existing.Id >= nextId)
                    nextId = existing.Id + 1;
            }
            var offMapResult = OffMapMarketPlacer.Place(
                _mapData, _state.Economy, _state.Transport, nextId, _marketZoneMaxTransportCost);
            foreach (var offMapMarket in offMapResult.Markets)
            {
                // OffMapMarketPlacer pre-populates Goods and prices, but runtime IDs are unresolved
                // until the market is bound to the active goods registry.
                offMapMarket.BindGoods(_state.Economy.Goods);
                _state.Economy.Markets[offMapMarket.Id] = offMapMarket;
            }
            if (offMapResult.Markets.Count > 0)
            {
                SimLog.Log("Market",
                    $"Placed {offMapResult.Markets.Count} off-map markets offering {offMapResult.TotalGoodsOffered} goods");
            }

            // Build lookup table (assigns cells and counties to markets)
            _state.Economy.RebuildCellToMarketLookup();
            LogMarketAssignmentSummary();
        }

        private void LogMarketAssignmentSummary()
        {
            // Log distribution of counties per market
            var countiesPerMarket = new Dictionary<int, int>();
            foreach (var kvp in _state.Economy.CountyToMarket)
            {
                if (!countiesPerMarket.ContainsKey(kvp.Value))
                    countiesPerMarket[kvp.Value] = 0;
                countiesPerMarket[kvp.Value]++;
            }

            foreach (var market in _state.Economy.Markets.Values)
            {
                int count = countiesPerMarket.TryGetValue(market.Id, out var c) ? c : 0;
                SimLog.Log("Market", $"Market '{market.Name}' assigned {count} counties");
            }

            SimLog.Log("Market", $"Initialized {_state.Economy.Markets.Count} markets, {_state.Economy.CountyToMarket.Count} counties have market access");
        }

        private void RecomputeMarketZones()
        {
            foreach (var market in _state.Economy.Markets.Values)
            {
                MarketPlacer.ComputeMarketZone(market, _mapData, _state.Transport, maxTransportCost: _marketZoneMaxTransportCost);
            }

            _state.Economy.RebuildCellToMarketLookup();
        }

        private void InitializeMarketGoods(Market market)
        {
            market.BindGoods(_state.Economy.Goods);
            market.Goods.Clear();
            bool isOffMap = market.Type == MarketType.OffMap;

            foreach (var good in _state.Economy.Goods.All)
            {
                float price;
                if (isOffMap && market.OffMapGoodIds != null && market.OffMapGoodIds.Contains(good.Id))
                    price = good.BasePrice * market.OffMapPriceMultiplier;
                else
                    price = good.BasePrice;

                market.Goods[good.Id] = new MarketGoodState
                {
                    GoodId = good.Id,
                    RuntimeId = good.RuntimeId,
                    BasePrice = price,
                    Price = price,
                    Supply = 0,
                    Demand = 0
                };
            }

            market.RebuildRuntimeGoodIndex();
        }
    }
}
