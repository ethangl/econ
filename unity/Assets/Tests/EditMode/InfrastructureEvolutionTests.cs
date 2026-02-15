using System;
using System.Collections.Generic;
using System.Reflection;
using EconSim.Core.Common;
using EconSim.Core.Data;
using EconSim.Core.Economy;
using EconSim.Core.Simulation;
using EconSim.Core.Simulation.Systems;
using EconSim.Core.Transport;
using EconSim.Renderer;
using NUnit.Framework;

namespace EconSim.Tests
{
    public class InfrastructureEvolutionTests
    {
        [SetUp]
        public void SetUp()
        {
            SimLog.LogAction = _ => { };
        }

        [Test]
        public void StaticBackbone_BuildsSharedPathTiers_ForMajorCounties()
        {
            var mapData = BuildLinearMap();
            var economy = new EconomyState();
            var transport = new TransportGraph(mapData);
            transport.SetRoadState(economy.Roads);

            var state = new SimulationState
            {
                Economy = economy,
                Transport = transport
            };

            var stats = StaticTransportBackboneBuilder.Build(state, mapData);

            Assert.That(stats.MajorCountyCount, Is.GreaterThanOrEqualTo(2));
            Assert.That(stats.RoutePairCount, Is.GreaterThan(0));
            Assert.That(stats.RoutedPairCount, Is.GreaterThan(0));
            Assert.That(stats.EdgeCount, Is.GreaterThan(0));
            Assert.That(economy.Roads.GetRoadTier(1, 2), Is.Not.EqualTo(RoadTier.None));
            Assert.That(economy.Roads.GetRoadTier(2, 3), Is.Not.EqualTo(RoadTier.None));
        }

        [Test]
        public void TransportGraph_MountainPenalty_UsesWorldUnitCalibration()
        {
            var mapData = new MapData
            {
                Info = new MapInfo
                {
                    World = CreateWorldInfo(
                        cellSizeKm: 2.5f,
                        mapWidthKm: 10f,
                        mapHeightKm: 10f,
                        seaLevel: 0f,
                        maxElevationMeters: 8000f,
                        maxSeaDepthMeters: 2000f)
                },
                Cells = new List<Cell>
                {
                    new Cell
                    {
                        Id = 1,
                        IsLand = true,
                        SeaRelativeElevation = Elevation.HumanAltitudeEffectStartMeters,
                        HasSeaRelativeElevation = true,
                        BiomeId = 1,
                        MovementCost = 42f,
                        NeighborIds = new List<int>(),
                        Center = new Vec2(0, 0)
                    },
                    new Cell
                    {
                        Id = 2,
                        IsLand = true,
                        SeaRelativeElevation = Elevation.HumanAltitudeImpassableMeters + 100f,
                        HasSeaRelativeElevation = true,
                        BiomeId = 1,
                        MovementCost = 100f,
                        NeighborIds = new List<int>(),
                        Center = new Vec2(1, 0)
                    }
                },
                Biomes = new List<Biome>
                {
                    new Biome { Id = 1, Name = "Plains" }
                },
                Counties = new List<County>(),
                Provinces = new List<Province>(),
                Realms = new List<Realm>(),
                Rivers = new List<River>(),
                Burgs = new List<Burg>(),
                Features = new List<Feature>(),
                Vertices = new List<Vec2>()
            };
            mapData.BuildLookups();

            var transport = new TransportGraph(mapData);

            float foothillCost = transport.GetCellMovementCost(mapData.CellById[1]);
            float peakCost = transport.GetCellMovementCost(mapData.CellById[2]);

            Assert.That(foothillCost, Is.EqualTo(42f).Within(0.0001f), "Cell with moderate movement cost should return per-cell value.");
            Assert.That(peakCost, Is.EqualTo(100f).Within(0.0001f), "Cell at impassable threshold should be blocked.");
        }

