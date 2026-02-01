using EconSim.Core.Data;

namespace EconSim.Core.Simulation.Systems
{
    /// <summary>
    /// Minimal tick system for testing the tick loop.
    /// </summary>
    public class DebugTickSystem : ITickSystem
    {
        public string Name => "Debug";
        public int TickInterval => SimulationConfig.Intervals.Daily;

        /// <summary>
        /// Total number of ticks this system has processed.
        /// </summary>
        public int TickCount { get; private set; }

        public void Initialize(SimulationState state, MapData mapData)
        {
            TickCount = 0;
        }

        public void Tick(SimulationState state, MapData mapData)
        {
            TickCount++;
        }
    }
}
