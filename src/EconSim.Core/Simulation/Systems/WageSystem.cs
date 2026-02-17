using System;
using System.Collections.Generic;
using EconSim.Core.Data;
using EconSim.Core.Economy;

namespace EconSim.Core.Simulation.Systems
{
    /// <summary>
    /// Economy V2 wage and household income system.
    /// </summary>
    public class WageSystem : ITickSystem
    {
        private const float BasketEmaAlpha = 2f / 31f;
        private const float DailyClamp = 0.02f;
        private readonly Dictionary<int, int> _zonePopulationByMarket = new Dictionary<int, int>();

        public string Name => "Wages";
        public int TickInterval => SimulationConfig.Intervals.Daily;

        public void Initialize(SimulationState state, MapData mapData)
        {
        }

        public void Tick(SimulationState state, MapData mapData)
        {
            var economy = state.Economy;
            if (economy == null)
                return;

            float basketCost = ComputeBasicBasketCost(economy);
            state.SmoothedBasketCost = state.SmoothedBasketCost <= 0f
                ? basketCost
                : state.SmoothedBasketCost + (basketCost - state.SmoothedBasketCost) * BasketEmaAlpha;

            float rawSubsistence = state.SmoothedBasketCost * 1.2f;
            float prevSubsistence = state.SubsistenceWage > 0f ? state.SubsistenceWage : rawSubsistence;
            float min = prevSubsistence * (1f - DailyClamp);
            float max = prevSubsistence * (1f + DailyClamp);
            state.SubsistenceWage = Clamp(rawSubsistence, min, max);

            int dayIndex = state.CurrentDay % 7;
            var facilities = economy.GetFacilitiesDense();
            for (int i = 0; i < facilities.Count; i++)
            {
                var facility = facilities[i];
                var def = economy.FacilityDefs.Get(facility.TypeId);
                if (def == null || !facility.IsActive)
                    continue;

                if (facility.AssignedWorkers <= 0)
                {
                    // Keep idle facilities at subsistence offer and decay legacy debt cheaply.
                    facility.WageRate = state.SubsistenceWage;
                    if (facility.WageDebtDays > 0 && state.CurrentDay % 2 == 0)
                        facility.WageDebtDays--;
                    continue;
                }

                float margin = facility.RollingAvgRevenue - facility.RollingAvgInputCost;
                int requiredLabor = facility.GetRequiredLabor(def);
                if (margin > 0f && requiredLabor > 0)
                {
                    float maxWage = margin / requiredLabor;
                    facility.WageRate = Math.Max(maxWage * 0.7f, state.SubsistenceWage);
                }
                else
                {
                    facility.WageRate = state.SubsistenceWage;
                }

                float wageBill = facility.WageRate * facility.AssignedWorkers;
                float paid = Math.Min(Math.Max(0f, facility.Treasury), wageBill);
                facility.BeginDayMetrics(state.CurrentDay);
                facility.Treasury -= paid;
                facility.AddWageBillForDay(dayIndex, paid);

                if (economy.Counties.TryGetValue(facility.CountyId, out var county))
                {
                    county.Population.Treasury += paid;
                }

                if (wageBill > 0f)
                {
                    float coverage = paid / wageBill;
                    if (coverage < 0.60f)
                    {
                        facility.WageDebtDays++;
                    }
                    else if (coverage >= 0.95f && facility.WageDebtDays > 0)
                    {
                        facility.WageDebtDays--;
                    }
                    else if (coverage >= 0.80f && facility.WageDebtDays > 0 && state.CurrentDay % 3 == 0)
                    {
                        // Partial coverage still allows gradual recovery.
                        facility.WageDebtDays--;
                    }
                }
            }
        }

        private float ComputeBasicBasketCost(EconomyState economy)
        {
            _zonePopulationByMarket.Clear();
            foreach (var kvp in economy.CountyToMarket)
            {
                if (!economy.Counties.TryGetValue(kvp.Key, out var county))
                    continue;

                _zonePopulationByMarket.TryGetValue(kvp.Value, out int population);
                _zonePopulationByMarket[kvp.Value] = population + county.Population.Total;
            }

            float weightedBasket = 0f;
            float weightedPopulation = 0f;

            foreach (var market in economy.Markets.Values)
            {
                if (market.Type != MarketType.Legitimate)
                    continue;

                if (!_zonePopulationByMarket.TryGetValue(market.Id, out int zonePopulation))
                    continue;

                if (zonePopulation <= 0)
                    continue;

                float marketBasket = 0f;
                foreach (var good in economy.Goods.ConsumerGoods)
                {
                    if (good.NeedCategory != NeedCategory.Basic)
                        continue;
                    if (good.RuntimeId < 0)
                        continue;

                    if (!market.TryGetGoodState(good.RuntimeId, out var marketGood))
                        continue;

                    marketBasket += marketGood.Price * good.BaseConsumptionKgPerCapitaPerDay;
                }

                weightedBasket += marketBasket * zonePopulation;
                weightedPopulation += zonePopulation;
            }

            if (weightedPopulation > 0)
                return weightedBasket / weightedPopulation;

            // Fallback to base prices if no legitimate zones are assigned yet.
            float baseBasket = 0f;
            foreach (var good in economy.Goods.ConsumerGoods)
            {
                if (good.NeedCategory == NeedCategory.Basic)
                    baseBasket += good.BasePrice * good.BaseConsumptionKgPerCapitaPerDay;
            }

            return baseBasket;
        }

        private static float Clamp(float value, float min, float max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }
}