        [Test]
        public void EconomyInitializer_IronOreThreshold_RemainsStableWithWorldMetadata()
        {
            var mapData = new MapData
            {
                Info = new MapInfo
                {
                    Seed = "42",
                    World = CreateWorldInfo(
                        cellSizeKm: 2.5f,
                        mapWidthKm: 10f,
                        mapHeightKm: 10f,
                        seaLevel: 0f,
                        maxElevationMeters: 8000f,
                        maxSeaDepthMeters: 2000f)
                },
                Cells = new List<Cell>
                {
                    new Cell
                    {
                        Id = 1,
                        IsLand = true,
                        SeaRelativeElevation = 2000f,
                        HasSeaRelativeElevation = true,
                        BiomeId = 1,
                        MovementCost = 74f,
                        CountyId = 10,
                        NeighborIds = new List<int> { 2 },
                        Center = new Vec2(0, 0)
                    },
                    new Cell
                    {
                        Id = 2,
                        IsLand = true,
                        SeaRelativeElevation = 6000f,
                        HasSeaRelativeElevation = true,
                        BiomeId = 1,
                        MovementCost = 92f,
                        CountyId = 20,
                        NeighborIds = new List<int> { 1 },
                        Center = new Vec2(1, 0)
                    }
                },
                Biomes = new List<Biome>
                {
                    new Biome { Id = 1, Name = "Mountain" }
                },
                Counties = new List<County>
                {
                    new County { Id = 10, SeatCellId = 1, CellIds = new List<int> { 1 }, TotalPopulation = 5000, Centroid = new Vec2(0, 0) },
                    new County { Id = 20, SeatCellId = 2, CellIds = new List<int> { 2 }, TotalPopulation = 5000, Centroid = new Vec2(1, 0) }
                },
                Provinces = new List<Province>(),
                Realms = new List<Realm>(),
                Rivers = new List<River>(),
                Burgs = new List<Burg>(),
                Features = new List<Feature>(),
                Vertices = new List<Vec2>()
            };
            mapData.BuildLookups();

            var economy = EconomyInitializer.Initialize(mapData);
            var lowCounty = economy.GetCounty(10);
            var highCounty = economy.GetCounty(20);

            Assert.That(lowCounty.Resources.ContainsKey("iron_ore"), Is.False, "Low-elevation county should not receive iron ore.");
            Assert.That(highCounty.Resources.ContainsKey("iron_ore"), Is.True, "High-elevation county should receive iron ore.");
        }

        [Test]
        public void TransportGraph_DistanceNormalization_ScalesWithWorldCellSize()
        {
            var baselineMap = BuildTwoCellTransportMap(distanceKm: 30f, cellSizeKm: 2.5f);
            var worldScaleMap = BuildTwoCellTransportMap(distanceKm: 60f, cellSizeKm: 5f);

            var baselineGraph = new TransportGraph(baselineMap);
            var worldScaleGraph = new TransportGraph(worldScaleMap);

            float baselineCost = baselineGraph.GetEdgeCost(baselineMap.CellById[1], baselineMap.CellById[2]);
            float worldScaleCost = worldScaleGraph.GetEdgeCost(worldScaleMap.CellById[1], worldScaleMap.CellById[2]);

            Assert.That(baselineCost, Is.EqualTo(42f).Within(0.0001f), "Normalization should keep 30km @ 2.5km cells near 1x distance factor.");
            Assert.That(worldScaleCost, Is.EqualTo(baselineCost).Within(0.0001f),
                "Distance normalization should preserve equivalent edge cost when world scale doubles.");
        }

        [Test]
        public void MarketPlacer_ZoneBudget_RequiresWorldMetadata()
        {
            var mapData = new MapData
            {
                Info = new MapInfo
                {
                }
            };
            Assert.Throws<InvalidOperationException>(() => MarketPlacer.ResolveMarketZoneMaxTransportCost(mapData));
        }

        [Test]
        public void MarketPlacer_ZoneBudget_ScalesWithLargerWorldAtSameCellSize()
        {
            var mapData = BuildBudgetMap(mapWidthKm: 666.6667f, mapHeightKm: 375f, cellSizeKm: 2.5f);
            float budget = MarketPlacer.ResolveMarketZoneMaxTransportCost(mapData);
            Assert.That(budget, Is.EqualTo(200f).Within(0.5f),
                "Doubling map dimensions at same cell size should roughly double market zone budget.");
        }

        [Test]
        public void MarketPlacer_ZoneBudget_RemainsStableWhenWorldAndCellSizeScaleTogether()
        {
            var mapData = BuildBudgetMap(mapWidthKm: 666.6667f, mapHeightKm: 375f, cellSizeKm: 5f);
            float budget = MarketPlacer.ResolveMarketZoneMaxTransportCost(mapData);
            Assert.That(budget, Is.EqualTo(100f).Within(0.5f),
                "If world dimensions and cell size scale together, normalized budget should remain stable.");
        }

