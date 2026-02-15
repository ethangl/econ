using System;
using System.Collections.Generic;
using EconSim.Core.Common;
using EconSim.Core.Data;
using EconSim.Core.Economy;

namespace EconSim.Core.Simulation.Systems
{
    /// <summary>
    /// Handles trade between counties and markets.
    /// - Counties with surplus sell to markets
    /// - Counties with deficit buy from markets
    /// - Market prices adjust based on supply/demand
    /// </summary>
    public class TradeSystem : ITickSystem
    {
        public string Name => "Trade";

        // Trade runs weekly per design doc
        public int TickInterval => SimulationConfig.Intervals.Weekly;

        // How much of surplus to sell each trade tick (prevents instant equilibrium)
        private const float SellRatio = 0.5f;

        // How much of deficit to try to buy each trade tick
        private const float BuyRatio = 0.5f;

        // Price adjustment parameters
        private const float PriceAdjustmentRate = 0.1f;  // How fast prices move
        private const float MinPriceMultiplier = 0.1f;   // Floor = 10% of base price
        private const float MaxPriceMultiplier = 10f;    // Ceiling = 10x base price

        // Thresholds for surplus/deficit (as days of consumption buffer)
        private const float SurplusThreshold = 7f;   // Sell if have >7 days of stock
        private const float DeficitThreshold = 3f;   // Buy if have <3 days of stock

        // For non-consumer goods (raw/refined), sell if stockpile exceeds this
        private const float NonConsumerSurplusThreshold = 10f;

        // Transport cost markup: fraction of goods "lost" to transport costs per unit of transport cost
        // At TransportCostMarkup = 0.01, a county with transport cost 10 loses 10% of purchased goods to fees
        private const float TransportCostMarkup = 0.01f;

        // Black market price floor (higher than legitimate markets)
        private const float BlackMarketMinPrice = 0.5f;
        private const float TransportCacheMaxCost = 200f;

        // Cache transport costs from counties to markets (computed once per market)
        private Dictionary<int, Dictionary<int, float>> _transportCosts;

        // Reference to black market for theft tracking
        private Market _blackMarket;

        public void Initialize(SimulationState state, MapData mapData)
        {
            // Cache reference to black market
            _blackMarket = state.Economy.BlackMarket;
            RebuildTransportCaches(state);
            SimLog.Log("Trade", $"Initialized with {state.Economy.Markets.Count} markets (including black market), transport costs cached");
        }

        public void Tick(SimulationState state, MapData mapData)
        {
            var economy = state.Economy;

            // Track total trade volume this tick for logging
            var totalSold = new Dictionary<string, float>();
            var totalBought = new Dictionary<string, float>();
            var totalStolen = new Dictionary<string, float>();

            // Reset all markets (including black market)
            foreach (var market in economy.Markets.Values)
            {
                foreach (var goodState in market.Goods.Values)
                {
                    // For black market, preserve accumulated supply (stolen goods persist)
                    // For off-map markets, preserve supply (replenished by OffMapSupplySystem)
                    if (market.Type == MarketType.Black || market.Type == MarketType.OffMap)
                    {
                        goodState.SupplyOffered = goodState.Supply;
                        goodState.Demand = 0;
                    }
                    else
                    {
                        goodState.Supply = 0;
                        goodState.SupplyOffered = 0;
                        goodState.Demand = 0;
                    }
                }
            }

            // Process legitimate markets
            foreach (var market in economy.Markets.Values)
            {
                // Skip black market in normal processing
                if (market.Type == MarketType.Black)
                    continue;

                // Process each county assigned to this market
                // Counties are assigned via CountyToMarket (computed from cell transport costs)
                foreach (var countyEcon in economy.Counties.Values)
                {
                    // Skip if this county isn't assigned to this market
                    if (!economy.CountyToMarket.TryGetValue(countyEcon.CountyId, out var assignedMarketId)
                        || assignedMarketId != market.Id)
                        continue;

                    ProcessCountyTrade(countyEcon, market, economy, mapData, totalSold, totalBought, totalStolen);
                }

                // Update prices based on supply/demand
                UpdateMarketPrices(market);
            }

            // Update black market prices
            if (_blackMarket != null)
            {
                UpdateMarketPrices(_blackMarket, isBlackMarket: true);
            }

            // Log summary (disabled for cleaner console - enable when debugging trade)
            // if (totalSold.Count > 0 || totalBought.Count > 0 || totalStolen.Count > 0)
            // {
            //     LogTradeSummary(state.CurrentDay, totalSold, totalBought, totalStolen);
            // }
        }

