using System;
using System.Collections.Generic;

namespace EconSim.Core.Economy
{
    /// <summary>
    /// Type of market - affects behavior and accessibility.
    /// </summary>
    public enum MarketType
    {
        /// <summary>Normal market with physical location and zone.</summary>
        Legitimate = 0,
        // Value 1 is reserved for the removed legacy black-market type.
        /// <summary>Off-map virtual market - represents trade with the outside world via edge access point.</summary>
        OffMap = 2
    }

    /// <summary>
    /// A market is a physical trading location in a county.
    /// Nearby counties can buy/sell goods through the market.
    /// </summary>
    [Serializable]
    public class Market
    {
        /// <summary>
        /// Type of market (Legitimate or OffMap).
        /// </summary>
        public MarketType Type { get; set; } = MarketType.Legitimate;
        /// <summary>
        /// Unique market identifier.
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// Cell ID where the market is physically located.
        /// </summary>
        public int LocationCellId { get; set; }

        /// <summary>
        /// Name of the market (often the burg name).
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Set of cell IDs that can access this market.
        /// Computed from transport accessibility.
        /// </summary>
        public HashSet<int> ZoneCellIds { get; set; } = new HashSet<int>();

        /// <summary>
        /// Transport cost from the market to each cell in its zone.
        /// Used to determine which market a cell should belong to when zones overlap.
        /// </summary>
        public Dictionary<int, float> ZoneCellCosts { get; set; } = new Dictionary<int, float>();

        /// <summary>
        /// Current market state for each good: supply, demand, price.
        /// </summary>
        public Dictionary<string, MarketGoodState> Goods { get; set; } = new Dictionary<string, MarketGoodState>();
        [NonSerialized] private readonly Dictionary<int, MarketGoodState> _goodsByRuntimeId = new Dictionary<int, MarketGoodState>();

        /// <summary>
        /// Pending buy orders grouped by good runtime ID and (day,buyer) key.
        /// These are not tradable until promoted at market tick start.
        /// </summary>
        public Dictionary<int, Dictionary<long, BuyOrder>> PendingBuyOrdersByGood { get; } = new Dictionary<int, Dictionary<long, BuyOrder>>();

        /// <summary>
        /// Tradable buy orders grouped by good runtime ID and buyer ID.
        /// Cleared during the market tick and reset after clearing.
        /// </summary>
        public Dictionary<int, Dictionary<int, BuyOrder>> TradableBuyOrdersByGood { get; } = new Dictionary<int, Dictionary<int, BuyOrder>>();

        /// <summary>
        /// Pending consignment lots grouped by good runtime ID and (day,seller) key.
        /// These are not tradable until promoted at market tick start.
        /// </summary>
        public Dictionary<int, Dictionary<long, ConsignmentLot>> PendingInventoryByGood { get; } = new Dictionary<int, Dictionary<long, ConsignmentLot>>();

        /// <summary>
        /// Tradable inventory grouped by good runtime ID and seller ID.
        /// Quantities represent stock available in the current clearing pass.
        /// </summary>
        public Dictionary<int, Dictionary<int, float>> TradableInventoryByGood { get; } = new Dictionary<int, Dictionary<int, float>>();

        /// <summary>
        /// Reservation floor price (Crowns per kg) for tradable inventory by good runtime ID and seller ID.
        /// </summary>
        public Dictionary<int, Dictionary<int, float>> TradableInventoryMinPriceByGood { get; } = new Dictionary<int, Dictionary<int, float>>();

        /// <summary>
        /// Strategic reserve inventory by good runtime ID (kg).
        /// Reserves are persistent buffers and are not directly tradable until released.
        /// </summary>
        public Dictionary<int, float> ReserveByGood { get; } = new Dictionary<int, float>();

        /// <summary>
        /// Number of pending buy orders across all books.
        /// </summary>
        public int PendingBuyOrderCount { get; private set; }

        /// <summary>
        /// Number of inventory lots across all books.
        /// </summary>
        public int InventoryLotCount { get; private set; }

        /// <summary>
        /// For OffMap markets: good IDs that this market supplies from off-map.
        /// Null/empty for non-OffMap markets.
        /// </summary>
        public HashSet<string> OffMapGoodIds { get; set; }