        [Test]
        public void WorldScale_DistanceNormalization_RequiresWorldMetadata()
        {
            Assert.Throws<InvalidOperationException>(() => WorldScale.ResolveDistanceNormalizationKm(null));

            var info = new MapInfo
            {
                World = CreateWorldInfo(cellSizeKm: 5f, mapWidthKm: 100f, mapHeightKm: 50f)
            };

            Assert.That(WorldScale.ResolveDistanceNormalizationKm(info), Is.EqualTo(60f).Within(0.0001f));
        }

        [Test]
        public void SimulationBootstrapCache_LatitudeMatch_RejectsFiniteVsNonFinite()
        {
            MethodInfo method = typeof(SimulationRunner).GetMethod(
                "CacheLatitudeMatches",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null, "Could not find SimulationRunner.CacheLatitudeMatches via reflection.");

            Assert.That((bool)method.Invoke(null, new object[] { float.NaN, 10f }), Is.False);
            Assert.That((bool)method.Invoke(null, new object[] { 10f, float.PositiveInfinity }), Is.False);
            Assert.That((bool)method.Invoke(null, new object[] { float.NaN, float.NaN }), Is.True);
            Assert.That((bool)method.Invoke(null, new object[] { 10f, 10.00005f }), Is.True);
        }

        [Test]
        public void OverlayTextureCache_LatitudeMatch_RejectsFiniteVsNonFinite()
        {
            MethodInfo method = typeof(MapOverlayManager).GetMethod(
                "CacheFloatMatches",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null, "Could not find MapOverlayManager.CacheFloatMatches via reflection.");

            Assert.That((bool)method.Invoke(null, new object[] { float.NegativeInfinity, -20f }), Is.False);
            Assert.That((bool)method.Invoke(null, new object[] { -20f, float.NaN }), Is.False);
            Assert.That((bool)method.Invoke(null, new object[] { float.NaN, float.NaN }), Is.True);
            Assert.That((bool)method.Invoke(null, new object[] { -20f, -20.00005f }), Is.True);
        }

        [Test]
        public void RebuildCellToMarketLookup_IgnoresOffMapMarketsDuringCountyAssignment()
        {
            var mapData = new MapData
            {
                Info = new MapInfo
                {
                    World = CreateWorldInfo(cellSizeKm: 2.5f, mapWidthKm: 10f, mapHeightKm: 10f)
                },
                Cells = new List<Cell>
                {
                    new Cell { Id = 1, IsLand = true, BiomeId = 1, MovementCost = 42f, SeaRelativeElevation = 15f, HasSeaRelativeElevation = true, NeighborIds = new List<int> { 2 }, CountyId = 10, Center = new Vec2(0, 0) },
                    new Cell { Id = 2, IsLand = true, BiomeId = 1, MovementCost = 42f, SeaRelativeElevation = 15f, HasSeaRelativeElevation = true, NeighborIds = new List<int> { 1 }, CountyId = 20, Center = new Vec2(1, 0) }
                },
                Biomes = new List<Biome>
                {
                    new Biome { Id = 1, Name = "Plains" }
                },
                Counties = new List<County>
                {
                    new County { Id = 10, SeatCellId = 1, CellIds = new List<int> { 1 }, TotalPopulation = 4000, Centroid = new Vec2(0, 0) },
                    new County { Id = 20, SeatCellId = 2, CellIds = new List<int> { 2 }, TotalPopulation = 4000, Centroid = new Vec2(1, 0) }
                },
                Provinces = new List<Province>(),
                Realms = new List<Realm>(),
                Rivers = new List<River>(),
                Burgs = new List<Burg>(),
                Features = new List<Feature>(),
                Vertices = new List<Vec2>()
            };
            mapData.BuildLookups();

            var economy = new EconomyState();
            economy.InitializeFromMap(mapData);

            var localMarket = new Market
            {
                Id = 1,
                Type = MarketType.Legitimate,
                Name = "Local",
                LocationCellId = 1
            };
            localMarket.ZoneCellIds.Add(1);
            localMarket.ZoneCellIds.Add(2);
            localMarket.ZoneCellCosts[1] = 10f;
            localMarket.ZoneCellCosts[2] = 10f;

            var offMap = new Market
            {
                Id = 2,
                Type = MarketType.OffMap,
                Name = "OffMap",
                LocationCellId = 2,
                OffMapGoodIds = new HashSet<string> { "iron_ore" }
            };
            offMap.ZoneCellIds.Add(1);
            offMap.ZoneCellIds.Add(2);
            offMap.ZoneCellCosts[1] = 1f;
            offMap.ZoneCellCosts[2] = 1f;

            economy.Markets[1] = localMarket;
            economy.Markets[2] = offMap;

            economy.RebuildCellToMarketLookup();

            Assert.That(economy.CountyToMarket[10], Is.EqualTo(1),
                "County should remain assigned to legitimate market, not off-map supplemental market.");
            Assert.That(economy.CountyToMarket[20], Is.EqualTo(1),
                "County should remain assigned to legitimate market, not off-map supplemental market.");
        }

