using System;
using EconSim.Core.Data;
using EconSim.Core.Simulation;
using EconSim.Core.Simulation.Systems;

namespace EconSim.Core.Economy
{
    /// <summary>
    /// Global inter-realm market. Runs daily AFTER FiscalSystem (feudal redistribution + minting).
    ///
    /// Phase 8: Post-relief county deficit scan — record remaining unmet pop consumption per realm.
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

        static readonly float[] ConsumptionPerPop = Goods.ConsumptionPerPop;

        int[] _realmIds;
        float[] _prices;

        /// <summary>County ID → Realm ID (for deficit scan).</summary>
        int[] _countyToRealm;

        public void Initialize(SimulationState state, MapData mapData)
        {
            int realmCount = mapData.Realms.Count;
            _realmIds = new int[realmCount];
            for (int i = 0; i < realmCount; i++)
                _realmIds[i] = mapData.Realms[i].Id;

            _prices = new float[Goods.Count];
            Array.Copy(Goods.BasePrice, _prices, Goods.Count);

            // Build county → realm mapping for deficit scan
            int maxProvId = 0;
            foreach (var prov in mapData.Provinces)
                if (prov.Id > maxProvId) maxProvId = prov.Id;

            var provinceToRealm = new int[maxProvId + 1];
            foreach (var prov in mapData.Provinces)
                provinceToRealm[prov.Id] = prov.RealmId;

            int maxCountyId = 0;
            foreach (var county in mapData.Counties)
                if (county.Id > maxCountyId) maxCountyId = county.Id;

            _countyToRealm = new int[maxCountyId + 1];
            foreach (var county in mapData.Counties)
            {
                int provId = county.ProvinceId;
                _countyToRealm[county.Id] = provId >= 0 && provId < provinceToRealm.Length
                    ? provinceToRealm[provId]
                    : 0;
            }
        }

        public void Tick(SimulationState state, MapData mapData)
        {
            var econ = state.Economy;
            var counties = econ.Counties;
            var realms = econ.Realms;
            int realmCount = _realmIds.Length;

            // Trade accumulators now cleared by FiscalSystem.ResetAccumulators.

            // Phase 8: Post-relief county deficit scan — record remaining unmet pop consumption per realm
            for (int g = 0; g < Goods.Count; g++)
            {
                float targetStockPerPop = Goods.TargetStockPerPop[g];
                float retainRate = targetStockPerPop > 0f ? 0f : ConsumptionPerPop[g];
                float spoilageRate = Goods.Defs[g].SpoilageRate;
                float durableCatchUpRate = Goods.DurableCatchUpRate[g];
                if (retainRate <= 0f && targetStockPerPop <= 0f) continue;

                for (int i = 0; i < counties.Length; i++)
                {
                    var ce = counties[i];
                    if (ce == null) continue;

                    float shortfall;
                    if (targetStockPerPop > 0f)
                    {
                        float targetStock = ce.Population * targetStockPerPop;
                        float stockGap = Math.Max(0f, targetStock - ce.Stock[g]);
                        float maintenanceNeed = ce.Stock[g] * spoilageRate;
                        shortfall = maintenanceNeed + stockGap * durableCatchUpRate;
                    }
                    else
                    {
                        shortfall = ce.Population * retainRate - ce.Stock[g];
                    }

                    if (shortfall > 0f)
                        realms[_countyToRealm[i]].Deficit[g] += shortfall;
                }
            }

            // Inter-realm trade
            Span<float> netPosition = stackalloc float[realmCount];

            var buyPriority = Goods.BuyPriority;
            for (int bp = 0; bp < buyPriority.Length; bp++)
            {
                int g = buyPriority[bp];

                float totalSupply = 0f;
                float totalDemand = 0f;

                for (int r = 0; r < realmCount; r++)
                {
                    var re = realms[_realmIds[r]];
                    float stock = re.Stockpile[g];
                    float deficit = re.Deficit[g];

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
                    continue;

                // Price discovery: compute clearing price from supply/demand ratio
                float basePrice = Goods.BasePrice[g];
                float rawPrice = basePrice * totalDemand / totalSupply;
                float price = Math.Max(Goods.MinPrice[g], Math.Min(rawPrice, Goods.MaxPrice[g]));
                _prices[g] = price;

                // Trade execution moved to FiscalSystem Phase C (cross-realm county trade).
            }

            // Price discovery for intermediate goods (no direct demand, facility-driven)
            for (int g = 0; g < Goods.Count; g++)
            {
                if (Goods.HasDirectDemand[g]) continue;

                float capacity = econ.ProductionCapacity[g];
                if (capacity <= 0f) continue;

                float totalFacDemand = 0f;
                for (int i = 0; i < counties.Length; i++)
                {
                    var ce = counties[i];
                    if (ce == null) continue;
                    totalFacDemand += ce.FacilityInputNeed[g];
                }

                float rawPrice = totalFacDemand > 0f
                    ? Goods.BasePrice[g] * totalFacDemand / capacity
                    : Goods.MinPrice[g];
                _prices[g] = Math.Max(Goods.MinPrice[g], Math.Min(rawPrice, Goods.MaxPrice[g]));
            }

            Array.Copy(_prices, state.Economy.MarketPrices, Goods.Count);
        }
    }
}