        private void RebuildTransportCaches(SimulationState state)
        {
            _transportCosts = new Dictionary<int, Dictionary<int, float>>();
            state.Transport?.ClearCache();

            foreach (var market in state.Economy.Markets.Values)
            {
                if (market.Type == MarketType.Black)
                    continue;

                var costsToMarket = new Dictionary<int, float>();
                _transportCosts[market.Id] = costsToMarket;

                // Prefer precomputed market-zone costs produced during market placement/bootstrap cache load.
                // This avoids rerunning expensive reachability scans during system initialization.
                if (market.ZoneCellCosts != null && market.ZoneCellCosts.Count > 0)
                {
                    foreach (var kvp in market.ZoneCellCosts)
                    {
                        if (kvp.Value <= TransportCacheMaxCost)
                        {
                            costsToMarket[kvp.Key] = kvp.Value;
                        }
                    }
                    continue;
                }

                var reachable = state.Transport.FindReachable(market.LocationCellId, TransportCacheMaxCost);
                foreach (var kvp in reachable)
                {
                    costsToMarket[kvp.Key] = kvp.Value;
                }
            }
        }

        private void ProcessCountyTrade(
            CountyEconomy county,
            Market market,
            EconomyState economy,
            MapData mapData,
            Dictionary<string, float> totalSold,
            Dictionary<string, float> totalBought,
            Dictionary<string, float> totalStolen)
        {
            // Get the county's seat cell for transport calculations
            int seatCellId = GetCountySeatCell(county.CountyId, mapData);
            if (seatCellId < 0) return;

            // Get transport efficiency for this county-market pair (using seat cell)
            float transportEfficiency = GetTransportEfficiency(seatCellId, market);

            // Sell export buffer contents unconditionally (extraction output earmarked for market)
            foreach (var item in county.ExportBuffer.All)
            {
                float amount = item.Value;
                if (amount < 0.01f) continue;
                var good = economy.Goods.Get(item.Key);
                if (good == null) continue;
                SellToMarket(county, market, good, amount, transportEfficiency, totalSold, totalStolen, fromExportBuffer: true);
            }
            county.ExportBuffer.Clear();

            // For each good the county has or needs
            foreach (var good in economy.Goods.All)
            {
                float stockpiled = county.Stockpile.Get(good.Id);

                if (good.NeedCategory.HasValue)
                {
                    // Consumer goods: sell surplus, buy deficit based on consumption
                    float dailyConsumption = good.BaseConsumption > 0
                        ? county.Population.Total * good.BaseConsumption
                        : 0;

                    float surplusStock = dailyConsumption * SurplusThreshold;
                    float deficitStock = dailyConsumption * DeficitThreshold;

                    if (stockpiled > surplusStock && surplusStock > 0)
                    {
                        // SELL: Have more than we need
                        float excess = stockpiled - surplusStock;
                        SellToMarket(county, market, good, excess, transportEfficiency, totalSold, totalStolen);
                    }
                    else if (stockpiled < deficitStock && dailyConsumption > 0)
                    {
                        // BUY: Need more than we have
                        float needed = deficitStock - stockpiled;
                        float toBuy = needed * BuyRatio;

                        if (toBuy > 0.01f)
                        {
                            var marketGood = market.Goods[good.Id];
                            marketGood.Demand += toBuy;
                            TryBuyFromMarkets(
                                county,
                                market,
                                good,
                                toBuy,
                                transportEfficiency,
                                seatCellId,
                                economy,
                                totalBought);
                        }
                    }
                }
                else
                {
                    // Non-consumer goods (raw/refined): sell excess, buy for facility needs

                    // SELL: If stockpile exceeds threshold, sell the excess
                    if (stockpiled > NonConsumerSurplusThreshold)
                    {
                        float excess = stockpiled - NonConsumerSurplusThreshold;
                        SellToMarket(county, market, good, excess, transportEfficiency, totalSold, totalStolen);
                    }

                    // BUY: Check if processing facilities need this good
                    float unmetDemand = county.UnmetDemand.TryGetValue(good.Id, out var ud) ? ud : 0;
                    if (unmetDemand > 0)
                    {
                        float toBuy = unmetDemand * BuyRatio;
                        var marketGood = market.Goods[good.Id];
                        marketGood.Demand += toBuy;
                        TryBuyFromMarkets(
                            county,
                            market,
                            good,
                            toBuy,
                            transportEfficiency,
                            seatCellId,
                            economy,
                            totalBought);
                    }
                }
            }
        }

