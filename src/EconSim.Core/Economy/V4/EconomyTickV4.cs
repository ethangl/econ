using System;
using System.Collections.Generic;
using EconSim.Core.Data;
using EconSim.Core.Simulation;
using EconSim.Core.Simulation.Systems;

namespace EconSim.Core.Economy.V4
{
    /// <summary>
    /// V4 economy tick system. Runs all 4 phases each day.
    /// Phase 4: cross-market trade (surplus→deficit, price convergence, tariffs).
    /// </summary>
    public class EconomyTickV4 : ITickSystem
    {
        public string Name => "EconomyV4";
        public int TickInterval => 1;

        // ── Subsistence consumption rates (kg/person/day) ──
        const float StapleNeedPerCapita = 1.0f;
        const float SaltNeedPerCapita = 0.01f;
        const float TimberNeedPerCapita = 0.05f;

        // ── Noble per-capita daily needs (kg/person/day per good in tier) ──
        const float UpperNobleStaplePerGood = 0.30f;  // ×4 goods = 1.2 kg food
        const float UpperNobleBasicPerGood = 0.03f;
        const float UpperNobleComfortPerGood = 0.01f;
        const float UpperNobleLuxuryPerGood = 0.02f;

        const float LowerNobleStaplePerGood = 0.25f;  // ×4 = 1.0 kg food
        const float LowerNobleBasicPerGood = 0.025f;
        const float LowerNobleComfortPerGood = 0.008f;
        const float LowerNobleLuxuryPerGood = 0.01f;

        // ── Upper commoner per-capita daily needs ──
        const float UpperCommonerStaplePerGood = 0.20f;  // ×4 = 0.8 kg food (artisans eat less than serfs)
        const float UpperCommonerBasicPerGood = 0.02f;
        const float UpperCommonerComfortPerGood = 0.005f;
        const float UpperCommonerLuxuryPerGood = 0.002f;

        // ── Clergy per-capita daily needs ──
        const float UpperClergyWorshipPerGood = 0.02f;   // candles + wine
        const float UpperClergyStaplePerGood = 0.20f;
        const float UpperClergyBasicPerGood = 0.015f;
        const float UpperClergyComfortPerGood = 0.005f;

        const float LowerClergyStaplePerGood = 0.25f;
        const float LowerClergyBasicPerGood = 0.02f;
        const float LowerClergyWorshipPerGood = 0.01f;   // candles only

        // ── Budget allocation (fraction of remaining treasury after serf feeding + stipend reserve) ──
        // Upper noble: 5% staples, 5% basics, 15% comforts, 75% luxuries
        // Lower noble: 15% staples, 10% basics, 25% comforts, 50% luxuries

        // ── Monetary parameters ──
        const float GoldCoinPerKg = 50f;         // each kg of gold minted → 50 coins
        const float GoldJewelryFraction = 0.25f;  // fraction of gold production reserved for jewelers
        const float SilverCoinPerKg = 10f;        // each kg of silver minted → 10 coins
        const float StipendPerCapita = 0.5f;      // Cr/lower noble/day
        const float NobleWagePerCapita = 0.25f;   // Cr/upper commoner/day, paid by upper noble
        const float ClergyWagePerCapita = 0.3f;   // Cr/lower clergy/day
        const float CoinWearRate = 0.001f;        // fraction of M lost per day
        const float BaseVelocity = 4.0f;          // base velocity of money

        // ── Tax & tithe ──
        const float TaxRate = 0.10f;         // lord skims 10% of upper commoner buys
        const float TitheRate = 0.10f;       // clergy skims 10% of upper commoner buys

        // ── Serf feeding ──
        const float SerfBudgetCap = 0.4f;    // max fraction of treasury for serf feeding

        // ── Facility budget ──
        const float FacilityInputBudgetShare = 0.50f; // fraction of upper commoner coin for inputs

        // ── Cross-market trade ──
        const float TransportRate = 0.0005f;     // cost per unit of bulk per unit of hub-to-hub distance
        const float TradeTariffRate = 0.05f;     // lord skims 5% of import value

        // ── Worship goods ──
        static readonly int[] WorshipGoods = { (int)GoodTypeV4.Candles, (int)GoodTypeV4.Wine };
        static readonly int[] LowerClergyWorshipGoods = { (int)GoodTypeV4.Candles };

        // ── Phase 5: Satisfaction weights ──
        const float WeightSurvival = 0.40f;
        const float WeightReligion = 0.25f;
        const float WeightStability = 0.20f;
        const float WeightEconomic = 0.10f;
        const float WeightGovernance = 0.05f;
        const float StabilityPlaceholder = 1.0f;
        const float GovernancePlaceholder = 0.7f;

        // ── Phase 5: Population dynamics ──
        const float BaseBirthRate = 0.04f / 360f;
        const float BaseDeathRate = 0.03f / 360f;
        const float SatisfactionBirthMod = 0.5f;
        const float SatisfactionDeathMod = 1.0f;
        const float MigrationRate = 0.001f / 360f;
        const float MigrationThreshold = 0.05f;
        const float UpperMobility = 1.0f;
        const float LowerMobility = 0.05f;

        private EconomyStateV4 _econ;

        // Reusable per-market scratch lists (avoid alloc per tick)
        private List<int>[] _buyByGood;
        private List<int>[] _sellByGood;

        // Scratch array for computing facility input needs per county
        private float[] _facilityInputNeed;

        public void Initialize(SimulationState state, MapData mapData)
        {
            _econ = EconomyInitializerV4.Initialize(state, mapData);
            state.EconomyV4 = _econ;

            int gc = GoodsV4.Count;
            _buyByGood = new List<int>[gc];
            _sellByGood = new List<int>[gc];
            for (int g = 0; g < gc; g++)
            {
                _buyByGood[g] = new List<int>();
                _sellByGood[g] = new List<int>();
            }

            _facilityInputNeed = new float[gc];
        }

        public void Tick(SimulationState state, MapData mapData)
        {
            GenerateOrders(state, mapData);
            ResolveMarkets(state, mapData);
            UpdateMoney(state, mapData);
            UpdateSatisfaction(state, mapData);
            UpdatePopulation(state, mapData);
        }

        // ════════════════════════════════════════════════════════════
        //  PHASE 1: GENERATE ORDERS
        // ════════════════════════════════════════════════════════════

