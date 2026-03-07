using System;
using EconSim.Core.Common;
using EconSim.Core.Data;
using EconSim.Core.Simulation;
using EconSim.Core.Simulation.Systems;

namespace EconSim.Core.Economy
{
    /// <summary>
    /// Consumption + satisfaction + demand computation each tick.
    /// Runs after ProductionSystem — reads post-production Stock[], writes Consumption[]/UnmetNeed[]/Satisfaction.
    /// </summary>
    public class ConsumptionSystem : ITickSystem
    {
        public string Name => "Consumption";
        public int TickInterval => SimulationConfig.Intervals.Daily;

        static readonly float[] ConsumptionPerPop = Goods.ConsumptionPerPop;
        const int Food = (int)GoodType.Wheat;

        // Satisfaction weights: staple pool + individual basics
        static readonly int[] IndividualBasicGoods;
        static readonly float[] IndividualBasicWeights;
        static readonly float StapleSatisfactionWeight;

        const float NeedsWeight = 0.7f;
        const float ComfortWeight = 0.3f;
        int[] _countyIds;
        readonly float[] _effPopBuf = new float[5];

        static ConsumptionSystem()
        {
            float totalIndividualBasic = 0f;
            int count = 0;
            for (int g = 0; g < Goods.Count; g++)
            {
                if (Goods.Defs[g].Need == NeedCategory.Basic)
                {
                    count++;
                    totalIndividualBasic += ConsumptionPerPop[g];
                }
            }

            float totalDenom = Goods.StapleBudgetPerPop + totalIndividualBasic;
            StapleSatisfactionWeight = totalDenom > 0f ? Goods.StapleBudgetPerPop / totalDenom : 0f;

            IndividualBasicGoods = new int[count];
            IndividualBasicWeights = new float[count];
            int idx = 0;
            for (int g = 0; g < Goods.Count; g++)
            {
                if (Goods.Defs[g].Need == NeedCategory.Basic)
                {
                    IndividualBasicGoods[idx] = g;
                    IndividualBasicWeights[idx] = totalDenom > 0f ? ConsumptionPerPop[g] / totalDenom : 0f;
                    idx++;
                }
            }
        }

        public void Initialize(SimulationState state, MapData mapData)
        {
            // EconomyInitializer already ran via ProductionSystem — just cache countyIds
            _countyIds = new int[mapData.Counties.Count];
            for (int i = 0; i < mapData.Counties.Count; i++)
                _countyIds[i] = mapData.Counties[i].Id;
        }

