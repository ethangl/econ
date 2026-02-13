using System;
using System.Collections.Generic;
using EconSim.Core.Common;
using EconSim.Core.Data;
using EconSim.Core.Transport;

namespace EconSim.Core.Economy
{
    /// <summary>
    /// Cardinal direction for off-map market placement.
    /// </summary>
    public enum CardinalDirection
    {
        North,
        South,
        East,
        West
    }

    /// <summary>
    /// Places off-map virtual markets at map edges.
    /// Each market represents trade with distant lands in a cardinal direction.
    /// </summary>
    public static class OffMapMarketPlacer
    {
        // How far away the virtual trading partner is (km)
        private const float DefaultOffMapDistanceKm = 1000f;

        // Price multiplier: base + distance/2000
        private const float BasePriceMultiplier = 1.5f;
        private const float DistancePriceScale = 2000f;

        // Edge cell selection: boundary cells in this fraction of map extent count as "on that edge"
        private const float EdgeFraction = 0.15f;

        // Simplified temperature→biome thresholds (°C at sea level)
        private const float TropicalMinTemp = 20f;
        private const float TemperateMinTemp = 5f;
        private const float BorealMinTemp = -5f;
        // Below BorealMinTemp = polar/tundra

        // Default climate parameters (matching MapGenConfig defaults)
        private const float DefaultEquatorTempC = 29f;
        private const float DefaultNorthPoleTempC = -15f;
        private const float DefaultSouthPoleTempC = -25f;

        /// <summary>
        /// Result from placing off-map markets.
        /// </summary>
        public struct PlacementResult
        {
            public List<Market> Markets;
            public int TotalGoodsOffered;
        }

        /// <summary>
        /// Place off-map markets for directions that offer goods not available on the map.
        /// </summary>
        public static PlacementResult Place(
            MapData mapData,
            EconomyState economy,
            TransportGraph transport,
            int nextMarketId,
            float marketZoneMaxTransportCost)
        {
            var result = new PlacementResult { Markets = new List<Market>() };

            // 1. Scan on-map biomes
            var onMapBiomes = ScanOnMapBiomes(mapData);
            SimLog.Log("OffMap", $"On-map biomes: {string.Join(", ", onMapBiomes)}");

            // 2. Scan which raw goods are producible on-map
            var onMapGoods = ScanOnMapRawGoods(economy);
            SimLog.Log("OffMap", $"On-map raw goods: {string.Join(", ", onMapGoods)}");

            // 3. For each cardinal direction, check if off-map goods are available
            var world = mapData.Info.World;
            int id = nextMarketId;

            foreach (CardinalDirection dir in new[] {
                CardinalDirection.North, CardinalDirection.South,
                CardinalDirection.East, CardinalDirection.West })
            {
                var offMapGoods = DeriveOffMapGoods(dir, mapData, economy, onMapGoods);
                if (offMapGoods.Count == 0)
                {
                    SimLog.Log("OffMap", $"{dir}: no new goods — skipping");
                    continue;
                }

                // Find access cell on that edge
                int accessCellId = FindEdgeAccessCell(dir, mapData);
                if (accessCellId < 0)
                {
                    SimLog.Log("OffMap", $"{dir}: no suitable edge cell — skipping");
                    continue;
                }

                // Compute price multiplier from implied distance
                float distanceKm = DefaultOffMapDistanceKm;
                float priceMultiplier = BasePriceMultiplier + (distanceKm / DistancePriceScale);

                // Create the market
                var market = new Market
                {
                    Id = id++,
                    LocationCellId = accessCellId,
                    Name = $"{dir} Trade Route",
                    Type = MarketType.OffMap,
                    SuitabilityScore = 0,
                    OffMapGoodIds = new HashSet<string>(offMapGoods),
                    OffMapPriceMultiplier = priceMultiplier
                };

                // Initialize goods with inflated base prices for available goods
                foreach (var good in economy.Goods.All)
                {
                    bool isAvailable = offMapGoods.Contains(good.Id);
                    float basePrice = isAvailable
                        ? good.BasePrice * priceMultiplier
                        : good.BasePrice;
                    market.Goods[good.Id] = new MarketGoodState
                    {
                        GoodId = good.Id,
                        BasePrice = basePrice,
                        Price = basePrice,
                        Supply = 0,
                        Demand = 0
                    };
                }

                // Compute zone using Dijkstra from edge cell
                MarketPlacer.ComputeMarketZone(market, mapData, transport, maxTransportCost: marketZoneMaxTransportCost);

                result.Markets.Add(market);
                result.TotalGoodsOffered += offMapGoods.Count;

                var cell = mapData.CellById[accessCellId];
                SimLog.Log("OffMap",
                    $"{dir}: placed '{market.Name}' at cell {accessCellId} " +
                    $"(x={cell.Center.X:F0}, y={cell.Center.Y:F0}), " +
                    $"price×{priceMultiplier:F2}, goods=[{string.Join(", ", offMapGoods)}], " +
                    $"zone={market.ZoneCellIds.Count} cells");
            }

            return result;
        }