        void GenerateOrders(SimulationState state, MapData mapData)
        {
            // Clear order books
            for (int m = 1; m <= _econ.MarketCount; m++)
                _econ.Markets[m].Orders.Clear();

            int gc = GoodsV4.Count;
            int fc = FacilitiesV4.Count;

            for (int i = 0; i < _econ.Counties.Length; i++)
            {
                var ce = _econ.Counties[i];
                if (ce == null) continue;

                // Reset per-tick tracking
                ce.SerfFoodProvided = 0f;
                ce.UpperNobleSpend = 0f;
                ce.UpperNobleIncome = 0f;
                ce.LowerNobleSpend = 0f;
                ce.UpperCommonerIncome = 0f;
                ce.UpperCommonerSpend = 0f;
                ce.TaxRevenue = 0f;
                ce.TitheRevenue = 0f;
                ce.TariffRevenue = 0f;
                ce.UpperClergySpend = 0f;
                ce.UpperClergyIncome = 0f;
                ce.LowerClergySpend = 0f;
                ce.LowerClergyIncome = 0f;

                float pop = ce.LowerCommonerPop;

                // 1. Biome extraction: lower commoner pop × biome yield
                for (int g = 0; g < gc; g++)
                {
                    ce.Production[g] = pop > 0f ? pop * ce.Productivity[g] : 0f;
                    ce.Consumption[g] = 0f;
                }

                // 2. Subsistence consumption — staples
                float totalStapleProd = 0f;
                if (pop > 0f)
                {
                    for (int s = 0; s < GoodsV4.StapleGoods.Length; s++)
                        totalStapleProd += ce.Production[GoodsV4.StapleGoods[s]];

                    float totalStapleNeed = pop * StapleNeedPerCapita;

                    if (totalStapleProd > 0f && totalStapleNeed > 0f)
                    {
                        float ratio = Math.Min(totalStapleNeed / totalStapleProd, 1.0f);
                        for (int s = 0; s < GoodsV4.StapleGoods.Length; s++)
                        {
                            int g = GoodsV4.StapleGoods[s];
                            ce.Consumption[g] = ce.Production[g] * ratio;
                        }
                    }

                    // Salt and timber
                    int saltId = (int)GoodTypeV4.Salt;
                    ce.Consumption[saltId] = Math.Min(ce.Production[saltId], pop * SaltNeedPerCapita);
                    int timberId = (int)GoodTypeV4.Timber;
                    ce.Consumption[timberId] = Math.Min(ce.Production[timberId], pop * TimberNeedPerCapita);

                    // 3. Surplus
                    for (int g = 0; g < gc; g++)
                        ce.Surplus[g] = ce.Production[g] - ce.Consumption[g];

                    ce.FoodDeficit = totalStapleProd < totalStapleNeed;
                }
                else
                {
                    for (int g = 0; g < gc; g++)
                        ce.Surplus[g] = 0f;
                    ce.FoodDeficit = false;
                }

                // 4. Post sell orders for surplus (excluding Gold — minted, not sold)
                int marketId = _econ.CountyToMarket[i];
                if (marketId <= 0 || marketId > _econ.MarketCount) continue;
                var orders = _econ.Markets[marketId].Orders;

                for (int g = 0; g < gc; g++)
                {
                    if (g == (int)GoodTypeV4.Gold)
                    {
                        // Gold: jewelry fraction sold as raw material, rest minted in UpdateMoney
                        float jewelryGold = ce.Surplus[g] * GoldJewelryFraction;
                        if (jewelryGold > 0.001f)
                        {
                            orders.Add(new Order
                            {
                                CountyId = i,
                                GoodId = g,
                                Side = OrderSide.Sell,
                                Source = OrderSource.PeasantSurplus,
                                Quantity = jewelryGold,
                            });
                        }
                        continue;
                    }
                    if (g == (int)GoodTypeV4.Silver)
                        continue; // Silver: 100% minted in UpdateMoney, not sold on market
                    if (ce.Surplus[g] > 0.001f)
                    {
                        orders.Add(new Order
                        {
                            CountyId = i,
                            GoodId = g,
                            Side = OrderSide.Sell,
                            Source = OrderSource.PeasantSurplus,
                            Quantity = ce.Surplus[g],
                        });
                    }
                }

                // 5. Upper noble buy orders
                float unTreasury = ce.UpperNobleTreasury;
                float unPop = ce.UpperNobilityPop;
                float lnPop = ce.LowerNobilityPop;
                float priceLevel = _econ.Markets[marketId].PriceLevel;

                if (unPop > 0f && unTreasury > 0.01f)
                {
                    float serfBudget = 0f;

                    // Priority 1: Serf feeding (if deficit)
                    if (ce.FoodDeficit && pop > 0f)
                    {
                        float deficit = pop * StapleNeedPerCapita - totalStapleProd;
                        float perStaple = deficit / GoodsV4.StapleGoods.Length;

                        // Budget: up to SerfBudgetCap of treasury
                        float serfNeedValue = 0f;
                        for (int s = 0; s < GoodsV4.StapleGoods.Length; s++)
                            serfNeedValue += perStaple * GoodsV4.Value[GoodsV4.StapleGoods[s]];

                        serfBudget = Math.Min(serfNeedValue * priceLevel * 2f,
                            unTreasury * SerfBudgetCap);
                        float serfBidScale = serfNeedValue > 0f
                            ? serfBudget / (serfNeedValue * priceLevel)
                            : 0f;

                        for (int s = 0; s < GoodsV4.StapleGoods.Length; s++)
                        {
                            int g = GoodsV4.StapleGoods[s];
                            if (perStaple <= 0f) continue;
                            orders.Add(new Order
                            {
                                CountyId = i,
                                GoodId = g,
                                Side = OrderSide.Buy,
                                Source = OrderSource.SerfFeeding,
                                Quantity = perStaple,
                                MaxBid = GoodsV4.Value[g] * priceLevel * serfBidScale,
                            });
                        }
                    }

                    // Priority 2: Reserve stipend (handled in UpdateMoney, not a buy order)
                    float stipendReserve = lnPop * StipendPerCapita;

                    // Remaining budget for household
                    float remaining = Math.Max(0f, unTreasury - serfBudget - stipendReserve);

                    // Tier allocation: 5/5/15/75
                    PostTierOrders(orders, i, OrderSource.UpperNobility,
                        remaining * 0.05f, unPop, GoodsV4.StapleGoods, UpperNobleStaplePerGood, priceLevel);
                    PostTierOrders(orders, i, OrderSource.UpperNobility,
                        remaining * 0.05f, unPop, GoodsV4.BasicGoods, UpperNobleBasicPerGood, priceLevel);
                    PostTierOrders(orders, i, OrderSource.UpperNobility,
                        remaining * 0.15f, unPop, GoodsV4.ComfortGoods, UpperNobleComfortPerGood, priceLevel);
                    PostTierOrders(orders, i, OrderSource.UpperNobility,
                        remaining * 0.75f, unPop, GoodsV4.LuxuryGoods, UpperNobleLuxuryPerGood, priceLevel);
                }

                // 6. Lower noble buy orders
                float lnTreasury = ce.LowerNobleTreasury;
                if (lnPop > 0f && lnTreasury > 0.01f)
                {
                    // Tier allocation: 15/10/25/50
                    PostTierOrders(orders, i, OrderSource.LowerNobility,
                        lnTreasury * 0.15f, lnPop, GoodsV4.StapleGoods, LowerNobleStaplePerGood, priceLevel);
                    PostTierOrders(orders, i, OrderSource.LowerNobility,
                        lnTreasury * 0.10f, lnPop, GoodsV4.BasicGoods, LowerNobleBasicPerGood, priceLevel);
                    PostTierOrders(orders, i, OrderSource.LowerNobility,
                        lnTreasury * 0.25f, lnPop, GoodsV4.ComfortGoods, LowerNobleComfortPerGood, priceLevel);
                    PostTierOrders(orders, i, OrderSource.LowerNobility,
                        lnTreasury * 0.50f, lnPop, GoodsV4.LuxuryGoods, LowerNobleLuxuryPerGood, priceLevel);
                }

                // 7. Facility sell orders + input buy orders
                // Production throttled by min(input fill, output sell fill) from last tick.
                // Input orders scaled to match production, not full capacity.
                float ucPop = ce.UpperCommonerPop;
                if (ucPop > 0f)
                {
                    float popPerFacility = ucPop / fc;

                    for (int g = 0; g < gc; g++)
                        _facilityInputNeed[g] = 0f;

                    for (int f = 0; f < fc; f++)
                    {
                        var fac = FacilitiesV4.Defs[f];
                        int outputGoodId = (int)fac.Output;

                        // Compute input fill = min across all input goods
                        float inputFill = 1.0f;
                        for (int inp = 0; inp < fac.Inputs.Length; inp++)
                        {
                            int inputGoodId = (int)fac.Inputs[inp].Good;
                            inputFill = Math.Min(inputFill, ce.FacilityInputGoodFill[inputGoodId]);
                        }

                        // Sell fill from last tick (how much of output the market absorbed)
                        float sellFill = ce.FacilityOutputGoodFill[outputGoodId];

                        // Production = capacity × min(inputFill, sellFill)
                        // Floor at 0.1 to allow cold-start and recovery
                        float effectiveFill = Math.Max(
                            Math.Min(inputFill, Math.Max(sellFill, 0.1f)),
                            0.1f);
                        float outputVolume = popPerFacility * fac.ThroughputPerCapita * effectiveFill;

                        if (outputVolume > 0.001f)
                        {
                            orders.Add(new Order
                            {
                                CountyId = i,
                                GoodId = outputGoodId,
                                Side = OrderSide.Sell,
                                Source = OrderSource.Facility,
                                Quantity = outputVolume,
                            });
                        }

                        // 8. Input buy orders scaled to production (not full capacity)
                        for (int inp = 0; inp < fac.Inputs.Length; inp++)
                        {
                            int inputGoodId = (int)fac.Inputs[inp].Good;
                            _facilityInputNeed[inputGoodId] +=
                                popPerFacility * fac.ThroughputPerCapita * effectiveFill
                                * fac.Inputs[inp].Ratio;
                        }
                    }

                    float totalInputNeedValue = 0f;
                    for (int g = 0; g < gc; g++)
                    {
                        if (_facilityInputNeed[g] <= 0f) continue;
                        totalInputNeedValue += _facilityInputNeed[g] * GoodsV4.Value[g];
                    }

                    if (totalInputNeedValue > 0f)
                    {
                        // Bid at fair value — facilities are profitable so this is sustainable
                        for (int g = 0; g < gc; g++)
                        {
                            if (_facilityInputNeed[g] <= 0.001f) continue;
                            orders.Add(new Order
                            {
                                CountyId = i,
                                GoodId = g,
                                Side = OrderSide.Buy,
                                Source = OrderSource.FacilityInput,
                                Quantity = _facilityInputNeed[g],
                                MaxBid = GoodsV4.Value[g] * priceLevel,
                            });
                        }
                    }

                    // 9. Upper commoner household buy orders
                    float taxOverhead = 1f + TaxRate + TitheRate;
                    float householdBudget = Math.Max(0f, ce.UpperCommonerCoin / taxOverhead);

                    if (householdBudget > 0.01f)
                    {
                        // Tier allocation: 40% staples, 10% basics, 35% comforts, 15% luxuries
                        PostStapleOrdersEqual(orders, i, OrderSource.UpperCommoner,
                            householdBudget * 0.40f, ucPop, GoodsV4.StapleGoods, UpperCommonerStaplePerGood, priceLevel);
                        PostTierOrders(orders, i, OrderSource.UpperCommoner,
                            householdBudget * 0.10f, ucPop, GoodsV4.BasicGoods, UpperCommonerBasicPerGood, priceLevel);
                        PostTierOrders(orders, i, OrderSource.UpperCommoner,
                            householdBudget * 0.35f, ucPop, GoodsV4.ComfortGoods, UpperCommonerComfortPerGood, priceLevel);
                        PostTierOrders(orders, i, OrderSource.UpperCommoner,
                            householdBudget * 0.15f, ucPop, GoodsV4.LuxuryGoods, UpperCommonerLuxuryPerGood, priceLevel);
                    }
                }

                // 10. Upper clergy buy orders (from tithe treasury)
                float uclergyPop = ce.UpperClergyPop;
                float uclergyTreasury = ce.UpperClergyTreasury;
                float lclergyPop = ce.LowerClergyPop;

                if (uclergyPop > 0f && uclergyTreasury > 0.01f)
                {
                    // Reserve wages for lower clergy
                    float wageReserve = lclergyPop * ClergyWagePerCapita;
                    float clergyRemaining = Math.Max(0f, uclergyTreasury - wageReserve);

                    if (clergyRemaining > 0.01f)
                    {
                        // Tier allocation: 40% worship, 15% staples, 10% basics, 35% comforts
                        PostTierOrders(orders, i, OrderSource.UpperClergy,
                            clergyRemaining * 0.40f, uclergyPop, WorshipGoods, UpperClergyWorshipPerGood, priceLevel);
                        PostTierOrders(orders, i, OrderSource.UpperClergy,
                            clergyRemaining * 0.15f, uclergyPop, GoodsV4.StapleGoods, UpperClergyStaplePerGood, priceLevel);
                        PostTierOrders(orders, i, OrderSource.UpperClergy,
                            clergyRemaining * 0.10f, uclergyPop, GoodsV4.BasicGoods, UpperClergyBasicPerGood, priceLevel);
                        PostTierOrders(orders, i, OrderSource.UpperClergy,
                            clergyRemaining * 0.35f, uclergyPop, GoodsV4.ComfortGoods, UpperClergyComfortPerGood, priceLevel);
                    }
                }

                // 11. Lower clergy buy orders (from wages)
                float lcCoin = ce.LowerClergyCoin;
                if (lclergyPop > 0f && lcCoin > 0.01f)
                {
                    // Tier allocation: 50% staples, 25% basics, 25% worship (candles)
                    PostTierOrders(orders, i, OrderSource.LowerClergy,
                        lcCoin * 0.50f, lclergyPop, GoodsV4.StapleGoods, LowerClergyStaplePerGood, priceLevel);
                    PostTierOrders(orders, i, OrderSource.LowerClergy,
                        lcCoin * 0.25f, lclergyPop, GoodsV4.BasicGoods, LowerClergyBasicPerGood, priceLevel);
                    PostTierOrders(orders, i, OrderSource.LowerClergy,
                        lcCoin * 0.25f, lclergyPop, LowerClergyWorshipGoods, LowerClergyWorshipPerGood, priceLevel);
                }
            }

            // 12. Cross-market trade orders (uses last tick's surplus/deficit/prices)
            GenerateTradeOrders();
        }

