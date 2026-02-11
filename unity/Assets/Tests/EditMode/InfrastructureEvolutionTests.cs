using System.Collections.Generic;
using EconSim.Core.Common;
using EconSim.Core.Data;
using EconSim.Core.Economy;
using EconSim.Core.Simulation;
using EconSim.Core.Transport;
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
                    SeaLevel = 20f,
                    World = new WorldInfo
                    {
                        MaxElevationMeters = 8000f,
                        MaxSeaDepthMeters = 2000f
                    }
                },
                Cells = new List<Cell>
                {
                    new Cell
                    {
                        Id = 1,
                        IsLand = true,
                        Height = 69, // below legacy mountain start (70)
                        BiomeId = 1,
                        NeighborIds = new List<int>(),
                        Center = new Vec2(0, 0)
                    },
                    new Cell
                    {
                        Id = 2,
                        IsLand = true,
                        Height = 100, // peak height
                        BiomeId = 1,
                        NeighborIds = new List<int>(),
                        Center = new Vec2(1, 0)
                    }
                },
                Biomes = new List<Biome>
                {
                    new Biome { Id = 1, Name = "Plains", MovementCost = 50 }
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
            Assert.That(peakCost, Is.EqualTo(3f).Within(0.0001f), "Peak cell should reach full mountain penalty (~3x base).");
        }

        [Test]
        public void EconomyInitializer_IronOreThreshold_RemainsStableWithWorldMetadata()
        {
            var mapData = new MapData
            {
                Info = new MapInfo
                {
                    Seed = "42",
                    SeaLevel = 20f,
                    World = new WorldInfo
                    {
                        MaxElevationMeters = 8000f,
                        MaxSeaDepthMeters = 2000f
                    }
                },
                Cells = new List<Cell>
                {
                    new Cell
                    {
                        Id = 1,
                        IsLand = true,
                        Height = 40, // below iron threshold anchor (legacy absolute 50)
                        BiomeId = 1,
                        CountyId = 10,
                        NeighborIds = new List<int> { 2 },
                        Center = new Vec2(0, 0)
                    },
                    new Cell
                    {
                        Id = 2,
                        IsLand = true,
                        Height = 80, // above iron threshold anchor
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
            var legacyMap = BuildTwoCellTransportMap(distanceKm: 30f, cellSizeKm: null);
            var worldScaleMap = BuildTwoCellTransportMap(distanceKm: 60f, cellSizeKm: 5f);

            var legacyGraph = new TransportGraph(legacyMap);
            var worldScaleGraph = new TransportGraph(worldScaleMap);

            float legacyCost = legacyGraph.GetEdgeCost(legacyMap.CellById[1], legacyMap.CellById[2]);
            float worldScaleCost = worldScaleGraph.GetEdgeCost(worldScaleMap.CellById[1], worldScaleMap.CellById[2]);

            Assert.That(legacyCost, Is.EqualTo(1f).Within(0.0001f), "Legacy normalization should keep 30km edge near 1x distance factor.");
            Assert.That(worldScaleCost, Is.EqualTo(legacyCost).Within(0.0001f),
                "Distance normalization should preserve equivalent edge cost when world scale doubles.");
        }

        private static MapData BuildLinearMap()
        {
            var mapData = new MapData
            {
                Cells = new List<Cell>
                {
                    new Cell { Id = 1, IsLand = true, BiomeId = 1, Height = 35, NeighborIds = new List<int> { 2 }, CountyId = 10, Center = new Vec2(0, 0) },
                    new Cell { Id = 2, IsLand = true, BiomeId = 1, Height = 35, NeighborIds = new List<int> { 1, 3 }, CountyId = 20, Center = new Vec2(1, 0) },
                    new Cell { Id = 3, IsLand = true, BiomeId = 1, Height = 35, NeighborIds = new List<int> { 2 }, CountyId = 30, Center = new Vec2(2, 0) }
                },
                Biomes = new List<Biome>
                {
                    new Biome { Id = 1, Name = "Plains", MovementCost = 50 }
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

        private static MapData BuildTwoCellTransportMap(float distanceKm, float? cellSizeKm)
        {
            var info = new MapInfo
            {
                SeaLevel = 20f
            };

            if (cellSizeKm.HasValue)
            {
                info.World = new WorldInfo
                {
                    CellSizeKm = cellSizeKm.Value,
                    SeaLevelHeight = 20f,
                    MaxElevationMeters = 5000f,
                    MaxSeaDepthMeters = 1250f
                };
            }

            var mapData = new MapData
            {
                Info = info,
                Cells = new List<Cell>
                {
                    new Cell { Id = 1, IsLand = true, BiomeId = 1, Height = 35, NeighborIds = new List<int> { 2 }, Center = new Vec2(0, 0) },
                    new Cell { Id = 2, IsLand = true, BiomeId = 1, Height = 35, NeighborIds = new List<int> { 1 }, Center = new Vec2(distanceKm, 0) }
                },
                Biomes = new List<Biome>
                {
                    new Biome { Id = 1, Name = "Plains", MovementCost = 50 }
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
    }
}
