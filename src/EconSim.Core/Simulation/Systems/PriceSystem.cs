using EconSim.Core.Data;
using EconSim.Core.Economy;

namespace EconSim.Core.Simulation.Systems
{
    /// <summary>
    /// Economy V2 price adjustment system.
    /// </summary>
    public class PriceSystem : ITickSystem
    {
        private const float AdjustmentRate = 0.1f;
        private const float AdjustmentClamp = 0.5f;
        private const float MarketDepthEpsilon = 5f;
        private const float MaxDailyIncrease = 0.01f;
        private const float MaxDailyDecrease = 0.03f;
        private const float MinUpwardLiquidityGate = 0.15f;

        public string Name => "Prices";
        public int TickInterval => SimulationConfig.Intervals.Daily;

        public void Initialize(SimulationState state, MapData mapData)
        {
        }

        public void Tick(SimulationState state, MapData mapData)
        {
            var economy = state.Economy;
            if (economy == null)
                return;

            foreach (var market in economy.Markets.Values)
            {
                if (market.Type == MarketType.OffMap)
                    continue;

                foreach (var goodState in market.Goods.Values)
                {
                    float supply = goodState.Supply;
                    float demand = goodState.Demand;
                    float ratio = (demand + MarketDepthEpsilon) / (supply + MarketDepthEpsilon);

                    float adjustment = Clamp(ratio - 1f, -AdjustmentClamp, AdjustmentClamp);
                    float priceDelta = AdjustmentRate * adjustment;
                    if (priceDelta > 0f && demand > 0f)
                    {
                        float tradeFill = goodState.LastTradeVolume <= 0f
                            ? 0f
                            : Clamp(goodState.LastTradeVolume / demand, 0f, 1f);
                        float liquidityGate = MinUpwardLiquidityGate + ((1f - MinUpwardLiquidityGate) * tradeFill);
                        priceDelta *= liquidityGate;
                    }

                    priceDelta = Clamp(priceDelta, -MaxDailyDecrease, MaxDailyIncrease);
                    goodState.Price *= 1f + priceDelta;
                    if (goodState.Price < 0.0001f)
                        goodState.Price = 0.0001f;
                }
            }

            // Keep off-map prices fixed at the configured multiplier.
            foreach (var market in economy.Markets.Values)
            {
                if (market.Type != MarketType.OffMap)
                    continue;

                foreach (var goodState in market.Goods.Values)
                {
                    goodState.Price = goodState.BasePrice;
                }
            }
        }

        private static float Clamp(float value, float min, float max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }
}