        /// <summary>
        /// Scan which biome names are present on the map.
        /// </summary>
        static HashSet<string> ScanOnMapBiomes(MapData mapData)
        {
            var present = new HashSet<int>();
            foreach (var cell in mapData.Cells)
            {
                if (cell.IsLand)
                    present.Add(cell.BiomeId);
            }

            var names = new HashSet<string>();
            foreach (var biome in mapData.Biomes)
            {
                if (present.Contains(biome.Id))
                    names.Add(biome.Name);
            }
            return names;
        }

        /// <summary>
        /// Determine which raw goods are actually available on-map based on assigned county resources.
        /// </summary>
        static HashSet<string> ScanOnMapRawGoods(EconomyState economy)
        {
            var goods = new HashSet<string>();

            // Read actual assigned county resources so this matches economy initialization exactly
            // (including stochastic resources like gold and any terrain alias handling).
            foreach (var county in economy.Counties.Values)
            {
                foreach (var resourceId in county.Resources.Keys)
                {
                    GoodDef good = economy.Goods.Get(resourceId);
                    if (good?.Category == GoodCategory.Raw)
                        goods.Add(resourceId);
                }
            }

            return goods;
        }

        /// <summary>
        /// Derive which goods are available off-map in a given direction but NOT on-map.
        /// Includes full production chains (if raw good available, refined and finished also available).
        /// Mining goods (iron/copper/gold) are always available off-map.
        /// </summary>
        static HashSet<string> DeriveOffMapGoods(
            CardinalDirection direction,
            MapData mapData,
            EconomyState economy,
            HashSet<string> onMapGoods)
        {
            var world = mapData.Info.World;

            // Compute latitude at the off-map location
            float offMapLatitude = ComputeOffMapLatitude(direction, world);

            // Get biomes at that latitude
            var offMapBiomes = DeriveBiomesAtLatitude(offMapLatitude);

            // Match raw goods to off-map biomes
            var availableRaw = new HashSet<string>();

            // Mining goods always available off-map (mountains exist somewhere)
            availableRaw.Add("iron_ore");
            availableRaw.Add("copper_ore");
            availableRaw.Add("gold_ore");

            foreach (var good in economy.Goods.ByCategory(GoodCategory.Raw))
            {
                if (good.TerrainAffinity == null) continue;
                if (availableRaw.Contains(good.Id)) continue; // already added

                foreach (var terrain in good.TerrainAffinity)
                {
                    foreach (var biomeName in offMapBiomes)
                    {
                        if (TerrainAffinityMatcher.MatchesBiome(terrain, biomeName))
                        {
                            availableRaw.Add(good.Id);
                            break;
                        }
                    }
                    if (availableRaw.Contains(good.Id)) break;
                }
            }

            // Expand to full chains: if raw is available, refined and finished are too
            var allAvailable = new HashSet<string>(availableRaw);
            foreach (var good in economy.Goods.All)
            {
                if (good.Inputs == null || good.Inputs.Count == 0) continue;
                // Check if all inputs are available
                bool allInputsAvailable = true;
                foreach (var input in good.Inputs)
                {
                    if (!allAvailable.Contains(input.GoodId))
                    {
                        allInputsAvailable = false;
                        break;
                    }
                }
                if (allInputsAvailable)
                    allAvailable.Add(good.Id);
            }

            // Do a second pass for 3-tier chains (raw→refined→finished)
            foreach (var good in economy.Goods.All)
            {
                if (allAvailable.Contains(good.Id)) continue;
                if (good.Inputs == null || good.Inputs.Count == 0) continue;
                bool allInputsAvailable = true;
                foreach (var input in good.Inputs)
                {
                    if (!allAvailable.Contains(input.GoodId))
                    {
                        allInputsAvailable = false;
                        break;
                    }
                }
                if (allInputsAvailable)
                    allAvailable.Add(good.Id);
            }

            // Filter to only goods NOT producible on-map
            var result = new HashSet<string>();
            foreach (var goodId in allAvailable)
            {
                if (!onMapGoods.Contains(goodId))
                {
                    // For chain goods, check if the raw source is on-map
                    // Only include if the raw resource itself is missing
                    var good = economy.Goods.Get(goodId);
                    if (good == null) continue;

                    if (good.Category == GoodCategory.Raw)
                    {
                        result.Add(goodId);
                    }
                    else
                    {
                        // Refined/finished: include if any input's raw source is not on-map
                        if (HasMissingRawSource(good, economy, onMapGoods))
                            result.Add(goodId);
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Check if a refined/finished good has any input whose ultimate raw source is not on the map.
        /// </summary>
        static bool HasMissingRawSource(GoodDef good, EconomyState economy, HashSet<string> onMapGoods)
        {
            if (good.Inputs == null) return false;
            foreach (var input in good.Inputs)
            {
                var inputGood = economy.Goods.Get(input.GoodId);
                if (inputGood == null) continue;
                if (inputGood.Category == GoodCategory.Raw)
                {
                    if (!onMapGoods.Contains(inputGood.Id))
                        return true;
                }
                else
                {
                    if (HasMissingRawSource(inputGood, economy, onMapGoods))
                        return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Compute the latitude of the off-map trading partner for a given direction.
        /// N/S: offset latitude by ~1000km. E/W: use map's mid-latitude (same climate zone but different resources).
        /// </summary>
        static float ComputeOffMapLatitude(CardinalDirection direction, WorldInfo world)
        {
            float kmPerDegree = 111f;
            float offsetDeg = DefaultOffMapDistanceKm / kmPerDegree; // ~9 degrees

            switch (direction)
            {
                case CardinalDirection.North:
                    return Math.Min(90f, world.LatitudeNorth + offsetDeg);
                case CardinalDirection.South:
                    return Math.Max(-90f, world.LatitudeSouth - offsetDeg);
                case CardinalDirection.East:
                case CardinalDirection.West:
                    // E/W: same latitude band but assume diverse terrain
                    return (world.LatitudeSouth + world.LatitudeNorth) / 2f;
                default:
                    return 0f;
            }
        }

        /// <summary>
        /// Derive plausible biome names at a given latitude using simplified temperature model.
        /// </summary>
        static HashSet<string> DeriveBiomesAtLatitude(float latitude)
        {
            float temp = SeaLevelTemperature(latitude);
            var biomes = new HashSet<string>();

            if (temp >= TropicalMinTemp)
            {
                // Tropical zone
                biomes.Add("Tropical Rainforest");
                biomes.Add("Tropical Dry Forest");
                biomes.Add("Savanna");
                biomes.Add("Grassland"); // tropical grasslands exist
            }
            else if (temp >= TemperateMinTemp)
            {
                // Temperate zone
                biomes.Add("Temperate Forest");
                biomes.Add("Grassland");
                biomes.Add("Woodland");
                biomes.Add("Scrubland");
            }
            else if (temp >= BorealMinTemp)
            {
                // Boreal/subarctic zone
                biomes.Add("Boreal Forest");
                biomes.Add("Tundra");
                biomes.Add("Grassland"); // steppe-like
            }
            else
            {
                // Polar
                biomes.Add("Tundra");
                biomes.Add("Glacier");
            }

            // Highland/mountain biomes always present (mountains exist everywhere)
            biomes.Add("Alpine Barren");
            biomes.Add("Mountain Shrub");

            return biomes;
        }

        /// <summary>
        /// Simplified sea-level temperature from latitude (matches TemperatureModelOps.SeaLevelTemperature).
        /// </summary>
        static float SeaLevelTemperature(float latitude)
        {
            float absLat = Math.Abs(latitude);
            if (absLat <= 15f)
                return DefaultEquatorTempC;

            float t = (absLat - 15f) / (90f - 15f);
            if (t > 1f) t = 1f;
            float cosT = (float)Math.Cos(t * Math.PI / 2f);

            float poleTemp = latitude >= 0f ? DefaultNorthPoleTempC : DefaultSouthPoleTempC;
            return poleTemp + (DefaultEquatorTempC - poleTemp) * cosT;
        }

        /// <summary>
        /// Find the best edge cell for a given cardinal direction.
        /// Prefers coastal land cells with burgs, then any coastal land, then any land cell on that edge.
        /// </summary>
        static int FindEdgeAccessCell(CardinalDirection direction, MapData mapData)
        {
            float minX = float.MaxValue, maxX = float.MinValue;
            float minY = float.MaxValue, maxY = float.MinValue;

            foreach (var cell in mapData.Cells)
            {
                if (!cell.IsLand) continue;
                if (cell.Center.X < minX) minX = cell.Center.X;
                if (cell.Center.X > maxX) maxX = cell.Center.X;
                if (cell.Center.Y < minY) minY = cell.Center.Y;
                if (cell.Center.Y > maxY) maxY = cell.Center.Y;
            }

            float rangeX = maxX - minX;
            float rangeY = maxY - minY;
            if (rangeX <= 0 || rangeY <= 0) return -1;

            // Collect candidate edge cells
            var candidates = new List<Cell>();
            foreach (var cell in mapData.Cells)
            {
                if (!cell.IsLand) continue;
                if (!IsOnEdge(cell, direction, minX, maxX, minY, maxY, rangeX, rangeY))
                    continue;
                candidates.Add(cell);
            }

            if (candidates.Count == 0) return -1;

            // Score candidates: prefer boundary cells, coastal, has burg
            int bestCellId = -1;
            float bestScore = float.MinValue;

            foreach (var cell in candidates)
            {
                float score = 0;
                if (cell.IsBoundary) score += 10;
                if (cell.CoastDistance >= 0 && cell.CoastDistance <= 2) score += 5;
                if (cell.HasBurg) score += 3;
                // Prefer cells further toward the edge
                score += EdgeProximityScore(cell, direction, minX, maxX, minY, maxY);

                if (score > bestScore)
                {
                    bestScore = score;
                    bestCellId = cell.Id;
                }
            }

            return bestCellId;
        }

        static bool IsOnEdge(Cell cell, CardinalDirection dir,
            float minX, float maxX, float minY, float maxY,
            float rangeX, float rangeY)
        {
            float threshold;
            switch (dir)
            {
                case CardinalDirection.North:
                    threshold = maxY - rangeY * EdgeFraction;
                    return cell.Center.Y >= threshold;
                case CardinalDirection.South:
                    threshold = minY + rangeY * EdgeFraction;
                    return cell.Center.Y <= threshold;
                case CardinalDirection.East:
                    threshold = maxX - rangeX * EdgeFraction;
                    return cell.Center.X >= threshold;
                case CardinalDirection.West:
                    threshold = minX + rangeX * EdgeFraction;
                    return cell.Center.X <= threshold;
                default:
                    return false;
            }
        }

        static float EdgeProximityScore(Cell cell, CardinalDirection dir,
            float minX, float maxX, float minY, float maxY)
        {
            switch (dir)
            {
                case CardinalDirection.North: return (cell.Center.Y - minY) / Math.Max(1f, maxY - minY);
                case CardinalDirection.South: return (maxY - cell.Center.Y) / Math.Max(1f, maxY - minY);
                case CardinalDirection.East:  return (cell.Center.X - minX) / Math.Max(1f, maxX - minX);
                case CardinalDirection.West:  return (maxX - cell.Center.X) / Math.Max(1f, maxX - minX);
                default: return 0f;
            }
        }
    }
}