        // ── Trade order generation ──

        void GenerateTradeOrders()
        {
            int gc = GoodsV4.Count;

            for (int mA = 1; mA <= _econ.MarketCount; mA++)
            {
                var marketA = _econ.Markets[mA];
                for (int mB = mA + 1; mB <= _econ.MarketCount; mB++)
                {
                    var marketB = _econ.Markets[mB];
                    float distance = _econ.HubToHubCost[mA][mB];
                    if (distance <= 0f) continue;

                    for (int g = 0; g < gc; g++)
                    {
                        if (g == (int)GoodTypeV4.Gold || g == (int)GoodTypeV4.Silver) continue;

                        // Try A→B (A exports to B)
                        TryPostTrade(mA, mB, g, marketA, marketB, distance);
                        // Try B→A (B exports to A)
                        TryPostTrade(mB, mA, g, marketB, marketA, distance);
                    }
                }
            }
        }

        void TryPostTrade(int srcId, int dstId, int g,
            MarketStateV4 src, MarketStateV4 dst, float distance)
        {
            float surplus = src.LastSurplus[g];
            float deficit = dst.LastDeficit[g];
            if (surplus <= 0f || deficit <= 0f) return;

            float priceDiff = dst.ClearingPrice[g] - src.ClearingPrice[g];
            float transportCost = distance * GoodsV4.Bulk[g] * TransportRate;
            float profit = priceDiff - transportCost;
            if (profit <= 0f) return;

            float maxVolume = Math.Min(surplus, deficit);
            float margin = profit / Math.Max(src.ClearingPrice[g], 0.01f);
            float volume = maxVolume * Math.Min(margin, 1f);
            if (volume < 0.01f) return;

            // Buy order in source market (goods leaving)
            src.Orders.Add(new Order
            {
                CountyId = src.HubCountyId,
                GoodId = g,
                Side = OrderSide.Buy,
                Source = OrderSource.Trade,
                Quantity = volume,
                MaxBid = dst.ClearingPrice[g] - transportCost,
                SourceMarketId = dstId,
            });

            // Sell order in dest market (goods arriving)
            dst.Orders.Add(new Order
            {
                CountyId = dst.HubCountyId,
                GoodId = g,
                Side = OrderSide.Sell,
                Source = OrderSource.Trade,
                Quantity = volume,
                SourceMarketId = srcId,
            });
        }