        /// <summary>
        /// For OffMap markets: the price multiplier applied to off-map goods.
        /// </summary>
        public float OffMapPriceMultiplier { get; set; } = 1f;

        [NonSerialized] private GoodRegistry _goodsRegistry;

        /// <summary>
        /// Bind this market to the active good registry so book keys can use dense runtime IDs.
        /// </summary>
        public void BindGoods(GoodRegistry goodsRegistry)
        {
            _goodsRegistry = goodsRegistry;
            RebuildRuntimeGoodIndex();
        }

        /// <summary>
        /// Rebuild runtime-id lookup for market good state.
        /// Call after mutating <see cref="Goods"/>.
        /// </summary>
        public void RebuildRuntimeGoodIndex()
        {
            _goodsByRuntimeId.Clear();
            foreach (var goodState in Goods.Values)
            {
                if (goodState == null)
                    continue;

                int runtimeId = goodState.RuntimeId >= 0
                    ? goodState.RuntimeId
                    : ResolveRuntimeIdForRead(goodState.GoodId);
                if (runtimeId < 0)
                    continue;

                goodState.RuntimeId = runtimeId;
                _goodsByRuntimeId[runtimeId] = goodState;
            }
        }

        /// <summary>
        /// Try get market good state by dense runtime ID.
        /// </summary>
        public bool TryGetGoodState(int goodRuntimeId, out MarketGoodState goodState)
        {
            return _goodsByRuntimeId.TryGetValue(goodRuntimeId, out goodState);
        }

        /// <summary>
        /// Append a pending buy order to this market's order books.
        /// </summary>
        public void AddPendingBuyOrder(BuyOrder order)
        {
            if (order.Quantity <= 0f)
                return;
            int goodRuntimeId = ResolveRuntimeIdForWrite(order.GoodId, order.GoodRuntimeId ?? -1);
            if (goodRuntimeId < 0)
                return;
            order.GoodRuntimeId = goodRuntimeId;

            if (!PendingBuyOrdersByGood.TryGetValue(goodRuntimeId, out var book))
            {
                book = new Dictionary<long, BuyOrder>();
                PendingBuyOrdersByGood[goodRuntimeId] = book;
            }

            long key = ComposeEntryKey(order.DayPosted, order.BuyerId);
            if (book.TryGetValue(key, out var existing))
            {
                float existingQty = existing.Quantity;
                float totalQty = existingQty + order.Quantity;
                if (totalQty > 0f)
                {
                    existing.TransportCost =
                        (existing.TransportCost * existingQty + order.TransportCost * order.Quantity) / totalQty;
                }

                existing.Quantity = totalQty;
                existing.MaxSpend += order.MaxSpend;
                book[key] = existing;
            }
            else
            {
                book[key] = order;
                PendingBuyOrderCount++;
            }
        }

        /// <summary>
        /// Append an inventory lot to this market's lot books.
        /// </summary>
        public void AddInventoryLot(ConsignmentLot lot)
        {
            if (lot.Quantity <= 0f)
                return;
            int goodRuntimeId = ResolveRuntimeIdForWrite(lot.GoodId, lot.GoodRuntimeId ?? -1);
            if (goodRuntimeId < 0)
                return;
            lot.GoodRuntimeId = goodRuntimeId;

            if (!PendingInventoryByGood.TryGetValue(goodRuntimeId, out var book))
            {
                book = new Dictionary<long, ConsignmentLot>();
                PendingInventoryByGood[goodRuntimeId] = book;
            }

            long key = ComposeEntryKey(lot.DayListed, lot.SellerId);
            if (book.TryGetValue(key, out var existing))
            {
                float existingQty = Math.Max(0f, existing.Quantity);
                float incomingQty = Math.Max(0f, lot.Quantity);
                float totalQty = existingQty + incomingQty;
                if (totalQty > 0f)
                {
                    float existingMin = Math.Max(0f, existing.MinUnitPrice);
                    float incomingMin = Math.Max(0f, lot.MinUnitPrice);
                    existing.MinUnitPrice = (existingMin * existingQty + incomingMin * incomingQty) / totalQty;
                }

                existing.Quantity = totalQty;
                book[key] = existing;
            }
            else
            {
                lot.MinUnitPrice = Math.Max(0f, lot.MinUnitPrice);
                book[key] = lot;
                InventoryLotCount++;
            }
        }

