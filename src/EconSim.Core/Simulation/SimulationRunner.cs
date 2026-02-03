using System.Collections.Generic;
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
        private readonly MapData _mapData;
        private readonly SimulationState _state;
        private readonly List<ITickSystem> _systems;
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
        {
            _mapData = mapData;
            _state = new SimulationState();
            _systems = new List<ITickSystem>();
            _accumulator = 0f;

            // Initialize economy
            Profiler.Begin("EconomyInitializer");
            _state.Economy = EconomyInitializer.Initialize(mapData);
            Profiler.End();

            // Initialize transport graph
            Profiler.Begin("TransportGraph");
            _state.Transport = new TransportGraph(mapData);
            _state.Transport.SetRoadState(_state.Economy.Roads);
            Profiler.End();
            SimLog.Log("Transport", "Transport graph initialized");

            // Place markets (requires transport for accessibility scoring)
            Profiler.Begin("InitializeMarkets");
            InitializeMarkets();
            Profiler.End();

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

        private void InitializeMarkets()
        {
            // Initialize the black market first (ID 0, no physical location)
            InitializeBlackMarket();

            var usedCells = new HashSet<int>();
            var usedStates = new HashSet<int>();
            int marketCount = 3;

            for (int i = 0; i < marketCount; i++)
            {
                int cellId = MarketPlacer.FindBestMarketLocation(
                    _mapData, _state.Transport, _state.Economy,
                    excludeCells: usedCells,
                    excludeStates: usedStates);

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

                // Initialize market goods for all tradeable goods
                foreach (var good in _state.Economy.Goods.All)
                {
                    market.Goods[good.Id] = new MarketGoodState
                    {
                        GoodId = good.Id,
                        BasePrice = good.BasePrice,
                        Price = good.BasePrice,
                        Supply = 0,
                        Demand = 0
                    };
                }

                // Compute zone with generous transport cost budget
                MarketPlacer.ComputeMarketZone(market, _mapData, _state.Transport, maxTransportCost: 100f);
                _state.Economy.Markets[market.Id] = market;

                usedCells.Add(cellId);
                usedStates.Add(cell.StateId);

                var stateName = _mapData.StateById.TryGetValue(cell.StateId, out var state)
                    ? state.Name
                    : "Unknown";
                SimLog.Log("Market", $"Placed market '{market.Name}' at cell {cellId} in {stateName} (score: {market.SuitabilityScore:F1})");
            }

            // Build lookup table (assigns cells and counties to markets)
            _state.Economy.RebuildCellToMarketLookup();

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

        /// <summary>
        /// Initialize the black market - a global underground market with no physical location.
        /// </summary>
        private void InitializeBlackMarket()
        {
            var blackMarket = new Market
            {
                Id = EconomyState.BlackMarketId,
                LocationCellId = -1,  // No physical location
                Name = "Black Market",
                Type = MarketType.Black,
                SuitabilityScore = 0
            };

            // Initialize goods with 2x base price (black market premium)
            const float BlackMarketPriceMultiplier = 2.0f;
            foreach (var good in _state.Economy.Goods.All)
            {
                float blackMarketPrice = good.BasePrice * BlackMarketPriceMultiplier;
                blackMarket.Goods[good.Id] = new MarketGoodState
                {
                    GoodId = good.Id,
                    BasePrice = blackMarketPrice,
                    Price = blackMarketPrice,
                    Supply = 0,
                    Demand = 0
                };
            }

            _state.Economy.Markets[blackMarket.Id] = blackMarket;
            SimLog.Log("Market", "Initialized black market (global, 2x base prices)");
        }
    }
}
