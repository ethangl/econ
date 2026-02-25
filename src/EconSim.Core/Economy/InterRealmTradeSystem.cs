using System;
using EconSim.Core.Data;
using EconSim.Core.Simulation;
using EconSim.Core.Simulation.Systems;

namespace EconSim.Core.Economy
{
    /// <summary>
    /// Runs daily AFTER FiscalSystem. Two jobs:
    /// 1. Post-relief county deficit scan — feeds realm-level trade in FiscalSystem Phase C.
    /// 2. Per-market price discovery — supply-side stock throttle.
    /// </summary>
    public class InterRealmTradeSystem : ITickSystem
    {
        public string Name => "InterRealmTrade";
        public int TickInterval => SimulationConfig.Intervals.Daily;

        static readonly float[] ConsumptionPerPop = Goods.ConsumptionPerPop;

        /// <summary>County ID → Realm ID (for deficit scan).</summary>
        int[] _countyToRealm;
        int[] _countyIds;

        /// <summary>County IDs partitioned by market. [marketId] → county ID array.</summary>
        int[][] _marketCountyIds;

        /// <summary>Number of markets (1-indexed).</summary>
        int _marketCount;

        /// <summary>Per-market production capacity scratch. [marketId][goodId].</summary>
        float[][] _marketProductionCap;

        public void Initialize(SimulationState state, MapData mapData)
        {
            var econ = state.Economy;

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

            _countyIds = new int[mapData.Counties.Count];
            _countyToRealm = new int[maxCountyId + 1];
            for (int i = 0; i < mapData.Counties.Count; i++)
            {
                var county = mapData.Counties[i];
                _countyIds[i] = county.Id;
                int provId = county.ProvinceId;
                _countyToRealm[county.Id] = provId >= 0 && provId < provinceToRealm.Length
                    ? provinceToRealm[provId]
                    : 0;
            }

            // Build per-market county partitions
            _marketCount = econ.Markets != null ? econ.Markets.Length - 1 : 0;
            _marketCountyIds = new int[_marketCount + 1][];
            if (_marketCount > 0)
            {
                var lists = new System.Collections.Generic.List<int>[_marketCount + 1];
                for (int m = 1; m <= _marketCount; m++)
                    lists[m] = new System.Collections.Generic.List<int>();

                foreach (var county in mapData.Counties)
                {
                    int marketId = econ.CountyToMarket[county.Id];
                    if (marketId >= 1 && marketId <= _marketCount)
                        lists[marketId].Add(county.Id);
                }

                for (int m = 1; m <= _marketCount; m++)
                    _marketCountyIds[m] = lists[m].ToArray();
            }

            // Per-market production capacity scratch
            _marketProductionCap = new float[_marketCount + 1][];
            for (int m = 1; m <= _marketCount; m++)
                _marketProductionCap[m] = new float[Goods.Count];
        }