        /// <summary>
        /// Add inventory directly to tradable books for current-day clearing.
        /// Used for reserve releases and other immediate injections.
        /// </summary>
        public void AddTradableInventory(int goodRuntimeId, int sellerId, float quantity, float minUnitPrice = 0f)
        {
            if (goodRuntimeId < 0 || sellerId == 0 || quantity <= 0f)
                return;

            if (!TradableInventoryByGood.TryGetValue(goodRuntimeId, out var tradable))
            {
                tradable = new Dictionary<int, float>();
                TradableInventoryByGood[goodRuntimeId] = tradable;
            }

            tradable.TryGetValue(sellerId, out float existingQty);
            float mergedQty = existingQty + quantity;
            tradable[sellerId] = mergedQty;

            if (!TradableInventoryMinPriceByGood.TryGetValue(goodRuntimeId, out var minPrices))
            {
                minPrices = new Dictionary<int, float>();
                TradableInventoryMinPriceByGood[goodRuntimeId] = minPrices;
            }

            float incomingMin = Math.Max(0f, minUnitPrice);
            if (minPrices.TryGetValue(sellerId, out float existingMin) && existingQty > 0f && mergedQty > 0f)
            {
                minPrices[sellerId] = (existingMin * existingQty + incomingMin * quantity) / mergedQty;
            }
            else
            {
                minPrices[sellerId] = incomingMin;
            }
        }

        /// <summary>
        /// Get reserve inventory for a good runtime ID.
        /// </summary>
        public float GetReserve(int goodRuntimeId)
        {
            if (goodRuntimeId < 0)
                return 0f;

            return ReserveByGood.TryGetValue(goodRuntimeId, out float quantity) ? quantity : 0f;
        }

        /// <summary>
        /// Add to reserve inventory for a good runtime ID.
        /// </summary>
        public void AddReserve(int goodRuntimeId, float quantity)
        {
            if (goodRuntimeId < 0 || quantity <= 0f)
                return;

            ReserveByGood[goodRuntimeId] = GetReserve(goodRuntimeId) + quantity;
        }

        /// <summary>
        /// Remove quantity from reserve inventory and return actual removed amount.
        /// </summary>
        public float RemoveReserve(int goodRuntimeId, float quantity)
        {
            if (goodRuntimeId < 0 || quantity <= 0f)
                return 0f;

            float current = GetReserve(goodRuntimeId);
            float removed = Math.Min(current, quantity);
            float remaining = current - removed;
            if (remaining <= 0f)
                ReserveByGood.Remove(goodRuntimeId);
            else
                ReserveByGood[goodRuntimeId] = remaining;

            return removed;
        }

        /// <summary>
        /// Promote pending orders/lots from previous days into tradable books.
        /// </summary>
        public void PromotePendingBooks(int currentDay)
        {
            PromotePendingOrders(currentDay);
            PromotePendingInventory(currentDay);
            RecountBooks();
        }

        /// <summary>
        /// Clear tradable orders after market clearing.
        /// </summary>
        public void ClearTradableOrders()
        {
            TradableBuyOrdersByGood.Clear();
            RecountBooks();
        }

        /// <summary>
        /// Apply decay to inventory for a good in both pending and tradable books.
        /// </summary>
        public void ApplyDecayForGood(string goodId, float decayRate, float lotCullThreshold)
        {
            int goodRuntimeId = ResolveRuntimeIdForRead(goodId);
            if (goodRuntimeId < 0)
                return;

            ApplyDecayForGood(goodRuntimeId, decayRate, lotCullThreshold);
        }