        [Test]
        public void OffMapSupplySystem_ReplenishesConfiguredGoodsOnly()
        {
            var state = new SimulationState
            {
                Economy = new EconomyState()
            };

            var offMap = new Market
            {
                Id = 5,
                Type = MarketType.OffMap,
                Name = "OffMap",
                OffMapGoodIds = new HashSet<string> { "spices" }
            };
            offMap.Goods["spices"] = new MarketGoodState
            {
                GoodId = "spices",
                Supply = 3f,
                SupplyOffered = 3f,
                Price = 20f,
                BasePrice = 20f
            };
            offMap.Goods["wheat"] = new MarketGoodState
            {
                GoodId = "wheat",
                Supply = 7f,
                SupplyOffered = 7f,
                Price = 1f,
                BasePrice = 1f
            };
            state.Economy.Markets[offMap.Id] = offMap;

            var system = new OffMapSupplySystem();
            system.Initialize(state, null);
            system.Tick(state, null);

            Assert.That(offMap.Goods["spices"].Supply, Is.EqualTo(1000f).Within(0.0001f));
            Assert.That(offMap.Goods["spices"].SupplyOffered, Is.EqualTo(1000f).Within(0.0001f));
            Assert.That(offMap.Goods["wheat"].Supply, Is.EqualTo(7f).Within(0.0001f),
                "Only goods listed in OffMapGoodIds should be replenished.");
            Assert.That(offMap.Goods["wheat"].SupplyOffered, Is.EqualTo(7f).Within(0.0001f),
                "Only goods listed in OffMapGoodIds should be replenished.");
        }

        [Test]
        public void OffMapMarketPlacer_DoesNotOfferRawGoodsAlreadyPresentOnMap()
        {
            var mapData = BuildOffMapPlacementMap();

            var economy = new EconomyState();
            InitialData.RegisterAll(economy);
            economy.Counties[1] = new CountyEconomy(1);
            economy.Counties[1].Resources["wheat"] = 1f;

            var transport = new TransportGraph(mapData);
            var result = OffMapMarketPlacer.Place(
                mapData,
                economy,
                transport,
                nextMarketId: 10,
                marketZoneMaxTransportCost: 100f);

            Assert.That(result.Markets.Count, Is.GreaterThan(0), "Expected at least one off-map market to be placed.");
            foreach (var market in result.Markets)
            {
                Assert.That(market.Type, Is.EqualTo(MarketType.OffMap));
                Assert.That(market.OffMapGoodIds, Is.Not.Null.And.Not.Empty);
                Assert.That(market.OffMapGoodIds.Contains("wheat"), Is.False,
                    "Raw goods already available on-map should be filtered out from off-map offerings.");
            }
        }

        [Test]
        public void OffMapMarketPlacer_RespectsAlternateInputRecipes_WhenDeterminingOnMapProduction()
        {
            var mapData = BuildOffMapPlacementMap();

            var economy = new EconomyState();
            InitialData.RegisterAll(economy);
            economy.Counties[1] = new CountyEconomy(1);
            economy.Counties[1].Resources["rye"] = 1f;

            var transport = new TransportGraph(mapData);
            var result = OffMapMarketPlacer.Place(
                mapData,
                economy,
                transport,
                nextMarketId: 10,
                marketZoneMaxTransportCost: 100f);

            Assert.That(result.Markets.Count, Is.GreaterThan(0), "Expected at least one off-map market to be placed.");
            foreach (var market in result.Markets)
            {
                Assert.That(market.OffMapGoodIds.Contains("flour"), Is.False,
                    "Flour should be treated as on-map producible via rye_mill input override.");
                Assert.That(market.OffMapGoodIds.Contains("bread"), Is.False,
                    "Bread should be treated as on-map producible when flour is reachable via alternate recipes.");
            }
        }

