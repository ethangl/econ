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
        /// Transport graph for pathfinding between cells.
        /// </summary>
        public TransportGraph Transport { get; set; }

        /// <summary>
        /// Road state (static backbone traffic and tiers).
        /// </summary>
        public RoadState Roads { get; set; }

        /// <summary>
        /// Runtime timing metrics for whole ticks and tick systems.
        /// </summary>
        public SimulationPerformanceStats Performance { get; set; } = new SimulationPerformanceStats();

        /// <summary>
        /// Economy state (production, consumption, stock per county).
        /// </summary>
        public EconomyState Economy { get; set; }
    }
}