        /// <summary>
        /// Apply decay to inventory for a good runtime ID in both pending and tradable books.
        /// </summary>
        public void ApplyDecayForGood(int goodRuntimeId, float decayRate, float lotCullThreshold)
        {
            if (decayRate <= 0f || goodRuntimeId < 0)
                return;

            float keep = Math.Max(0f, 1f - decayRate);

            if (PendingInventoryByGood.TryGetValue(goodRuntimeId, out var pending))
            {
                var remove = new List<long>();
                var updates = new List<KeyValuePair<long, ConsignmentLot>>();
                foreach (var kvp in pending)
                {
                    var lot = kvp.Value;
                    if (lot.Quantity <= lotCullThreshold)
                    {
                        remove.Add(kvp.Key);
                        continue;
                    }

                    lot.Quantity *= keep;
                    if (lot.Quantity <= lotCullThreshold)
                        remove.Add(kvp.Key);
                    else
                        updates.Add(new KeyValuePair<long, ConsignmentLot>(kvp.Key, lot));
                }

                for (int i = 0; i < updates.Count; i++)
                    pending[updates[i].Key] = updates[i].Value;
                for (int i = 0; i < remove.Count; i++)
                    pending.Remove(remove[i]);
                if (pending.Count == 0)
                    PendingInventoryByGood.Remove(goodRuntimeId);
            }

            if (TradableInventoryByGood.TryGetValue(goodRuntimeId, out var tradable))
            {
                TradableInventoryMinPriceByGood.TryGetValue(goodRuntimeId, out var tradableMinPrices);
                var remove = new List<int>();
                var updates = new List<KeyValuePair<int, float>>();
                foreach (var kvp in tradable)
                {
                    float quantity = kvp.Value * keep;
                    if (quantity <= lotCullThreshold)
                        remove.Add(kvp.Key);
                    else
                        updates.Add(new KeyValuePair<int, float>(kvp.Key, quantity));
                }

                for (int i = 0; i < updates.Count; i++)
                    tradable[updates[i].Key] = updates[i].Value;
                for (int i = 0; i < remove.Count; i++)
                {
                    tradable.Remove(remove[i]);
                    tradableMinPrices?.Remove(remove[i]);
                }
                if (tradable.Count == 0)
                {
                    TradableInventoryByGood.Remove(goodRuntimeId);
                    TradableInventoryMinPriceByGood.Remove(goodRuntimeId);
                }
            }

            if (ReserveByGood.TryGetValue(goodRuntimeId, out float reserveQty))
            {
                float decayed = reserveQty * keep;
                if (decayed <= lotCullThreshold)
                    ReserveByGood.Remove(goodRuntimeId);
                else
                    ReserveByGood[goodRuntimeId] = decayed;
            }

            RecountBooks();
        }

        /// <summary>
        /// Try get tradable orders for a given good.
        /// </summary>
        public bool TryGetTradableOrders(string goodId, out Dictionary<int, BuyOrder> orders)
        {
            int goodRuntimeId = ResolveRuntimeIdForRead(goodId);
            if (goodRuntimeId < 0)
            {
                orders = null;
                return false;
            }

            return TryGetTradableOrders(goodRuntimeId, out orders);
        }

        /// <summary>
        /// Try get tradable orders for a given good runtime ID.
        /// </summary>
        public bool TryGetTradableOrders(int goodRuntimeId, out Dictionary<int, BuyOrder> orders)
        {
            return TradableBuyOrdersByGood.TryGetValue(goodRuntimeId, out orders);
        }

        /// <summary>
        /// Try get tradable inventory for a given good.
        /// </summary>
        public bool TryGetTradableInventory(string goodId, out Dictionary<int, float> inventoryBySeller)
        {
            int goodRuntimeId = ResolveRuntimeIdForRead(goodId);
            if (goodRuntimeId < 0)
            {
                inventoryBySeller = null;
                return false;
            }

            return TryGetTradableInventory(goodRuntimeId, out inventoryBySeller);
        }

        /// <summary>
        /// Try get tradable inventory for a given good runtime ID.
        /// </summary>
        public bool TryGetTradableInventory(int goodRuntimeId, out Dictionary<int, float> inventoryBySeller)
        {
            return TradableInventoryByGood.TryGetValue(goodRuntimeId, out inventoryBySeller);
        }

        /// <summary>
        /// Try get reservation floor prices for tradable inventory by seller.
        /// </summary>
        public bool TryGetTradableInventoryMinPrices(int goodRuntimeId, out Dictionary<int, float> minPriceBySeller)
        {
            return TradableInventoryMinPriceByGood.TryGetValue(goodRuntimeId, out minPriceBySeller);
        }

        /// <summary>
        /// Total inventory (pending + tradable) for a good.
        /// </summary>
        public float GetTotalInventory(string goodId)
        {
            int goodRuntimeId = ResolveRuntimeIdForRead(goodId);
            return goodRuntimeId >= 0 ? GetTotalInventory(goodRuntimeId) : 0f;
        }

