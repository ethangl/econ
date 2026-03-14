using System;
using System.Collections.Generic;
using EconSim.Core.Common;
using EconSim.Core.Data;
using EconSim.Core.Simulation;

namespace EconSim.Core.Economy
{
    /// <summary>
    /// Initializes v4 economy state from MapData. Called once during simulation setup.
    /// </summary>
    public static class EconomyInitializer
    {
        public static EconomyState Initialize(SimulationState state, MapData mapData)
        {
            int maxCountyId = 0;
            foreach (var county in mapData.Counties)
                if (county.Id > maxCountyId) maxCountyId = county.Id;

            var econ = new EconomyState();
            econ.Counties = new CountyEconomy[maxCountyId + 1];

            foreach (var county in mapData.Counties)
            {
                var ce = new CountyEconomy();

                // Split population by estate shares (reuses v3 proportions)
                float pop = county.TotalPopulation;
                ce.LowerCommonerPop = pop * Estates.DefaultShare[(int)Estate.LowerCommoner];
                ce.UpperCommonerPop = pop * Estates.DefaultShare[(int)Estate.UpperCommoner];
                ce.LowerNobilityPop = pop * Estates.DefaultShare[(int)Estate.LowerNobility];
                ce.UpperNobilityPop = pop * Estates.DefaultShare[(int)Estate.UpperNobility];
                ce.LowerClergyPop   = pop * Estates.DefaultShare[(int)Estate.LowerClergy];
                ce.UpperClergyPop   = pop * Estates.DefaultShare[(int)Estate.UpperClergy];

                // Compute biome productivity for v4 goods
                ComputeProductivity(county, mapData, ce.Productivity);

                // Seed upper commoner coin for initial household spending.
                // Facility inputs are unconstrained by coin (artisan credit),
                // but household buy orders need some starting balance.
                // Gold minting → noble spending → facility sales sustains the cycle.
                ce.UpperCommonerCoin = ce.UpperCommonerPop * 0.1f;

                econ.Counties[county.Id] = ce;
            }

            // Track initial total population for growth rate analysis
            float initPop = 0f;
            foreach (var ce2 in econ.Counties)
                if (ce2 != null) initPop += ce2.TotalPopulation;
            econ.InitialTotalPopulation = initPop;

            // Initialize markets — reuses v3's one-per-realm approach
            InitializeMarkets(mapData, econ, maxCountyId, state);

            SimLog.Log("Economy",
                $"Initialized: {mapData.Counties.Count} counties, {econ.MarketCount} markets, " +
                $"{Goods.Count} goods, {Facilities.Count} facilities");

            return econ;
        }

        static void ComputeProductivity(County county, MapData mapData, float[] output)
        {
            int landCells = 0;
            for (int g = 0; g < Goods.Count; g++)
                output[g] = 0f;

            foreach (int cellId in county.CellIds)
            {
                var cell = mapData.CellById[cellId];
                if (!cell.IsLand) continue;
                landCells++;

                for (int g = 0; g < Goods.Count; g++)
                {
                    float y = Goods.BiomeYield[cell.BiomeId, g];
                    output[g] += y;
                }
            }

            if (landCells > 0)
            {
                for (int g = 0; g < Goods.Count; g++)
                    output[g] /= landCells;
            }
        }

        static void InitializeMarkets(MapData mapData, EconomyState econ, int maxCountyId, SimulationState state)
        {
            int maxRealmId = 0;
            foreach (var realm in mapData.Realms)
                if (realm.Id > maxRealmId) maxRealmId = realm.Id;

            // Build realm → capital county/cell mapping
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

            // Fallback: highest-pop county in realm
            foreach (var realm in mapData.Realms)
            {
                if (realmCapitalCounty[realm.Id] >= 0) continue;
                int bestId = -1;
                float bestPop = -1f;
                foreach (var county in mapData.Counties)
                {
                    if (county.RealmId != realm.Id) continue;
                    var ce = econ.Counties[county.Id];
                    if (ce != null && ce.TotalPopulation > bestPop)
                    {
                        bestPop = ce.TotalPopulation;
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

            // Create markets
            int marketCount = mapData.Realms.Count;
            econ.Markets = new MarketState[marketCount + 1];
            econ.CountyToMarket = new int[maxCountyId + 1];
            econ.MarketCount = marketCount;

            var realmIdToMarketId = new int[maxRealmId + 1];
            for (int m = 0; m < mapData.Realms.Count; m++)
            {
                var realm = mapData.Realms[m];
                int marketId = m + 1;
                realmIdToMarketId[realm.Id] = marketId;

                var market = new MarketState(marketId);
                market.HubCountyId = realmCapitalCounty[realm.Id];
                market.HubCellId = realmCapitalCell[realm.Id];
                market.HubRealmId = realm.Id;
                econ.Markets[marketId] = market;
            }

            // Assign counties to markets
            foreach (var county in mapData.Counties)
            {
                int realmId = county.RealmId;
                int marketId = (realmId >= 0 && realmId < realmIdToMarketId.Length)
                    ? realmIdToMarketId[realmId]
                    : 1;
                econ.CountyToMarket[county.Id] = marketId;
                econ.Markets[marketId].CountyIds.Add(county.Id);
            }

            // Hub-to-hub transport cost matrix
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
                        if (cost == float.MaxValue) cost = 1000f;
                        econ.HubToHubCost[m1][m2] = cost;
                        econ.HubToHubCost[m2][m1] = cost;
                    }
                }
            }
        }
    }
}
