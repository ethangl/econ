using System;
using EconSim.Core.Economy;
using EconSim.Core.Transport;

namespace EconSim.Core.Simulation
{
    /// <summary>
    /// Current state of the simulation.
    /// </summary>
    public class SimulationState
    {
        /// <summary>
        /// Current simulation day (starts at 1).
        /// </summary>
        public int CurrentDay { get; set; } = 1;

        /// <summary>
        /// Whether the simulation is running (not paused).
        /// </summary>
        public bool IsRunning { get; set; }

        /// <summary>
        /// Current time scale (days per second).
        /// </summary>
        public float TimeScale { get; set; } = SimulationConfig.Speed.Normal; // 1 day/sec

        /// <summary>
        /// Total ticks processed since start (for debugging).
        /// </summary>
        public int TotalTicksProcessed { get; set; }

        /// <summary>
        /// Effective economy seed used for deterministic replay.
        /// </summary>
        public int EconomySeed { get; set; }

        /// <summary>
        /// Dedicated deterministic RNG stream for economy systems.
        /// </summary>
        public Random EconomyRng { get; set; }

        /// <summary>
        /// Economic state (goods, facilities, county economies).
        /// </summary>
        public EconomyState Economy { get; set; }

        /// <summary>
        /// Transport graph for pathfinding between cells.
        /// </summary>
        public TransportGraph Transport { get; set; }

        /// <summary>
        /// Current subsistence wage used by wage/labor systems.
        /// </summary>
        public float SubsistenceWage { get; set; }

        /// <summary>
        /// Smoothed 30-day basic basket cost EMA.
        /// </summary>
        public float SmoothedBasketCost { get; set; }

        /// <summary>
        /// End-of-day economy telemetry snapshot.
        /// </summary>
        public EconomyTelemetry Telemetry { get; set; } = new EconomyTelemetry();

        /// <summary>
        /// Runtime timing metrics for whole ticks and tick systems.
        /// </summary>
        public SimulationPerformanceStats Performance { get; set; } = new SimulationPerformanceStats();
    }
}