        /// <summary>
        /// Total inventory (pending + tradable) for a good runtime ID.
        /// </summary>
        public float GetTotalInventory(int goodRuntimeId)
        {
            float total = 0f;

            if (PendingInventoryByGood.TryGetValue(goodRuntimeId, out var pending))
            {
                foreach (var kvp in pending)
                    total += kvp.Value.Quantity;
            }

            if (TradableInventoryByGood.TryGetValue(goodRuntimeId, out var tradable))
            {
                foreach (var kvp in tradable)
                    total += kvp.Value;
            }

            return total;
        }

        /// <summary>
        /// Tradable supply for a good.
        /// </summary>
        public float GetTradableSupply(string goodId)
        {
            int goodRuntimeId = ResolveRuntimeIdForRead(goodId);
            return goodRuntimeId >= 0 ? GetTradableSupply(goodRuntimeId) : 0f;
        }

        /// <summary>
        /// Tradable supply for a good runtime ID.
        /// </summary>
        public float GetTradableSupply(int goodRuntimeId)
        {
            float total = 0f;
            if (!TradableInventoryByGood.TryGetValue(goodRuntimeId, out var tradable))
                return 0f;

            foreach (var kvp in tradable)
                total += kvp.Value;
            return total;
        }

        /// <summary>
        /// Tradable demand for a good.
        /// </summary>
        public float GetTradableDemand(string goodId)
        {
            int goodRuntimeId = ResolveRuntimeIdForRead(goodId);
            return goodRuntimeId >= 0 ? GetTradableDemand(goodRuntimeId) : 0f;
        }

        /// <summary>
        /// Tradable demand for a good runtime ID.
        /// </summary>
        public float GetTradableDemand(int goodRuntimeId)
        {
            float total = 0f;
            if (!TradableBuyOrdersByGood.TryGetValue(goodRuntimeId, out var tradable))
                return 0f;

            foreach (var kvp in tradable)
                total += kvp.Value.Quantity;
            return total;
        }

        /// <summary>
        /// Remove tiny entries from all books.
        /// </summary>
        public void CullBooks(float lotCullThreshold)
        {
            CullOrderBook(PendingBuyOrdersByGood, lotCullThreshold);
            CullOrderBook(TradableBuyOrdersByGood, lotCullThreshold);
            CullPendingInventory(lotCullThreshold);
            CullTradableInventory(lotCullThreshold);
            RecountBooks();
        }

        private void PromotePendingOrders(int currentDay)
        {
            var emptyGoods = new List<int>();

            foreach (var byGood in PendingBuyOrdersByGood)
            {
                int goodRuntimeId = byGood.Key;
                var pending = byGood.Value;
                var promotedKeys = new List<long>();
                Dictionary<int, BuyOrder> tradable = null;

                foreach (var entry in pending)
                {
                    var order = entry.Value;
                    if (order.DayPosted >= currentDay)
                        continue;

                    if (tradable == null)
                    {
                        if (!TradableBuyOrdersByGood.TryGetValue(goodRuntimeId, out tradable))
                        {
                            tradable = new Dictionary<int, BuyOrder>();
                            TradableBuyOrdersByGood[goodRuntimeId] = tradable;
                        }
                    }

                    MergeTradableOrder(tradable, order);
                    promotedKeys.Add(entry.Key);
                }

                for (int i = 0; i < promotedKeys.Count; i++)
                    pending.Remove(promotedKeys[i]);

                if (pending.Count == 0)
                    emptyGoods.Add(goodRuntimeId);
            }

            for (int i = 0; i < emptyGoods.Count; i++)
                PendingBuyOrdersByGood.Remove(emptyGoods[i]);
        }