        /// <summary>
        /// Post buy orders for a tier of goods. Budget is split across goods in the tier,
        /// quantity = pop × perCapitaPerGood, maxBid = budget-proportional so total max spend = budget.
        /// </summary>
        static void PostTierOrders(List<Order> orders, int countyId, OrderSource source,
            float budget, float pop, int[] tierGoods, float perCapitaPerGood, float priceLevel)
        {
            if (budget <= 0f || pop <= 0f || tierGoods.Length == 0) return;

            // Total value of needs at base value × price level
            float totalNeedValue = 0f;
            for (int i = 0; i < tierGoods.Length; i++)
            {
                int g = tierGoods[i];
                if (GoodsV4.Value[g] <= 0f) continue; // skip Gold
                totalNeedValue += pop * perCapitaPerGood * GoodsV4.Value[g];
            }

            if (totalNeedValue <= 0f) return;

            // bidScale: budget / (need value at price level). >1 means can outbid equilibrium.
            // Cap at 3x to prevent nobles with huge treasuries from bidding 600x base.
            float bidScale = Math.Min(budget / (totalNeedValue * priceLevel), 3.0f);

            for (int i = 0; i < tierGoods.Length; i++)
            {
                int g = tierGoods[i];
                if (GoodsV4.Value[g] <= 0f) continue;
                float qty = pop * perCapitaPerGood;
                if (qty <= 0f) continue;

                orders.Add(new Order
                {
                    CountyId = countyId,
                    GoodId = g,
                    Side = OrderSide.Buy,
                    Source = source,
                    Quantity = qty,
                    MaxBid = GoodsV4.Value[g] * priceLevel * bidScale,
                });
            }
        }

        /// <summary>
        /// Post staple buy orders with budget split equally per good (not by value).
        /// Poor buyers should spend equally on each food type, bidding high on cheap goods
        /// rather than wasting budget on expensive ones they can't win.
        /// </summary>
        static void PostStapleOrdersEqual(List<Order> orders, int countyId, OrderSource source,
            float budget, float pop, int[] tierGoods, float perCapitaPerGood, float priceLevel)
        {
            if (budget <= 0f || pop <= 0f || tierGoods.Length == 0) return;

            int validCount = 0;
            for (int i = 0; i < tierGoods.Length; i++)
                if (GoodsV4.Value[tierGoods[i]] > 0f) validCount++;
            if (validCount == 0) return;

            float perGoodBudget = budget / validCount;

            for (int i = 0; i < tierGoods.Length; i++)
            {
                int g = tierGoods[i];
                if (GoodsV4.Value[g] <= 0f) continue;
                float qty = pop * perCapitaPerGood;
                if (qty <= 0f) continue;

                float needValue = qty * GoodsV4.Value[g] * priceLevel;
                float maxBid = needValue > 0f
                    ? GoodsV4.Value[g] * priceLevel * (perGoodBudget / needValue)
                    : 0f;

                orders.Add(new Order
                {
                    CountyId = countyId,
                    GoodId = g,
                    Side = OrderSide.Buy,
                    Source = source,
                    Quantity = qty,
                    MaxBid = maxBid,
                });
            }
        }

