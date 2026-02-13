using System;
using System.Collections.Generic;
using System.Reflection;
using EconSim.Core.Common;
using EconSim.Core.Data;
using EconSim.Core.Economy;
using EconSim.Core.Simulation;
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
                        NeighborIds = new List<int>(),
                        Center = new Vec2(1, 0)
                    }
                },
                Biomes = new List<Biome>
                {
                    new Biome { Id = 1, Name = "Plains", MovementCost = 1 }
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

            Assert.That(foothillCost, Is.EqualTo(1f).Within(0.0001f), "Cell below mountain threshold should not pay height penalty.");
            Assert.That(peakCost, Is.EqualTo(100f).Within(0.0001f), "Cell above impassable threshold should be blocked.");
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
                        CountyId = 20,
                        NeighborIds = new List<int> { 1 },
                        Center = new Vec2(1, 0)
                    }
                },
                Biomes = new List<Biome>
                {
                    new Biome { Id = 1, Name = "Mountain", MovementCost = 80 }
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

            Assert.That(baselineCost, Is.EqualTo(1f).Within(0.0001f), "Normalization should keep 30km @ 2.5km cells near 1x distance factor.");
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
                    new Cell { Id = 1, IsLand = true, BiomeId = 1, SeaRelativeElevation = 15f, HasSeaRelativeElevation = true, NeighborIds = new List<int> { 2 }, CountyId = 10, Center = new Vec2(0, 0) },
                    new Cell { Id = 2, IsLand = true, BiomeId = 1, SeaRelativeElevation = 15f, HasSeaRelativeElevation = true, NeighborIds = new List<int> { 1, 3 }, CountyId = 20, Center = new Vec2(1, 0) },
                    new Cell { Id = 3, IsLand = true, BiomeId = 1, SeaRelativeElevation = 15f, HasSeaRelativeElevation = true, NeighborIds = new List<int> { 2 }, CountyId = 30, Center = new Vec2(2, 0) }
                },
                Biomes = new List<Biome>
                {
                    new Biome { Id = 1, Name = "Plains", MovementCost = 1 }
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
                    new Cell { Id = 1, IsLand = true, BiomeId = 1, SeaRelativeElevation = 15f, HasSeaRelativeElevation = true, NeighborIds = new List<int> { 2 }, Center = new Vec2(0, 0) },
                    new Cell { Id = 2, IsLand = true, BiomeId = 1, SeaRelativeElevation = 15f, HasSeaRelativeElevation = true, NeighborIds = new List<int> { 1 }, Center = new Vec2(distanceKm, 0) }
                },
                Biomes = new List<Biome>
                {
                    new Biome { Id = 1, Name = "Plains", MovementCost = 1 }
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
