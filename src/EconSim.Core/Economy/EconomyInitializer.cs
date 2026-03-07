using System;
using System.Collections.Generic;
using EconSim.Core.Common;
using EconSim.Core.Data;
using EconSim.Core.Simulation;

namespace EconSim.Core.Economy
{
    /// <summary>
    /// Static helper — all economy initialization logic (state, facilities, markets, virtual market).
    /// Called once by the first registered economy system (ProductionSystem).
    /// </summary>
    public static class EconomyInitializer
    {
        public static void Initialize(SimulationState state, MapData mapData)
        {
            int maxCountyId = 0;
            foreach (var county in mapData.Counties)
            {
                if (county.Id > maxCountyId)
                    maxCountyId = county.Id;
            }

            var econ = new EconomyState();
            econ.Counties = new CountyEconomy[maxCountyId + 1];

            var world = mapData.Info.World;
            float mapHeight = mapData.Info.Height;

            foreach (var county in mapData.Counties)
            {
                var ce = new CountyEconomy();
                ce.Population = county.TotalPopulation;
                Estates.ComputeEstatePop(ce.Population, ce.EstatePop);
                ComputeCountyProductivity(county, mapData, ce.Productivity);

                // Cache latitude for seasonal calculations
                float normalizedY = mapHeight > 0 ? county.Centroid.Y / mapHeight : 0.5f;
                ce.Latitude = world.LatitudeSouth + (world.LatitudeNorth - world.LatitudeSouth) * normalizedY;

                // Durable goods start at zero — built up naturally via production

                // Seed treasury so trade + taxation can bootstrap
                ce.Treasury = ce.Population * 1.0f;

                econ.Counties[county.Id] = ce;
            }

            // Compute median productivity per good (static)
            econ.MedianProductivity = new float[Goods.Count];
            for (int g = 0; g < Goods.Count; g++)
            {
                var productivities = new List<float>(mapData.Counties.Count);
                foreach (var county in mapData.Counties)
                    productivities.Add(econ.Counties[county.Id].Productivity[g]);
                productivities.Sort();
                if (productivities.Count > 0)
                {
                    int mid = productivities.Count / 2;
                    econ.MedianProductivity[g] = productivities.Count % 2 == 0
                        ? (productivities[mid - 1] + productivities[mid]) / 2f
                        : productivities[mid];
                }
            }

            // Build county adjacency graph
            econ.CountyAdjacency = BuildCountyAdjacency(mapData, maxCountyId);

            // Facility placement
            var facilities = new List<Facility>();
            var countyFacilityIndices = new List<int>[maxCountyId + 1];
            for (int ci = 0; ci <= maxCountyId; ci++)
                countyFacilityIndices[ci] = new List<int>();

            foreach (var county in mapData.Counties)
            {
                var ce = econ.Counties[county.Id];
                if (ce == null) continue;

                for (int f = 0; f < Facilities.Count; f++)
                {
                    var def = Facilities.Defs[f];
                    int idx = facilities.Count;
                    var facility = new Facility(def.Type, county.Id, county.SeatCellId);
                    facilities.Add(facility);
                    countyFacilityIndices[county.Id].Add(idx);
                }
            }

            econ.Facilities = facilities.ToArray();
            econ.CountyFacilityIndices = countyFacilityIndices;

            // Initialize province/realm economy arrays
            int maxProvId = 0;
            foreach (var prov in mapData.Provinces)
                if (prov.Id > maxProvId) maxProvId = prov.Id;

            econ.Provinces = new ProvinceEconomy[maxProvId + 1];
            foreach (var prov in mapData.Provinces)
                econ.Provinces[prov.Id] = new ProvinceEconomy();

            int maxRealmId = 0;
            foreach (var realm in mapData.Realms)
                if (realm.Id > maxRealmId) maxRealmId = realm.Id;

            econ.Realms = new RealmEconomy[maxRealmId + 1];
            foreach (var realm in mapData.Realms)
                econ.Realms[realm.Id] = new RealmEconomy();

            // Population caches per province and realm
            econ.ProvincePop = new float[maxProvId + 1];
            econ.RealmPop = new float[maxRealmId + 1];
            ComputePopulationCaches(econ, mapData);

            // Demand signal array (populated each tick)
            econ.EffectiveDemandPerPop = new float[Goods.Count];

            // Seed market prices from base prices so crown payments are non-zero on day 1
            Array.Copy(Goods.BasePrice, econ.MarketPrices, Goods.Count);

            // Derive per-county sabbath day from seat cell's religion
            econ.CountySabbathDay = new int[maxCountyId + 1];
            for (int i = 0; i <= maxCountyId; i++)
                econ.CountySabbathDay[i] = 6; // default Sunday
            foreach (var county in mapData.Counties)
            {
                var cell = mapData.CellById[county.SeatCellId];
                if (cell.ReligionId > 0 && mapData.ReligionById != null
                    && mapData.ReligionById.TryGetValue(cell.ReligionId, out var religion))
                {
                    econ.CountySabbathDay[county.Id] = religion.SabbathDay;
                }
            }

            // One market per realm — hub at realm capital burg's county
            InitializeMarkets(mapData, econ, maxCountyId, state);

            // Virtual overseas market for geographically scarce goods
            InitializeVirtualMarket(mapData, econ, maxCountyId, state);

            state.Economy = econ;
        }

