using EconSim.Core.Data;

namespace EconSim.Core.Simulation
{
    /// <summary>
    /// Main interface for the simulation, used by the Unity frontend.
    /// </summary>
    public interface ISimulation
    {
        /// <summary>
        /// Advance the simulation by deltaTime seconds.
        /// Call this from Unity's Update loop.
        /// </summary>
        void Tick(float deltaTime);

        /// <summary>
        /// Get the current map data.
        /// </summary>
        MapData GetMapData();

        /// <summary>
        /// Get the current simulation state.
        /// </summary>
        SimulationState GetState();

        /// <summary>
        /// Current time scale (days per second).
        /// </summary>
        float TimeScale { get; set; }

        /// <summary>
        /// Whether the simulation is paused.
        /// </summary>
        bool IsPaused { get; set; }
    }
}
