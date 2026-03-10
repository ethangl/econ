using EconSim.Core.Data;
using EconSim.Core.Simulation;
using EconSim.Core.Simulation.Systems;

namespace EconSim.Core.Economy.V4
{
    /// <summary>
    /// V4 economy tick system. Runs all 4 phases each day.
    /// Phase 0: shell only — all phases are empty stubs.
    /// </summary>
    public class EconomyTickV4 : ITickSystem
    {
        public string Name => "EconomyV4";
        public int TickInterval => 1;

        private EconomyStateV4 _econ;

        public void Initialize(SimulationState state, MapData mapData)
        {
            _econ = EconomyInitializerV4.Initialize(state, mapData);
            state.EconomyV4 = _econ;
        }

        public void Tick(SimulationState state, MapData mapData)
        {
            GenerateOrders(state, mapData);
            ResolveMarkets(state, mapData);
            UpdateMoney(state, mapData);
            UpdateSatisfaction(state, mapData);
        }

        /// <summary>
        /// Phase 1: Generate buy/sell orders from all economic actors.
        /// Stub — implemented in Phase 1+.
        /// </summary>
        void GenerateOrders(SimulationState state, MapData mapData)
        {
            // Clear order books
            for (int m = 1; m <= _econ.MarketCount; m++)
                _econ.Markets[m].Orders.Clear();
        }

        /// <summary>
        /// Phase 2: Resolve each market — compute clearing prices, fill orders.
        /// Stub — implemented in Phase 2.
        /// </summary>
        void ResolveMarkets(SimulationState state, MapData mapData)
        {
        }

        /// <summary>
        /// Phase 3: Update money supply — minting, stipends, wages, wear.
        /// Stub — implemented in Phase 2.
        /// </summary>
        void UpdateMoney(SimulationState state, MapData mapData)
        {
        }

        /// <summary>
        /// Phase 4: Compute per-class satisfaction from order fulfillment.
        /// Stub — implemented in Phase 5.
        /// </summary>
        void UpdateSatisfaction(SimulationState state, MapData mapData)
        {
        }
    }
}
