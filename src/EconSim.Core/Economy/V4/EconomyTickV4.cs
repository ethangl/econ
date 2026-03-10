using System;
using System.Collections.Generic;
using EconSim.Core.Data;
using EconSim.Core.Simulation;
using EconSim.Core.Simulation.Systems;

namespace EconSim.Core.Economy.V4
{
    /// <summary>
    /// V4 economy tick system. Runs all 4 phases each day.
    /// Phase 3: upper commoners + facilities, tax/tithe, clergy economy.
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
        const float UpperCommonerStaplePerGood = 0.25f;  // ×4 = 1.0 kg food
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
        // Upper noble: 10% staples, 10% basics, 30% comforts, 50% luxuries
        // Lower noble: 20% staples, 15% basics, 40% comforts, 25% luxuries

        // ── Monetary parameters ──
        const float GoldCoinPerKg = 50f;     // each kg of gold minted → 50 coins
        const float StipendPerCapita = 0.5f; // Cr/lower noble/day
        const float ClergyWagePerCapita = 0.3f; // Cr/lower clergy/day
        const float CoinWearRate = 0.001f;   // fraction of M lost per day
        const float BaseVelocity = 4.0f;     // base velocity of money

        // ── Tax & tithe ──
        const float TaxRate = 0.10f;         // lord skims 10% of upper commoner buys
        const float TitheRate = 0.10f;       // clergy skims 10% of upper commoner buys

        // ── Serf feeding ──
        const float SerfBudgetCap = 0.4f;    // max fraction of treasury for serf feeding

        // ── Facility budget ──
        const float FacilityInputBudgetShare = 0.50f; // fraction of upper commoner coin for inputs

        // ── Worship goods ──
        static readonly int[] WorshipGoods = { (int)GoodTypeV4.Candles, (int)GoodTypeV4.Wine };
        static readonly int[] LowerClergyWorshipGoods = { (int)GoodTypeV4.Candles };

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
                    if (g == (int)GoodTypeV4.Gold) continue;
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