        [Test]
        public void ProductionSystem_UsesFacilityInputOverride_WhenGoodDefaultInputsAreMissing()
        {
            var economy = new EconomyState();
            economy.Goods.Register(new GoodDef
            {
                Id = "wheat",
                Name = "Wheat",
                Category = GoodCategory.Raw
            });
            economy.Goods.Register(new GoodDef
            {
                Id = "alt_food",
                Name = "Alt Food",
                Category = GoodCategory.Refined,
                Inputs = null
            });
            economy.FacilityDefs.Register(new FacilityDef
            {
                Id = "alt_mill",
                Name = "Alt Mill",
                OutputGoodId = "alt_food",
                LaborRequired = 1,
                LaborType = LaborType.Unskilled,
                BaseThroughput = 2f,
                IsExtraction = false,
                InputOverrides = new List<GoodInput> { new GoodInput("wheat", 1) }
            });

            var county = new CountyEconomy(1)
            {
                Population = CountyPopulation.FromTotal(1000)
            };
            county.Stockpile.Add("wheat", 10f);
            economy.Counties[1] = county;

            var facility = new Facility
            {
                Id = 1,
                TypeId = "alt_mill",
                CountyId = 1,
                CellId = 1
            };
            economy.Facilities[facility.Id] = facility;
            county.FacilityIds.Add(facility.Id);

            var state = new SimulationState
            {
                Economy = economy
            };

            var system = new ProductionSystem();
            system.Initialize(state, null);
            system.Tick(state, null);

            Assert.That(county.Stockpile.Get("alt_food"), Is.GreaterThan(0f),
                "Processing should run from facility override inputs even when good default inputs are null.");
            Assert.That(county.Stockpile.Get("wheat"), Is.LessThan(10f),
                "Input goods should be consumed when production succeeds.");
        }

        private static MapData BuildLinearMap()
        {
            var mapData = new MapData
            {
                Info = new MapInfo
                {
                    World = CreateWorldInfo(cellSizeKm: 2.5f, mapWidthKm: 10f, mapHeightKm: 10f)
                },
                Cells = new List<Cell>
                {
                    new Cell { Id = 1, IsLand = true, BiomeId = 1, MovementCost = 42f, SeaRelativeElevation = 15f, HasSeaRelativeElevation = true, NeighborIds = new List<int> { 2 }, CountyId = 10, Center = new Vec2(0, 0) },
                    new Cell { Id = 2, IsLand = true, BiomeId = 1, MovementCost = 42f, SeaRelativeElevation = 15f, HasSeaRelativeElevation = true, NeighborIds = new List<int> { 1, 3 }, CountyId = 20, Center = new Vec2(1, 0) },
                    new Cell { Id = 3, IsLand = true, BiomeId = 1, MovementCost = 42f, SeaRelativeElevation = 15f, HasSeaRelativeElevation = true, NeighborIds = new List<int> { 2 }, CountyId = 30, Center = new Vec2(2, 0) }
                },
                Biomes = new List<Biome>
                {
                    new Biome { Id = 1, Name = "Plains" }
                },
                Counties = new List<County>
                {
                    new County { Id = 10, SeatCellId = 1, CellIds = new List<int> { 1 }, TotalPopulation = 22000, Centroid = new Vec2(0, 0) },
                    new County { Id = 20, SeatCellId = 2, CellIds = new List<int> { 2 }, TotalPopulation = 18000, Centroid = new Vec2(1, 0) },
                    new County { Id = 30, SeatCellId = 3, CellIds = new List<int> { 3 }, TotalPopulation = 16000, Centroid = new Vec2(2, 0) }
                },
                Provinces = new List<Province>(),
                Realms = new List<Realm>(),
                Rivers = new List<River>(),
                Burgs = new List<Burg>(),
                Features = new List<Feature>(),
                Vertices = new List<Vec2>()
            };

            mapData.BuildLookups();
            return mapData;
        }