        private void PromotePendingInventory(int currentDay)
        {
            var emptyGoods = new List<int>();

            foreach (var byGood in PendingInventoryByGood)
            {
                int goodRuntimeId = byGood.Key;
                var pending = byGood.Value;
                var promotedKeys = new List<long>();
                Dictionary<int, float> tradable = null;
                Dictionary<int, float> tradableMinPrices = null;

                foreach (var entry in pending)
                {
                    var lot = entry.Value;
                    if (lot.DayListed >= currentDay)
                        continue;

                    if (tradable == null)
                    {
                        if (!TradableInventoryByGood.TryGetValue(goodRuntimeId, out tradable))
                        {
                            tradable = new Dictionary<int, float>();
                            TradableInventoryByGood[goodRuntimeId] = tradable;
                        }

                        if (!TradableInventoryMinPriceByGood.TryGetValue(goodRuntimeId, out tradableMinPrices))
                        {
                            tradableMinPrices = new Dictionary<int, float>();
                            TradableInventoryMinPriceByGood[goodRuntimeId] = tradableMinPrices;
                        }
                    }

                    if (tradableMinPrices == null
                        && !TradableInventoryMinPriceByGood.TryGetValue(goodRuntimeId, out tradableMinPrices))
                    {
                        tradableMinPrices = new Dictionary<int, float>();
                        TradableInventoryMinPriceByGood[goodRuntimeId] = tradableMinPrices;
                    }

                    tradable.TryGetValue(lot.SellerId, out float quantity);
                    float incomingQty = Math.Max(0f, lot.Quantity);
                    float mergedQty = quantity + incomingQty;
                    tradable[lot.SellerId] = mergedQty;

                    float incomingMin = Math.Max(0f, lot.MinUnitPrice);
                    if (tradableMinPrices.TryGetValue(lot.SellerId, out float existingMin) && quantity > 0f && mergedQty > 0f)
                    {
                        tradableMinPrices[lot.SellerId] = (existingMin * quantity + incomingMin * incomingQty) / mergedQty;
                    }
                    else
                    {
                        tradableMinPrices[lot.SellerId] = incomingMin;
                    }
                    promotedKeys.Add(entry.Key);
                }

                for (int i = 0; i < promotedKeys.Count; i++)
                    pending.Remove(promotedKeys[i]);

                if (pending.Count == 0)
                    emptyGoods.Add(goodRuntimeId);
            }

            for (int i = 0; i < emptyGoods.Count; i++)
                PendingInventoryByGood.Remove(emptyGoods[i]);
        }

        private static void MergeTradableOrder(Dictionary<int, BuyOrder> tradable, BuyOrder order)
        {
            if (tradable.TryGetValue(order.BuyerId, out var existing))
            {
                float existingQty = existing.Quantity;
                float totalQty = existingQty + order.Quantity;
                if (totalQty > 0f)
                {
                    existing.TransportCost =
                        (existing.TransportCost * existingQty + order.TransportCost * order.Quantity) / totalQty;
                }

                existing.Quantity = totalQty;
                existing.MaxSpend += order.MaxSpend;
                existing.DayPosted = Math.Min(existing.DayPosted, order.DayPosted);
                tradable[order.BuyerId] = existing;
            }
            else
            {
                tradable[order.BuyerId] = order;
            }
        }

        private static void CullOrderBook<T>(Dictionary<int, Dictionary<T, BuyOrder>> byGood, float lotCullThreshold)
        {
            var emptyGoods = new List<int>();
            foreach (var goodEntry in byGood)
            {
                var book = goodEntry.Value;
                var removeKeys = new List<T>();
                foreach (var entry in book)
                {
                    if (entry.Value.Quantity <= lotCullThreshold || entry.Value.MaxSpend <= 0f)
                        removeKeys.Add(entry.Key);
                }

                for (int i = 0; i < removeKeys.Count; i++)
                    book.Remove(removeKeys[i]);

                if (book.Count == 0)
                    emptyGoods.Add(goodEntry.Key);
            }

            for (int i = 0; i < emptyGoods.Count; i++)
                byGood.Remove(emptyGoods[i]);
        }

        private void CullPendingInventory(float lotCullThreshold)
        {
            var emptyGoods = new List<int>();
            foreach (var goodEntry in PendingInventoryByGood)
            {
                var book = goodEntry.Value;
                var removeKeys = new List<long>();
                foreach (var entry in book)
                {
                    if (entry.Value.Quantity <= lotCullThreshold)
                        removeKeys.Add(entry.Key);
                }

                for (int i = 0; i < removeKeys.Count; i++)
                    book.Remove(removeKeys[i]);

                if (book.Count == 0)
                    emptyGoods.Add(goodEntry.Key);
            }

            for (int i = 0; i < emptyGoods.Count; i++)
                PendingInventoryByGood.Remove(emptyGoods[i]);
        }

