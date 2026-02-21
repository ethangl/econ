using System;
using EconSim.Core.Data;
using EconSim.Core.Simulation;
using EconSim.Core.Simulation.Systems;

namespace EconSim.Core.Economy
{
    /// <summary>
    /// Runs daily AFTER FiscalSystem. Two jobs:
    /// 1. Post-relief county deficit scan — feeds realm-level trade in FiscalSystem Phase C.
    /// 2. Price discovery for all produced goods — supply-side stock throttle.
    /// </summary>
    public class InterRealmTradeSystem : ITickSystem
    {
        public string Name => "InterRealmTrade";
        public int TickInterval => SimulationConfig.Intervals.Daily;

        static readonly float[] ConsumptionPerPop = Goods.ConsumptionPerPop;

        float[] _prices;

        /// <summary>County ID → Realm ID (for deficit scan).</summary>
        int[] _countyToRealm;

        public void Initialize(SimulationState state, MapData mapData)
        {
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

            // Price discovery for all produced goods.
            // Demand = steady-state need (consumption + admin + durable replacement + facility input).
            // Supply = production capacity + stock / buffer. Stock-based throttling is supply-side only.
            const float StockBufferDays = 7f;
            for (int g = 0; g < Goods.Count; g++)
            {
                float capacity = econ.ProductionCapacity[g];
                if (capacity <= 0f) continue;

                float demandPerPop = ConsumptionPerPop[g]
                                   + Goods.CountyAdminPerPop[g];
                float replacementPerPop = Goods.TargetStockPerPop[g] * Goods.Defs[g].SpoilageRate;

                float totalDemand = 0f;
                float totalStock = 0f;
                for (int i = 0; i < counties.Length; i++)
                {
                    var ce = counties[i];
                    if (ce == null) continue;
                    totalDemand += ce.FacilityInputNeed[g]
                                 + ce.Population * (demandPerPop + replacementPerPop);
                    totalStock += ce.Stock[g];
                }

                float supply = capacity + totalStock / StockBufferDays;
                float rawPrice = totalDemand > 0f
                    ? Goods.BasePrice[g] * totalDemand / supply
                    : Goods.MinPrice[g];
                _prices[g] = Math.Max(Goods.MinPrice[g], Math.Min(rawPrice, Goods.MaxPrice[g]));
            }

            Array.Copy(_prices, state.Economy.MarketPrices, Goods.Count);
        }
    }
}
