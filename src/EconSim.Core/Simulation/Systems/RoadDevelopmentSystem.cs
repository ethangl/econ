using EconSim.Core.Common;
using EconSim.Core.Data;

namespace EconSim.Core.Simulation.Systems
{
    /// <summary>
    /// Converts accumulated traffic into infrastructure changes on a slow cadence.
    /// Roads are upgraded monthly instead of continuously.
    /// </summary>
    public class RoadDevelopmentSystem : ITickSystem
    {
        public string Name => "RoadDevelopment";
        public int TickInterval => SimulationConfig.Intervals.Monthly;

        public void Initialize(SimulationState state, MapData mapData)
        {
            SimLog.Log("Roads", "Road development system initialized (monthly)");
        }

        public void Tick(SimulationState state, MapData mapData)
        {
            var roads = state.Economy?.Roads;
            if (roads == null) return;

            int changedEdges = roads.CommitPendingTraffic();
            if (changedEdges > 0)
            {
                // Transport graph uses road multipliers in edge costs.
                // Clear path cache only when tier changes are committed.
                state.Transport?.ClearCache();
                SimLog.Log("Roads", $"Day {state.CurrentDay}: committed road upgrades on {changedEdges} edges");
            }
        }
    }
}
