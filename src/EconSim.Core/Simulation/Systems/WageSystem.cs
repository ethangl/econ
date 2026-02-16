using System;
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

        public string Name => "Wages";
        public int TickInterval => SimulationConfig.Intervals.Daily;

        public void Initialize(SimulationState state, MapData mapData)
        {
        }

        public void Tick(SimulationState state, MapData mapData)
        {
            if (!SimulationConfig.UseEconomyV2)
                return;

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
            foreach (var facility in economy.Facilities.Values)
            {
                var def = economy.FacilityDefs.Get(facility.TypeId);
                if (def == null || !facility.IsActive)
                    continue;

                float margin = facility.RollingAvgRevenue - facility.RollingAvgInputCost;
                if (margin > 0f && def.LaborRequired > 0)
                {
                    float maxWage = margin / def.LaborRequired;
                    facility.WageRate = Math.Max(maxWage * 0.7f, state.SubsistenceWage);
                }
                else
                {
                    facility.WageRate = state.SubsistenceWage;
                }

                float wageBill = facility.WageRate * facility.AssignedWorkers;
                float paid = Math.Min(Math.Max(0f, facility.Treasury), wageBill);
                facility.Treasury -= paid;
                facility.AddWageBillForDay(dayIndex, paid);

                if (economy.Counties.TryGetValue(facility.CountyId, out var county))
                {
                    county.Population.Treasury += paid;
                }

                if (facility.AssignedWorkers > 0 && wageBill > 0f)
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
                else if (facility.WageDebtDays > 0 && state.CurrentDay % 2 == 0)
                {
                    // Idle facilities recover slowly, not instantly.
                    facility.WageDebtDays--;
                }
            }
        }

        private static float ComputeBasicBasketCost(EconomyState economy)
        {
            float weightedBasket = 0f;
            float weightedPopulation = 0f;

            foreach (var market in economy.Markets.Values)
            {
                if (market.Type != MarketType.Legitimate)
                    continue;

                int zonePopulation = 0;
                foreach (var kvp in economy.CountyToMarket)
                {
                    if (kvp.Value != market.Id)
                        continue;

                    if (economy.Counties.TryGetValue(kvp.Key, out var county))
                        zonePopulation += county.Population.Total;
                }

                if (zonePopulation <= 0)
                    continue;

                float marketBasket = 0f;
                foreach (var good in economy.Goods.ConsumerGoods)
                {
                    if (good.NeedCategory != NeedCategory.Basic)
                        continue;

                    if (!market.Goods.TryGetValue(good.Id, out var marketGood))
                        continue;

                    marketBasket += marketGood.Price * good.BaseConsumption;
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
                    baseBasket += good.BasePrice * good.BaseConsumption;
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