        public void Tick(SimulationState state, MapData mapData)
        {
            var econ = state.Economy;
            var counties = econ.Counties;
            var countyIds = _countyIds;
            int goodsCount = Goods.Count;

            for (int i = 0; i < countyIds.Length; i++)
            {
                int countyId = countyIds[i];
                var ce = counties[countyId];
                if (ce == null) continue;

                float[] effPop = _effPopBuf;
                Estates.ComputeEffectivePop(ce.EstatePop, effPop);

                // Non-staple consumption (durables wear + individual basics)
                for (int g = 0; g < goodsCount; g++)
                {
                    if (Goods.Defs[g].Need == NeedCategory.Staple)
                        continue; // handled in pooled pass below

                    float tgt = Goods.TargetStockPerPop[g];
                    if (tgt > 0f)
                    {
                        // Durable: only wear removes stock; deficit is a demand signal
                        float targetStock = effPop[(int)Goods.Defs[g].Need] * tgt;
                        float replacement = ce.Stock[g] * Goods.Defs[g].SpoilageRate;
                        float wear = Math.Min(ce.Stock[g], replacement);
                        ce.Stock[g] -= wear;
                        ce.Consumption[g] = wear;
                        // Measure gap post-wear (consistent with InterRealmTradeSystem deficit scan)
                        float deficit = Math.Max(0f, targetStock - ce.Stock[g]);
                        ce.UnmetNeed[g] = deficit * Goods.DurableCatchUpRate[g];
                    }
                    else
                    {
                        float needed = effPop[(int)Goods.Defs[g].Need] * ConsumptionPerPop[g];
                        float consumed = Math.Min(ce.Stock[g], needed);
                        ce.Stock[g] -= consumed;
                        ce.Consumption[g] = consumed;
                        ce.UnmetNeed[g] = needed - consumed;
                    }
                }

                // Pooled staple consumption — people eat 1 kg/day of any combination
                float stapleBudget = effPop[(int)NeedCategory.Staple] * Goods.StapleBudgetPerPop;
                float totalStapleAvail = 0f;
                for (int s = 0; s < Goods.StapleGoods.Length; s++)
                    totalStapleAvail += ce.Stock[Goods.StapleGoods[s]];

                float totalStapleConsumed = 0f;
                if (totalStapleAvail >= stapleBudget)
                {
                    // Plenty: consume proportional to availability
                    for (int s = 0; s < Goods.StapleGoods.Length; s++)
                    {
                        int g = Goods.StapleGoods[s];
                        float consumed = ce.Stock[g] / totalStapleAvail * stapleBudget;
                        ce.Stock[g] -= consumed;
                        ce.Consumption[g] = consumed;
                        totalStapleConsumed += consumed;
                    }
                }
                else
                {
                    // Scarce: eat everything
                    for (int s = 0; s < Goods.StapleGoods.Length; s++)
                    {
                        int g = Goods.StapleGoods[s];
                        ce.Consumption[g] = ce.Stock[g];
                        totalStapleConsumed += ce.Stock[g];
                        ce.Stock[g] = 0f;
                    }
                }

                // Trade demand signals: shortfall against ideal share
                for (int s = 0; s < Goods.StapleGoods.Length; s++)
                {
                    int g = Goods.StapleGoods[s];
                    ce.UnmetNeed[g] = Math.Max(0f, effPop[(int)NeedCategory.Staple] * Goods.StapleIdealPerPop[g] - ce.Consumption[g]);
                }

                // Basic-needs fulfillment EMA (alpha ≈ 2/(30+1) ≈ 0.065, ~30-day smoothing)
                float stapleFulfillment = stapleBudget > 0f
                    ? Math.Min(1f, totalStapleConsumed / stapleBudget) : 1f;
                float dailyNeeds = StapleSatisfactionWeight * stapleFulfillment;
                for (int b = 0; b < IndividualBasicGoods.Length; b++)
                {
                    int g = IndividualBasicGoods[b];
                    float needed = effPop[(int)NeedCategory.Basic] * ConsumptionPerPop[g];
                    float ratio = needed > 0f ? Math.Min(1f, ce.Consumption[g] / needed) : 1f;
                    dailyNeeds += IndividualBasicWeights[b] * ratio;
                }
                ce.BasicSatisfaction += 0.065f * (dailyNeeds - ce.BasicSatisfaction);

                // Comfort fulfillment — average across categories, sum within each category
                var comfortCats = Goods.ComfortCategories;
                var comfortCatGoods = Goods.ComfortCategoryGoods;
                float comfortSum = 0f;
                for (int cat = 0; cat < comfortCats.Length; cat++)
                {
                    var catDef = comfortCats[cat];
                    float catTarget = effPop[(int)NeedCategory.Comfort] * catDef.TargetPerPop;
                    float catActual = 0f;
                    var members = comfortCatGoods[cat];
                    for (int j = 0; j < members.Length; j++)
                    {
                        int g = members[j];
                        catActual += catDef.IsDurable ? ce.Stock[g] : ce.Consumption[g];
                    }
                    comfortSum += catTarget > 0f ? Math.Min(1f, catActual / catTarget) : 1f;
                }
                float comfortFulfillment = comfortCats.Length > 0 ? comfortSum / comfortCats.Length : 1f;

                // Blended satisfaction: needs + comfort — drives migration
                float dailySatisfaction = NeedsWeight * dailyNeeds + ComfortWeight * comfortFulfillment;
                ce.Satisfaction += 0.065f * (dailySatisfaction - ce.Satisfaction);
            }

            // Compute effective demand per pop (used by FiscalSystem)
            var demandPerPop = econ.EffectiveDemandPerPop;
            float totalPop = 0f;
            for (int i = 0; i < countyIds.Length; i++)
                totalPop += counties[countyIds[i]].Population;
            for (int g = 0; g < goodsCount; g++)
            {
                if (Goods.TargetStockPerPop[g] > 0f && totalPop > 0f)
                {
                    float totalDemand = 0f;
                    for (int i = 0; i < countyIds.Length; i++)
                    {
                        var c = counties[countyIds[i]];
                        totalDemand += c.Consumption[g] + c.UnmetNeed[g];
                    }
                    demandPerPop[g] = totalDemand / totalPop;
                }
                else
                {
                    demandPerPop[g] = Goods.Defs[g].Need == NeedCategory.Staple
                        ? Goods.StapleIdealPerPop[g]
                        : ConsumptionPerPop[g];
                }
            }

            // Record snapshot only when explicitly enabled (EconDebugBridge runs)
            if (econ.CaptureSnapshots)
                econ.TimeSeries.Add(BuildSnapshot(state.CurrentDay, econ));
        }