        internal static void ComputeCountyProductivity(County county, MapData mapData, float[] output)
        {
            int landCells = 0;
            for (int g = 0; g < Goods.Count; g++)
                output[g] = 0f;

            const int FishIdx = (int)GoodType.Fish;

            foreach (int cellId in county.CellIds)
            {
                var cell = mapData.CellById[cellId];
                if (!cell.IsLand) continue;
                landCells++;
                float cellTemp = cell.Temperature;
                for (int g = 0; g < Goods.Count; g++)
                {
                    float y = Goods.BiomeYield[cell.BiomeId, g];
                    if (y > 0f && Goods.HasTemperatureGate[g]
                        && (cellTemp < Goods.MinTemperature[g] || cellTemp > Goods.MaxTemperature[g]))
                        continue;
                    y *= Goods.RockYieldModifier[cell.RockId, g];
                    output[g] += y;
                }

                // Coast-proximity fishing bonus
                switch (cell.CoastDistance)
                {
                    case 0: output[FishIdx] += 0.45f; break;
                    case 1: output[FishIdx] += 0.22f; break;
                    case 2: output[FishIdx] += 0.08f; break;
                }
            }

            if (landCells > 0)
            {
                for (int g = 0; g < Goods.Count; g++)
                    output[g] /= landCells;
            }
        }

        static void ComputePopulationCaches(EconomyState econ, MapData mapData)
        {
            Array.Clear(econ.ProvincePop, 0, econ.ProvincePop.Length);
            Array.Clear(econ.RealmPop, 0, econ.RealmPop.Length);

            foreach (var county in mapData.Counties)
            {
                var ce = econ.Counties[county.Id];
                if (ce == null) continue;

                int provId = county.ProvinceId;
                if (provId >= 0 && provId < econ.ProvincePop.Length)
                    econ.ProvincePop[provId] += ce.Population;
            }

            foreach (var prov in mapData.Provinces)
            {
                int realmId = prov.RealmId;
                if (realmId >= 0 && realmId < econ.RealmPop.Length)
                    econ.RealmPop[realmId] += econ.ProvincePop[prov.Id];
            }
        }

        static int[][] BuildCountyAdjacency(MapData mapData, int maxCountyId)
        {
            var adj = new int[maxCountyId + 1][];
            var sets = new HashSet<int>[maxCountyId + 1];

            foreach (var county in mapData.Counties)
            {
                sets[county.Id] = new HashSet<int>();
            }

            foreach (var cell in mapData.Cells)
            {
                int cid = cell.CountyId;
                if (cid <= 0 || sets[cid] == null) continue;

                foreach (int nid in cell.NeighborIds)
                {
                    var neighbor = mapData.CellById[nid];
                    int ncid = neighbor.CountyId;
                    if (ncid > 0 && ncid != cid && sets[ncid] != null)
                        sets[cid].Add(ncid);
                }
            }

            for (int i = 0; i <= maxCountyId; i++)
            {
                if (sets[i] != null)
                {
                    adj[i] = new int[sets[i].Count];
                    sets[i].CopyTo(adj[i]);
                }
            }

            return adj;
        }

