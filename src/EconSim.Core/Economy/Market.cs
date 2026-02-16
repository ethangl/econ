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

        /// <summary>
        /// Pending buy orders grouped by good ID and (day,buyer) key.
        /// These are not tradable until promoted at market tick start.
        /// </summary>
        public Dictionary<string, Dictionary<long, BuyOrder>> PendingBuyOrdersByGood { get; } = new Dictionary<string, Dictionary<long, BuyOrder>>();

        /// <summary>
        /// Tradable buy orders grouped by good ID and buyer ID.
        /// Cleared during the market tick and reset after clearing.
        /// </summary>
        public Dictionary<string, Dictionary<int, BuyOrder>> TradableBuyOrdersByGood { get; } = new Dictionary<string, Dictionary<int, BuyOrder>>();

        /// <summary>
        /// Pending consignment lots grouped by good ID and (day,seller) key.
        /// These are not tradable until promoted at market tick start.
        /// </summary>
        public Dictionary<string, Dictionary<long, ConsignmentLot>> PendingInventoryByGood { get; } = new Dictionary<string, Dictionary<long, ConsignmentLot>>();

        /// <summary>
        /// Tradable inventory grouped by good ID and seller ID.
        /// Quantities represent stock available in the current clearing pass.
        /// </summary>
        public Dictionary<string, Dictionary<int, float>> TradableInventoryByGood { get; } = new Dictionary<string, Dictionary<int, float>>();

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

        /// <summary>
        /// Append a pending buy order to this market's order books.
        /// </summary>
        public void AddPendingBuyOrder(BuyOrder order)
        {
            if (order.Quantity <= 0f)
                return;
            if (string.IsNullOrWhiteSpace(order.GoodId))
                return;

            if (!PendingBuyOrdersByGood.TryGetValue(order.GoodId, out var book))
            {
                book = new Dictionary<long, BuyOrder>();
                PendingBuyOrdersByGood[order.GoodId] = book;
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
            if (string.IsNullOrWhiteSpace(lot.GoodId))
                return;

            if (!PendingInventoryByGood.TryGetValue(lot.GoodId, out var book))
            {
                book = new Dictionary<long, ConsignmentLot>();
                PendingInventoryByGood[lot.GoodId] = book;
            }

            long key = ComposeEntryKey(lot.DayListed, lot.SellerId);
            if (book.TryGetValue(key, out var existing))
            {
                existing.Quantity += lot.Quantity;
                book[key] = existing;
            }
            else
            {
                book[key] = lot;
                InventoryLotCount++;
            }
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
            if (decayRate <= 0f || string.IsNullOrWhiteSpace(goodId))
                return;

            float keep = Math.Max(0f, 1f - decayRate);

            if (PendingInventoryByGood.TryGetValue(goodId, out var pending))
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
                    PendingInventoryByGood.Remove(goodId);
            }

            if (TradableInventoryByGood.TryGetValue(goodId, out var tradable))
            {
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
                    tradable.Remove(remove[i]);
                if (tradable.Count == 0)
                    TradableInventoryByGood.Remove(goodId);
            }

            RecountBooks();
        }

        /// <summary>
        /// Try get tradable orders for a given good.
        /// </summary>
        public bool TryGetTradableOrders(string goodId, out Dictionary<int, BuyOrder> orders)
        {
            return TradableBuyOrdersByGood.TryGetValue(goodId, out orders);
        }

        /// <summary>
        /// Try get tradable inventory for a given good.
        /// </summary>
        public bool TryGetTradableInventory(string goodId, out Dictionary<int, float> inventoryBySeller)
        {
            return TradableInventoryByGood.TryGetValue(goodId, out inventoryBySeller);
        }

        /// <summary>
        /// Total inventory (pending + tradable) for a good.
        /// </summary>
        public float GetTotalInventory(string goodId)
        {
            float total = 0f;

            if (PendingInventoryByGood.TryGetValue(goodId, out var pending))
            {
                foreach (var kvp in pending)
                    total += kvp.Value.Quantity;
            }

            if (TradableInventoryByGood.TryGetValue(goodId, out var tradable))
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
            float total = 0f;
            if (!TradableInventoryByGood.TryGetValue(goodId, out var tradable))
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
            float total = 0f;
            if (!TradableBuyOrdersByGood.TryGetValue(goodId, out var tradable))
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
            var emptyGoods = new List<string>();

            foreach (var byGood in PendingBuyOrdersByGood)
            {
                string goodId = byGood.Key;
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
                        if (!TradableBuyOrdersByGood.TryGetValue(goodId, out tradable))
                        {
                            tradable = new Dictionary<int, BuyOrder>();
                            TradableBuyOrdersByGood[goodId] = tradable;
                        }
                    }

                    MergeTradableOrder(tradable, order);
                    promotedKeys.Add(entry.Key);
                }

                for (int i = 0; i < promotedKeys.Count; i++)
                    pending.Remove(promotedKeys[i]);

                if (pending.Count == 0)
                    emptyGoods.Add(goodId);
            }

            for (int i = 0; i < emptyGoods.Count; i++)
                PendingBuyOrdersByGood.Remove(emptyGoods[i]);
        }

        private void PromotePendingInventory(int currentDay)
        {
            var emptyGoods = new List<string>();

            foreach (var byGood in PendingInventoryByGood)
            {
                string goodId = byGood.Key;
                var pending = byGood.Value;
                var promotedKeys = new List<long>();
                Dictionary<int, float> tradable = null;

                foreach (var entry in pending)
                {
                    var lot = entry.Value;
                    if (lot.DayListed >= currentDay)
                        continue;

                    if (tradable == null)
                    {
                        if (!TradableInventoryByGood.TryGetValue(goodId, out tradable))
                        {
                            tradable = new Dictionary<int, float>();
                            TradableInventoryByGood[goodId] = tradable;
                        }
                    }

                    tradable.TryGetValue(lot.SellerId, out float quantity);
                    tradable[lot.SellerId] = quantity + lot.Quantity;
                    promotedKeys.Add(entry.Key);
                }

                for (int i = 0; i < promotedKeys.Count; i++)
                    pending.Remove(promotedKeys[i]);

                if (pending.Count == 0)
                    emptyGoods.Add(goodId);
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

        private static void CullOrderBook<T>(Dictionary<string, Dictionary<T, BuyOrder>> byGood, float lotCullThreshold)
        {
            var emptyGoods = new List<string>();
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
            var emptyGoods = new List<string>();
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
            var emptyGoods = new List<string>();
            foreach (var goodEntry in TradableInventoryByGood)
            {
                var book = goodEntry.Value;
                var removeKeys = new List<int>();
                foreach (var entry in book)
                {
                    if (entry.Value <= lotCullThreshold)
                        removeKeys.Add(entry.Key);
                }

                for (int i = 0; i < removeKeys.Count; i++)
                    book.Remove(removeKeys[i]);

                if (book.Count == 0)
                    emptyGoods.Add(goodEntry.Key);
            }

            for (int i = 0; i < emptyGoods.Count; i++)
                TradableInventoryByGood.Remove(emptyGoods[i]);
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
        /// Current price per unit.
        /// Adjusts based on supply/demand ratio.
        /// </summary>
        public float Price { get; set; } = 1.0f;

        /// <summary>
        /// Base price (price when supply equals demand).
        /// </summary>
        public float BasePrice { get; set; } = 1.0f;

        /// <summary>
        /// Quantity traded in the last market tick.
        /// </summary>
        public float LastTradeVolume { get; set; }

        /// <summary>
        /// Total gold paid to sellers during the last clearing pass.
        /// </summary>
        public float Revenue { get; set; }
    }
}