        static EconomySnapshot BuildSnapshot(int day, EconomyState econ)
        {
            var snap = new EconomySnapshot();
            snap.Day = day;
            snap.MinStock = float.MaxValue;
            snap.MaxStock = float.MinValue;
            snap.TotalStockByGood = new float[Goods.Count];
            snap.TotalProductionByGood = new float[Goods.Count];
            snap.TotalConsumptionByGood = new float[Goods.Count];
            snap.TotalUnmetNeedByGood = new float[Goods.Count];
            snap.TotalDucalReliefByGood = new float[Goods.Count];
            snap.TotalProvincialStockpileByGood = new float[Goods.Count];
            snap.TotalRoyalStockpileByGood = new float[Goods.Count];
            snap.TotalGranaryRequisitionedByGood = new float[Goods.Count];
            snap.TotalIntraProvTradeBoughtByGood = new float[Goods.Count];
            snap.TotalIntraProvTradeSoldByGood = new float[Goods.Count];
            snap.TotalCrossProvTradeBoughtByGood = new float[Goods.Count];
            snap.TotalCrossProvTradeSoldByGood = new float[Goods.Count];
            snap.TotalCrossMarketTradeBoughtByGood = new float[Goods.Count];
            snap.TotalCrossMarketTradeSoldByGood = new float[Goods.Count];
            snap.TotalVMImportedByGood = new float[Goods.Count];
            snap.TotalVMExportedByGood = new float[Goods.Count];

            int countyCount = 0;
            snap.MinBasicSatisfaction = float.MaxValue;
            snap.MaxBasicSatisfaction = float.MinValue;
            snap.MinSatisfaction = float.MaxValue;
            snap.MaxSatisfaction = float.MinValue;
            float weightedBasicSatisfaction = 0f;
            float weightedSatisfaction = 0f;

            for (int i = 0; i < econ.Counties.Length; i++)
            {
                var ce = econ.Counties[i];
                if (ce == null) continue;

                countyCount++;

                for (int g = 0; g < Goods.Count; g++)
                {
                    snap.TotalStockByGood[g] += ce.Stock[g];
                    snap.TotalProductionByGood[g] += ce.Production[g];
                    snap.TotalConsumptionByGood[g] += ce.Consumption[g];
                    snap.TotalUnmetNeedByGood[g] += ce.UnmetNeed[g];
                }

                for (int g = 0; g < Goods.Count; g++)
                {
                    snap.TotalDucalReliefByGood[g] += ce.Relief[g];
                    snap.TotalGranaryRequisitionedByGood[g] += ce.GranaryRequisitioned[g];
                    snap.TotalIntraProvTradeBoughtByGood[g] += ce.TradeBought[g];
                    snap.TotalIntraProvTradeSoldByGood[g] += ce.TradeSold[g];
                    snap.TotalCrossProvTradeBoughtByGood[g] += ce.CrossProvTradeBought[g];
                    snap.TotalCrossProvTradeSoldByGood[g] += ce.CrossProvTradeSold[g];
                    snap.TotalCrossMarketTradeBoughtByGood[g] += ce.CrossMarketTradeBought[g];
                    snap.TotalCrossMarketTradeSoldByGood[g] += ce.CrossMarketTradeSold[g];
                    snap.TotalVMImportedByGood[g] += ce.VirtualMarketBought[g];
                    snap.TotalVMExportedByGood[g] += ce.VirtualMarketSold[g];
                }
                snap.TotalIntraProvTradeSpending += ce.TradeCrownsSpent;
                snap.TotalIntraProvTradeRevenue += ce.TradeCrownsEarned;
                snap.TotalCrossProvTradeSpending += ce.CrossProvTradeCrownsSpent;
                snap.TotalCrossProvTradeRevenue += ce.CrossProvTradeCrownsEarned;
                snap.TotalTradeTollsPaid += ce.TradeTollsPaid;
                snap.TotalCrossMarketTradeSpending += ce.CrossMarketTradeCrownsSpent;
                snap.TotalCrossMarketTradeRevenue += ce.CrossMarketTradeCrownsEarned;
                snap.TotalCrossMarketTollsPaid += ce.CrossMarketTollsPaid;
                snap.TotalCrossMarketTariffsPaid += ce.CrossMarketTariffsPaid;
                snap.TotalVMImportSpending += ce.VirtualMarketCrownsSpent;
                snap.TotalVMExportRevenue += ce.VirtualMarketCrownsEarned;
                snap.TotalVMTariffsPaid += ce.VirtualMarketTariffsPaid;

                float foodStock = ce.Stock[Food];
                if (foodStock < snap.MinStock) snap.MinStock = foodStock;
                if (foodStock > snap.MaxStock) snap.MaxStock = foodStock;

                // Staple-pooled starvation check
                float totalStapleProd = 0f;
                float totalStapleCons = 0f;
                for (int s = 0; s < Goods.StapleGoods.Length; s++)
                {
                    int sg = Goods.StapleGoods[s];
                    totalStapleProd += ce.Production[sg];
                    totalStapleCons += ce.Consumption[sg];
                }
                float stapleBudget = ce.Population * Goods.StapleBudgetPerPop;

                if (totalStapleCons < stapleBudget * 0.999f)
                    snap.ShortfallCounties++;
                else if (totalStapleProd < totalStapleCons)
                    snap.DeficitCounties++;
                else
                    snap.SurplusCounties++;

                // Population dynamics aggregates
                snap.TotalPopulation += ce.Population;
                snap.TotalBirths += ce.BirthsThisMonth;
                snap.TotalDeaths += ce.DeathsThisMonth;
                weightedBasicSatisfaction += ce.Population * ce.BasicSatisfaction;
                weightedSatisfaction += ce.Population * ce.Satisfaction;
                if (ce.BasicSatisfaction < snap.MinBasicSatisfaction) snap.MinBasicSatisfaction = ce.BasicSatisfaction;
                if (ce.BasicSatisfaction > snap.MaxBasicSatisfaction) snap.MaxBasicSatisfaction = ce.BasicSatisfaction;
                if (ce.Satisfaction < snap.MinSatisfaction) snap.MinSatisfaction = ce.Satisfaction;
                if (ce.Satisfaction > snap.MaxSatisfaction) snap.MaxSatisfaction = ce.Satisfaction;
                if (ce.BasicSatisfaction < 0.5f) snap.CountiesInDistress++;

                snap.TotalCountyTreasury += ce.Treasury;
                snap.TotalMonetaryTaxToProvince += ce.MonetaryTaxPaid;
                snap.TotalGranaryRequisitionCrowns += ce.GranaryRequisitionCrownsReceived;
                snap.TotalMarketFeesCollected += ce.MarketFeesReceived;
                snap.TotalTransportCostsPaid += ce.TransportCostsPaid;
            }

            // Backward-compat scalars = food values
            snap.TotalStock = snap.TotalStockByGood[Food];
            snap.TotalProduction = snap.TotalProductionByGood[Food];
            snap.TotalConsumption = snap.TotalConsumptionByGood[Food];
            snap.TotalUnmetNeed = snap.TotalUnmetNeedByGood[Food];

            if (countyCount == 0)
            {
                snap.MinStock = 0;
                snap.MaxStock = 0;
                snap.MinBasicSatisfaction = 0;
                snap.MaxBasicSatisfaction = 0;
                snap.MinSatisfaction = 0;
                snap.MaxSatisfaction = 0;
            }

            snap.AvgBasicSatisfaction = snap.TotalPopulation > 0f
                ? weightedBasicSatisfaction / snap.TotalPopulation
                : 0f;
            snap.AvgSatisfaction = snap.TotalPopulation > 0f
                ? weightedSatisfaction / snap.TotalPopulation
                : 0f;

            // Provincial stockpile stats
            if (econ.Provinces != null)
            {
                for (int i = 0; i < econ.Provinces.Length; i++)
                {
                    var pe = econ.Provinces[i];
                    if (pe == null) continue;
                    for (int g = 0; g < Goods.Count; g++)
                        snap.TotalProvincialStockpileByGood[g] += pe.Stockpile[g];
                    snap.TotalProvinceTreasury += pe.Treasury;
                    snap.TotalMonetaryTaxToRealm += pe.MonetaryTaxPaidToRealm;
                    snap.TotalProvinceAdminCost += pe.AdminCrownsCost;
                    snap.TotalTradeTollsCollected += pe.TradeTollsCollected;
                }
            }

            // Royal stockpile stats + treasury + trade
            snap.TotalTradeImportsByGood = new float[Goods.Count];
            snap.TotalTradeExportsByGood = new float[Goods.Count];
            snap.TotalRealmDeficitByGood = new float[Goods.Count];

            if (econ.Realms != null)
            {
                for (int i = 0; i < econ.Realms.Length; i++)
                {
                    var re = econ.Realms[i];
                    if (re == null) continue;
                    for (int g = 0; g < Goods.Count; g++)
                    {
                        snap.TotalRoyalStockpileByGood[g] += re.Stockpile[g];
                        snap.TotalTradeImportsByGood[g] += re.TradeImports[g];
                        snap.TotalTradeExportsByGood[g] += re.TradeExports[g];
                        snap.TotalRealmDeficitByGood[g] += re.Deficit[g];
                    }
                    snap.TotalRealmAdminCost += re.AdminCrownsCost;
                    snap.TotalTreasury += re.Treasury;
                    snap.TotalGoldMinted += re.GoldMinted;
                    snap.TotalSilverMinted += re.SilverMinted;
                    snap.TotalCrownsMinted += re.CrownsMinted;
                    snap.TotalTradeSpending += re.TradeSpending;
                    snap.TotalTradeRevenue += re.TradeRevenue;
                    snap.TotalCrossMarketTariffsCollected += re.TradeTariffsCollected;
                }
            }

            snap.TotalDomesticTreasury = snap.TotalCountyTreasury
                + snap.TotalProvinceTreasury + snap.TotalTreasury;

            // Market prices snapshot
            if (econ.MarketPrices != null)
            {
                snap.MarketPrices = new float[Goods.Count];
                Array.Copy(econ.MarketPrices, snap.MarketPrices, Goods.Count);
            }

            // Per-market prices snapshot
            if (econ.PerMarketPrices != null)
            {
                snap.PerMarketPrices = new float[econ.PerMarketPrices.Length][];
                for (int m = 1; m < econ.PerMarketPrices.Length; m++)
                {
                    if (econ.PerMarketPrices[m] != null)
                    {
                        snap.PerMarketPrices[m] = new float[Goods.Count];
                        Array.Copy(econ.PerMarketPrices[m], snap.PerMarketPrices[m], Goods.Count);
                    }
                }
            }

            // Backward-compat scalars = food values
            snap.TotalDucalRelief = snap.TotalDucalReliefByGood[Food];
            snap.TotalProvincialStockpile = snap.TotalProvincialStockpileByGood[Food];
            snap.TotalRoyalStockpile = snap.TotalRoyalStockpileByGood[Food];

            snap.MedianProductivity = econ.MedianProductivity[Food];

            // Virtual market state snapshot
            var vm = econ.VirtualMarket;
            if (vm != null)
            {
                snap.VMStock = new float[Goods.Count];
                snap.VMSellPrice = new float[Goods.Count];
                Array.Copy(vm.Stock, snap.VMStock, Goods.Count);
                Array.Copy(vm.SellPrice, snap.VMSellPrice, Goods.Count);
            }

            return snap;
        }
    }
}
