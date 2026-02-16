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
        private const float MinMultiplier = 0.25f;
        private const float MaxMultiplier = 4f;

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
                if (market.Type == MarketType.OffMap || market.Type == MarketType.Black)
                    continue;

                foreach (var goodState in market.Goods.Values)
                {
                    float supply = goodState.Supply;
                    float demand = goodState.Demand;
                    float ratio = (demand + 0.1f) / (supply + 0.1f);

                    float adjustment = Clamp(ratio - 1f, -AdjustmentClamp, AdjustmentClamp);
                    goodState.Price *= 1f + (AdjustmentRate * adjustment);

                    float basePrice = goodState.BasePrice;
                    goodState.Price = Clamp(goodState.Price, basePrice * MinMultiplier, basePrice * MaxMultiplier);
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