        /// <summary>
        /// Get the seat cell ID for a county (used for transport calculations).
        /// </summary>
        private int GetCountySeatCell(int countyId, MapData mapData)
        {
            if (mapData.CountyById != null && mapData.CountyById.TryGetValue(countyId, out var county))
            {
                return county.SeatCellId;
            }
            return -1;
        }

        /// <summary>
        /// Sell goods to market with transport loss and theft.
        /// </summary>
        private void SellToMarket(
            CountyEconomy county,
            Market market,
            GoodDef good,
            float excess,
            float transportEfficiency,
            Dictionary<string, float> totalSold,
            Dictionary<string, float> totalStolen,
            bool fromExportBuffer = false)
        {
            // Export buffer sells everything; normal surplus sells a fraction
            float toSell = fromExportBuffer ? excess : excess * SellRatio;
            if (toSell < 0.01f) return;

            // Export buffer goods are already removed (buffer cleared after loop);
            // normal surplus removes from county stockpile
            if (!fromExportBuffer)
                county.Stockpile.Remove(good.Id, toSell);

            // Amount that arrives at market (reduced by transport costs)
            float arriving = toSell * transportEfficiency;
            float lost = toSell - arriving;

            // Theft: portion of lost goods goes to black market (finished goods only)
            if (_blackMarket != null && lost > 0 && good.TheftRisk > 0 && good.IsFinished)
            {
                float stolen = lost * good.TheftRisk;
                if (stolen > 0.001f)
                {
                    _blackMarket.Goods[good.Id].Supply += stolen;
                    if (!totalStolen.ContainsKey(good.Id)) totalStolen[good.Id] = 0;
                    totalStolen[good.Id] += stolen;
                }
            }

            // Add to market supply
            var marketGood = market.Goods[good.Id];
            marketGood.Supply += arriving;
            marketGood.SupplyOffered += arriving;

            // Track (what was sent, not what arrived)
            if (!totalSold.ContainsKey(good.Id)) totalSold[good.Id] = 0;
            totalSold[good.Id] += toSell;
        }