                    // Tier allocation: 10/10/30/50
                    PostTierOrders(orders, i, OrderSource.UpperNobility,
                        remaining * 0.10f, unPop, GoodsV4.StapleGoods, UpperNobleStaplePerGood, priceLevel);
                    PostTierOrders(orders, i, OrderSource.UpperNobility,
                        remaining * 0.10f, unPop, GoodsV4.BasicGoods, UpperNobleBasicPerGood, priceLevel);
                    PostTierOrders(orders, i, OrderSource.UpperNobility,
                        remaining * 0.30f, unPop, GoodsV4.ComfortGoods, UpperNobleComfortPerGood, priceLevel);
                    PostTierOrders(orders, i, OrderSource.UpperNobility,
                        remaining * 0.50f, unPop, GoodsV4.LuxuryGoods, UpperNobleLuxuryPerGood, priceLevel);
                }

                // 6. Lower noble buy orders
                float lnTreasury = ce.LowerNobleTreasury;
                if (lnPop > 0f && lnTreasury > 0.01f)
                {
                    // Tier allocation: 20/15/40/25
                    PostTierOrders(orders, i, OrderSource.LowerNobility,
                        lnTreasury * 0.20f, lnPop, GoodsV4.StapleGoods, LowerNobleStaplePerGood, priceLevel);
                    PostTierOrders(orders, i, OrderSource.LowerNobility,
                        lnTreasury * 0.15f, lnPop, GoodsV4.BasicGoods, LowerNobleBasicPerGood, priceLevel);
                    PostTierOrders(orders, i, OrderSource.LowerNobility,
                        lnTreasury * 0.40f, lnPop, GoodsV4.ComfortGoods, LowerNobleComfortPerGood, priceLevel);
                    PostTierOrders(orders, i, OrderSource.LowerNobility,
                        lnTreasury * 0.25f, lnPop, GoodsV4.LuxuryGoods, LowerNobleLuxuryPerGood, priceLevel);
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
                        // Tier allocation: 40% staples, 25% basics, 30% comforts, 5% luxuries
                        PostTierOrders(orders, i, OrderSource.UpperCommoner,
                            householdBudget * 0.40f, ucPop, GoodsV4.StapleGoods, UpperCommonerStaplePerGood, priceLevel);
                        PostTierOrders(orders, i, OrderSource.UpperCommoner,
                            householdBudget * 0.25f, ucPop, GoodsV4.BasicGoods, UpperCommonerBasicPerGood, priceLevel);
                        PostTierOrders(orders, i, OrderSource.UpperCommoner,
                            householdBudget * 0.30f, ucPop, GoodsV4.ComfortGoods, UpperCommonerComfortPerGood, priceLevel);
                        PostTierOrders(orders, i, OrderSource.UpperCommoner,
                            householdBudget * 0.05f, ucPop, GoodsV4.LuxuryGoods, UpperCommonerLuxuryPerGood, priceLevel);
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
            float bidScale = budget / (totalNeedValue * priceLevel);

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

                // 1. Gold minting → upper noble treasury
                float goldProd = ce.Production[(int)GoodTypeV4.Gold];
                if (goldProd > 0f)
                {
                    float minted = goldProd * GoldCoinPerKg;
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

                // 3. Clergy wages: upper clergy → lower clergy
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

                // 4. Coin wear on M (upper commoner + lower clergy)
                if (ce.UpperCommonerCoin > 0f)
                    ce.UpperCommonerCoin *= (1f - CoinWearRate);
                if (ce.LowerClergyCoin > 0f)
                    ce.LowerClergyCoin *= (1f - CoinWearRate);

                // Clamp coin pools to 0 (can go slightly negative from tax/tithe)
                if (ce.UpperCommonerCoin < 0f) ce.UpperCommonerCoin = 0f;
                if (ce.LowerClergyCoin < 0f) ce.LowerClergyCoin = 0f;
            }
        }

        // ════════════════════════════════════════════════════════════
        //  PHASE 4: UPDATE SATISFACTION
        // ════════════════════════════════════════════════════════════

        void UpdateSatisfaction(SimulationState state, MapData mapData)
        {
            // Build per-county fulfillment from filled orders
            int countyLen = _econ.Counties.Length;
            var upperNobleDesired = new float[countyLen];
            var upperNobleFilled = new float[countyLen];
            var lowerNobleDesired = new float[countyLen];
            var lowerNobleFilled = new float[countyLen];
            var upperCommonerDesired = new float[countyLen];
            var upperCommonerFilled = new float[countyLen];
            var upperClergyDesired = new float[countyLen];
            var upperClergyFilled = new float[countyLen];
            var lowerClergyDesired = new float[countyLen];
            var lowerClergyFilled = new float[countyLen];

            for (int m = 1; m <= _econ.MarketCount; m++)
            {
                var orders = _econ.Markets[m].Orders;
                for (int i = 0; i < orders.Count; i++)
                {
                    var o = orders[i];
                    if (o.Side != OrderSide.Buy) continue;

                    float desiredVal = o.Quantity * GoodsV4.Value[o.GoodId];
                    float filledVal = o.FilledQuantity * GoodsV4.Value[o.GoodId];

                    switch (o.Source)
                    {
                        case OrderSource.UpperNobility:
                        case OrderSource.SerfFeeding:
                            upperNobleDesired[o.CountyId] += desiredVal;
                            upperNobleFilled[o.CountyId] += filledVal;
                            break;
                        case OrderSource.LowerNobility:
                            lowerNobleDesired[o.CountyId] += desiredVal;
                            lowerNobleFilled[o.CountyId] += filledVal;
                            break;
                        case OrderSource.UpperCommoner:
                            upperCommonerDesired[o.CountyId] += desiredVal;
                            upperCommonerFilled[o.CountyId] += filledVal;
                            break;
                        case OrderSource.UpperClergy:
                            upperClergyDesired[o.CountyId] += desiredVal;
                            upperClergyFilled[o.CountyId] += filledVal;
                            break;
                        case OrderSource.LowerClergy:
                            lowerClergyDesired[o.CountyId] += desiredVal;
                            lowerClergyFilled[o.CountyId] += filledVal;
                            break;
                    }
                }
            }

            for (int i = 0; i < countyLen; i++)
            {
                var ce = _econ.Counties[i];
                if (ce == null) continue;

                // Lower commoner survival: (local staple prod + lord-provided food) / staple need
                float pop = ce.LowerCommonerPop;
                if (pop > 0f)
                {
                    float totalStapleProd = 0f;
                    for (int s = 0; s < GoodsV4.StapleGoods.Length; s++)
                        totalStapleProd += ce.Production[GoodsV4.StapleGoods[s]];

                    float totalStapleNeed = pop * StapleNeedPerCapita;
                    ce.LowerCommonerSatisfaction = totalStapleNeed > 0f
                        ? Math.Min((totalStapleProd + ce.SerfFoodProvided) / totalStapleNeed, 1.0f)
                        : 0f;
                }

                // Noble satisfaction: fulfillment ratio of buy orders
                ce.UpperNobilitySatisfaction = upperNobleDesired[i] > 0f
                    ? Math.Min(upperNobleFilled[i] / upperNobleDesired[i], 1f)
                    : 0.5f;
                ce.LowerNobilitySatisfaction = lowerNobleDesired[i] > 0f
                    ? Math.Min(lowerNobleFilled[i] / lowerNobleDesired[i], 1f)
                    : 0.5f;

                // Upper commoner satisfaction: buy order fulfillment
                ce.UpperCommonerSatisfaction = upperCommonerDesired[i] > 0f
                    ? Math.Min(upperCommonerFilled[i] / upperCommonerDesired[i], 1f)
                    : 0.5f;

                // Clergy satisfaction: buy order fulfillment
                ce.UpperClergySatisfaction = upperClergyDesired[i] > 0f
                    ? Math.Min(upperClergyFilled[i] / upperClergyDesired[i], 1f)
                    : 0.5f;
                ce.LowerClergySatisfaction = lowerClergyDesired[i] > 0f
                    ? Math.Min(lowerClergyFilled[i] / lowerClergyDesired[i], 1f)
                    : 0.5f;
            }
        }
    }
}