        public void Tick(SimulationState state, MapData mapData)
        {
            var econ = state.Economy;
            var counties = econ.Counties;
            var realms = econ.Realms;
            var countyIds = _countyIds;

            // Trade accumulators now cleared by FiscalSystem.ResetAccumulators.

            // Post-relief county deficit scan — record remaining unmet pop consumption per realm
            for (int g = 0; g < Goods.Count; g++)
            {
                float targetStockPerPop = Goods.TargetStockPerPop[g];
                float retainRate = targetStockPerPop > 0f ? 0f : ConsumptionPerPop[g];
                float spoilageRate = Goods.Defs[g].SpoilageRate;
                float durableCatchUpRate = Goods.DurableCatchUpRate[g];
                if (retainRate <= 0f && targetStockPerPop <= 0f) continue;

                for (int i = 0; i < countyIds.Length; i++)
                {
                    int countyId = countyIds[i];
                    var ce = counties[countyId];
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
                        realms[_countyToRealm[countyId]].Deficit[g] += shortfall;
                }
            }

            // Per-market price discovery.
            // Demand = steady-state need (consumption + admin + durable replacement + facility input).
            // Supply = production capacity + stock / buffer. Stock-based throttling is supply-side only.
            const float StockBufferDays = 7f;

            // Compute per-market production capacity from global capacity
            // (approximate: partition global capacity proportional to county count)
            var globalCap = econ.ProductionCapacity;
            for (int m = 1; m <= _marketCount; m++)
                Array.Clear(_marketProductionCap[m], 0, Goods.Count);

            // Accumulate per-market capacity from county extraction + facility labor
            for (int m = 1; m <= _marketCount; m++)
            {
                var mCountyIds = _marketCountyIds[m];
                var mCap = _marketProductionCap[m];
                for (int i = 0; i < mCountyIds.Length; i++)
                {
                    int countyId = mCountyIds[i];
                    var ce = counties[countyId];
                    if (ce == null) continue;

                    // Extraction capacity (approximation — exact seasonal modifiers already applied in EconomySystem)
                    for (int g = 0; g < Goods.Count; g++)
                        mCap[g] += ce.Population * ce.Productivity[g];

                    // Facility labor capacity
                    var indices = econ.CountyFacilityIndices != null && countyId < econ.CountyFacilityIndices.Length
                        ? econ.CountyFacilityIndices[countyId] : null;
                    if (indices != null && econ.Facilities != null)
                    {
                        for (int fi = 0; fi < indices.Count; fi++)
                        {
                            var def = econ.Facilities[indices[fi]].Def;
                            if (def.LaborPerUnit > 0 && def.OutputAmount > 0f)
                                mCap[(int)def.OutputGood] += ce.Population * def.MaxLaborFraction / def.LaborPerUnit * def.OutputAmount;
                        }
                    }
                }
            }

            // Per-market price computation
            float totalPop = 0f;
            for (int i = 0; i < countyIds.Length; i++)
            {
                var ce = counties[countyIds[i]];
                if (ce != null) totalPop += ce.Population;
            }

            float[] marketPop = new float[_marketCount + 1];
            for (int m = 1; m <= _marketCount; m++)
            {
                var mCountyIds = _marketCountyIds[m];
                for (int i = 0; i < mCountyIds.Length; i++)
                {
                    var ce = counties[mCountyIds[i]];
                    if (ce != null) marketPop[m] += ce.Population;
                }
            }

            for (int g = 0; g < Goods.Count; g++)
            {
                if (Goods.IsDurable[g] || Goods.IsDurableInput[g])
                {
                    float bp = Goods.BasePrice[g];
                    for (int m = 1; m <= _marketCount; m++)
                        econ.PerMarketPrices[m][g] = bp;
                    econ.MarketPrices[g] = bp;
                    continue;
                }

                float demandPerPop = ConsumptionPerPop[g] + Goods.CountyAdminPerPop[g];
                float replacementPerPop = Goods.TargetStockPerPop[g] * Goods.Defs[g].SpoilageRate;

                float weightedPriceSum = 0f;

                for (int m = 1; m <= _marketCount; m++)
                {
                    float capacity = _marketProductionCap[m][g];
                    if (capacity <= 0f)
                    {
                        econ.PerMarketPrices[m][g] = Goods.MaxPrice[g];
                        weightedPriceSum += Goods.MaxPrice[g] * marketPop[m];
                        continue;
                    }

                    var mCountyIds = _marketCountyIds[m];
                    float mDemand = 0f;
                    float mStock = 0f;
                    for (int i = 0; i < mCountyIds.Length; i++)
                    {
                        var ce = counties[mCountyIds[i]];
                        if (ce == null) continue;
                        mDemand += ce.FacilityInputNeed[g]
                                 + ce.Population * (demandPerPop + replacementPerPop);
                        mStock += ce.Stock[g];
                    }

                    float supply = capacity + mStock / StockBufferDays;
                    float rawPrice = mDemand > 0f
                        ? Goods.BasePrice[g] * mDemand / supply
                        : Goods.MinPrice[g];
                    float price = Math.Max(Goods.MinPrice[g], Math.Min(rawPrice, Goods.MaxPrice[g]));
                    econ.PerMarketPrices[m][g] = price;
                    weightedPriceSum += price * marketPop[m];
                }

                // Population-weighted average for global MarketPrices
                econ.MarketPrices[g] = totalPop > 0f
                    ? weightedPriceSum / totalPop
                    : Goods.BasePrice[g];
            }
        }
    }
}