        static void InitializeMarkets(MapData mapData, EconomyState econ, int maxCountyId, SimulationState state)
        {
            // Build realm → capital county/cell mapping
            int maxRealmId = 0;
            foreach (var realm in mapData.Realms)
                if (realm.Id > maxRealmId) maxRealmId = realm.Id;

            var realmCapitalCounty = new int[maxRealmId + 1];
            var realmCapitalCell = new int[maxRealmId + 1];
            for (int i = 0; i <= maxRealmId; i++)
            {
                realmCapitalCounty[i] = -1;
                realmCapitalCell[i] = -1;
            }

            if (mapData.Burgs != null)
            {
                foreach (var realm in mapData.Realms)
                {
                    Burg burg = null;
                    foreach (var b in mapData.Burgs)
                        if (b.Id == realm.CapitalBurgId) { burg = b; break; }

                    if (burg != null && mapData.CellById.TryGetValue(burg.CellId, out var cell)
                        && cell.CountyId > 0 && cell.CountyId < econ.Counties.Length
                        && econ.Counties[cell.CountyId] != null)
                    {
                        realmCapitalCounty[realm.Id] = cell.CountyId;
                        realmCapitalCell[realm.Id] = burg.CellId;
                    }
                }
            }

            // Fallback: if a realm has no valid capital, pick highest-pop county in realm
            foreach (var realm in mapData.Realms)
            {
                if (realmCapitalCounty[realm.Id] >= 0) continue;
                int bestId = -1;
                float bestPop = -1f;
                foreach (var county in mapData.Counties)
                {
                    if (county.RealmId != realm.Id) continue;
                    var ce = econ.Counties[county.Id];
                    if (ce != null && ce.Population > bestPop)
                    {
                        bestPop = ce.Population;
                        bestId = county.Id;
                    }
                }
                if (bestId >= 0)
                {
                    realmCapitalCounty[realm.Id] = bestId;
                    foreach (var county in mapData.Counties)
                    {
                        if (county.Id == bestId)
                        {
                            realmCapitalCell[realm.Id] = county.SeatCellId;
                            break;
                        }
                    }
                }
            }

            // Create one MarketInfo per realm. Market IDs are 1-based, assigned in realm order.
            int marketCount = mapData.Realms.Count;
            econ.Markets = new MarketInfo[marketCount + 1]; // slot 0 unused
            var realmIdToMarketId = new int[maxRealmId + 1];

            for (int m = 0; m < mapData.Realms.Count; m++)
            {
                var realm = mapData.Realms[m];
                int marketId = m + 1;
                realmIdToMarketId[realm.Id] = marketId;
                econ.Markets[marketId] = new MarketInfo
                {
                    Id = marketId,
                    HubCountyId = realmCapitalCounty[realm.Id],
                    HubCellId = realmCapitalCell[realm.Id],
                    HubRealmId = realm.Id
                };
            }

            // CountyToMarket: each county maps to its own realm's market
            econ.CountyToMarket = new int[maxCountyId + 1];
            foreach (var county in mapData.Counties)
            {
                int realmId = county.RealmId;
                if (realmId >= 0 && realmId < realmIdToMarketId.Length)
                    econ.CountyToMarket[county.Id] = realmIdToMarketId[realmId];
                else
                    econ.CountyToMarket[county.Id] = 1; // fallback to first market
            }

            // Allocate and seed PerMarketPrices
            econ.PerMarketPrices = new float[marketCount + 1][];
            for (int m = 1; m <= marketCount; m++)
            {
                econ.PerMarketPrices[m] = new float[Goods.Count];
                Array.Copy(Goods.BasePrice, econ.PerMarketPrices[m], Goods.Count);
            }

            // Allocate empty MarketEmbargoes
            econ.MarketEmbargoes = new System.Collections.Generic.HashSet<int>[marketCount + 1];
            for (int m = 1; m <= marketCount; m++)
                econ.MarketEmbargoes[m] = new System.Collections.Generic.HashSet<int>();

            // Compute hub-to-hub transport cost matrix
            econ.HubToHubCost = new float[marketCount + 1][];
            for (int m = 1; m <= marketCount; m++)
                econ.HubToHubCost[m] = new float[marketCount + 1];

            if (state.Transport != null)
            {
                for (int m1 = 1; m1 <= marketCount; m1++)
                {
                    int cell1 = econ.Markets[m1].HubCellId;
                    if (cell1 < 0) continue;
                    for (int m2 = m1 + 1; m2 <= marketCount; m2++)
                    {
                        int cell2 = econ.Markets[m2].HubCellId;
                        if (cell2 < 0) continue;
                        float cost = state.Transport.GetTransportCost(cell1, cell2);
                        if (cost == float.MaxValue) cost = 1000f; // no path — large penalty
                        econ.HubToHubCost[m1][m2] = cost;
                        econ.HubToHubCost[m2][m1] = cost;
                    }
                }
            }
        }

