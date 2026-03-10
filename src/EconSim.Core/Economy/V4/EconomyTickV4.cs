using System;
using EconSim.Core.Data;
using EconSim.Core.Simulation;
using EconSim.Core.Simulation.Systems;

namespace EconSim.Core.Economy.V4
{
    /// <summary>
    /// V4 economy tick system. Runs all 4 phases each day.
    /// Phase 1: subsistence economy — biome extraction, consumption, surplus, satisfaction.
    /// </summary>
    public class EconomyTickV4 : ITickSystem
    {
        public string Name => "EconomyV4";
        public int TickInterval => 1;

        // ── Subsistence consumption rates (kg/person/day) ──
        // Grassland wheat yield ≈ 1.28 → at 1.0 need that's ~128% of staple needs (target: 120-150%)
        const float StapleNeedPerCapita = 1.0f;
        const float SaltNeedPerCapita = 0.01f;
        const float TimberNeedPerCapita = 0.05f;

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
        /// Phase 1: Biome extraction → subsistence consumption → surplus.
        /// </summary>
        void GenerateOrders(SimulationState state, MapData mapData)
        {
            // Clear order books
            for (int m = 1; m <= _econ.MarketCount; m++)
                _econ.Markets[m].Orders.Clear();

            int gc = GoodsV4.Count;

            for (int i = 0; i < _econ.Counties.Length; i++)
            {
                var ce = _econ.Counties[i];
                if (ce == null) continue;

                float pop = ce.LowerCommonerPop;
                if (pop <= 0f) continue;

                // 1. Biome extraction: lower commoner pop × biome yield
                for (int g = 0; g < gc; g++)
                {
                    ce.Production[g] = pop * ce.Productivity[g];
                    ce.Consumption[g] = 0f;
                }

                // 2. Subsistence consumption — staples
                float totalStapleProd = 0f;
                for (int s = 0; s < GoodsV4.StapleGoods.Length; s++)
                    totalStapleProd += ce.Production[GoodsV4.StapleGoods[s]];

                float totalStapleNeed = pop * StapleNeedPerCapita;

                if (totalStapleProd > 0f && totalStapleNeed > 0f)
                {
                    // Consume proportionally from available staples, capped at need
                    float ratio = Math.Min(totalStapleNeed / totalStapleProd, 1.0f);
                    for (int s = 0; s < GoodsV4.StapleGoods.Length; s++)
                    {
                        int g = GoodsV4.StapleGoods[s];
                        ce.Consumption[g] = ce.Production[g] * ratio;
                    }
                }

                // 3. Subsistence consumption — salt and timber (consume up to need)
                int saltId = (int)GoodTypeV4.Salt;
                ce.Consumption[saltId] = Math.Min(ce.Production[saltId], pop * SaltNeedPerCapita);

                int timberId = (int)GoodTypeV4.Timber;
                ce.Consumption[timberId] = Math.Min(ce.Production[timberId], pop * TimberNeedPerCapita);

                // 4. Surplus = production − consumption
                for (int g = 0; g < gc; g++)
                    ce.Surplus[g] = ce.Production[g] - ce.Consumption[g];

                // 5. Deficit tracking
                ce.FoodDeficit = totalStapleProd < totalStapleNeed;
            }
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
        /// Phase 1: survival satisfaction only for lower commoners.
        /// </summary>
        void UpdateSatisfaction(SimulationState state, MapData mapData)
        {
            for (int i = 0; i < _econ.Counties.Length; i++)
            {
                var ce = _econ.Counties[i];
                if (ce == null) continue;

                float pop = ce.LowerCommonerPop;
                if (pop <= 0f) continue;

                // Survival satisfaction = total staple production / total staple need
                float totalStapleProd = 0f;
                for (int s = 0; s < GoodsV4.StapleGoods.Length; s++)
                    totalStapleProd += ce.Production[GoodsV4.StapleGoods[s]];

                float totalStapleNeed = pop * StapleNeedPerCapita;
                ce.LowerCommonerSatisfaction = totalStapleNeed > 0f
                    ? Math.Min(totalStapleProd / totalStapleNeed, 1.0f)
                    : 0f;
            }
        }
    }
}