        private void CullTradableInventory(float lotCullThreshold)
        {
            var emptyGoods = new List<int>();
            foreach (var goodEntry in TradableInventoryByGood)
            {
                var book = goodEntry.Value;
                TradableInventoryMinPriceByGood.TryGetValue(goodEntry.Key, out var minPriceBook);
                var removeKeys = new List<int>();
                foreach (var entry in book)
                {
                    if (entry.Value <= lotCullThreshold)
                        removeKeys.Add(entry.Key);
                }

                for (int i = 0; i < removeKeys.Count; i++)
                {
                    book.Remove(removeKeys[i]);
                    minPriceBook?.Remove(removeKeys[i]);
                }

                if (book.Count == 0)
                    emptyGoods.Add(goodEntry.Key);
            }

            for (int i = 0; i < emptyGoods.Count; i++)
            {
                TradableInventoryByGood.Remove(emptyGoods[i]);
                TradableInventoryMinPriceByGood.Remove(emptyGoods[i]);
            }
        }

        private void RecountBooks()
        {
            int orderCount = 0;
            foreach (var kvp in PendingBuyOrdersByGood)
                orderCount += kvp.Value.Count;
            foreach (var kvp in TradableBuyOrdersByGood)
                orderCount += kvp.Value.Count;
            PendingBuyOrderCount = orderCount;

            int inventoryCount = 0;
            foreach (var kvp in PendingInventoryByGood)
                inventoryCount += kvp.Value.Count;
            foreach (var kvp in TradableInventoryByGood)
                inventoryCount += kvp.Value.Count;
            InventoryLotCount = inventoryCount;
        }

        /// <summary>
        /// Resolve a good ID to a runtime ID for market book keys.
        /// </summary>
        public int ResolveGoodRuntimeId(string goodId)
        {
            return ResolveRuntimeIdForRead(goodId);
        }

        private int ResolveRuntimeIdForRead(string goodId)
        {
            if (string.IsNullOrWhiteSpace(goodId))
                return -1;

            if (_goodsRegistry != null && _goodsRegistry.TryGetRuntimeId(goodId, out int registryRuntimeId))
                return registryRuntimeId;

            return -1;
        }

        private int ResolveRuntimeIdForWrite(string goodId, int hintRuntimeId)
        {
            if (hintRuntimeId >= 0)
                return hintRuntimeId;

            if (!string.IsNullOrWhiteSpace(goodId))
            {
                if (_goodsRegistry != null && _goodsRegistry.TryGetRuntimeId(goodId, out int registryRuntimeId))
                    return registryRuntimeId;
            }

            return -1;
        }

        private static long ComposeEntryKey(int day, int actorId)
        {
            return ((long)day << 32) | (uint)actorId;
        }
    }

    /// <summary>
    /// Market state for a single good type.
    /// </summary>
    [Serializable]
    public class MarketGoodState
    {
        /// <summary>
         /// Good type ID.
         /// </summary>
        public string GoodId { get; set; }

        /// <summary>
        /// Dense runtime ID for this good.
        /// </summary>
        public int RuntimeId { get; set; } = -1;

        /// <summary>
        /// Total quantity available for purchase (remaining after trades).
        /// Accumulated from county surpluses, reduced by purchases.
        /// </summary>
        public float Supply { get; set; }

        /// <summary>
        /// Total quantity offered this tick (before purchases).
        /// Use this for UI display.
        /// </summary>
        public float SupplyOffered { get; set; }

        /// <summary>
        /// Total quantity wanted.
        /// Accumulated from county deficits.
        /// </summary>
        public float Demand { get; set; }

        /// <summary>
        /// Current price per kilogram (Crowns/kg).
        /// Adjusts based on supply/demand ratio.
        /// </summary>
        public float Price { get; set; } = 1.0f;

        /// <summary>
        /// Base price in Crowns/kg (price when supply equals demand).
        /// </summary>
        public float BasePrice { get; set; } = 1.0f;

        /// <summary>
        /// Quantity traded in the last market tick.
        /// </summary>
        public float LastTradeVolume { get; set; }

        /// <summary>
        /// Total Crowns paid to sellers during the last clearing pass.
        /// </summary>
        public float Revenue { get; set; }
    }
}