        static void InitializeVirtualMarket(MapData mapData, EconomyState econ, int maxCountyId, SimulationState state)
        {
            var vm = new VirtualMarketState(Goods.Count, maxCountyId);

            // Globe-derived trade context (null when generating without globe)
            var trade = mapData.Info?.Trade;
            float volumeScale = trade?.TradeVolumeScale ?? 1f;
            float distancePriceFactor = trade != null ? 1f + 0.02f * trade.NearestContinentHops : 1f;

            // Configure traded goods: salt and spices
            int saltIdx = (int)GoodType.Salt;
            int spicesIdx = (int)GoodType.Spices;
            vm.TradedGoods.Add(saltIdx);
            vm.TradedGoods.Add(spicesIdx);

            // Salt: abundant foreign supply
            vm.TargetStock[saltIdx] = 5000f * volumeScale;
            vm.ReplenishRate[saltIdx] = 50f * volumeScale;
            vm.MaxStock[saltIdx] = 10000f * volumeScale;
            vm.Stock[saltIdx] = vm.TargetStock[saltIdx];
            vm.SellPrice[saltIdx] = Goods.BasePrice[saltIdx] * distancePriceFactor;
            vm.BuyPrice[saltIdx] = Goods.BasePrice[saltIdx] * 0.75f;

            // Spices: scarce luxury import
            vm.TargetStock[spicesIdx] = 10000f * volumeScale;
            vm.ReplenishRate[spicesIdx] = 500f * volumeScale;
            vm.MaxStock[spicesIdx] = 25000f * volumeScale;
            vm.Stock[spicesIdx] = vm.TargetStock[spicesIdx];
            vm.SellPrice[spicesIdx] = Goods.BasePrice[spicesIdx] * distancePriceFactor;
            vm.BuyPrice[spicesIdx] = Goods.BasePrice[spicesIdx] * 0.75f;

            // Fur: demand-only (foreign consumption absorbs domestic surplus)
            int furIdx = (int)GoodType.Fur;
            vm.TradedGoods.Add(furIdx);
            vm.TargetStock[furIdx] = 5000f * volumeScale;
            vm.ReplenishRate[furIdx] = -500f * volumeScale;   // foreign consumption drain
            vm.MaxStock[furIdx] = 10000f * volumeScale;
            vm.Stock[furIdx] = 0f;              // starts empty — demand only
            vm.SellPrice[furIdx] = Goods.BasePrice[furIdx];
            vm.BuyPrice[furIdx] = Goods.BasePrice[furIdx] * 0.75f / distancePriceFactor;

            // Silk: scarce luxury import (Silk Road)
            int silkIdx = (int)GoodType.Silk;
            vm.TradedGoods.Add(silkIdx);
            vm.TargetStock[silkIdx] = 8000f * volumeScale;
            vm.ReplenishRate[silkIdx] = 300f * volumeScale;
            vm.MaxStock[silkIdx] = 20000f * volumeScale;
            vm.Stock[silkIdx] = vm.TargetStock[silkIdx];
            vm.SellPrice[silkIdx] = Goods.BasePrice[silkIdx] * distancePriceFactor;
            vm.BuyPrice[silkIdx] = Goods.BasePrice[silkIdx] * 0.75f;

            vm.OverseasSurcharge = trade?.OverseasSurcharge ?? 0.02f;

            // Precompute per-county port cost via Dijkstra to nearest coast cell
            const float CostNormFactor = 0.00003f;
            const int CandidateCount = 5;

            var coastCells = new List<Cell>();
            foreach (var cell in mapData.Cells)
            {
                if (cell.IsLand && cell.CoastDistance == 1)
                    coastCells.Add(cell);
            }

            if (coastCells.Count > 0 && state.Transport != null)
            {
                int reachable = 0;
                int unreachable = 0;

                foreach (var county in mapData.Counties)
                {
                    int seatCellId = county.SeatCellId;
                    if (!mapData.CellById.TryGetValue(seatCellId, out var seatCell))
                    {
                        unreachable++;
                        continue;
                    }

                    // Find nearest ~N coast cells by Euclidean distance
                    float bestCost = float.MaxValue;

                    var candidates = new (int cellId, float eucDist)[Math.Min(CandidateCount, coastCells.Count)];
                    for (int i = 0; i < candidates.Length; i++)
                        candidates[i] = (-1, float.MaxValue);

                    for (int i = 0; i < coastCells.Count; i++)
                    {
                        float dist = Vec2.Distance(seatCell.Center, coastCells[i].Center);
                        int worstIdx = 0;
                        for (int j = 1; j < candidates.Length; j++)
                        {
                            if (candidates[j].eucDist > candidates[worstIdx].eucDist)
                                worstIdx = j;
                        }
                        if (dist < candidates[worstIdx].eucDist)
                            candidates[worstIdx] = (coastCells[i].Id, dist);
                    }

                    // Dijkstra to each candidate, take minimum
                    for (int i = 0; i < candidates.Length; i++)
                    {
                        if (candidates[i].cellId < 0) continue;
                        float cost = state.Transport.GetTransportCost(seatCellId, candidates[i].cellId);
                        if (cost < bestCost)
                            bestCost = cost;
                    }

                    if (bestCost < float.MaxValue)
                    {
                        vm.CountyPortCost[county.Id] = bestCost * CostNormFactor + vm.OverseasSurcharge;
                        reachable++;
                    }
                    else
                    {
                        unreachable++;
                    }
                }

                SimLog.Log("trade", $"Virtual market init: {coastCells.Count} coast cells, " +
                    $"{reachable} counties reachable, {unreachable} landlocked/unreachable");
            }
            else
            {
                SimLog.Log("trade", "Virtual market init: no coast cells or transport graph — VM disabled");
            }

            econ.VirtualMarket = vm;
        }
    }
}