        private static MapData BuildOffMapPlacementMap()
        {
            var mapData = new MapData
            {
                Info = new MapInfo
                {
                    World = CreateWorldInfo(cellSizeKm: 2.5f, mapWidthKm: 100f, mapHeightKm: 100f)
                },
                Cells = new List<Cell>
                {
                    new Cell
                    {
                        Id = 1, IsLand = true, BiomeId = 1, MovementCost = 42f, CountyId = 1, IsBoundary = true,
                        SeaRelativeElevation = 20f, HasSeaRelativeElevation = true, CoastDistance = 1,
                        NeighborIds = new List<int> { 5 }, Center = new Vec2(0f, 50f)
                    },
                    new Cell
                    {
                        Id = 2, IsLand = true, BiomeId = 1, MovementCost = 42f, CountyId = 1, IsBoundary = true,
                        SeaRelativeElevation = 20f, HasSeaRelativeElevation = true, CoastDistance = 1,
                        NeighborIds = new List<int> { 5 }, Center = new Vec2(100f, 50f)
                    },
                    new Cell
                    {
                        Id = 3, IsLand = true, BiomeId = 1, MovementCost = 42f, CountyId = 1, IsBoundary = true,
                        SeaRelativeElevation = 20f, HasSeaRelativeElevation = true, CoastDistance = 1,
                        NeighborIds = new List<int> { 5 }, Center = new Vec2(50f, 0f)
                    },
                    new Cell
                    {
                        Id = 4, IsLand = true, BiomeId = 1, MovementCost = 42f, CountyId = 1, IsBoundary = true,
                        SeaRelativeElevation = 20f, HasSeaRelativeElevation = true, CoastDistance = 1,
                        NeighborIds = new List<int> { 5 }, Center = new Vec2(50f, 100f)
                    },
                    new Cell
                    {
                        Id = 5, IsLand = true, BiomeId = 1, MovementCost = 42f, CountyId = 1, IsBoundary = false,
                        SeaRelativeElevation = 20f, HasSeaRelativeElevation = true, CoastDistance = 2,
                        NeighborIds = new List<int> { 1, 2, 3, 4 }, Center = new Vec2(50f, 50f)
                    }
                },
                Biomes = new List<Biome>
                {
                    new Biome { Id = 1, Name = "Grassland" }
                },
                Counties = new List<County>
                {
                    new County
                    {
                        Id = 1,
                        SeatCellId = 5,
                        CellIds = new List<int> { 1, 2, 3, 4, 5 },
                        TotalPopulation = 10000,
                        Centroid = new Vec2(50f, 50f)
                    }
                },
                Provinces = new List<Province>(),
                Realms = new List<Realm>(),
                Rivers = new List<River>(),
                Burgs = new List<Burg>(),
                Features = new List<Feature>(),
                Vertices = new List<Vec2>()
            };

            mapData.BuildLookups();
            return mapData;
        }

        private static MapData BuildTwoCellTransportMap(float distanceKm, float cellSizeKm)
        {
            var info = new MapInfo
            {
                World = CreateWorldInfo(cellSizeKm, mapWidthKm: distanceKm, mapHeightKm: 10f)
            };

            var mapData = new MapData
            {
                Info = info,
                Cells = new List<Cell>
                {
                    new Cell { Id = 1, IsLand = true, BiomeId = 1, MovementCost = 42f, SeaRelativeElevation = 15f, HasSeaRelativeElevation = true, NeighborIds = new List<int> { 2 }, Center = new Vec2(0, 0) },
                    new Cell { Id = 2, IsLand = true, BiomeId = 1, MovementCost = 42f, SeaRelativeElevation = 15f, HasSeaRelativeElevation = true, NeighborIds = new List<int> { 1 }, Center = new Vec2(distanceKm, 0) }
                },
                Biomes = new List<Biome>
                {
                    new Biome { Id = 1, Name = "Plains" }
                },
                Counties = new List<County>(),
                Provinces = new List<Province>(),
                Realms = new List<Realm>(),
                Rivers = new List<River>(),
                Burgs = new List<Burg>(),
                Features = new List<Feature>(),
                Vertices = new List<Vec2>()
            };

            mapData.BuildLookups();
            return mapData;
        }

        private static MapData BuildBudgetMap(float mapWidthKm, float mapHeightKm, float cellSizeKm)
        {
            return new MapData
            {
                Info = new MapInfo
                {
                    World = CreateWorldInfo(cellSizeKm, mapWidthKm, mapHeightKm)
                }
            };
        }

        private static WorldInfo CreateWorldInfo(
            float cellSizeKm,
            float mapWidthKm,
            float mapHeightKm,
            float seaLevel = 0f,
            float maxElevationMeters = 5000f,
            float maxSeaDepthMeters = 1250f)
        {
            return new WorldInfo
            {
                CellSizeKm = cellSizeKm,
                MapWidthKm = mapWidthKm,
                MapHeightKm = mapHeightKm,
                MapAreaKm2 = mapWidthKm * mapHeightKm,
                LatitudeSouth = 30f,
                LatitudeNorth = 31f,
                MinHeight = -maxSeaDepthMeters,
                SeaLevelHeight = seaLevel,
                MaxHeight = maxElevationMeters,
                MaxElevationMeters = maxElevationMeters,
                MaxSeaDepthMeters = maxSeaDepthMeters
            };
        }
    }
}