        /// <summary>
        /// Try to buy goods from local market, black market, and then accessible off-map routes.
        /// Returns amount actually bought.
        /// </summary>
        private float TryBuyFromMarkets(
            CountyEconomy county,
            Market localMarket,
            GoodDef good,
            float toBuy,
            float transportEfficiency,
            int seatCellId,
            EconomyState economy,
            Dictionary<string, float> totalBought)
        {
            float remaining = toBuy;
            float totalBoughtAmount = 0;

            var localGood = localMarket.Goods[good.Id];
            var blackGood = _blackMarket?.Goods[good.Id];

            // Calculate effective prices (local price adjusted for transport loss)
            // If transport efficiency is 0.8, you pay for 1 unit but get 0.8, so effective price is 1.25x
            float localEffectivePrice = localGood.Supply > 0 ? localGood.Price / transportEfficiency : float.MaxValue;
            float blackPrice = blackGood != null && blackGood.Supply > 0 ? blackGood.Price : float.MaxValue;

            while (remaining > 0.01f)
            {
                // Recalculate availability each iteration
                float localAvailable = localGood.Supply;
                float blackAvailable = blackGood?.Supply ?? 0;

                if (localAvailable <= 0 && blackAvailable <= 0)
                    break;

                // Recalculate effective prices
                localEffectivePrice = localAvailable > 0 ? localGood.Price / transportEfficiency : float.MaxValue;
                blackPrice = blackAvailable > 0 ? blackGood.Price : float.MaxValue;

                // Buy from cheaper source
                if (localEffectivePrice <= blackPrice && localAvailable > 0)
                {
                    // Buy from local market (with transport loss)
                    float actualBuy = Math.Min(remaining, localAvailable);
                    localGood.Supply -= actualBuy;

                    // Amount that arrives at county (reduced by transport costs)
                    float arriving = actualBuy * transportEfficiency;
                    county.Stockpile.Add(good.Id, arriving);

                    // Track theft from buying transport losses too (finished goods only)
                    float lost = actualBuy - arriving;
                    if (_blackMarket != null && lost > 0 && good.TheftRisk > 0 && good.IsFinished)
                    {
                        float stolen = lost * good.TheftRisk;
                        if (stolen > 0.001f)
                        {
                            _blackMarket.Goods[good.Id].Supply += stolen;
                        }
                    }

                    totalBoughtAmount += actualBuy;
                    remaining -= actualBuy;
                }
                else if (blackAvailable > 0)
                {
                    // Buy from black market (no transport loss - they deliver)
                    float actualBuy = Math.Min(remaining, blackAvailable);
                    blackGood.Supply -= actualBuy;
                    blackGood.Demand += actualBuy;  // Track demand on black market

                    // Full amount arrives (black market delivers directly)
                    county.Stockpile.Add(good.Id, actualBuy);

                    totalBoughtAmount += actualBuy;
                    remaining -= actualBuy;
                }
                else
                {
                    break;
                }
            }

            if (remaining > 0.01f)
            {
                float boughtOffMap = TryBuyFromOffMapMarkets(
                    county,
                    good,
                    remaining,
                    seatCellId,
                    economy);
                totalBoughtAmount += boughtOffMap;
            }

            if (totalBoughtAmount > 0)
            {
                if (!totalBought.ContainsKey(good.Id)) totalBought[good.Id] = 0;
                totalBought[good.Id] += totalBoughtAmount;
            }

            return totalBoughtAmount;
        }

        private float TryBuyFromOffMapMarkets(
            CountyEconomy county,
            GoodDef good,
            float toBuy,
            int seatCellId,
            EconomyState economy)
        {
            float remaining = toBuy;
            float totalBoughtAmount = 0f;

            while (remaining > 0.01f)
            {
                Market bestMarket = null;
                MarketGoodState bestState = null;
                float bestTransportEfficiency = 0f;
                float bestEffectivePrice = float.MaxValue;

                foreach (var market in economy.Markets.Values)
                {
                    if (market.Type != MarketType.OffMap)
                        continue;
                    if (market.OffMapGoodIds == null || !market.OffMapGoodIds.Contains(good.Id))
                        continue;
                    if (!market.Goods.TryGetValue(good.Id, out var marketState) || marketState.Supply <= 0f)
                        continue;
                    if (!TryGetOffMapTransportEfficiency(seatCellId, market, out float efficiency))
                        continue;

                    float effectivePrice = marketState.Price / efficiency;
                    if (effectivePrice < bestEffectivePrice)
                    {
                        bestEffectivePrice = effectivePrice;
                        bestMarket = market;
                        bestState = marketState;
                        bestTransportEfficiency = efficiency;
                    }
                }

                if (bestMarket == null || bestState == null)
                    break;

                float actualBuy = Math.Min(remaining, bestState.Supply);
                bestState.Supply -= actualBuy;
                bestState.Demand += actualBuy;

                float arriving = actualBuy * bestTransportEfficiency;
                county.Stockpile.Add(good.Id, arriving);

                float lost = actualBuy - arriving;
                if (_blackMarket != null && lost > 0 && good.TheftRisk > 0 && good.IsFinished)
                {
                    float stolen = lost * good.TheftRisk;
                    if (stolen > 0.001f)
                        _blackMarket.Goods[good.Id].Supply += stolen;
                }

                totalBoughtAmount += actualBuy;
                remaining -= actualBuy;
            }

            return totalBoughtAmount;
        }

