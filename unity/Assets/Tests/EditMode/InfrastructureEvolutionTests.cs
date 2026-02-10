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
    }
}