        // ════════════════════════════════════════════════════════════
        //  PHASE 2: RESOLVE MARKETS
        // ════════════════════════════════════════════════════════════

        void ResolveMarkets(SimulationState state, MapData mapData)
        {
            int gc = GoodsV4.Count;

            for (int m = 1; m <= _econ.MarketCount; m++)
            {
                var market = _econ.Markets[m];
                var orders = market.Orders;

                // Compute M for this market
                float totalM = 0f;
                for (int c = 0; c < market.CountyIds.Count; c++)
                {
                    var ce = _econ.Counties[market.CountyIds[c]];
                    if (ce != null) totalM += ce.MoneySupply;
                }
                market.TotalMoneySupply = totalM;

                // Compute Q (total real output)
                float Q = 0f;
                for (int i = 0; i < orders.Count; i++)
                {
                    var o = orders[i];
                    if (o.Side == OrderSide.Sell)
                        Q += o.Quantity * GoodsV4.Value[o.GoodId];
                }
                market.TotalRealOutput = Q;

                // Price level: max((M × V) / Q, 1.0)
                float priceLevel = Q > 0f
                    ? Math.Max((totalM * BaseVelocity) / Q, 1.0f)
                    : 1.0f;
                market.PriceLevel = priceLevel;

                // Bucket orders by good
                for (int g = 0; g < gc; g++)
                {
                    _buyByGood[g].Clear();
                    _sellByGood[g].Clear();
                }
                for (int i = 0; i < orders.Count; i++)
                {
                    var o = orders[i];
                    if (o.Side == OrderSide.Buy)
                        _buyByGood[o.GoodId].Add(i);
                    else
                        _sellByGood[o.GoodId].Add(i);
                }

                // Resolve per good
                for (int g = 0; g < gc; g++)
                {
                    var buys = _buyByGood[g];
                    var sells = _sellByGood[g];

                    float totalSell = 0f;
                    for (int i = 0; i < sells.Count; i++)
                        totalSell += orders[sells[i]].Quantity;

                    float totalBuy = 0f;
                    for (int i = 0; i < buys.Count; i++)
                        totalBuy += orders[buys[i]].Quantity;

                    if (totalSell <= 0f && totalBuy <= 0f)
                    {
                        market.ClearingPrice[g] = GoodsV4.Value[g] * priceLevel;
                        continue;
                    }

                    // Scarcity
                    float denom = Math.Max(Math.Min(totalBuy, totalSell), 1f);
                    float scarcity = Math.Max(-1f, Math.Min(1f,
                        (totalBuy - totalSell) / denom));
                    float clearingPrice = GoodsV4.Value[g] * (1f + 0.75f * scarcity) * priceLevel;
                    market.ClearingPrice[g] = clearingPrice;

                    if (clearingPrice <= 0f || totalSell <= 0f) continue;

                    // Sort buy orders by maxBid descending
                    buys.Sort((a, b) => orders[b].MaxBid.CompareTo(orders[a].MaxBid));

                    // Fill buy orders top-down
                    float remainingSupply = totalSell;
                    float totalFilledBuy = 0f;

                    for (int bi = 0; bi < buys.Count; bi++)
                    {
                        int idx = buys[bi];
                        var o = orders[idx];

                        if (o.MaxBid < clearingPrice || remainingSupply <= 0f)
                            break;

                        float filled = Math.Min(o.Quantity, remainingSupply);
                        o.FilledQuantity = filled;
                        orders[idx] = o;

                        remainingSupply -= filled;
                        totalFilledBuy += filled;
                    }

                    // Fill sell orders proportionally
                    float sellFillRatio = Math.Min(totalFilledBuy / totalSell, 1f);
                    for (int si = 0; si < sells.Count; si++)
                    {
                        int idx = sells[si];
                        var o = orders[idx];
                        o.FilledQuantity = o.Quantity * sellFillRatio;
                        orders[idx] = o;
                    }
                }

                // Route coin for all filled orders + apply tax/tithe
                for (int i = 0; i < orders.Count; i++)
                {
                    var o = orders[i];
                    if (o.FilledQuantity <= 0f) continue;

                    float amount = o.FilledQuantity * market.ClearingPrice[o.GoodId];

                    // ── Cross-market trade coin routing ──
                    if (o.Source == OrderSource.Trade)
                    {
                        if (o.Side == OrderSide.Buy)
                        {
                            // Trade buy in exporting market: importing market's lord pays
                            var importMarket = _econ.Markets[o.SourceMarketId];
                            var importHub = _econ.Counties[importMarket.HubCountyId];
                            if (importHub != null)
                                importHub.UpperNobleTreasury -= amount;
                        }
                        else // Sell
                        {
                            // Trade sell in importing market: exporting market's lord receives (minus tariff)
                            var exportMarket = _econ.Markets[o.SourceMarketId];
                            var exportHub = _econ.Counties[exportMarket.HubCountyId];
                            float tariff = amount * TradeTariffRate;
                            if (exportHub != null)
                            {
                                exportHub.UpperNobleTreasury += (amount - tariff);
                                exportHub.UpperNobleIncome += (amount - tariff);
                            }
                            // Tariff stays with importing market's lord
                            var importHub = _econ.Counties[market.HubCountyId];
                            if (importHub != null)
                            {
                                importHub.UpperNobleTreasury += tariff;
                                importHub.UpperNobleIncome += tariff;
                                importHub.TariffRevenue += tariff;
                            }
                        }
                        continue;
                    }

                    var ce = _econ.Counties[o.CountyId];
                    if (ce == null) continue;

                    if (o.Side == OrderSide.Buy)
                    {
                        // Deduct from buyer
                        switch (o.Source)
                        {
                            case OrderSource.SerfFeeding:
                            case OrderSource.UpperNobility:
                                ce.UpperNobleTreasury -= amount;
                                ce.UpperNobleSpend += amount;
                                break;
                            case OrderSource.LowerNobility:
                                ce.LowerNobleTreasury -= amount;
                                ce.LowerNobleSpend += amount;
                                break;
                            case OrderSource.UpperClergy:
                                ce.UpperClergyTreasury -= amount;
                                ce.UpperClergySpend += amount;
                                break;
                            case OrderSource.LowerClergy:
                                ce.LowerClergyCoin -= amount;
                                ce.LowerClergySpend += amount;
                                break;
                            case OrderSource.FacilityInput:
                            case OrderSource.UpperCommoner:
                                ce.UpperCommonerCoin -= amount;
                                ce.UpperCommonerSpend += amount;
                                break;
                        }

                        // Track serf feeding for satisfaction
                        if (o.Source == OrderSource.SerfFeeding)
                            ce.SerfFoodProvided += o.FilledQuantity;

                        // Tax + tithe on upper commoner purchases (lower clergy exempt)
                        if (o.Source == OrderSource.UpperCommoner || o.Source == OrderSource.FacilityInput)
                        {
                            float tax = amount * TaxRate;
                            float tithe = amount * TitheRate;
                            ce.UpperCommonerCoin -= (tax + tithe);
                            ce.UpperNobleTreasury += tax;
                            ce.UpperNobleIncome += tax;
                            ce.TaxRevenue += tax;
                            ce.UpperClergyTreasury += tithe;
                            ce.UpperClergyIncome += tithe;
                            ce.TitheRevenue += tithe;
                        }
                    }
                    else // Sell
                    {
                        // Credit to seller
                        switch (o.Source)
                        {
                            case OrderSource.PeasantSurplus:
                                ce.UpperNobleTreasury += amount;
                                ce.UpperNobleIncome += amount;
                                break;
                            case OrderSource.Facility:
                                ce.UpperCommonerCoin += amount;
                                ce.UpperCommonerIncome += amount;
                                break;
                        }
                    }
                }

                // Compute facility fill rates for each county in this market
                // Reset to 0 — only goods with actual orders get fill rates
                for (int c = 0; c < market.CountyIds.Count; c++)
                {
                    var ce = _econ.Counties[market.CountyIds[c]];
                    if (ce == null) continue;
                    for (int g = 0; g < gc; g++)
                    {
                        ce.FacilityInputGoodFill[g] = 0f;
                        ce.FacilityOutputGoodFill[g] = 0f;
                    }
                }

                // Set fill rates from actual orders
                for (int i = 0; i < orders.Count; i++)
                {
                    var o = orders[i];
                    if (o.Quantity <= 0f) continue;

                    var ce = _econ.Counties[o.CountyId];
                    if (ce == null) continue;

                    if (o.Source == OrderSource.FacilityInput && o.Side == OrderSide.Buy)
                    {
                        ce.FacilityInputGoodFill[o.GoodId] = o.FilledQuantity / o.Quantity;
                    }
                    else if (o.Source == OrderSource.Facility && o.Side == OrderSide.Sell)
                    {
                        ce.FacilityOutputGoodFill[o.GoodId] = o.FilledQuantity / o.Quantity;
                    }
                }

                // Compute domestic surplus/deficit for next tick's trade orders.
                // Single pass: accumulate per-good domestic sell/buy/filledBuy.
                var domSell = new float[gc];
                var domBuy = new float[gc];
                var domFilledBuy = new float[gc];

                for (int i = 0; i < orders.Count; i++)
                {
                    var o = orders[i];
                    if (o.Source == OrderSource.Trade) continue;

                    if (o.Side == OrderSide.Sell)
                        domSell[o.GoodId] += o.Quantity;
                    else
                    {
                        domBuy[o.GoodId] += o.Quantity;
                        domFilledBuy[o.GoodId] += o.FilledQuantity;
                    }
                }

                for (int g = 0; g < gc; g++)
                {
                    market.LastSurplus[g] = Math.Max(0f, domSell[g] - domFilledBuy[g]);
                    market.LastDeficit[g] = Math.Max(0f, domBuy[g] - domFilledBuy[g]);
                }
            }
        }