        /// <summary>
        /// Get transport efficiency (0-1) for goods moving between a county and market.
        /// 1.0 = no loss, 0.5 = half lost to transport costs.
        /// </summary>
        private float GetTransportEfficiency(int cellId, Market market)
        {
            if (!_transportCosts.TryGetValue(market.Id, out var costsToMarket))
                return 1.0f;

            if (!costsToMarket.TryGetValue(cellId, out var cost))
                return 0.5f;  // Not in cache, assume moderate cost

            return ComputeTransportEfficiencyFromCost(cost);
        }

        private bool TryGetOffMapTransportEfficiency(int cellId, Market market, out float transportEfficiency)
        {
            transportEfficiency = 0f;
            if (market?.ZoneCellCosts == null)
                return false;

            if (!market.ZoneCellCosts.TryGetValue(cellId, out float cost))
                return false;

            transportEfficiency = ComputeTransportEfficiencyFromCost(cost);
            return true;
        }

        private static float ComputeTransportEfficiencyFromCost(float cost)
        {
            // Efficiency decreases with transport cost
            // efficiency = 1 / (1 + cost * markup)
            float efficiency = 1f / (1f + cost * TransportCostMarkup);

            // Clamp to reasonable range (at least 50% arrives)
            return Math.Max(0.5f, Math.Min(1f, efficiency));
        }

        private void UpdateMarketPrices(Market market, bool isBlackMarket = false)
        {
            foreach (var goodState in market.Goods.Values)
            {
                // Calculate price bounds relative to this good's base price
                float basePrice = goodState.BasePrice;
                float minPrice = isBlackMarket
                    ? Math.Max(BlackMarketMinPrice, basePrice * MinPriceMultiplier)
                    : basePrice * MinPriceMultiplier;
                float maxPrice = basePrice * MaxPriceMultiplier;

                // Price adjusts based on supply/demand ratio
                // More supply than demand = price drops
                // More demand than supply = price rises

                float supply = goodState.Supply;
                float demand = goodState.Demand;

                if (supply > 0 || demand > 0)
                {
                    // Calculate ratio (add small epsilon to avoid division by zero)
                    float ratio = (demand + 0.1f) / (supply + 0.1f);

                    // Adjust price towards equilibrium
                    // ratio > 1 means more demand, price should rise
                    // ratio < 1 means more supply, price should fall
                    float priceChange = (ratio - 1f) * PriceAdjustmentRate;
                    goodState.Price = Math.Max(minPrice, Math.Min(maxPrice,
                        goodState.Price * (1f + priceChange)));

                    // Record trade volume
                    goodState.LastTradeVolume = Math.Min(supply, demand);
                }
                else
                {
                    // No activity, price slowly returns to base
                    goodState.Price = goodState.Price * 0.99f + basePrice * 0.01f;
                    // Ensure minimum price is respected
                    goodState.Price = Math.Max(minPrice, goodState.Price);
                }
            }
        }

        private void LogTradeSummary(int day, Dictionary<string, float> sold, Dictionary<string, float> bought, Dictionary<string, float> stolen)
        {
            var soldSummary = new List<string>();
            foreach (var kvp in sold)
            {
                soldSummary.Add($"{kvp.Key}:{kvp.Value:F0}");
            }

            var boughtSummary = new List<string>();
            foreach (var kvp in bought)
            {
                boughtSummary.Add($"{kvp.Key}:{kvp.Value:F0}");
            }

            // var stolenSummary = new List<string>();
            // foreach (var kvp in stolen)
            // {
            //     if (kvp.Value >= 0.1f)  // Only log meaningful theft
            //         stolenSummary.Add($"{kvp.Key}:{kvp.Value:F1}");
            // }

            var parts = new List<string>();
            if (soldSummary.Count > 0)
                parts.Add($"sold=[{string.Join(", ", soldSummary)}]");
            if (boughtSummary.Count > 0)
                parts.Add($"bought=[{string.Join(", ", boughtSummary)}]");
            // if (stolenSummary.Count > 0)
            //     parts.Add($"stolen=[{string.Join(", ", stolenSummary)}]");

            if (parts.Count > 0)
            {
                SimLog.Log("Trade", $"Day {day}: {string.Join(", ", parts)}");
            }
        }
    }
}
