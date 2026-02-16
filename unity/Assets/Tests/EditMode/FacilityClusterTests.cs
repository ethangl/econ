using System;
using System.Collections.Generic;
using EconSim.Core.Common;
using EconSim.Core.Data;
using EconSim.Core.Economy;
using NUnit.Framework;

namespace EconSim.Tests
{
    [TestFixture]
    public class FacilityClusterTests
    {
        [Test]
        public void FacilityScaling_UnitCountScalesLaborAndThroughput()
        {
            var def = new FacilityDef
            {
                Id = "test_facility",
                LaborRequired = 10,
                BaseThroughput = 5f
            };

            var facility = new Facility
            {
                IsActive = true,
                UnitCount = 4,
                AssignedWorkers = 40
            };

            Assert.That(facility.GetRequiredLabor(def), Is.EqualTo(40));
            Assert.That(facility.GetNominalThroughput(def), Is.EqualTo(20f).Within(0.0001f));
            Assert.That(facility.GetEfficiency(def), Is.EqualTo(1f).Within(0.0001f));
            Assert.That(facility.GetThroughput(def), Is.EqualTo(20f).Within(0.0001f));

            facility.AssignedWorkers = 20;
            float expectedEfficiency = (float)Math.Pow(0.5f, 0.7f);
            Assert.That(facility.GetEfficiency(def), Is.EqualTo(expectedEfficiency).Within(0.0001f));
            Assert.That(facility.GetThroughput(def), Is.EqualTo(20f * expectedEfficiency).Within(0.0001f));
        }

        [Test]
        public void EconomyState_CreateFacility_SetsClusterUnitCount()
        {
            var economy = new EconomyState();
            var map = new MapData
            {
                Cells = new List<Cell>
                {
                    new Cell { Id = 1, IsLand = true, CountyId = 10, NeighborIds = new List<int>() }
                },
                Counties = new List<County>
                {
                    new County { Id = 10, SeatCellId = 1, CellIds = new List<int> { 1 }, TotalPopulation = 1000 }
                },
                Provinces = new List<Province>(),
                Realms = new List<Realm>(),
                Rivers = new List<River>(),
                Burgs = new List<Burg>(),
                Features = new List<Feature>(),
                Biomes = new List<Biome>(),
                Vertices = new List<Vec2>()
            };
            map.BuildLookups();
            economy.InitializeFromMap(map);

            var facility = economy.CreateFacility("farm", 1, unitCount: 7);

            Assert.That(facility.UnitCount, Is.EqualTo(7));
            Assert.That(economy.GetCounty(10).FacilityIds, Contains.Item(facility.Id));
            Assert.That(economy.Facilities.Count, Is.EqualTo(1));
        }
    }
}