        // ════════════════════════════════════════════════════════════
        //  PHASE 3: UPDATE MONEY
        // ════════════════════════════════════════════════════════════

        void UpdateMoney(SimulationState state, MapData mapData)
        {
            for (int i = 0; i < _econ.Counties.Length; i++)
            {
                var ce = _econ.Counties[i];
                if (ce == null) continue;

                // 1. Gold + silver minting → upper noble treasury
                float goldProd = ce.Production[(int)GoodTypeV4.Gold];
                if (goldProd > 0f)
                {
                    float mintableGold = goldProd * (1f - GoldJewelryFraction);
                    float minted = mintableGold * GoldCoinPerKg;
                    ce.UpperNobleTreasury += minted;
                    ce.UpperNobleIncome += minted;
                }
                float silverProd = ce.Production[(int)GoodTypeV4.Silver];
                if (silverProd > 0f)
                {
                    float minted = silverProd * SilverCoinPerKg;
                    ce.UpperNobleTreasury += minted;
                    ce.UpperNobleIncome += minted;
                }

                // 2. Stipend: upper noble → lower noble
                float stipend = Math.Min(
                    ce.LowerNobilityPop * StipendPerCapita,
                    Math.Max(0f, ce.UpperNobleTreasury));
                if (stipend > 0f)
                {
                    ce.UpperNobleTreasury -= stipend;
                    ce.UpperNobleSpend += stipend;
                    ce.LowerNobleTreasury += stipend;
                }

                // 3. Noble wages: handled at market level below

                // 4. Clergy wages: upper clergy → lower clergy
                float wages = Math.Min(
                    ce.LowerClergyPop * ClergyWagePerCapita,
                    Math.Max(0f, ce.UpperClergyTreasury));
                if (wages > 0f)
                {
                    ce.UpperClergyTreasury -= wages;
                    ce.UpperClergySpend += wages;
                    ce.LowerClergyCoin += wages;
                    ce.LowerClergyIncome += wages;
                }

                // 5. Coin wear on M (upper commoner + lower clergy)
                if (ce.UpperCommonerCoin > 0f)
                    ce.UpperCommonerCoin *= (1f - CoinWearRate);
                if (ce.LowerClergyCoin > 0f)
                    ce.LowerClergyCoin *= (1f - CoinWearRate);

                // Clamp coin pools to 0 (can go slightly negative from tax/tithe)
                if (ce.UpperCommonerCoin < 0f) ce.UpperCommonerCoin = 0f;
                if (ce.LowerClergyCoin < 0f) ce.LowerClergyCoin = 0f;
            }

            // 3. Noble wages: pooled at market level.
            // Total wage bill and total noble treasury within each market,
            // then pay proportionally (wealthy lords subsidize poor ones within a realm).
            for (int m = 1; m <= _econ.MarketCount; m++)
            {
                var ids = _econ.Markets[m].CountyIds;
                float totalWageBill = 0f;
                float totalTreasury = 0f;

                for (int c = 0; c < ids.Count; c++)
                {
                    var ce = _econ.Counties[ids[c]];
                    totalWageBill += ce.UpperCommonerPop * NobleWagePerCapita;
                    totalTreasury += Math.Max(0f, ce.UpperNobleTreasury);
                }

                if (totalWageBill <= 0f || totalTreasury <= 0f) continue;

                // Pay up to available treasury, distributed proportionally to each county's UC pop
                float payable = Math.Min(totalWageBill, totalTreasury);
                float payRate = payable / totalWageBill; // fraction of full wage actually paid

                // Deduct from treasuries proportionally to each county's share of total treasury
                float treasuryScale = payable / totalTreasury;

                for (int c = 0; c < ids.Count; c++)
                {
                    var ce = _econ.Counties[ids[c]];
                    float wage = ce.UpperCommonerPop * NobleWagePerCapita * payRate;
                    float deduction = Math.Max(0f, ce.UpperNobleTreasury) * treasuryScale;

                    ce.UpperNobleTreasury -= deduction;
                    ce.UpperNobleSpend += deduction;
                    ce.UpperCommonerCoin += wage;
                    ce.UpperCommonerIncome += wage;
                }
            }
        }

