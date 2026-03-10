using System;
using System.Collections.Generic;
using EconSim.Core.Data;
using EconSim.Core.Simulation;
using EconSim.Core.Simulation.Systems;

namespace EconSim.Core.Economy.V4
{
    /// <summary>
    /// V4 economy tick system. Runs all 4 phases each day.
    /// Phase 2: local market resolution — sell orders, noble buy orders, clearing prices,
    /// gold minting, stipend transfers.
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

        // ── Budget allocation (fraction of remaining treasury after serf feeding + stipend reserve) ──
        // Upper noble: 10% staples, 10% basics, 30% comforts, 50% luxuries
        // Lower noble: 20% staples, 15% basics, 40% comforts, 25% luxuries

        // ── Monetary parameters ──
        const float GoldCoinPerKg = 50f;     // each kg of gold minted → 50 coins
        const float StipendPerCapita = 0.5f; // Cr/lower noble/day
        const float CoinWearRate = 0.001f;   // fraction of M lost per day
        const float BaseVelocity = 4.0f;     // base velocity of money

        // ── Serf feeding ──
        const float SerfBudgetCap = 0.4f;    // max fraction of treasury for serf feeding

        private EconomyStateV4 _econ;

        // Reusable per-market scratch lists (avoid alloc per tick)
        private List<int>[] _buyByGood;
        private List<int>[] _sellByGood;

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

            for (int i = 0; i < _econ.Counties.Length; i++)
            {
                var ce = _econ.Counties[i];
                if (ce == null) continue;

                // Reset per-tick tracking
                ce.SerfFoodProvided = 0f;
                ce.UpperNobleSpend = 0f;
                ce.UpperNobleIncome = 0f;
                ce.LowerNobleSpend = 0f;

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

                // Route coin for all filled orders
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
                                break;
                            case OrderSource.LowerClergy:
                                ce.LowerClergyCoin -= amount;
                                break;
                            case OrderSource.UpperCommoner:
                                ce.UpperCommonerCoin -= amount;
                                break;
                        }

                        // Track serf feeding for satisfaction
                        if (o.Source == OrderSource.SerfFeeding)
                            ce.SerfFoodProvided += o.FilledQuantity;
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
                            case OrderSource.UpperCommoner:
                                ce.UpperCommonerCoin += amount;
                                break;
                        }
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

                // 3. Coin wear on M (upper commoner + lower clergy)
                if (ce.UpperCommonerCoin > 0f)
                    ce.UpperCommonerCoin *= (1f - CoinWearRate);
                if (ce.LowerClergyCoin > 0f)
                    ce.LowerClergyCoin *= (1f - CoinWearRate);
            }
        }

        // ════════════════════════════════════════════════════════════
        //  PHASE 4: UPDATE SATISFACTION
        // ════════════════════════════════════════════════════════════

        void UpdateSatisfaction(SimulationState state, MapData mapData)
        {
            // Build per-county fulfillment from filled orders
            // Iterate all markets' orders to compute fulfillment ratios
            var upperNobleDesired = new float[_econ.Counties.Length];
            var upperNobleFilled = new float[_econ.Counties.Length];
            var lowerNobleDesired = new float[_econ.Counties.Length];
            var lowerNobleFilled = new float[_econ.Counties.Length];

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
                    }
                }
            }

            for (int i = 0; i < _econ.Counties.Length; i++)
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
                    : 0.5f; // no orders posted (no treasury) → neutral
                ce.LowerNobilitySatisfaction = lowerNobleDesired[i] > 0f
                    ? Math.Min(lowerNobleFilled[i] / lowerNobleDesired[i], 1f)
                    : 0.5f;
            }
        }
    }
}
