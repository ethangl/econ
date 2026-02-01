using EconSim.Core.Data;

namespace EconSim.Core.Simulation.Systems
{
    /// <summary>
    /// Interface for simulation subsystems that run on the tick loop.
    /// </summary>
    public interface ITickSystem
    {
        /// <summary>
        /// Display name for debugging/logging.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// How often this system ticks. 1 = every tick, 7 = weekly, 30 = monthly, etc.
        /// </summary>
        int TickInterval { get; }

        /// <summary>
        /// Called once when the system is registered.
        /// </summary>
        void Initialize(SimulationState state, MapData mapData);

        /// <summary>
        /// Called every TickInterval days.
        /// </summary>
        void Tick(SimulationState state, MapData mapData);
    }
}
