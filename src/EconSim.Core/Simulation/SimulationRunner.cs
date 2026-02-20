using System.Collections.Generic;
using System.Diagnostics;
using EconSim.Core.Common;
using EconSim.Core.Data;
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
        {
            _mapData = mapData;
            _state = new SimulationState();
            _systems = new List<ITickSystem>();
            _accumulator = 0f;

            // Initialize road state
            _state.Roads = new RoadState();

            // Initialize transport graph
            Profiler.Begin("TransportGraph");
            _state.Transport = new TransportGraph(mapData);
            _state.Transport.SetRoadState(_state.Roads);
            Profiler.End();
            SimLog.Log("Transport", "Transport graph initialized");

            // Build static transport backbone
            if (SimulationConfig.Roads.BuildStaticNetworkAtInit)
            {
                Profiler.Begin("StaticTransportBackbone");
                var stats = StaticTransportBackboneBuilder.Build(_state, _mapData);
                Profiler.End();
                SimLog.Log("Roads",
                    $"Static backbone: majors={stats.MajorCountyCount}/{stats.CandidateCountyCount}, " +
                    $"pairs={stats.RoutedPairCount}/{stats.RoutePairCount}, missing={stats.MissingPairCount}, " +
                    $"edges={stats.EdgeCount}, thresholds(path={stats.PathThreshold:F2}, road={stats.RoadThreshold:F2})");
            }
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
    }
}
