using System.Collections.Generic;
using EconSim.Core.Common;
using EconSim.Core.Data;
using EconSim.Core.Economy;
using EconSim.Core.Simulation.Systems;
using EconSim.Core.Transport;

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
            _state.Economy = EconomyInitializer.Initialize(mapData);

            // Initialize transport graph
            _state.Transport = new TransportGraph(mapData);
            SimLog.Log("Transport", "Transport graph initialized");

            // Place markets (requires transport for accessibility scoring)
            InitializeMarkets();

            // Register core systems (order matters!)
            RegisterSystem(new ProductionSystem());
            RegisterSystem(new ConsumptionSystem());
            RegisterSystem(new TradeSystem());
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
            // Place 1 market for v1
            var markets = MarketPlacer.PlaceMarkets(_mapData, _state.Transport, _state.Economy, count: 1);

            foreach (var market in markets)
            {
                // Compute zone with generous transport cost budget
                MarketPlacer.ComputeMarketZone(market, _mapData, _state.Transport, maxTransportCost: 100f);
                _state.Economy.Markets[market.Id] = market;
            }

            // Build lookup table
            _state.Economy.RebuildCellToMarketLookup();

            SimLog.Log("Market", $"Initialized {markets.Count} markets, {_state.Economy.CellToMarket.Count} cells have market access");
        }
    }
}