        // ════════════════════════════════════════════════════════════
        //  PHASE 4: UPDATE SATISFACTION
        // ════════════════════════════════════════════════════════════

        void UpdateSatisfaction(SimulationState state, MapData mapData)
        {
            // Build per-county fulfillment split by order source and good tier.
            // Source indices: 0=UpperNobility(+SerfFeeding), 1=LowerNobility,
            //   2=UpperCommoner, 3=UpperClergy, 4=LowerClergy
            const int SrcCount = 5;
            int countyLen = _econ.Counties.Length;

            var stapleDesired = new float[countyLen * SrcCount];
            var stapleFilled  = new float[countyLen * SrcCount];
            var econDesired   = new float[countyLen * SrcCount];
            var econFilled    = new float[countyLen * SrcCount];

            // Shared religion: worship goods (candles + wine) from clergy orders
            var worshipDesired = new float[countyLen];
            var worshipFilled  = new float[countyLen];

            for (int m = 1; m <= _econ.MarketCount; m++)
            {
                var orders = _econ.Markets[m].Orders;
                for (int i = 0; i < orders.Count; i++)
                {
                    var o = orders[i];
                    if (o.Side != OrderSide.Buy) continue;

                    float desiredVal = o.Quantity * GoodsV4.Value[o.GoodId];
                    float filledVal = o.FilledQuantity * GoodsV4.Value[o.GoodId];

                    int srcIdx;
                    switch (o.Source)
                    {
                        case OrderSource.UpperNobility:
                        case OrderSource.SerfFeeding:
                            srcIdx = 0; break;
                        case OrderSource.LowerNobility:
                            srcIdx = 1; break;
                        case OrderSource.UpperCommoner:
                            srcIdx = 2; break;
                        case OrderSource.UpperClergy:
                            srcIdx = 3; break;
                        case OrderSource.LowerClergy:
                            srcIdx = 4; break;
                        default:
                            continue; // FacilityInput, PeasantSurplus, Trade
                    }

                    int idx = o.CountyId * SrcCount + srcIdx;
                    var tier = GoodsV4.Tier[o.GoodId];

                    if (tier == NeedTierV4.Staple)
                    {
                        // Quantity-weighted for survival: a kg of food is a kg of food
                        stapleDesired[idx] += o.Quantity;
                        stapleFilled[idx] += o.FilledQuantity;
                    }
                    else
                    {
                        // Economic component: comfort+luxury for UC/clergy, luxury only for nobles
                        bool isEcon;
                        if (srcIdx <= 1) // nobles
                            isEcon = tier == NeedTierV4.Luxury;
                        else // UC, clergy
                            isEcon = tier >= NeedTierV4.Comfort;

                        if (isEcon)
                        {
                            econDesired[idx] += desiredVal;
                            econFilled[idx] += filledVal;
                        }
                    }

                    // Worship tracking (clergy orders for candles/wine → shared religion)
                    if ((srcIdx == 3 || srcIdx == 4) &&
                        (o.GoodId == (int)GoodTypeV4.Candles || o.GoodId == (int)GoodTypeV4.Wine))
                    {
                        worshipDesired[o.CountyId] += desiredVal;
                        worshipFilled[o.CountyId] += filledVal;
                    }
                }
            }

            float stabilityEtc = WeightStability * StabilityPlaceholder
                               + WeightGovernance * GovernancePlaceholder;

            for (int i = 0; i < countyLen; i++)
            {
                var ce = _econ.Counties[i];
                if (ce == null) continue;

                int b = i * SrcCount;

                // Shared religion component
                float religion = worshipDesired[i] > 0f
                    ? Math.Min(worshipFilled[i] / worshipDesired[i], 1f)
                    : 0.5f;
                ce.ReligionSatisfaction = religion;

                // ── Lower commoner (no market orders) ──
                float lcSurvival = 0f;
                if (ce.LowerCommonerPop > 0f)
                {
                    float totalStapleProd = 0f;
                    for (int s = 0; s < GoodsV4.StapleGoods.Length; s++)
                        totalStapleProd += ce.Production[GoodsV4.StapleGoods[s]];
                    float totalStapleNeed = ce.LowerCommonerPop * StapleNeedPerCapita;
                    lcSurvival = totalStapleNeed > 0f
                        ? Math.Min((totalStapleProd + ce.SerfFoodProvided) / totalStapleNeed, 1f)
                        : 0f;
                }
                ce.SurvivalSatisfaction = lcSurvival;
                ce.LowerCommonerSatisfaction = WeightSurvival * lcSurvival
                    + WeightReligion * religion + WeightEconomic * 0.5f + stabilityEtc;

                // ── Upper nobility ──
                float unSurv = SafeRatio(stapleFilled[b + 0], stapleDesired[b + 0]);
                float unEcon = SafeRatio(econFilled[b + 0], econDesired[b + 0]);
                ce.UpperNobilitySatisfaction = WeightSurvival * unSurv
                    + WeightReligion * religion + WeightEconomic * unEcon + stabilityEtc;

                // ── Lower nobility ──
                float lnSurv = SafeRatio(stapleFilled[b + 1], stapleDesired[b + 1]);
                float lnEcon = SafeRatio(econFilled[b + 1], econDesired[b + 1]);
                ce.LowerNobilitySatisfaction = WeightSurvival * lnSurv
                    + WeightReligion * religion + WeightEconomic * lnEcon + stabilityEtc;

                // ── Upper commoner ──
                float ucSurv = SafeRatio(stapleFilled[b + 2], stapleDesired[b + 2]);
                float ucEcon = SafeRatio(econFilled[b + 2], econDesired[b + 2]);
                ce.EconomicSatisfaction = ucEcon;
                ce.UpperCommonerSatisfaction = WeightSurvival * ucSurv
                    + WeightReligion * religion + WeightEconomic * ucEcon + stabilityEtc;

                // ── Upper clergy ──
                float uclSurv = SafeRatio(stapleFilled[b + 3], stapleDesired[b + 3]);
                float uclEcon = SafeRatio(econFilled[b + 3], econDesired[b + 3]);
                ce.UpperClergySatisfaction = WeightSurvival * uclSurv
                    + WeightReligion * religion + WeightEconomic * uclEcon + stabilityEtc;

                // ── Lower clergy ──
                float lclSurv = SafeRatio(stapleFilled[b + 4], stapleDesired[b + 4]);
                float lclEcon = SafeRatio(econFilled[b + 4], econDesired[b + 4]);
                ce.LowerClergySatisfaction = WeightSurvival * lclSurv
                    + WeightReligion * religion + WeightEconomic * lclEcon + stabilityEtc;
            }
        }

