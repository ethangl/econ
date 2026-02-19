using System;
using EconSim.Core.Data;
using EconSim.Core.Simulation;
using EconSim.Core.Simulation.Systems;

namespace EconSim.Core.Economy
{
    /// <summary>
    /// Global inter-realm market. Runs daily AFTER TradeSystem (feudal redistribution + minting).
    ///
    /// For each tradeable good (in buy-priority order):
    /// 1. Compute net position per realm: surplus from stockpile minus deficit.
    ///    Self-satisfy first so a realm doesn't sell food then buy it back.
    /// 2. Compute clearing price from supply/demand ratio, clamped to [min, max].
    /// 3. Treasury-limit each buyer's effective demand.
    /// 4. Fill ratio = min(1, totalSupply / totalEffectiveDemand).
    /// 5. Execute: sellers lose stock, gain revenue; buyers gain goods, lose coin.
    /// </summary>
    public class InterRealmTradeSystem : ITickSystem
    {
        public string Name => "InterRealmTrade";
        public int TickInterval => SimulationConfig.Intervals.Daily;

        int[] _realmIds;
        float[] _prices;

        public void Initialize(SimulationState state, MapData mapData)
        {
            int realmCount = mapData.Realms.Count;
            _realmIds = new int[realmCount];
            for (int i = 0; i < realmCount; i++)
                _realmIds[i] = mapData.Realms[i].Id;

            _prices = new float[Goods.Count];
            Array.Copy(Goods.BasePrice, _prices, Goods.Count);
        }

        public void Tick(SimulationState state, MapData mapData)
        {
            var realms = state.Economy.Realms;
            int realmCount = _realmIds.Length;

            // Temporary per-realm arrays for net position calculation
            Span<float> netPosition = stackalloc float[realmCount]; // positive = seller, negative = buyer

            // Process each tradeable good in buy-priority order
            var buyPriority = Goods.BuyPriority;
            for (int bp = 0; bp < buyPriority.Length; bp++)
            {
                int g = buyPriority[bp];

                // Step 1: Compute net position per realm (self-satisfy first)
                float totalSupply = 0f;
                float totalDemand = 0f;

                for (int r = 0; r < realmCount; r++)
                {
                    var re = realms[_realmIds[r]];
                    float stock = re.Stockpile[g];
                    float deficit = re.Deficit[g];

                    // Self-satisfy: realm uses own stockpile to cover own deficit
                    float selfSatisfy = Math.Min(stock, deficit);
                    stock -= selfSatisfy;
                    deficit -= selfSatisfy;

                    float net = stock - deficit;
                    netPosition[r] = net;

                    if (net > 0f)
                        totalSupply += net;
                    else if (net < 0f)
                        totalDemand += -net;
                }

                if (totalSupply <= 0f || totalDemand <= 0f)
                {
                    // No trade possible — keep previous price
                    continue;
                }

                // Step 2: Clearing price from supply/demand ratio
                float basePrice = Goods.BasePrice[g];
                float rawPrice = basePrice * totalDemand / totalSupply;
                float price = Math.Max(Goods.MinPrice[g], Math.Min(rawPrice, Goods.MaxPrice[g]));
                _prices[g] = price;

                // Step 3: Treasury-limit each buyer's effective demand
                float totalEffectiveDemand = 0f;
                Span<float> effectiveDemand = stackalloc float[realmCount];

                for (int r = 0; r < realmCount; r++)
                {
                    if (netPosition[r] >= 0f)
                    {
                        effectiveDemand[r] = 0f;
                        continue;
                    }

                    float want = -netPosition[r];
                    float canAfford = realms[_realmIds[r]].Treasury / price;
                    float eff = Math.Min(want, canAfford);
                    effectiveDemand[r] = eff;
                    totalEffectiveDemand += eff;
                }

                if (totalEffectiveDemand <= 0f)
                    continue;

                // Step 4: Fill ratio
                float fillRatio = Math.Min(1f, totalSupply / totalEffectiveDemand);
                float sellRatio = Math.Min(1f, totalEffectiveDemand / totalSupply);

                // Step 5: Execute trades
                for (int r = 0; r < realmCount; r++)
                {
                    var re = realms[_realmIds[r]];

                    if (netPosition[r] > 0f)
                    {
                        // Seller
                        float sold = netPosition[r] * sellRatio;
                        float revenue = sold * price;
                        re.Stockpile[g] -= sold;
                        re.Treasury += revenue;
                        re.TradeExports[g] += sold;
                        re.TradeRevenue += revenue;
                    }
                    else if (effectiveDemand[r] > 0f)
                    {
                        // Buyer
                        float bought = effectiveDemand[r] * fillRatio;
                        float cost = bought * price;
                        re.Stockpile[g] += bought;
                        re.Treasury -= cost;
                        re.TradeImports[g] += bought;
                        re.TradeSpending += cost;
                    }
                }
            }

            // Write prices to economy state for snapshot capture
            Array.Copy(_prices, state.Economy.MarketPrices, Goods.Count);
        }
    }
}
