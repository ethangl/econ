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
        private const float MinPrice = 0.1f;             // Floor price
        private const float MaxPrice = 10f;              // Ceiling price

        // Thresholds for surplus/deficit (as days of consumption buffer)
        private const float SurplusThreshold = 7f;   // Sell if have >7 days of stock
        private const float DeficitThreshold = 3f;   // Buy if have <3 days of stock

        // Transport cost markup: fraction of goods "lost" to transport costs per unit of transport cost
        // At TransportCostMarkup = 0.01, a county with transport cost 10 loses 10% of purchased goods to fees
        private const float TransportCostMarkup = 0.01f;

        // Cache transport costs from counties to markets (computed once per market)
        private Dictionary<int, Dictionary<int, float>> _transportCosts;

        public void Initialize(SimulationState state, MapData mapData)
        {
            // Precompute transport costs from each county to its market
            _transportCosts = new Dictionary<int, Dictionary<int, float>>();

            foreach (var market in state.Economy.Markets.Values)
            {
                var costsToMarket = new Dictionary<int, float>();
                _transportCosts[market.Id] = costsToMarket;

                // Use FindReachable results which already have costs
                var reachable = state.Transport.FindReachable(market.LocationCellId, 200f);
                foreach (var kvp in reachable)
                {
                    costsToMarket[kvp.Key] = kvp.Value;
                }
            }

            SimLog.Log("Trade", $"Initialized with {state.Economy.Markets.Count} markets, transport costs cached");
        }

        public void Tick(SimulationState state, MapData mapData)
        {
            var economy = state.Economy;

            // Track total trade volume this tick for logging
            var totalSold = new Dictionary<string, float>();
            var totalBought = new Dictionary<string, float>();

            foreach (var market in economy.Markets.Values)
            {
                // Reset market supply/demand tracking for this tick
                foreach (var goodState in market.Goods.Values)
                {
                    goodState.Supply = 0;
                    goodState.SupplyOffered = 0;
                    goodState.Demand = 0;
                }

                // Process each county in the market zone
                foreach (var cellId in market.ZoneCellIds)
                {
                    if (!economy.Counties.TryGetValue(cellId, out var county))
                        continue;

                    ProcessCountyTrade(county, market, economy, totalSold, totalBought);
                }

                // Update prices based on supply/demand
                UpdateMarketPrices(market);
            }

            // Log summary
            if (totalSold.Count > 0 || totalBought.Count > 0)
            {
                LogTradeSummary(state.CurrentDay, totalSold, totalBought);
            }
        }

        private void ProcessCountyTrade(
            CountyEconomy county,
            Market market,
            EconomyState economy,
            Dictionary<string, float> totalSold,
            Dictionary<string, float> totalBought)
        {
            // Get transport efficiency for this county-market pair
            float transportEfficiency = GetTransportEfficiency(county.CellId, market);

            // For each good the county has or needs
            foreach (var good in economy.Goods.All)
            {
                float stockpiled = county.Stockpile.Get(good.Id);

                // Calculate expected consumption (if consumer good)
                float dailyConsumption = 0;
                if (good.NeedCategory.HasValue && good.BaseConsumption > 0)
                {
                    dailyConsumption = county.Population.Total * good.BaseConsumption;
                }

                // Calculate surplus/deficit thresholds
                float surplusStock = dailyConsumption * SurplusThreshold;
                float deficitStock = dailyConsumption * DeficitThreshold;

                if (stockpiled > surplusStock && surplusStock > 0)
                {
                    // SELL: Have more than we need
                    // When selling, transport costs reduce what arrives at market
                    float excess = stockpiled - surplusStock;
                    float toSell = excess * SellRatio;

                    if (toSell > 0.01f)
                    {
                        // Remove from county stockpile
                        county.Stockpile.Remove(good.Id, toSell);

                        // Amount that arrives at market (reduced by transport costs)
                        float arriving = toSell * transportEfficiency;

                        // Add to market supply
                        var marketGood = market.Goods[good.Id];
                        marketGood.Supply += arriving;
                        marketGood.SupplyOffered += arriving;  // Track total offered (for UI)

                        // Track (what was sent, not what arrived)
                        if (!totalSold.ContainsKey(good.Id)) totalSold[good.Id] = 0;
                        totalSold[good.Id] += toSell;
                    }
                }
                else if (stockpiled < deficitStock && dailyConsumption > 0)
                {
                    // BUY: Need more than we have
                    // When buying, transport costs reduce what you receive
                    float needed = deficitStock - stockpiled;
                    float toBuy = needed * BuyRatio;

                    if (toBuy > 0.01f)
                    {
                        // Record demand
                        var marketGood = market.Goods[good.Id];
                        marketGood.Demand += toBuy;

                        // Buy from market supply
                        float actualBuy = Math.Min(toBuy, marketGood.Supply);
                        if (actualBuy > 0)
                        {
                            marketGood.Supply -= actualBuy;

                            // Amount that arrives at county (reduced by transport costs)
                            float arriving = actualBuy * transportEfficiency;
                            county.Stockpile.Add(good.Id, arriving);

                            if (!totalBought.ContainsKey(good.Id)) totalBought[good.Id] = 0;
                            totalBought[good.Id] += actualBuy;
                        }
                    }
                }

                // For non-consumer goods (raw/refined), check if processing facilities need them
                if (!good.NeedCategory.HasValue)
                {
                    float unmetDemand = county.UnmetDemand.TryGetValue(good.Id, out var ud) ? ud : 0;
                    if (unmetDemand > 0)
                    {
                        var marketGood = market.Goods[good.Id];
                        marketGood.Demand += unmetDemand * BuyRatio;

                        float actualBuy = Math.Min(unmetDemand * BuyRatio, marketGood.Supply);
                        if (actualBuy > 0)
                        {
                            marketGood.Supply -= actualBuy;

                            // Amount that arrives (reduced by transport costs)
                            float arriving = actualBuy * transportEfficiency;
                            county.Stockpile.Add(good.Id, arriving);

                            if (!totalBought.ContainsKey(good.Id)) totalBought[good.Id] = 0;
                            totalBought[good.Id] += actualBuy;
                        }
                    }
                }
            }
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

            // Efficiency decreases with transport cost
            // efficiency = 1 / (1 + cost * markup)
            float efficiency = 1f / (1f + cost * TransportCostMarkup);

            // Clamp to reasonable range (at least 50% arrives)
            return Math.Max(0.5f, Math.Min(1f, efficiency));
        }

        private void UpdateMarketPrices(Market market)
        {
            foreach (var goodState in market.Goods.Values)
            {
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
                    goodState.Price = Math.Max(MinPrice, Math.Min(MaxPrice,
                        goodState.Price * (1f + priceChange)));

                    // Record trade volume
                    goodState.LastTradeVolume = Math.Min(supply, demand);
                }
                else
                {
                    // No activity, price slowly returns to base
                    goodState.Price = goodState.Price * 0.99f + goodState.BasePrice * 0.01f;
                }
            }
        }

        private void LogTradeSummary(int day, Dictionary<string, float> sold, Dictionary<string, float> bought)
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

            if (soldSummary.Count > 0 || boughtSummary.Count > 0)
            {
                SimLog.Log("Trade", $"Day {day}: sold=[{string.Join(", ", soldSummary)}], bought=[{string.Join(", ", boughtSummary)}]");
            }
        }
    }
}