        static float SafeRatio(float filled, float desired)
        {
            return desired > 0f ? Math.Min(filled / desired, 1f) : 0.5f;
        }

        // ════════════════════════════════════════════════════════════
        //  PHASE 5b: POPULATION DYNAMICS
        // ════════════════════════════════════════════════════════════

        void UpdatePopulation(SimulationState state, MapData mapData)
        {
            // ── Birth/Death per county per class ──
            for (int i = 0; i < _econ.Counties.Length; i++)
            {
                var ce = _econ.Counties[i];
                if (ce == null) continue;

                ce.Births = 0f;
                ce.Deaths = 0f;
                ce.NetMigration = 0f;

                ApplyBirthDeath(ref ce.LowerCommonerPop, ce.LowerCommonerSatisfaction, ref ce.Births, ref ce.Deaths);
                ApplyBirthDeath(ref ce.UpperCommonerPop, ce.UpperCommonerSatisfaction, ref ce.Births, ref ce.Deaths);
                ApplyBirthDeath(ref ce.LowerNobilityPop, ce.LowerNobilitySatisfaction, ref ce.Births, ref ce.Deaths);
                ApplyBirthDeath(ref ce.UpperNobilityPop, ce.UpperNobilitySatisfaction, ref ce.Births, ref ce.Deaths);
                ApplyBirthDeath(ref ce.LowerClergyPop, ce.LowerClergySatisfaction, ref ce.Births, ref ce.Deaths);
                ApplyBirthDeath(ref ce.UpperClergyPop, ce.UpperClergySatisfaction, ref ce.Births, ref ce.Deaths);
            }

            // ── Migration within markets ──
            for (int m = 1; m <= _econ.MarketCount; m++)
            {
                var market = _econ.Markets[m];
                if (market.CountyIds.Count < 2) continue;

                MigrateWithinMarket(market, isUpper: true, UpperMobility);
                MigrateWithinMarket(market, isUpper: false, LowerMobility);
            }
        }

        static void ApplyBirthDeath(ref float pop, float satisfaction, ref float totalBirths, ref float totalDeaths)
        {
            if (pop <= 0f) return;
            float births = pop * BaseBirthRate * (1f + SatisfactionBirthMod * satisfaction);
            float deaths = pop * BaseDeathRate * (1f + SatisfactionDeathMod * (1f - satisfaction));
            pop += births - deaths;
            if (pop < 0f) pop = 0f;
            totalBirths += births;
            totalDeaths += deaths;
        }

        void MigrateWithinMarket(MarketStateV4 market, bool isUpper, float mobility)
        {
            var ids = market.CountyIds;
            int n = ids.Count;

            // Population-weighted mean satisfaction
            float satWt = 0f, popTotal = 0f;
            for (int c = 0; c < n; c++)
            {
                var ce = _econ.Counties[ids[c]];
                float pop = isUpper ? ce.UpperCommonerPop : ce.LowerCommonerPop;
                float sat = isUpper ? ce.UpperCommonerSatisfaction : ce.LowerCommonerSatisfaction;
                satWt += sat * pop;
                popTotal += pop;
            }
            if (popTotal <= 0f) return;
            float meanSat = satWt / popTotal;

            float totalLeaving = 0f, totalArriving = 0f;
            var desire = new float[n];

            for (int c = 0; c < n; c++)
            {
                var ce = _econ.Counties[ids[c]];
                float pop = isUpper ? ce.UpperCommonerPop : ce.LowerCommonerPop;
                float sat = isUpper ? ce.UpperCommonerSatisfaction : ce.LowerCommonerSatisfaction;
                if (pop <= 0f) continue;

                float gap = sat - meanSat;
                if (gap < -MigrationThreshold)
                {
                    float rate = MigrationRate * mobility * (-gap - MigrationThreshold) * pop;
                    desire[c] = -rate;
                    totalLeaving += rate;
                }
                else if (gap > MigrationThreshold)
                {
                    float rate = MigrationRate * mobility * (gap - MigrationThreshold) * pop;
                    desire[c] = rate;
                    totalArriving += rate;
                }
            }

            if (totalLeaving <= 0f) return;
            float scale = totalArriving > 0f ? totalLeaving / totalArriving : 0f;

            for (int c = 0; c < n; c++)
            {
                if (desire[c] == 0f) continue;
                var ce = _econ.Counties[ids[c]];

                float delta = desire[c] < 0f ? desire[c] : desire[c] * scale;

                if (isUpper)
                    ce.UpperCommonerPop = Math.Max(0f, ce.UpperCommonerPop + delta);
                else
                    ce.LowerCommonerPop = Math.Max(0f, ce.LowerCommonerPop + delta);

                ce.NetMigration += delta;
            }
        }
    }
}
