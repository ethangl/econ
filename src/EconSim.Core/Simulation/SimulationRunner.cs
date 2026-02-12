using System;
using System.Collections.Generic;
using System.IO;
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
        private const int BootstrapCacheVersion = 1;
        private const string BootstrapCacheFileName = "simulation_bootstrap.bin";

        private readonly MapData _mapData;
        private readonly SimulationState _state;
        private readonly List<ITickSystem> _systems;
        private readonly float _marketZoneMaxTransportCost;
        private readonly string _bootstrapCachePath;
        private readonly int _cacheRootSeed;
        private readonly int _cacheMapGenSeed;
        private readonly int _cacheEconomySeed;
        private float _accumulator;

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

            // Register core systems (order matters!)
            RegisterSystem(new ProductionSystem());
            RegisterSystem(new ConsumptionSystem());
            RegisterSystem(new TradeSystem());
            RegisterSystem(new TheftSystem());
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

            while (_accumulator >= secondsPerDay)
            {
                _accumulator -= secondsPerDay;
                ProcessTick();
            }
        }

        private void ProcessTick()
        {
            _state.CurrentDay++;
            _state.TotalTicksProcessed++;

            // Run each system if it's time for it to tick
            foreach (var system in _systems)
            {
                if (_state.CurrentDay % system.TickInterval == 0)
                {
                    system.Tick(_state, _mapData);
                }
            }
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

                int rootSeed = reader.ReadInt32();
                int mapGenSeed = reader.ReadInt32();
                int economySeed = reader.ReadInt32();
                int countyCount = reader.ReadInt32();
                int cellCount = reader.ReadInt32();
                bool staticNetworkBuilt = reader.ReadBoolean();

                if (!IsSimulationBootstrapCacheCompatible(rootSeed, mapGenSeed, economySeed, countyCount, cellCount, staticNetworkBuilt))
                    return false;

                _state.Economy.Markets.Clear();

                int marketCount = reader.ReadInt32();
                for (int i = 0; i < marketCount; i++)
                {
                    int marketId = reader.ReadInt32();
                    int locationCellId = reader.ReadInt32();
                    string name = reader.ReadString();
                    int typeRaw = reader.ReadInt32();
                    float suitabilityScore = reader.ReadSingle();

                    if (marketId < 0)
                        continue;

                    MarketType type = typeRaw == (int)MarketType.Black
                        ? MarketType.Black
                        : MarketType.Legitimate;

                    var market = new Market
                    {
                        Id = marketId,
                        LocationCellId = locationCellId,
                        Name = string.IsNullOrWhiteSpace(name) ? $"Market {marketId}" : name,
                        Type = type,
                        SuitabilityScore = suitabilityScore
                    };

                    InitializeMarketGoods(market);
                    _state.Economy.Markets[market.Id] = market;
                }

                if (!_state.Economy.Markets.ContainsKey(EconomyState.BlackMarketId))
                {
                    InitializeBlackMarket(logInitialization: false);
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
                writer.Write(_cacheRootSeed);
                writer.Write(_cacheMapGenSeed);
                writer.Write(_cacheEconomySeed);
                writer.Write(_mapData?.Counties?.Count ?? 0);
                writer.Write(_mapData?.Cells?.Count ?? 0);
                writer.Write(staticNetworkBuilt);

                writer.Write(_state.Economy.Markets.Count);
                foreach (var market in _state.Economy.Markets.Values)
                {
                    writer.Write(market.Id);
                    writer.Write(market.LocationCellId);
                    writer.Write(market.Name ?? string.Empty);
                    writer.Write((int)market.Type);
                    writer.Write(market.SuitabilityScore);
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

                SimLog.Log("Bootstrap", $"Saved simulation bootstrap cache: {_bootstrapCachePath}");
            }
            catch (Exception ex)
            {
                SimLog.Log("Bootstrap", $"Failed to save simulation bootstrap cache: {ex.Message}");
            }
        }

        private static int ResolveEconomySeedForCache(MapData mapData, int? explicitSeed)
        {
            if (explicitSeed.HasValue)
                return explicitSeed.Value;

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
            // Initialize the black market first (ID 0, no physical location)
            InitializeBlackMarket();

            var usedCells = new HashSet<int>();
            var usedRealms = new HashSet<int>();
            int marketCount = 3;

            for (int i = 0; i < marketCount; i++)
            {
                int cellId = MarketPlacer.FindBestMarketLocation(
                    _mapData, _state.Transport, _state.Economy,
                    excludeCells: usedCells,
                    excludeRealms: usedRealms);

                if (cellId < 0) break;

                var cell = _mapData.CellById[cellId];
                var burg = cell.HasBurg
                    ? _mapData.Burgs.Find(b => b.Id == cell.BurgId)
                    : null;

                var market = new Market
                {
                    Id = i + 1,
                    LocationCellId = cellId,
                    Name = burg?.Name ?? $"Market {i + 1}",
                    SuitabilityScore = MarketPlacer.ComputeSuitability(cell, _mapData, _state.Transport, _state.Economy)
                };

                InitializeMarketGoods(market);

                // Compute zone using world-scale normalized transport budget.
                MarketPlacer.ComputeMarketZone(market, _mapData, _state.Transport, maxTransportCost: _marketZoneMaxTransportCost);
                _state.Economy.Markets[market.Id] = market;

                usedCells.Add(cellId);
                usedRealms.Add(cell.RealmId);

                var realmName = _mapData.RealmById.TryGetValue(cell.RealmId, out var realm)
                    ? realm.Name
                    : "Unknown";
                SimLog.Log("Market", $"Placed market '{market.Name}' at cell {cellId} in {realmName} (score: {market.SuitabilityScore:F1})");
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
                if (market.Type == MarketType.Black)
                    continue;

                MarketPlacer.ComputeMarketZone(market, _mapData, _state.Transport, maxTransportCost: _marketZoneMaxTransportCost);
            }

            _state.Economy.RebuildCellToMarketLookup();
        }

        /// <summary>
        /// Initialize the black market - a global underground market with no physical location.
        /// </summary>
        private void InitializeBlackMarket(bool logInitialization = true)
        {
            var blackMarket = new Market
            {
                Id = EconomyState.BlackMarketId,
                LocationCellId = -1,  // No physical location
                Name = "Black Market",
                Type = MarketType.Black,
                SuitabilityScore = 0
            };

            InitializeMarketGoods(blackMarket);
            _state.Economy.Markets[blackMarket.Id] = blackMarket;

            if (logInitialization)
            {
                SimLog.Log("Market", "Initialized black market (global, 2x base prices)");
            }
        }

        private void InitializeMarketGoods(Market market)
        {
            market.Goods.Clear();
            const float BlackMarketPriceMultiplier = 2.0f;
            bool isBlackMarket = market.Type == MarketType.Black;

            foreach (var good in _state.Economy.Goods.All)
            {
                float price = isBlackMarket ? good.BasePrice * BlackMarketPriceMultiplier : good.BasePrice;
                market.Goods[good.Id] = new MarketGoodState
                {
                    GoodId = good.Id,
                    BasePrice = price,
                    Price = price,
                    Supply = 0,
                    Demand = 0
                };
            }
        }
    }
}
