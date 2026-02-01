using EconSim.Core.Economy;

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
        /// Economic state (goods, facilities, county economies).
        /// </summary>
        public EconomyState Economy { get; set; }
    }
}
